using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Audit;
using EstrichOne.Api.Modules.Lexware;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Lexware;

/// <summary>
/// Änderungsabgleich verknüpfter Kunden und Beleg-PDF gegen die nachgebildete Lexware-API: Erstabgleich ohne Überschreiben,
/// Übernahme einseitiger Änderungen in beide Richtungen, Konflikte mit Entscheidung je Feld, Richtungseinstellung, Schalter
/// je Kunde, fehlende und archivierte Kontakte, Verhalten bei nicht erreichbarem Lexware Office. Belege bleiben lesend.
/// </summary>
public sealed class LexwareChangeSyncTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Reconciliation = "/api/v1/settings/integrations/lexware/reconciliation";
    private const string Settings = "/api/v1/settings/integrations/lexware";

    private readonly FakeLexware _lexware = new();
    private ApiFactory _factory = null!;
    private HttpClient _admin = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
        {
            ["EstrichOne:Lexware:MinRequestInterval"] = "00:00:00", ["EstrichOne:Lexware:RetryDelay"] = "00:00:00.020",
            ["EstrichOne:Lexware:BackgroundChangeSync"] = "false",
        }, services => services.AddHttpClient(LexwareClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _lexware));
        _admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        await SendAsync<JsonObject>(HttpMethod.Put, "/api/v1/settings/modules", new { modules = new Dictionary<string, bool> { ["lexware"] = true } });
        await SendAsync<LexwareSettingsResponse>(HttpMethod.Put, Settings, new { apiKey = FakeLexware.Key });
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _factory.DisposeAsync();
        _lexware.Dispose();
    }

    private Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK) =>
        SendAsync<T>(_admin, method, path, body, expected);

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, options: TestJson.Options) };
        using var response = await client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Ct))!;
    }

    private static async Task<string> ProblemAsync(HttpClient client, HttpMethod method, string path, HttpStatusCode expected, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, options: TestJson.Options) };
        using var response = await client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(expected, text);
        var problem = JsonNode.Parse(text)!;
        return $"{problem["code"]} {problem["detail"]}";
    }

    private async Task<T> DbAsync<T>(Func<EstrichOneDbContext, Task<T>> query)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>());
    }

    private async Task<HttpClient> ClientWithAsync(string user, params string[] permissions)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var role = Role.Create(Guid.CreateVersion7(), "Rolle " + user, "", isSystem: false, permissions);
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        db.Add(role);
        await db.SaveChangesAsync(Ct);
        return await _factory.CreateClientAsAsync(user, role.Id);
    }

    /// <summary>Ein Lauf des Hintergrunddienstes (in den Tests von Hand angestoßen).</summary>
    private async Task<LexwareChangeSyncRun> RunAsync(bool full = false)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LexwareChangeSyncService>().RunAsync(full, Ct, reload: true);
    }

    private async Task<Guid> CustomerAsync(string name, string kind = "company", string? email = null, string? phone = null, string? street = null,
        string? postalCode = null, string? city = null, bool lexwareSync = true)
    {
        var id = Guid.CreateVersion7();
        await SendAsync<JsonObject>(HttpMethod.Post, "/api/v1/contacts", new { id, kind, name, email, phone, street, postalCode, city, lexwareSync },
            HttpStatusCode.Created);
        return id;
    }

    private Task<JsonObject> GetCustomerAsync(Guid id) => SendAsync<JsonObject>(HttpMethod.Get, $"/api/v1/contacts/{id}");

    /// <summary>Kunde wie im Web bearbeiten: lesen, Felder ändern, mit der gelesenen Version speichern.</summary>
    private async Task<JsonObject> EditCustomerAsync(Guid id, Action<JsonObject> change)
    {
        var customer = await GetCustomerAsync(id);
        change(customer);
        if (customer["email"]?.GetValue<string>() is { } email)
        {
            customer["emails"] = new JsonArray(new JsonObject { ["address"] = email });
        }

        if (customer["phone"]?.GetValue<string>() is { } phone)
        {
            customer["phones"] = new JsonArray(new JsonObject { ["kind"] = "landline", ["number"] = phone });
        }

        return await SendAsync<JsonObject>(HttpMethod.Put, $"/api/v1/contacts/{id}", customer);
    }

    private Task<CustomerLexwareResponse> LinkAsync(Guid customer, string lexwareContactId) =>
        SendAsync<CustomerLexwareResponse>(HttpMethod.Post, $"/api/v1/lexware/customers/{customer}/link", new { lexwareContactId });

    private Task<CustomerLexwareResponse> StateAsync(Guid customer) => SendAsync<CustomerLexwareResponse>(HttpMethod.Get, $"/api/v1/lexware/customers/{customer}");

    private JsonObject Billing(string lexwareContactId) => _lexware.Contacts[lexwareContactId]["addresses"]!["billing"]![0]!.AsObject();

    /// <summary>Kunde und Lexware-Kontakt mit gleichen Daten, verknüpft und einmal abgeglichen.</summary>
    private async Task<(Guid Customer, string Lexware)> SyncedPairAsync()
    {
        var lexware = _lexware.AddContact("Huber Bau GmbH", email: "info@huber.example", phone: "08031 1111", street: "Altweg 1", zip: "83022", city: "Rosenheim");
        var customer = await CustomerAsync("Huber Bau GmbH", email: "info@huber.example", phone: "08031 1111", street: "Altweg 1", postalCode: "83022",
            city: "Rosenheim");
        (await LinkAsync(customer, lexware)).Sync!.State.ShouldBe("synced");
        (await RunAsync()).Checked.ShouldBe(0);
        return (customer, lexware);
    }

    [Fact]
    public async Task FirstSync_ShowsExistingDifferencesAsDeviations_AndOverwritesNothing()
    {
        var lexware = _lexware.AddContact("Huber Bau GmbH", email: "info@huber.example", phone: "08031 1111", street: "Neuweg 2", zip: "83022", city: "Rosenheim");
        var customer = await CustomerAsync("Huber Bau GmbH", email: "info@huber.example", phone: "+49 8031 1111", street: "Altweg 1", postalCode: "83022",
            city: "Kolbermoor");
        // Schon beim Verknüpfen wird verglichen (mit dem dabei gelesenen Kontakt), aber nichts übernommen.
        var linked = await LinkAsync(customer, lexware);
        linked.Sync!.State.ShouldBe("deviations");
        linked.Sync.Deviations.Count.ShouldBe(2);

        var run = await RunAsync(full: true);
        (run.Active, run.Full, run.Checked, run.Failed, run.Error).ShouldBe((true, true, 0, 0, null));

        // Unterschiede bleiben stehen, bis jemand entscheidet. Gleiches (auch anders geschriebene Telefonnummer) ist abgeglichen.
        var state = await StateAsync(customer);
        state.Sync!.State.ShouldBe("deviations");
        state.Sync.SyncedAt.ShouldNotBeNull();
        state.Sync.Deviations.ShouldBe([new LexwareDeviationField("street", "Straße", "Altweg 1", "Neuweg 2"),
            new LexwareDeviationField("city", "Ort", "Kolbermoor", "Rosenheim")]);
        _lexware.Calls.ShouldAllBe(c => c.Method == "GET");
        (await GetCustomerAsync(customer))["street"]!.GetValue<string>().ShouldBe("Altweg 1");

        var overview = await SendAsync<LexwareReconciliationResponse>(HttpMethod.Get, Reconciliation);
        overview.Deviations.ShouldBe(1);
        var item = overview.DeviationItems!.ShouldHaveSingleItem();
        (item.Customer.Id, item.LexwareContactId, item.Fields.Count).ShouldBe((customer, lexware, 2));
        overview.LinkedItems.ShouldHaveSingleItem().Sync!.State.ShouldBe("deviations");
        (await SendAsync<LexwareSettingsResponse>(HttpMethod.Get, Settings)).CustomersWithDeviations.ShouldBe(1);

        // Ein weiterer Lauf ohne Änderung fragt Lexware nicht erneut je Kontakt und ändert nichts.
        var calls = _lexware.Calls.Count;
        (await RunAsync()).Checked.ShouldBe(0);
        _lexware.Calls.Count.ShouldBe(calls);
        (await DbAsync(db => db.Set<AuditEvent>().AsNoTracking().CountAsync(e => e.Action == "lexware.contact_synced", Ct))).ShouldBe(0);
    }

    [Fact]
    public async Task ChangeInEstrichOne_UpdatesLexwareContact_WithFreshVersion_AndKeepsEverythingElse()
    {
        var (customer, lexware) = await SyncedPairAsync();
        _lexware.Contacts[lexware]["emailAddresses"]!["office"] = new JsonArray("buero@huber.example");
        var number = _lexware.Contacts[lexware]["roles"]!["customer"]!["number"]!.GetValue<int>();

        await EditCustomerAsync(customer, c =>
        {
            c["name"] = "Huber Bau GmbH & Co. KG";
            c["street"] = "Neuweg 2";
            c["email"] = "kontakt@huber.example";
        });
        (await StateAsync(customer)).Sync!.State.ShouldBe("pending");
        (await RunAsync()).Checked.ShouldBe(1);

        var put = _lexware.CallsTo("PUT", "/v1/contacts/").ShouldHaveSingleItem();
        put.Path.ShouldBe($"/v1/contacts/{lexware}");
        JsonNode.Parse(put.Body!)!["version"]!.GetValue<int>().ShouldBe(1);
        var stored = _lexware.Contacts[lexware];
        stored["company"]!["name"]!.GetValue<string>().ShouldBe("Huber Bau GmbH & Co. KG");
        Billing(lexware).ToJsonString().ShouldBe("""{"street":"Neuweg 2","zip":"83022","city":"Rosenheim","countryCode":"DE"}""");
        stored["emailAddresses"]!.ToJsonString().ShouldBe("""{"business":["kontakt@huber.example"],"office":["buero@huber.example"]}""");
        stored["roles"]!["customer"]!["number"]!.GetValue<int>().ShouldBe(number); // Kundennummern werden nie überschrieben
        stored["note"]!.GetValue<string>().ShouldBe("Stammkunde");

        var state = await StateAsync(customer);
        state.Sync!.State.ShouldBe("synced");
        state.Link!.Name.ShouldBe("Huber Bau GmbH & Co. KG");
        var audit = await DbAsync(db => db.Set<AuditEvent>().AsNoTracking().SingleAsync(e => e.Action == "lexware.contact_synced", Ct));
        audit.ActorUserId.ShouldBeNull();
        System.Text.Json.JsonSerializer.Serialize(audit).ShouldContain("EstrichONE nach Lexware Office");
        System.Text.Json.JsonSerializer.Serialize(audit).ShouldContain("Name, Stra");

        // Die eigene Änderung kommt nicht als Änderung aus Lexware zurück.
        (await RunAsync(full: true)).Failed.ShouldBe(0);
        _lexware.CallsTo("PUT", "/v1/contacts/").Count().ShouldBe(1);
        (await GetCustomerAsync(customer))["name"]!.GetValue<string>().ShouldBe("Huber Bau GmbH & Co. KG");
        _lexware.Calls.ShouldNotContain(c => c.Method != "GET" && c.Method != "PUT");
    }

    [Fact]
    public async Task ChangeInLexware_IsDetectedByVersion_AndTakenOverIntoTheCustomer()
    {
        var (customer, lexware) = await SyncedPairAsync();
        var numberBefore = (await GetCustomerAsync(customer))["customerNumber"]!.GetValue<string>();
        _lexware.Edit(lexware, c =>
        {
            c["addresses"]!["billing"]![0]!["city"] = "Kolbermoor";
            c["phoneNumbers"]!["business"] = new JsonArray("08031 2222");
            c["roles"]!["customer"]!["number"] = 99999;
        });

        // Ohne vollständigen Lauf fällt die Änderung in Lexware nicht auf; der vollständige Lauf liest die Kontaktliste.
        (await RunAsync()).Checked.ShouldBe(0);
        (await RunAsync(full: true)).Checked.ShouldBe(1);

        var after = await GetCustomerAsync(customer);
        (after["city"]!.GetValue<string>(), after["phone"]!.GetValue<string>(), after["street"]!.GetValue<string>())
            .ShouldBe(("Kolbermoor", "08031 2222", "Altweg 1"));
        after["customerNumber"]!.GetValue<string>().ShouldBe(numberBefore);
        _lexware.Calls.ShouldAllBe(c => c.Method == "GET");
        var state = await StateAsync(customer);
        state.Sync!.State.ShouldBe("synced");
        state.Link!.Number.ShouldBe("99999");
        System.Text.Json.JsonSerializer.Serialize(await DbAsync(db => db.Set<AuditEvent>().AsNoTracking()
            .SingleAsync(e => e.Action == "lexware.contact_synced", Ct))).ShouldContain("Lexware Office nach EstrichONE");

        // Die übernommene Änderung gilt nicht als neue Änderung in EstrichONE.
        (await RunAsync()).Checked.ShouldBe(0);
    }

    [Fact]
    public async Task BothSidesChangedTheSameField_IsNotOverwritten_UserDecidesPerFieldOrForAll()
    {
        var (customer, lexware) = await SyncedPairAsync();
        await EditCustomerAsync(customer, c =>
        {
            c["street"] = "Neuweg 2";
            c["city"] = "Kolbermoor";
            c["email"] = "a@huber.example";
        });
        _lexware.Edit(lexware, c =>
        {
            c["addresses"]!["billing"]![0]!["street"] = "Neuer Weg 2";
            c["addresses"]!["billing"]![0]!["city"] = "Bad Aibling";
            c["addresses"]!["billing"]![0]!["zip"] = "83043";
            c["emailAddresses"]!["business"] = new JsonArray("b@huber.example");
        });

        (await RunAsync()).Checked.ShouldBe(1);

        // PLZ hat nur Lexware geändert: übernommen. Straße, Ort und E-Mail haben beide geändert: Abweichungen.
        var state = await StateAsync(customer);
        state.Sync!.State.ShouldBe("deviations");
        state.Sync.Deviations.Select(d => (d.Field, d.EstrichOne, d.Lexware)).ShouldBe([("street", "Neuweg 2", "Neuer Weg 2"),
            ("city", "Kolbermoor", "Bad Aibling"), ("email", "a@huber.example", "b@huber.example")]);
        (await GetCustomerAsync(customer))["postalCode"]!.GetValue<string>().ShouldBe("83043");
        _lexware.CallsTo("PUT", "/v1/contacts/").ShouldBeEmpty();

        // Ungültige Entscheidung und fehlendes Recht.
        (await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{customer}/sync/resolve", HttpStatusCode.UnprocessableEntity,
            new { fields = new { street = "beide" } })).ShouldStartWith("LEXWARE_INVALID_CHOICE");
        using var assigner = await ClientWithAsync("zuordner", "invoices.read", "invoices.assign", "contacts.read");
        (await ProblemAsync(assigner, HttpMethod.Post, $"/api/v1/lexware/customers/{customer}/sync/resolve", HttpStatusCode.Forbidden,
            new { source = "lexware" })).ShouldStartWith("PERMISSION_DENIED");

        // Je Feld: Straße aus EstrichONE, Ort aus Lexware; die E-Mail bleibt offen.
        var partly = await SendAsync<CustomerLexwareResponse>(HttpMethod.Post, $"/api/v1/lexware/customers/{customer}/sync/resolve",
            new { fields = new { street = "estrichOne", city = "lexware" } });
        partly.Sync!.Deviations.ShouldHaveSingleItem().Field.ShouldBe("email");
        Billing(lexware)["street"]!.GetValue<string>().ShouldBe("Neuweg 2");
        Billing(lexware)["city"]!.GetValue<string>().ShouldBe("Bad Aibling");
        (await GetCustomerAsync(customer))["city"]!.GetValue<string>().ShouldBe("Bad Aibling");

        // Gesamt: Lexware übernehmen.
        var done = await SendAsync<CustomerLexwareResponse>(HttpMethod.Post, $"/api/v1/lexware/customers/{customer}/sync/resolve", new { source = "lexware" });
        done.Sync!.State.ShouldBe("synced");
        (await GetCustomerAsync(customer))["email"]!.GetValue<string>().ShouldBe("b@huber.example");
        (await SendAsync<LexwareReconciliationResponse>(HttpMethod.Get, Reconciliation)).Deviations.ShouldBe(0);
        var audits = await DbAsync(db => db.Set<AuditEvent>().AsNoTracking().Where(e => e.Action == "lexware.contact_synced").ToListAsync(Ct));
        audits.Count(a => a.ActorUserId is not null).ShouldBe(3); // Straße nach Lexware, Ort und E-Mail nach EstrichONE
        (await RunAsync(full: true)).Failed.ShouldBe(0);
        (await StateAsync(customer)).Sync!.State.ShouldBe("synced");
    }

    [Theory]
    [InlineData("toLexware", true, false)]
    [InlineData("fromLexware", false, true)]
    [InlineData("off", false, false)]
    public async Task Direction_Setting_IsRespected(string direction, bool push, bool pull)
    {
        var (customer, lexware) = await SyncedPairAsync();
        var saved = await SendAsync<LexwareSettingsResponse>(HttpMethod.Put, Settings, new { changeSync = direction });
        saved.ChangeSync.ToString().ShouldBe(direction, StringCompareShould.IgnoreCase);
        await EditCustomerAsync(customer, c => c["street"] = "Neuweg 2");
        _lexware.Edit(lexware, c => c["addresses"]!["billing"]![0]!["city"] = "Kolbermoor");

        var run = await RunAsync(full: true);

        run.Active.ShouldBe(direction != "off");
        _lexware.CallsTo("PUT", "/v1/contacts/").Count().ShouldBe(push ? 1 : 0);
        Billing(lexware)["street"]!.GetValue<string>().ShouldBe(push ? "Neuweg 2" : "Altweg 1");
        (await GetCustomerAsync(customer))["city"]!.GetValue<string>().ShouldBe(pull ? "Kolbermoor" : "Rosenheim");
        var state = await StateAsync(customer);
        if (direction == "off")
        {
            state.Sync!.State.ShouldBe("off");
            (await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{customer}/sync/run", HttpStatusCode.Conflict))
                .ShouldStartWith("LEXWARE_CHANGE_SYNC_OFF");

            // Wieder eingeschaltet: Erstabgleich. Die Unterschiede aus der Zwischenzeit werden gezeigt, nicht still übernommen.
            await SendAsync<LexwareSettingsResponse>(HttpMethod.Put, Settings, new { changeSync = "both" });
            await RunAsync(full: true);
            (await StateAsync(customer)).Sync!.Deviations.Select(d => d.Field).ShouldBe(["street", "city"]);
            _lexware.CallsTo("PUT", "/v1/contacts/").ShouldBeEmpty();
            return;
        }

        // Was die Richtung nicht übernimmt, bleibt als Abweichung sichtbar.
        state.Sync!.Deviations.ShouldHaveSingleItem().Field.ShouldBe(push ? "city" : "street");
    }

    [Fact]
    public async Task Default_IsBothDirections_AndUnknownDirectionIsRejected()
    {
        (await SendAsync<LexwareSettingsResponse>(HttpMethod.Get, Settings)).ChangeSync.ShouldBe(LexwareChangeSync.Both);
        using var request = new HttpRequestMessage(HttpMethod.Put, Settings) { Content = JsonContent.Create(new { changeSync = "irgendwie" }) };
        using var response = await _admin.SendAsync(request, Ct);
        ((int)response.StatusCode).ShouldBeInRange(400, 422);
    }

    [Fact]
    public async Task SyncSwitchOff_OrArchivedCustomer_IsNotSynced_AndStartsOverWhenSwitchedOn()
    {
        var (customer, lexware) = await SyncedPairAsync();
        await SendAsync<CustomerLexwareResponse>(HttpMethod.Put, $"/api/v1/lexware/customers/{customer}/sync", new { enabled = false });
        await EditCustomerAsync(customer, c => c["street"] = "Neuweg 2");
        _lexware.Edit(lexware, c => c["addresses"]!["billing"]![0]!["city"] = "Kolbermoor");
        var calls = _lexware.CallsTo("GET", "/v1/contacts/").Count();

        (await RunAsync(full: true)).Checked.ShouldBe(0);

        _lexware.CallsTo("PUT", "/v1/contacts/").ShouldBeEmpty();
        _lexware.CallsTo("GET", "/v1/contacts/").Count().ShouldBe(calls);
        (await GetCustomerAsync(customer))["city"]!.GetValue<string>().ShouldBe("Rosenheim");
        (await StateAsync(customer)).Sync!.State.ShouldBe("disabled");
        (await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{customer}/sync/run", HttpStatusCode.Conflict))
            .ShouldStartWith("LEXWARE_SYNC_DISABLED");

        // Wieder eingeschaltet: Was sich in der Zwischenzeit unterschieden hat, wird als Abweichung gezeigt.
        await SendAsync<CustomerLexwareResponse>(HttpMethod.Put, $"/api/v1/lexware/customers/{customer}/sync", new { enabled = true });
        (await RunAsync()).Checked.ShouldBe(1);
        (await StateAsync(customer)).Sync!.Deviations.Select(d => d.Field).ShouldBe(["street", "city"]);
        _lexware.CallsTo("PUT", "/v1/contacts/").ShouldBeEmpty();

        // Archivierte Kunden ruhen; der Lexware-Kontakt bleibt unberührt und die Verknüpfung bestehen.
        await SendAsync<JsonObject>(HttpMethod.Post, $"/api/v1/contacts/{customer}/archive");
        (await RunAsync(full: true)).Checked.ShouldBe(0);
        var archived = await StateAsync(customer);
        archived.Sync!.State.ShouldBe("paused");
        archived.Link!.LexwareContactId.ShouldBe(lexware);
        _lexware.Calls.ShouldAllBe(c => c.Method == "GET");
    }

    [Fact]
    public async Task MissingOrArchivedLexwareContact_IsShownClearly_AndNothingIsWritten()
    {
        var (customer, lexware) = await SyncedPairAsync();
        var gone = _lexware.AddContact("Lang Zimmerei", email: "lang@example.org");
        var other = await CustomerAsync("Lang Zimmerei", email: "lang@example.org");
        await LinkAsync(other, gone);
        await RunAsync();

        _lexware.Edit(lexware, c => c["archived"] = true);
        _lexware.Contacts.TryRemove(gone, out _);
        await EditCustomerAsync(customer, c => c["street"] = "Neuweg 2");
        (await RunAsync(full: true)).Failed.ShouldBe(0);

        (await StateAsync(customer)).Sync!.State.ShouldBe("archived");
        (await StateAsync(other)).Sync!.State.ShouldBe("missing");
        _lexware.CallsTo("PUT", "/v1/contacts/").ShouldBeEmpty();
        var overview = await SendAsync<LexwareReconciliationResponse>(HttpMethod.Get, Reconciliation);
        overview.LinkedItems.ShouldAllBe(p => p.MissingInLexware);
        overview.LinkedItems.Select(p => p.Sync!.State).ShouldBe(["archived", "missing"], ignoreOrder: true);

        // Wird der Kontakt in Lexware wiederhergestellt, läuft der Abgleich weiter.
        _lexware.Edit(lexware, c => c["archived"] = false);
        await RunAsync(full: true);
        (await StateAsync(customer)).Sync!.State.ShouldBe("synced");
        Billing(lexware)["street"]!.GetValue<string>().ShouldBe("Neuweg 2");
    }

    [Fact]
    public async Task LexwareDown_NeverBlocksSavingTheCustomer_AndIsCaughtUpLater()
    {
        var (customer, lexware) = await SyncedPairAsync();
        _lexware.Raw["/v1/contacts"] = (503, "");

        // Speichern in EstrichONE hängt nicht an Lexware Office.
        (await EditCustomerAsync(customer, c => c["street"] = "Neuweg 2"))["street"]!.GetValue<string>().ShouldBe("Neuweg 2");
        var run = await RunAsync();
        run.Error.ShouldNotBeNull();
        run.Error.ShouldContain("nicht verfügbar");
        (await SendAsync<LexwareSettingsResponse>(HttpMethod.Get, Settings)).LastChangeSyncError.ShouldNotBeNull();
        (await StateAsync(customer)).Sync!.State.ShouldBe("pending");
        (await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{customer}/sync/run", HttpStatusCode.ServiceUnavailable))
            .ShouldStartWith("LEXWARE_UNAVAILABLE");

        // Wieder erreichbar: Der Abgleich wird nachgeholt.
        _lexware.Raw.Clear();
        (await RunAsync(full: true)).Error.ShouldBeNull();
        (await StateAsync(customer)).Sync!.State.ShouldBe("synced");
        Billing(lexware)["street"]!.GetValue<string>().ShouldBe("Neuweg 2");
        (await SendAsync<LexwareSettingsResponse>(HttpMethod.Get, Settings)).LastChangeSyncError.ShouldBeNull();
    }

    [Fact]
    public async Task RejectedUpdate_IsShownAtTheCustomer_AndRetriedLaterWithGrowingDelay()
    {
        var (customer, lexware) = await SyncedPairAsync();
        _lexware.PutFailure = (406, """{"status":406,"message":"Validation failed","details":[{"field":"addresses.billing[0].zip","message":"ungültig"}]}""");
        await EditCustomerAsync(customer, c => c["postalCode"] = "8302");

        var run = await RunAsync();
        (run.Checked, run.Failed, run.Error).ShouldBe((1, 1, null)); // liegt an diesem Kontakt, nicht an Lexware Office insgesamt
        var state = await StateAsync(customer);
        state.Sync!.State.ShouldBe("error");
        state.Sync.Error!.ShouldContain("addresses.billing[0].zip ungültig");

        // Nicht bei jedem Lauf erneut: erst nach der Wartezeit.
        _lexware.PutFailure = null;
        (await RunAsync()).Checked.ShouldBe(0);
        _factory.Clock.Advance(TimeSpan.FromMinutes(3));
        (await RunAsync()).Checked.ShouldBe(1);
        (await StateAsync(customer)).Sync!.State.ShouldBe("synced");
        Billing(lexware)["zip"]!.GetValue<string>().ShouldBe("8302");
        _lexware.CallsTo("PUT", "/v1/contacts/").Count().ShouldBe(2);
    }

    [Fact]
    public async Task ConflictOnPut_IsRetriedWithFreshData_AndRefreshInOverviewSyncsToo()
    {
        var (customer, lexware) = await SyncedPairAsync();
        await EditCustomerAsync(customer, c => c["street"] = "Neuweg 2");
        // Zwischen Lesen und Schreiben ändert jemand in Lexware Office den Ort.
        _lexware.BeforePut = stored =>
        {
            stored["version"] = stored["version"]!.GetValue<int>() + 1;
            stored["addresses"]!["billing"]![0]!["city"] = "Kolbermoor";
        };

        // „Aktualisieren“ in der Abgleichübersicht gleicht die Änderungen mit ab.
        var overview = await SendAsync<LexwareReconciliationResponse>(HttpMethod.Get, Reconciliation + "?refresh=true");

        overview.ChangeSyncError.ShouldBeNull();
        overview.Deviations.ShouldBe(0);
        _lexware.CallsTo("PUT", "/v1/contacts/").Count().ShouldBe(2); // erster Versuch 409, zweiter mit neuer Version
        Billing(lexware).ToJsonString().ShouldBe("""{"street":"Neuweg 2","zip":"83022","city":"Kolbermoor","countryCode":"DE"}""");
        var after = await GetCustomerAsync(customer);
        (after["street"]!.GetValue<string>(), after["city"]!.GetValue<string>()).ShouldBe(("Neuweg 2", "Kolbermoor"));
        (await StateAsync(customer)).Sync!.State.ShouldBe("synced");
    }

    [Fact]
    public async Task DeletedCustomer_LosesItsLink_LexwareContactStaysUntouched()
    {
        var (customer, lexware) = await SyncedPairAsync();
        var number = (await GetCustomerAsync(customer))["customerNumber"]!.GetValue<string>();
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/contacts/{customer}?confirm={Uri.EscapeDataString(number)}");
        using var response = await _admin.SendAsync(request, Ct);
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync(Ct));

        (await DbAsync(db => db.Set<LexwareContactLink>().AnyAsync(Ct))).ShouldBeFalse();
        (await RunAsync(full: true)).Checked.ShouldBe(0);
        _lexware.Contacts.ContainsKey(lexware).ShouldBeTrue();
        _lexware.Calls.ShouldAllBe(c => c.Method == "GET");
    }

    // ---------- Beleg als PDF ----------

    [Fact]
    public async Task VoucherDocument_IsStreamedForCustomerAndBauakte_ReadOnly_WithChecks()
    {
        var lexContact = _lexware.AddContact("Huber Bau GmbH");
        var customer = await CustomerAsync("Huber Bau GmbH");
        var stranger = await CustomerAsync("Fremd GmbH");
        await LinkAsync(customer, lexContact);
        var bauakte = Guid.CreateVersion7();
        await SendAsync<JsonObject>(HttpMethod.Post, "/api/v1/bauakten",
            new { id = bauakte, title = "EFH Huber", customerId = customer, street = "Am Anger 7", postalCode = "83022", city = "Rosenheim" }, HttpStatusCode.Created);
        var invoice = _lexware.AddVoucher("invoice", "open", "RE1042", "2026-10-01", "2026-10-15", 1000m, 1190m, lexContact, "Huber Bau GmbH");
        var draft = _lexware.AddVoucher("quotation", "draft", null, "2026-11-20", null, 500m, 595m, lexContact, "Huber Bau GmbH");
        var foreign = _lexware.AddVoucher("invoice", "open", "RE7777", "2026-11-01", "2099-01-01", 1m, 1.19m, _lexware.AddContact("Fremd GmbH"), "Fremd GmbH");
        var assigned = await SendAsync<LexwareVoucherResponse>(HttpMethod.Post, $"/api/v1/bauakten/{bauakte}/lexware/vouchers/assign",
            new { lexwareId = invoice, type = "invoice" }, HttpStatusCode.Created);
        var assignedDraft = await SendAsync<LexwareVoucherResponse>(HttpMethod.Post, $"/api/v1/bauakten/{bauakte}/lexware/vouchers/assign",
            new { lexwareId = draft, type = "quotation" }, HttpStatusCode.Created);

        // Am Kunden: Anzeige im Browser.
        using (var shown = await _admin.GetAsync(new Uri($"/api/v1/lexware/customers/{customer}/vouchers/{invoice}/document", UriKind.Relative), Ct))
        {
            shown.StatusCode.ShouldBe(HttpStatusCode.OK);
            shown.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
            shown.Content.Headers.ContentDisposition!.ToString().ShouldBe("inline; filename=\"Rechnung-RE1042.pdf\"");
            shown.Headers.CacheControl!.NoStore.ShouldBeTrue();
            (await shown.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(FakeLexware.Pdf(invoice));
        }

        // An der Bauakte: Herunterladen; kurz zwischengespeichert, kein zweiter Abruf bei Lexware.
        using (var download = await _admin.GetAsync(new Uri($"/api/v1/bauakten/{bauakte}/lexware/vouchers/{assigned.Id}/document?download=true", UriKind.Relative), Ct))
        {
            download.StatusCode.ShouldBe(HttpStatusCode.OK);
            download.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
            download.Content.Headers.ContentDisposition.FileName.ShouldBe("Rechnung-RE1042.pdf");
            (await download.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(FakeLexware.Pdf(invoice));
        }

        _lexware.Calls.Count(c => c.Path.EndsWith("/file", StringComparison.Ordinal)).ShouldBe(1);

        // Entwurf: klare Meldung an beiden Stellen.
        foreach (var path in new[] { $"/api/v1/lexware/customers/{customer}/vouchers/{draft}/document", $"/api/v1/bauakten/{bauakte}/lexware/vouchers/{assignedDraft.Id}/document" })
        {
            (await ProblemAsync(_admin, HttpMethod.Get, path, HttpStatusCode.Conflict))
                .ShouldBe("LEXWARE_NO_DOCUMENT Für Entwürfe stellt Lexware Office kein PDF bereit.");
        }

        // Der Beleg muss zum Kunden bzw. zur Bauakte gehören.
        (await ProblemAsync(_admin, HttpMethod.Get, $"/api/v1/lexware/customers/{customer}/vouchers/{foreign}/document", HttpStatusCode.NotFound))
            .ShouldStartWith("LEXWARE_VOUCHER_NOT_FOUND");
        (await ProblemAsync(_admin, HttpMethod.Get, $"/api/v1/lexware/customers/{stranger}/vouchers/{invoice}/document", HttpStatusCode.NotFound))
            .ShouldStartWith("LEXWARE_VOUCHER_NOT_FOUND");
        (await ProblemAsync(_admin, HttpMethod.Get, $"/api/v1/bauakten/{Guid.CreateVersion7()}/lexware/vouchers/{assigned.Id}/document", HttpStatusCode.NotFound))
            .ShouldStartWith("LEXWARE_NOT_LINKED");

        // Recht invoices.read.
        using var reader = await ClientWithAsync("leser", "contacts.read");
        using (var denied = await reader.GetAsync(new Uri($"/api/v1/lexware/customers/{customer}/vouchers/{invoice}/document", UriKind.Relative), Ct))
        {
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        // Lexware nicht erreichbar: verständliche Meldung, kein Serverfehler.
        _factory.Clock.Advance(TimeSpan.FromMinutes(3));
        _lexware.Raw["/v1/invoices"] = (503, "");
        (await ProblemAsync(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware/vouchers/{assigned.Id}/document", HttpStatusCode.ServiceUnavailable))
            .ShouldStartWith("LEXWARE_UNAVAILABLE");

        // Alles nur gelesen.
        _lexware.Calls.ShouldAllBe(c => c.Method == "GET");
    }
}
