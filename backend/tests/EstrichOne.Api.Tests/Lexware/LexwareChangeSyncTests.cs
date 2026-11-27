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
        await SendAsync<JsonObject>(HttpMethod.Post, "/api/v1/contacts", new { lexwareCreateAnyway = true, id, kind, name, email, phone, street, postalCode, city, lexwareSync },
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

    // ---------- Neue Kunden automatisch in Lexware Office anlegen ----------

    private async Task<int> AutoCreateAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LexwareAutoCreateService>().RunAsync(Ct);
    }

    private int Posts() => _lexware.CallsTo("POST", "/v1/contacts").Count();

    [Fact]
    public async Task NewCustomer_WithoutSimilarContact_IsCreatedInLexwareExactlyOnce_AndLinked()
    {
        (await SendAsync<LexwareSettingsResponse>(HttpMethod.Get, Settings)).AutoCreate.ShouldBeTrue();
        _lexware.AddContact("Ganz Anderer Betrieb", email: "anders@example.org");
        var customer = await CustomerAsync("Fliesen Brandl GmbH", email: "info@brandl.example", street: "Hofweg 2", postalCode: "83080", city: "Oberaudorf");
        Posts().ShouldBe(0); // das Speichern des Kunden wartet nicht auf Lexware Office
        (await StateAsync(customer)).Creation!.State.ShouldBe("pending");

        (await AutoCreateAsync()).ShouldBe(1);

        Posts().ShouldBe(1);
        var state = await StateAsync(customer);
        state.Creation.ShouldBeNull();
        state.Link!.Origin.ShouldBe(LexwareLinkOrigin.CreatedInLexware);
        var stored = _lexware.Contacts[state.Link.LexwareContactId];
        stored["company"]!["name"]!.GetValue<string>().ShouldBe("Fliesen Brandl GmbH");
        stored["addresses"]!["billing"]![0]!["zip"]!.GetValue<string>().ShouldBe("83080");
        var audit = await DbAsync(db => db.Set<AuditEvent>().AsNoTracking().SingleAsync(e => e.Action == "lexware.contact_created", Ct));
        audit.ActorUserId.ShouldBeNull();

        // Weitere Läufe legen nichts mehr an; der Erstabgleich danach schreibt nichts.
        (await AutoCreateAsync()).ShouldBe(0);
        await RunAsync(full: true);
        Posts().ShouldBe(1);
        _lexware.CallsTo("PUT", "/v1/contacts/").ShouldBeEmpty();
        (await StateAsync(customer)).Sync!.State.ShouldBe("synced");
    }

    [Fact]
    public async Task NewCustomer_WithSimilarContact_OrSwitchOff_OrSettingOff_IsNotCreated()
    {
        _lexware.AddContact("HUBER BAU GMBH & Co. KG", email: "buero@huber.example", zip: "83022");
        var similar = await CustomerAsync("Huber Bau GmbH", email: "buero@huber.example");
        var off = await CustomerAsync("Nur Hier Bau GmbH", lexwareSync: false);

        (await AutoCreateAsync()).ShouldBe(0);
        Posts().ShouldBe(0);
        (await StateAsync(similar)).Creation!.State.ShouldBe("duplicates");
        (await StateAsync(off)).Creation.ShouldBeNull();
        // Der Kunde mit Treffer steht in der Abgleichübersicht unter den möglichen Dubletten.
        var overview = await SendAsync<LexwareReconciliationResponse>(HttpMethod.Get, Reconciliation);
        overview.Suggestions.ShouldHaveSingleItem().Customer.Id.ShouldBe(similar);
        overview.OnlyEstrichOneItems.ShouldHaveSingleItem().Creation.ShouldBe("duplicates");
        (await AutoCreateAsync()).ShouldBe(0); // nicht bei jedem Lauf erneut

        // Schalter später eingeschaltet: dieselbe Regel, ohne Treffer wird angelegt.
        await SendAsync<CustomerLexwareResponse>(HttpMethod.Put, $"/api/v1/lexware/customers/{off}/sync", new { enabled = true });
        (await AutoCreateAsync()).ShouldBe(1);
        (await StateAsync(off)).Link.ShouldNotBeNull();
        Posts().ShouldBe(1);

        // Einstellung aus: nichts wird angelegt. Wieder ein: Die Kunden aus der Zwischenzeit gelten als Bestand.
        (await SendAsync<LexwareSettingsResponse>(HttpMethod.Put, Settings, new { autoCreate = false })).AutoCreate.ShouldBeFalse();
        var later = await CustomerAsync("Spätere Anlage GmbH");
        (await AutoCreateAsync()).ShouldBe(0);
        (await StateAsync(later)).Creation!.State.ShouldBe("manual");
        await SendAsync<LexwareSettingsResponse>(HttpMethod.Put, Settings, new { autoCreate = true });
        (await AutoCreateAsync()).ShouldBe(0);
        (await StateAsync(later)).Creation!.State.ShouldBe("manual");
        var afterwards = await CustomerAsync("Neu Danach GmbH");
        (await AutoCreateAsync()).ShouldBe(1);
        (await StateAsync(afterwards)).Link.ShouldNotBeNull();
        Posts().ShouldBe(2);

        // Bewusst gelöste Verknüpfung: kein erneutes Anlegen von selbst.
        using var unlink = await _admin.DeleteAsync(new Uri($"/api/v1/lexware/customers/{afterwards}/link", UriKind.Relative), Ct);
        unlink.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await AutoCreateAsync()).ShouldBe(0);
        Posts().ShouldBe(2);
    }

    [Fact]
    public async Task FailedCreation_IsShown_RetriedWithFreshDuplicateCheck_AndNeverCreatesTwice()
    {
        var customer = await CustomerAsync("Fliesen Brandl GmbH", email: "info@brandl.example");
        _lexware.Raw["/v1/contacts"] = (422, """{"IssueList":[{"i18nKey":"missing_entity","source":"company.name","type":"validation_failure"}]}""");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            // Die Kontaktliste liegt schon vor; abgelehnt wird erst das Anlegen.
            _lexware.Raw.Clear();
            await scope.ServiceProvider.GetRequiredService<LexwareService>().ContactsAsync(
                await scope.ServiceProvider.GetRequiredService<LexwareService>().SettingsAsync(Ct), refresh: true, Ct);
            _lexware.Raw["/v1/contacts"] = (422, """{"IssueList":[{"i18nKey":"missing_entity","source":"company.name","type":"validation_failure"}]}""");
        }

        (await AutoCreateAsync()).ShouldBe(0);
        var failed = await StateAsync(customer);
        failed.Creation!.State.ShouldBe("failed");
        failed.Creation.Error!.ShouldContain("company.name missing_entity");
        (await GetCustomerAsync(customer))["name"]!.GetValue<string>().ShouldBe("Fliesen Brandl GmbH"); // der Kunde selbst ist unberührt

        // Nicht bei jedem Lauf erneut. Inzwischen hat jemand den Kontakt in Lexware Office von Hand angelegt:
        // Der neue Versuch prüft zuerst erneut und legt nichts doppelt an.
        _lexware.Raw.Clear();
        var posts = Posts();
        (await AutoCreateAsync()).ShouldBe(0);
        Posts().ShouldBe(posts);
        _lexware.AddContact("Fliesen Brandl GmbH", email: "info@brandl.example");
        var retried = await SendAsync<CustomerLexwareResponse>(HttpMethod.Post, $"/api/v1/lexware/customers/{customer}/create/retry");
        retried.Creation!.State.ShouldBe("duplicates");
        retried.Link.ShouldBeNull();
        Posts().ShouldBe(posts);
        _lexware.Contacts.Values.Count(c => c["company"]?["name"]?.GetValue<string>() == "Fliesen Brandl GmbH").ShouldBe(1);

        // Ohne Treffer legt „Erneut versuchen“ an.
        var second = await CustomerAsync("Estrich Sonnleitner");
        _lexware.Raw["/v1/contacts"] = (503, "");
        (await AutoCreateAsync()).ShouldBe(0);
        _lexware.Raw.Clear();
        var done = await SendAsync<CustomerLexwareResponse>(HttpMethod.Post, $"/api/v1/lexware/customers/{second}/create/retry");
        done.Link.ShouldNotBeNull();
        Posts().ShouldBe(posts + 2); // der abgelehnte Versuch und der erfolgreiche
        _lexware.Contacts.Values.Count(c => c["company"]?["name"]?.GetValue<string>() == "Estrich Sonnleitner").ShouldBe(1);
    }

    [Fact]
    public async Task BulkEnable_ShowsBeforehandHowManyWouldBeCreated()
    {
        _lexware.AddContact("HUBER BAU GMBH & Co. KG", email: "buero@huber.example");
        var similar = await CustomerAsync("Huber Bau GmbH", email: "buero@huber.example", lexwareSync: false);
        var fresh = await CustomerAsync("Fliesen Brandl GmbH", lexwareSync: false);
        const string enable = Reconciliation + "/enable-sync";

        var preview = await SendAsync<EnableLexwareSyncResponse>(HttpMethod.Post, enable, new { contactIds = new[] { similar, fresh }, preview = true });
        (preview.Changed, preview.WouldCreate, preview.WithMatches).ShouldBe((2, 1, 1));
        (await GetCustomerAsync(fresh))["lexwareSync"]!.GetValue<bool>().ShouldBeFalse(); // die Vorschau ändert nichts
        (await AutoCreateAsync()).ShouldBe(0);

        var done = await SendAsync<EnableLexwareSyncResponse>(HttpMethod.Post, enable, new { contactIds = new[] { similar, fresh } });
        (done.Changed, done.WouldCreate, done.WithMatches).ShouldBe((2, 1, 1));
        (await AutoCreateAsync()).ShouldBe(1);
        (await StateAsync(fresh)).Link.ShouldNotBeNull();
        (await StateAsync(similar)).Creation!.State.ShouldBe("duplicates");
        Posts().ShouldBe(1);
    }

    // ---------- Verbindliche Prüfung vor dem Anlegen eines Kunden ----------

    [Fact]
    public async Task CreatingACustomer_ChecksLexwareFirst_WithFinalData_AndSavesOnlyWithoutHitsOrAfterConfirmation()
    {
        const string contacts = "/api/v1/contacts";
        for (var i = 0; i < 300; i++)
        {
            _lexware.AddContact($"Reihenhaus {i:000} GmbH", email: $"haus{i}@example.org");
        }

        var huber = _lexware.AddContact("HUBER BAU GMBH & Co. KG", email: "buero@huber.example", street: "Am Anger 7", zip: "83022", city: "Rosenheim");
        async Task<(HttpStatusCode Status, JsonNode Body)> PostAsync(object body)
        {
            using var response = await _admin.PostAsJsonAsync(contacts, body, TestJson.Options, Ct);
            return (response.StatusCode, JsonNode.Parse(await response.Content.ReadAsStringAsync(Ct))!);
        }

        // Treffer: nichts wird gespeichert, die Antwort nennt die Kandidaten.
        var id = Guid.CreateVersion7();
        var (status, body) = await PostAsync(new { id, kind = "company", name = "Huber Bau GmbH", postalCode = "83022" });
        status.ShouldBe(HttpStatusCode.Conflict);
        body["code"]!.GetValue<string>().ShouldBe("LEXWARE_CUSTOMER_EXISTS");
        body["detail"]!.GetValue<string>().ShouldStartWith("Achtung: Dieser Kunde ist bereits in Lexware Office vorhanden.");
        body["candidates"]![0]!["contact"]!["id"]!.GetValue<string>().ShouldBe(huber);
        body["candidates"]![0]!["reasons"]!.ToJsonString().ShouldContain("gleicher Name und PLZ");
        body["canImport"]!.GetValue<bool>().ShouldBeTrue();
        (await DbAsync(db => db.Set<Modules.Contacts.Exports.Contact>().AnyAsync(c => c.Id == id, Ct))).ShouldBeFalse();
        // Präzise: die ganze Kontaktliste (mehrere Seiten) und gezielte Abfragen nach dem Namen, alles nur lesend.
        _lexware.CallsTo("GET", "/v1/contacts").Count(c => c.Query.Contains("page=1")).ShouldBeGreaterThan(0);
        _lexware.CallsTo("GET", "/v1/contacts").ShouldContain(c => c.Query.Contains("name=Huber Bau GmbH"));
        _lexware.Calls.ShouldAllBe(c => c.Method == "GET");

        // Ein Kontakt, der eben erst in Lexware Office entstanden ist (noch nicht in der zwischengespeicherten Liste), wird gefunden.
        _lexware.AddContact("Fliesen Brandl GmbH", email: "info@brandl.example");
        (await PostAsync(new { id = Guid.CreateVersion7(), kind = "company", name = "Fliesen Brandl" })).Status.ShouldBe(HttpStatusCode.Conflict);
        (await PostAsync(new { id = Guid.CreateVersion7(), kind = "person", name = "Jemand Anderes", email = "INFO@brandl.example" })).Body["code"]!
            .GetValue<string>().ShouldBe("LEXWARE_CUSTOMER_EXISTS");

        // Ausdrückliche Bestätigung: Der Kunde wird angelegt. Wiederholung derselben Anfrage bleibt ohne Folgen.
        (await PostAsync(new { id, kind = "company", name = "Huber Bau GmbH", postalCode = "83022", lexwareCreateAnyway = true })).Status
            .ShouldBe(HttpStatusCode.Created);
        (await PostAsync(new { id, kind = "company", name = "Huber Bau GmbH", postalCode = "83022" })).Status.ShouldBe(HttpStatusCode.OK);

        // Kein Treffer: wird ohne Bestätigung angelegt. Auch mit ausgeschaltetem Abgleich wird geprüft.
        (await PostAsync(new { id = Guid.CreateVersion7(), kind = "company", name = "Malerbetrieb Kern" })).Status.ShouldBe(HttpStatusCode.Created);
        (await PostAsync(new { id = Guid.CreateVersion7(), kind = "company", name = "Reihenhaus 077 GmbH", lexwareSync = false })).Body["code"]!
            .GetValue<string>().ShouldBe("LEXWARE_CUSTOMER_EXISTS");

        // Lexware Office nicht erreichbar: nicht stillschweigend anlegen, sondern eigener Fehler; mit Bestätigung ohne Prüfung.
        _lexware.Raw["/v1/contacts"] = (503, "");
        var blocked = Guid.CreateVersion7();
        var (downStatus, down) = await PostAsync(new { id = blocked, kind = "company", name = "Estrich Sonnleitner" });
        downStatus.ShouldBe(HttpStatusCode.Conflict);
        down["code"]!.GetValue<string>().ShouldBe("LEXWARE_CHECK_UNAVAILABLE");
        down["detail"]!.GetValue<string>().ShouldContain("Der Kunde wurde nicht angelegt.");
        (await DbAsync(db => db.Set<Modules.Contacts.Exports.Contact>().AnyAsync(c => c.Id == blocked, Ct))).ShouldBeFalse();
        (await PostAsync(new { id = blocked, kind = "company", name = "Estrich Sonnleitner", lexwareSkipCheck = true })).Status.ShouldBe(HttpStatusCode.Created);

        // „Ohne Prüfung“ gilt nur, wenn die Prüfung nicht möglich war: Ist Lexware erreichbar, zählen die Treffer.
        _lexware.Raw.Clear();
        (await PostAsync(new { id = Guid.CreateVersion7(), kind = "company", name = "Reihenhaus 012 GmbH", lexwareSkipCheck = true })).Body["code"]!
            .GetValue<string>().ShouldBe("LEXWARE_CUSTOMER_EXISTS");

        // Modul aus: Die Anlage läuft wie bisher, ohne Aufruf bei Lexware Office.
        await SendAsync<JsonObject>(HttpMethod.Put, "/api/v1/settings/modules", new { modules = new Dictionary<string, bool> { ["lexware"] = false } });
        var calls = _lexware.Calls.Count;
        (await PostAsync(new { id = Guid.CreateVersion7(), kind = "company", name = "Reihenhaus 013 GmbH" })).Status.ShouldBe(HttpStatusCode.Created);
        _lexware.Calls.Count.ShouldBe(calls);
    }

    // ---------- Prüfung beim Anlegen eines Kunden ----------

    [Fact]
    public async Task NewCustomerCheck_WarnsWhenContactExistsInLexware_AndNeverBlocks()
    {
        const string check = "/api/v1/lexware/contacts/check";
        var huber = _lexware.AddContact("HUBER BAU GMBH & Co. KG", email: "buero@huber.example", street: "Am Anger 7", zip: "83022", city: "Rosenheim");
        var lang = _lexware.AddContact("Zimmerei Lang", email: "info@lang.example");
        var existing = await CustomerAsync("Lang Zimmerei", email: "info@lang.example");
        await LinkAsync(existing, lang);

        // Treffer mit Grund, auch wenn der Kunde später nicht abgeglichen werden soll.
        var hit = await SendAsync<CheckLexwareContactResponse>(HttpMethod.Post, check, new { name = "Huber Bau GmbH", kind = "company", postalCode = "83022" });
        (hit.Checked, hit.Message, hit.CanImport).ShouldBe((true, null, true));
        var candidate = hit.Candidates.ShouldHaveSingleItem();
        (candidate.Contact.Id, candidate.Contact.CustomerNumber, candidate.Contact.City).ShouldBe((huber, "10001", "Rosenheim"));
        candidate.Reasons.ShouldContain("gleicher Name und PLZ");
        candidate.Contact.LinkedContactId.ShouldBeNull();

        // Der Kontakt gehört schon zu einem EstrichONE-Kunden: Hinweis auf diesen statt zweiter Übernahme.
        var taken = (await SendAsync<CheckLexwareContactResponse>(HttpMethod.Post, check, new { name = "Lang", email = "INFO@lang.example" })).Candidates.ShouldHaveSingleItem();
        (taken.Contact.LinkedContactId, taken.Contact.LinkedContactName, taken.Strong).ShouldBe((existing, "Lang Zimmerei", true));

        // Kein Treffer, zu wenig Eingabe.
        (await SendAsync<CheckLexwareContactResponse>(HttpMethod.Post, check, new { name = "Malerbetrieb Kern" })).Candidates.ShouldBeEmpty();
        (await SendAsync<CheckLexwareContactResponse>(HttpMethod.Post, check, new { name = "H" })).Candidates.ShouldBeEmpty();
        _lexware.Calls.ShouldAllBe(c => c.Method == "GET");

        // Ohne das Recht, Belege zuzuordnen: Warnung ja, Übernahme nein. Ohne das Recht, Kunden zu bearbeiten: kein Zugriff.
        using var office = await ClientWithAsync("kundenpflege", "contacts.read", "contacts.edit");
        (await SendAsync<CheckLexwareContactResponse>(office, HttpMethod.Post, check, new { name = "Huber Bau GmbH" })).CanImport.ShouldBeFalse();
        using var reader = await ClientWithAsync("nurlesen", "contacts.read", "invoices.read");
        using (var denied = await reader.PostAsJsonAsync(check, new { name = "Huber Bau GmbH" }, Ct))
        {
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        // Übernehmen legt den Kunden mit den Lexware-Daten an, verknüpft ihn und schaltet den Abgleich ein.
        var imported = await SendAsync<ImportLexwareContactResponse>(HttpMethod.Post, $"/api/v1/lexware/contacts/{huber}/import", new { createAnyway = false });
        imported.Imported.ShouldBeTrue();
        var created = await GetCustomerAsync(imported.ContactId!.Value);
        (created["name"]!.GetValue<string>(), created["lexwareSync"]!.GetValue<bool>()).ShouldBe(("HUBER BAU GMBH & Co. KG", true));
        (await StateAsync(imported.ContactId.Value)).Link!.LexwareContactId.ShouldBe(huber);

        // Lexware Office nicht erreichbar: Hinweis statt Fehler, das Anlegen bleibt möglich.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<LexwareService>().ForgetContacts();
        }

        _lexware.Raw["/v1/contacts"] = (503, "");
        var down = await SendAsync<CheckLexwareContactResponse>(HttpMethod.Post, check, new { name = "Huber Bau GmbH" });
        (down.Checked, down.Message, down.Candidates.Count).ShouldBe((false, "Die Prüfung in Lexware Office ist derzeit nicht möglich.", 0));
        await CustomerAsync("Huber Bau Zweitanlage");
    }

    // ---------- Lexware-Daten in der Bauakte ----------

    [Fact]
    public async Task Bauakte_ShowsLexwareContactData_SyncState_AndVoucherSummary_WithoutCallingLexware()
    {
        var lexContact = _lexware.AddContact("HUBER BAU GMBH", email: "rechnung@huber.example", street: "Postfach 7", zip: "83001", city: "Rosenheim");
        var customer = await CustomerAsync("Huber Bau GmbH", email: "info@huber.example", street: "Postfach 7", postalCode: "83001", city: "Rosenheim");
        var unlinked = await CustomerAsync("Lang Zimmerei");
        async Task<Guid> BauakteAsync(Guid customerId)
        {
            var id = Guid.CreateVersion7();
            await SendAsync<JsonObject>(HttpMethod.Post, "/api/v1/bauakten",
                new { id, title = "EFH", customerId, street = "Am Anger 7", postalCode = "83022", city = "Rosenheim" }, HttpStatusCode.Created);
            return id;
        }

        var bauakte = await BauakteAsync(customer);
        var other = await BauakteAsync(unlinked);

        // Ohne Verknüpfung: kein Kontaktblock.
        (await SendAsync<BauakteLexwareResponse>(HttpMethod.Get, $"/api/v1/bauakten/{other}/lexware")).Contact.ShouldBeNull();
        (await SendAsync<BauakteLexwareResponse>(HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware")).Contact.ShouldBeNull();

        await LinkAsync(customer, lexContact);
        foreach (var (type, status, number, due, gross) in new[]
                 {
                     ("invoice", "open", "RE1", "2099-01-01", 10591.00m), ("invoice", "open", "RE2", "2020-01-31", 5176.50m),
                     ("invoice", "paid", "RE3", "2026-01-31", 1190m), ("invoice", "draft", (string?)null, "2026-01-31", 99m),
                     ("quotation", "accepted", "AG1", "2026-01-31", 5355m), ("creditnote", "voided", "GS1", "2026-01-31", 50m),
                 })
        {
            var id = _lexware.AddVoucher(type, status, number, "2026-01-10", due, gross, gross, lexContact, "HUBER BAU GMBH");
            await SendAsync<LexwareVoucherResponse>(HttpMethod.Post, $"/api/v1/bauakten/{bauakte}/lexware/vouchers/assign", new { lexwareId = id, type },
                HttpStatusCode.Created);
        }

        var calls = _lexware.Calls.Count;
        var view = await SendAsync<BauakteLexwareResponse>(HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware");
        _lexware.Calls.Count.ShouldBe(calls); // Kontaktdaten und Summen kommen aus dem gespeicherten Stand

        var contact = view.Contact.ShouldNotBeNull();
        (contact.LexwareContactId, contact.Name, contact.Number).ShouldBe((lexContact, "HUBER BAU GMBH", "10001"));
        contact.Url.ShouldBe($"https://app.lexware.de/permalink/contacts/view/{lexContact}");
        (contact.Street, contact.PostalCode, contact.City, contact.Email).ShouldBe(("Postfach 7", "83001", "Rosenheim", "rechnung@huber.example"));
        contact.DifferingFields.ShouldBe(["name", "email"]);
        contact.Sync.State.ShouldBe("deviations");
        contact.Sync.SyncedAt.ShouldNotBeNull();
        view.Summary.ShouldBe("4 Rechnungen: offen 15.767,50 €, davon überfällig 5.176,50 €, bezahlt 1.190,00 €, 1 im Entwurf. 1 Angebot: angenommen 5.355,00 €. 1 weiterer Beleg");

        // Nach der Entscheidung: keine Abweichung mehr, die Lexware-Werte entsprechen dem Kunden.
        await SendAsync<CustomerLexwareResponse>(HttpMethod.Post, $"/api/v1/lexware/customers/{customer}/sync/resolve", new { source = "lexware" });
        var after = (await SendAsync<BauakteLexwareResponse>(HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware")).Contact!;
        (after.Sync.State, after.DifferingFields.Count, after.Email).ShouldBe(("synced", 0, "rechnung@huber.example"));
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
            shown.Headers.GetValues("X-Frame-Options").ShouldBe(["SAMEORIGIN"]); // Vorschau im Rahmen derselben Herkunft
            (await shown.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(FakeLexware.Pdf(invoice));
        }

        // An der Bauakte: Herunterladen; kurz zwischengespeichert, kein zweiter Abruf bei Lexware.
        using (var download = await _admin.GetAsync(new Uri($"/api/v1/bauakten/{bauakte}/lexware/vouchers/{assigned.Id}/document?download=true", UriKind.Relative), Ct))
        {
            download.StatusCode.ShouldBe(HttpStatusCode.OK);
            download.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");
            download.Content.Headers.ContentDisposition.FileName.ShouldBe("Rechnung-RE1042.pdf");
            download.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
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
