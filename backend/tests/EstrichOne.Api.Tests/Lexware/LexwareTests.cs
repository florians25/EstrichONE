using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Audit;
using EstrichOne.Api.Modules.Lexware;
using EstrichOne.Api.Modules.Organization;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Lexware;

/// <summary>
/// Lexware-Office-Anbindung gegen eine nachgebildete API (gefälschter <see cref="HttpMessageHandler"/>, kein echter Aufruf):
/// Modulschalter, Betrieb ohne Schlüssel, verschlüsselter Schlüssel, Verbindungstest, Rechte, Kundenverknüpfung mit
/// beidseitiger Dublettenprüfung, Abgleichübersicht, Anzeige von Rechnungen und Angeboten an Kunde und Bauakte (nur lesend),
/// Statusabgleich und Zuordnung zu Bauakten. Kein Test und kein Endpunkt legt einen Beleg in Lexware Office an.
/// </summary>
public sealed class LexwareTests(PostgresFixture postgres) : IAsyncLifetime
{
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
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _factory.DisposeAsync();
        _lexware.Dispose();
    }

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string path, object? body = null,
        HttpStatusCode expected = HttpStatusCode.OK)
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

    private Task<JsonObject> EnableAsync(bool on = true) =>
        SendAsync<JsonObject>(_admin, HttpMethod.Put, "/api/v1/settings/modules", new { modules = new Dictionary<string, bool> { ["lexware"] = on } });

    private async Task ConfigureAsync(string key = FakeLexware.Key)
    {
        await EnableAsync();
        (await SendAsync<LexwareSettingsResponse>(_admin, HttpMethod.Put, "/api/v1/settings/integrations/lexware", new { apiKey = key })).HasApiKey.ShouldBeTrue();
    }

    private async Task<Guid> CustomerAsync(string name, string kind = "company", string? email = null, string? phone = null, string? street = null,
        string? postalCode = null, string? city = null)
    {
        var id = Guid.CreateVersion7();
        await SendAsync<JsonObject>(_admin, HttpMethod.Post, "/api/v1/contacts", new { id, kind, name, email, phone, street, postalCode, city },
            HttpStatusCode.Created);
        return id;
    }

    private async Task<Guid> BauakteAsync(string title, Guid? customerId, object? project = null)
    {
        var id = Guid.CreateVersion7();
        await SendAsync<JsonObject>(_admin, HttpMethod.Post, "/api/v1/bauakten",
            new { id, title, customerId, street = "Am Anger 7", postalCode = "83022", city = "Rosenheim", project }, HttpStatusCode.Created);
        return id;
    }

    private async Task<T> DbAsync<T>(Func<EstrichOneDbContext, Task<T>> query)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>());
    }

    // ---------- Modul und Einrichtung ----------

    [Fact]
    public async Task ModuleOff_EndpointsAreInactive_ButSettingsStayReachable()
    {
        var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var status = await SendAsync<LexwareStatusResponse>(office, HttpMethod.Get, "/api/v1/lexware/status");
        status.Enabled.ShouldBeFalse();
        status.Configured.ShouldBeFalse();
        status.CanRead.ShouldBeTrue();
        status.CanAssign.ShouldBeTrue();
        status.CanManage.ShouldBeFalse();

        var bauakte = await BauakteAsync("EFH ohne Lexware", null);
        (await ProblemAsync(office, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware", HttpStatusCode.Conflict)).ShouldStartWith("MODULE_DISABLED");
        (await ProblemAsync(office, HttpMethod.Post, $"/api/v1/bauakten/{bauakte}/lexware/vouchers/assign", HttpStatusCode.Conflict,
            new { lexwareId = "x", type = "invoice" })).ShouldStartWith("MODULE_DISABLED");
        (await ProblemAsync(_admin, HttpMethod.Get, "/api/v1/lexware/contacts", HttpStatusCode.Conflict)).ShouldContain("LEXWARE_NOT_CONFIGURED");

        // Erst einrichten, dann einschalten: Einstellungen und Verbindungstest gehen auch bei abgeschaltetem Modul.
        var settings = await SendAsync<LexwareSettingsResponse>(_admin, HttpMethod.Get, "/api/v1/settings/integrations/lexware");
        settings.ModuleEnabled.ShouldBeFalse();
        settings.HasApiKey.ShouldBeFalse();
        settings.BaseUrl.ShouldBe("https://api.lexware.io");
        settings.KeyHelpUrl.ShouldBe("https://app.lexware.de/addons/public-api");
        (await ProblemAsync(_admin, HttpMethod.Post, "/api/v1/settings/integrations/lexware/test", HttpStatusCode.Conflict)).ShouldStartWith("LEXWARE_NOT_CONFIGURED");
        _lexware.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task ModuleOn_WithoutKey_GivesClearMessages_NeverServerErrors()
    {
        await EnableAsync();
        var customer = await CustomerAsync("Huber Bau GmbH");
        var bauakte = await BauakteAsync("EFH Huber", customer);

        var list = await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware");
        list.Configured.ShouldBeFalse();
        list.CanAssign.ShouldBeFalse();
        list.Vouchers.ShouldBeEmpty();
        list.Customer!.LexwareContactId.ShouldBeNull();

        foreach (var (method, path, body) in new (HttpMethod, string, object?)[]
                 {
                     (HttpMethod.Post, $"/api/v1/bauakten/{bauakte}/lexware/vouchers/assign", new { lexwareId = "x", type = "invoice" }),
                     (HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware/candidates?number=RE1", null),
                     (HttpMethod.Get, $"/api/v1/lexware/customers/{customer}/candidates", null),
                     (HttpMethod.Post, $"/api/v1/lexware/customers/{customer}/create", new { }),
                     (HttpMethod.Get, "/api/v1/lexware/contacts", null),
                     (HttpMethod.Get, "/api/v1/settings/integrations/lexware/reconciliation", null),
                 })
        {
            var problem = await ProblemAsync(_admin, method, path, HttpStatusCode.Conflict, body);
            problem.ShouldStartWith("LEXWARE_NOT_CONFIGURED");
            problem.ShouldContain("API-Schlüssel");
        }

        _lexware.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Settings_KeyEncryptedAndNeverReturned_ConnectionTestShowsCompany()
    {
        await EnableAsync();
        using var raw = await _admin.PutAsJsonAsync("/api/v1/settings/integrations/lexware", new { apiKey = FakeLexware.Key, newCustomersSync = false }, Ct);
        var text = await raw.Content.ReadAsStringAsync(Ct);
        raw.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        text.ShouldNotContain(FakeLexware.Key);
        text.ShouldContain("\"hasApiKey\":true");

        var stored = await DbAsync(db => db.Set<LexwareSettingsRow>().AsNoTracking().SingleAsync(Ct));
        stored.ProtectedApiKey.ShouldNotBeNull();
        stored.ProtectedApiKey.ShouldNotContain(FakeLexware.Key);
        stored.NewCustomersSync.ShouldBeFalse();

        var test = await SendAsync<LexwareTestResponse>(_admin, HttpMethod.Post, "/api/v1/settings/integrations/lexware/test");
        test.Ok.ShouldBeTrue();
        test.OrganizationName.ShouldBe("Estrichbau Muster GmbH");
        _lexware.Calls.ShouldHaveSingleItem().Path.ShouldBe("/v1/profile");

        // Speichern ohne Schlüssel lässt den gespeicherten stehen.
        var kept = await SendAsync<LexwareSettingsResponse>(_admin, HttpMethod.Put, "/api/v1/settings/integrations/lexware", new { });
        kept.HasApiKey.ShouldBeTrue();
        kept.NewCustomersSync.ShouldBeFalse();
        kept.OrganizationName.ShouldBe("Estrichbau Muster GmbH");

        // Falscher Schlüssel: verständliche Meldung statt Fehlerstatus.
        await SendAsync<LexwareSettingsResponse>(_admin, HttpMethod.Put, "/api/v1/settings/integrations/lexware", new { apiKey = "falscher-schluessel" });
        var failed = await SendAsync<LexwareTestResponse>(_admin, HttpMethod.Post, "/api/v1/settings/integrations/lexware/test");
        failed.Ok.ShouldBeFalse();
        failed.Error!.ShouldContain("API-Schlüssel wurde von Lexware Office abgelehnt");

        // Entfernen.
        var cleared = await SendAsync<LexwareSettingsResponse>(_admin, HttpMethod.Put, "/api/v1/settings/integrations/lexware", new { clearApiKey = true });
        cleared.HasApiKey.ShouldBeFalse();

        // Weder Schlüssel noch verschlüsselter Wert im Protokoll.
        var audit = await DbAsync(db => db.Set<AuditEvent>().AsNoTracking().Where(e => e.Action == "lexware.settings_changed").ToListAsync(Ct));
        audit.Count.ShouldBe(4);
        System.Text.Json.JsonSerializer.Serialize(audit).ShouldNotContain("schluessel");
    }

    [Fact]
    public async Task BaseUrl_OnlyLexwareHostsAreAccepted()
    {
        (await ProblemAsync(_admin, HttpMethod.Put, "/api/v1/settings/integrations/lexware", HttpStatusCode.UnprocessableEntity,
            new { apiKey = FakeLexware.Key, baseUrl = "https://evil.example.org" })).ShouldStartWith("LEXWARE_INVALID_BASE_URL");
        (await ProblemAsync(_admin, HttpMethod.Put, "/api/v1/settings/integrations/lexware", HttpStatusCode.UnprocessableEntity,
            new { baseUrl = "http://api.lexware.io" })).ShouldStartWith("LEXWARE_INVALID_BASE_URL");
        (await ProblemAsync(_admin, HttpMethod.Put, "/api/v1/settings/integrations/lexware", HttpStatusCode.UnprocessableEntity,
            new { baseUrl = "https://169.254.169.254/latest" })).ShouldStartWith("LEXWARE_INVALID_BASE_URL");
        (await DbAsync(db => db.Set<LexwareSettingsRow>().CountAsync(Ct))).ShouldBe(0);

        var legacy = await SendAsync<LexwareSettingsResponse>(_admin, HttpMethod.Put, "/api/v1/settings/integrations/lexware",
            new { apiKey = FakeLexware.Key, baseUrl = "https://api.lexoffice.io/" });
        legacy.BaseUrl.ShouldBe("https://api.lexoffice.io");
        legacy.CustomBaseUrl.ShouldBeTrue();
        (await SendAsync<LexwareTestResponse>(_admin, HttpMethod.Post, "/api/v1/settings/integrations/lexware/test")).Ok.ShouldBeTrue();
    }

    [Fact]
    public async Task Permissions_AreEnforced()
    {
        await ConfigureAsync();
        var customer = await CustomerAsync("Huber Bau GmbH");
        var bauakte = await BauakteAsync("EFH Huber", customer);
        var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);

        foreach (var path in new[]
                 {
                     $"/api/v1/bauakten/{bauakte}/lexware", $"/api/v1/bauakten/{bauakte}/lexware/candidates", $"/api/v1/lexware/customers/{customer}/vouchers", $"/api/v1/lexware/customers/{customer}",
                     $"/api/v1/lexware/customers/{customer}/candidates", "/api/v1/lexware/contacts", "/api/v1/settings/integrations/lexware",
                 })
        {
            using var response = await worker.GetAsync(new Uri(path, UriKind.Relative), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
        }

        using var assign = await worker.PostAsJsonAsync($"/api/v1/bauakten/{bauakte}/lexware/vouchers/assign", new { lexwareId = "x", type = "invoice" }, Ct);
        assign.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Einen Beleg in Lexware Office anzulegen ist für niemanden möglich: Die Endpunkte der Beleganlage gibt es nicht.
        foreach (var path in new[] { $"/api/v1/bauakten/{bauakte}/lexware/invoices", $"/api/v1/bauakten/{bauakte}/lexware/vouchers", $"/api/v1/bauakten/{bauakte}/lexware/quotations" })
        {
            using var none = await _admin.PostAsJsonAsync(path, new { lines = new[] { new { kind = "custom", name = "Estrich", quantity = 1, unitPrice = 1 } } }, Ct);
            none.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        }

        _lexware.Calls.ShouldNotContain(c => c.Method != "GET");

        // Büro: Rechnungen ja, Einstellungen und Abgleichübersicht nein.
        (await SendAsync<BauakteLexwareResponse>(office, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware")).CanAssign.ShouldBeTrue();
        foreach (var path in new[] { "/api/v1/settings/integrations/lexware", "/api/v1/settings/integrations/lexware/reconciliation" })
        {
            using var response = await office.GetAsync(new Uri(path, UriKind.Relative), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
        }

        using var anonymous = _factory.CreateClient();
        using var unauthenticated = await anonymous.GetAsync(new Uri("/api/v1/lexware/status", UriKind.Relative), Ct);
        unauthenticated.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------- Kunden ----------

    [Fact]
    public async Task CreateContact_AlwaysChecksDuplicatesFirst_ThenLinksOrCreatesDeliberately()
    {
        await ConfigureAsync();
        var existing = _lexware.AddContact("HUBER BAU GMBH & Co. KG", email: "buero@huber-bau.de", zip: "83022", street: "Am Anger 7", city: "Rosenheim");
        _lexware.AddContact("Zimmerei Lang", zip: "83022");
        var customer = await CustomerAsync("Huber Bau GmbH", email: "Buero@Huber-Bau.de", phone: "08031 12345", street: "Am Anger 7", postalCode: "83022",
            city: "Rosenheim");

        // Ohne Bestätigung: nichts angelegt, Treffer mit Gründen.
        var check = await SendAsync<CreateLexwareContactResponse>(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{customer}/create", new { });
        check.Created.ShouldBeFalse();
        var candidate = check.Candidates.ShouldHaveSingleItem();
        candidate.Contact.Id.ShouldBe(existing);
        candidate.Strong.ShouldBeTrue();
        candidate.Reasons.ShouldBe([LexwareMatching.SameEmail, LexwareMatching.SameNameAndPostalCode, LexwareMatching.SameAddress]);
        _lexware.CallsTo("POST", "/v1/contacts").ShouldBeEmpty();

        // Dieselben Treffer in der Vorschau; die Kontaktliste wird dafür nicht erneut geladen (Cache).
        var preview = await SendAsync<LexwareCandidatesResponse>(_admin, HttpMethod.Get, $"/api/v1/lexware/customers/{customer}/candidates");
        preview.Candidates.ShouldHaveSingleItem().Contact.Id.ShouldBe(existing);
        _lexware.CallsTo("GET", "/v1/contacts").Count().ShouldBe(1);

        // Gezielte Suche über den Namensfilter der API.
        var search = await SendAsync<LexwareCandidatesResponse>(_admin, HttpMethod.Get, $"/api/v1/lexware/customers/{customer}/candidates?q=Lang");
        search.Candidates.ShouldHaveSingleItem().Contact.Name.ShouldBe("Zimmerei Lang");
        search.Candidates[0].Score.ShouldBe(0);
        _lexware.Calls.Last().Query.ShouldContain("name=Lang");
        _lexware.Calls.Last().Query.ShouldContain("customer=true");

        // Mit dem Treffer verknüpfen.
        var linked = await SendAsync<CustomerLexwareResponse>(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{customer}/link",
            new { lexwareContactId = existing });
        linked.Link!.LexwareContactId.ShouldBe(existing);
        linked.Link.Number.ShouldBe("10001");
        linked.Link.Url.ShouldBe($"https://app.lexware.de/permalink/contacts/view/{existing}");
        linked.Link.Origin.ShouldBe(LexwareLinkOrigin.Linked);

        // Ein Lexware-Kontakt gehört zu genau einem Kunden und umgekehrt.
        var twin = await CustomerAsync("Huber Bau Zweigstelle");
        var conflict = await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{twin}/link", HttpStatusCode.Conflict,
            new { lexwareContactId = existing });
        conflict.ShouldStartWith("LEXWARE_ALREADY_LINKED");
        conflict.ShouldContain("bereits mit dem Kunden „Huber Bau GmbH“ verknüpft");
        (await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{customer}/create", HttpStatusCode.Conflict, new { createAnyway = true }))
            .ShouldStartWith("LEXWARE_ALREADY_LINKED");

        // Bewusst neu anlegen trotz Treffer (ähnlicher Name): Rolle Kunde, Firma, Anschrift, E-Mail, Telefon.
        var deliberate = await SendAsync<CreateLexwareContactResponse>(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{twin}/create", new { });
        deliberate.Created.ShouldBeFalse();
        deliberate.Candidates.ShouldHaveSingleItem().Contact.LinkedContactName.ShouldBe("Huber Bau GmbH");
        var created = await SendAsync<CreateLexwareContactResponse>(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{twin}/create",
            new { createAnyway = true });
        created.Created.ShouldBeTrue();
        created.Link!.Origin.ShouldBe(LexwareLinkOrigin.CreatedInLexware);
        created.Link.Number.ShouldBe("10003");

        // Verknüpfung lösen: in Lexware bleibt alles, danach wieder frei.
        using var unlink = await _admin.DeleteAsync(new Uri($"/api/v1/lexware/customers/{customer}/link", UriKind.Relative), Ct);
        unlink.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync<CustomerLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/lexware/customers/{customer}")).Link.ShouldBeNull();
        _lexware.Contacts.ContainsKey(existing).ShouldBeTrue();
        _lexware.Calls.ShouldNotContain(c => c.Method == "DELETE" || c.Method == "PUT");

        var actions = await DbAsync(db => db.Set<AuditEvent>().AsNoTracking().Where(e => e.Action.StartsWith("lexware.contact")).Select(e => e.Action).ToListAsync(Ct));
        actions.ShouldBe(["lexware.contact_linked", "lexware.contact_created", "lexware.contact_unlinked"], ignoreOrder: true);
    }

    [Fact]
    public async Task CreateContact_WithoutDuplicates_SendsCustomerPayload()
    {
        await ConfigureAsync();
        var person = Guid.CreateVersion7();
        await SendAsync<JsonObject>(_admin, HttpMethod.Post, "/api/v1/contacts", new
        {
            id = person, kind = "person", name = "Anna Obermaier", salutation = "Frau", street = "Kirchweg 3", postalCode = "83024", city = "Rosenheim",
            phones = new[] { new { kind = "mobile", number = "0171 5550001" }, new { kind = "landline", number = "08031 5550002" } },
            emails = new[] { new { address = "anna@obermaier.example" } },
        }, HttpStatusCode.Created);

        var created = await SendAsync<CreateLexwareContactResponse>(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{person}/create", new { });
        created.Created.ShouldBeTrue();
        var body = JsonNode.Parse(_lexware.CallsTo("POST", "/v1/contacts").ShouldHaveSingleItem().Body!)!;
        body["version"]!.GetValue<int>().ShouldBe(0);
        body["roles"]!.ToJsonString().ShouldBe("""{"customer":{}}""");
        body["person"]!.ToJsonString().ShouldBe("""{"lastName":"Obermaier","salutation":"Frau","firstName":"Anna"}""");
        body["addresses"]!["billing"]![0]!.ToJsonString().ShouldBe("""{"countryCode":"DE","street":"Kirchweg 3","zip":"83024","city":"Rosenheim"}""");
        body["emailAddresses"]!["business"]![0]!.GetValue<string>().ShouldBe("anna@obermaier.example");
        body["phoneNumbers"]!["business"]![0]!.GetValue<string>().ShouldBe("08031 5550002");
        body["phoneNumbers"]!["mobile"]![0]!.GetValue<string>().ShouldBe("0171 5550001");
        body["note"]!.GetValue<string>().ShouldStartWith("EstrichONE Kundennummer");

        // Der neue Kontakt ist sofort bekannt: ein zweiter gleicher Kunde findet ihn als Dublette, ohne die Liste neu zu laden.
        var twin = await CustomerAsync("Obermaier Anna", kind: "person", email: "anna@obermaier.example");
        var check = await SendAsync<CreateLexwareContactResponse>(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{twin}/create", new { });
        check.Created.ShouldBeFalse();
        check.Candidates.ShouldHaveSingleItem().Contact.LinkedContactId.ShouldBe(person);
        _lexware.CallsTo("POST", "/v1/contacts").Count().ShouldBe(1);
    }

    [Fact]
    public async Task ImportFromLexware_ChecksDuplicatesInEstrichOne_ThenCreatesCustomerWithNumber()
    {
        await ConfigureAsync();
        var known = _lexware.AddContact("Schmid Estrich e.K.", email: "info@schmid-estrich.example", zip: "80331", city: "München");
        var fresh = _lexware.AddContact("Hans Gruber", company: false, phone: "+49 89 4441122", street: "Lindenstr. 4", zip: "80335", city: "München");
        _lexware.AddContact("Nur Lieferant GmbH", customer: false);
        for (var i = 0; i < 4; i++)
        {
            _lexware.AddContact($"Reihenhaus {i}");
        }

        var local = await CustomerAsync("Estrich Schmid", email: "INFO@schmid-estrich.example", postalCode: "80331");

        // Liste seitenweise mit Suche; Lieferanten ohne Kundenrolle erscheinen nicht.
        var page = await SendAsync<LexwareContactListResponse>(_admin, HttpMethod.Get, "/api/v1/lexware/contacts?size=4&page=1");
        page.Total.ShouldBe(6);
        page.Items.Count.ShouldBe(2);
        var found = await SendAsync<LexwareContactListResponse>(_admin, HttpMethod.Get, "/api/v1/lexware/contacts?q=gruber münchen");
        found.Items.ShouldHaveSingleItem().Id.ShouldBe(fresh);
        _lexware.CallsTo("GET", "/v1/contacts").Count().ShouldBe(1);

        // Treffer in EstrichONE: nicht anlegen, sondern zur Entscheidung zurückgeben.
        var candidates = await SendAsync<List<LexwareCustomerCandidate>>(_admin, HttpMethod.Get, $"/api/v1/lexware/contacts/{known}/candidates");
        candidates.ShouldHaveSingleItem().Customer.Id.ShouldBe(local);
        var blocked = await SendAsync<ImportLexwareContactResponse>(_admin, HttpMethod.Post, $"/api/v1/lexware/contacts/{known}/import", new { });
        blocked.Imported.ShouldBeFalse();
        blocked.Candidates.ShouldHaveSingleItem().Reasons.ShouldBe([LexwareMatching.SameEmail, LexwareMatching.SameNameAndPostalCode]);
        (await DbAsync(db => db.Set<Modules.Contacts.Exports.Contact>().CountAsync(Ct))).ShouldBe(1);

        // Entscheidung „verknüpfen“.
        await SendAsync<CustomerLexwareResponse>(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{local}/link", new { lexwareContactId = known });

        // Ohne Treffer: Kunde mit Nummer aus dem Nummernkreis, verknüpft.
        var imported = await SendAsync<ImportLexwareContactResponse>(_admin, HttpMethod.Post, $"/api/v1/lexware/contacts/{fresh}/import", new { });
        imported.Imported.ShouldBeTrue();
        imported.CustomerNumber.ShouldNotBeNullOrWhiteSpace();
        var contact = await SendAsync<JsonObject>(_admin, HttpMethod.Get, $"/api/v1/contacts/{imported.ContactId}");
        contact["kind"]!.GetValue<string>().ShouldBe("person");
        contact["name"]!.GetValue<string>().ShouldBe("Hans Gruber");
        contact["salutation"]!.GetValue<string>().ShouldBe("Herr");
        contact["street"]!.GetValue<string>().ShouldBe("Lindenstr. 4");
        contact["phone"]!.GetValue<string>().ShouldBe("+49 89 4441122");
        var link = await SendAsync<CustomerLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/lexware/customers/{imported.ContactId}");
        link.Link!.Origin.ShouldBe(LexwareLinkOrigin.ImportedFromLexware);

        // Zweimal übernehmen geht nicht.
        (await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/lexware/contacts/{fresh}/import", HttpStatusCode.Conflict, new { createAnyway = true }))
            .ShouldStartWith("LEXWARE_ALREADY_LINKED");
        var unlinked = await SendAsync<LexwareContactListResponse>(_admin, HttpMethod.Get, "/api/v1/lexware/contacts?unlinked=true");
        unlinked.Total.ShouldBe(4);

        // Ohne das Recht, Kunden zu bearbeiten, wird nichts übernommen.
        await using var scope = _factory.Services.CreateAsyncScope();
        var role = Modules.Access.Role.Create(Guid.CreateVersion7(), "Nur Rechnungen", "", isSystem: false, ["invoices.read", "invoices.assign", "contacts.read"]);
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        db.Add(role);
        await db.SaveChangesAsync(Ct);
        var billing = await _factory.CreateClientAsAsync("rechnung", role.Id);
        var other = _lexware.Contacts.Values.First(c => c["company"]?["name"]?.GetValue<string>() == "Reihenhaus 0")["id"]!.GetValue<string>();
        (await ProblemAsync(billing, HttpMethod.Post, $"/api/v1/lexware/contacts/{other}/import", HttpStatusCode.Forbidden, new { })).ShouldStartWith("PERMISSION_DENIED");
    }

    [Fact]
    public async Task Reconciliation_CountsListsAndLinksOnlyUniqueMatchesAfterPreview()
    {
        await ConfigureAsync();
        var huber = await CustomerAsync("Huber Bau GmbH", email: "buero@huber-bau.example");
        var lang = await CustomerAsync("Zimmerei Lang", email: "info@lang.example");
        var schmidA = await CustomerAsync("Estrich Schmid", postalCode: "80331");
        await CustomerAsync("Estrich Schmid e.K.", postalCode: "80331"); // Dublette innerhalb von EstrichONE
        var alone = await CustomerAsync("Nur in EstrichONE");
        var linkedLocal = await CustomerAsync("Bereits verknüpft GmbH");

        var lexHuber = _lexware.AddContact("Huber Bau", email: "buero@huber-bau.example");
        var lexLang1 = _lexware.AddContact("Lang Holzbau", email: "info@lang.example");
        var lexLang2 = _lexware.AddContact("Zimmerei Lang GmbH", email: "info@lang.example"); // zwei Gegenstücke: nicht eindeutig
        var lexSchmid = _lexware.AddContact("Schmid Estrich", zip: "80331");
        var lexOnly = _lexware.AddContact("Nur in Lexware AG");
        var lexLinked = _lexware.AddContact("Bereits verknüpft GmbH");
        await SendAsync<CustomerLexwareResponse>(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{linkedLocal}/link", new { lexwareContactId = lexLinked });

        var overview = await SendAsync<LexwareReconciliationResponse>(_admin, HttpMethod.Get, "/api/v1/settings/integrations/lexware/reconciliation");
        overview.Linked.ShouldBe(1);
        overview.OnlyEstrichOne.ShouldBe(5);
        overview.OnlyLexware.ShouldBe(5);
        overview.LinkedItems.ShouldHaveSingleItem().Lexware!.Id.ShouldBe(lexLinked);
        overview.OnlyEstrichOneItems.Select(c => c.Id).ShouldContain(alone);
        overview.OnlyLexwareItems.Select(c => c.Id).ShouldContain(lexOnly);
        overview.UniqueMatches.ShouldBe(1);
        var unique = overview.Suggestions.Where(s => s.Unique).ShouldHaveSingleItem();
        (unique.Customer.Id, unique.Lexware.Id).ShouldBe((huber, lexHuber));
        overview.Suggestions.Where(s => s.Customer.Id == lang).Select(s => s.Lexware.Id).ShouldBe([lexLang1, lexLang2], ignoreOrder: true);
        overview.Suggestions.Where(s => s.Customer.Id == lang).ShouldAllBe(s => !s.Unique);
        overview.Suggestions.ShouldContain(s => s.Customer.Id == schmidA && s.Lexware.Id == lexSchmid && !s.Unique);
        overview.InternalDuplicates.ShouldContain(d => d.System == "estrichOne" && d.Reasons.Contains(LexwareMatching.SameNameAndPostalCode));
        overview.InternalDuplicates.ShouldContain(d => d.System == "lexware" && d.Reasons.Contains(LexwareMatching.SameEmail));
        overview.PossibleDuplicates.ShouldBe(overview.Suggestions.Count + overview.InternalDuplicates.Count);

        // Massenaktion: nur eindeutige Paare; ein untergeschobenes mehrdeutiges Paar wird übersprungen.
        var result = await SendAsync<LinkUniqueLexwareResponse>(_admin, HttpMethod.Post, "/api/v1/settings/integrations/lexware/reconciliation/link-unique", new
        {
            pairs = new object[] { new { contactId = huber, lexwareContactId = lexHuber }, new { contactId = lang, lexwareContactId = lexLang1 } },
        });
        result.Linked.ShouldBe(1);
        result.Skipped.ShouldBe(1);
        var after = await SendAsync<LexwareReconciliationResponse>(_admin, HttpMethod.Get, "/api/v1/settings/integrations/lexware/reconciliation");
        after.Linked.ShouldBe(2);
        after.UniqueMatches.ShouldBe(0);
        (await SendAsync<CustomerLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/lexware/customers/{lang}")).Link.ShouldBeNull();

        // Kontaktliste kurz zwischengespeichert; „Aktualisieren“ lädt neu.
        _lexware.CallsTo("GET", "/v1/contacts").Count(c => c.Query.Contains("page=", StringComparison.Ordinal) && !c.Path.Contains("/v1/contacts/", StringComparison.Ordinal)).ShouldBe(1);
        await SendAsync<LexwareReconciliationResponse>(_admin, HttpMethod.Get, "/api/v1/settings/integrations/lexware/reconciliation?refresh=true");
        _lexware.Calls.Count(c => c.Path == "/v1/contacts" && c.Method == "GET").ShouldBe(2);
        _factory.Clock.Advance(TimeSpan.FromMinutes(6));
        await SendAsync<LexwareReconciliationResponse>(_admin, HttpMethod.Get, "/api/v1/settings/integrations/lexware/reconciliation");
        _lexware.Calls.Count(c => c.Path == "/v1/contacts" && c.Method == "GET").ShouldBe(3);
    }

    [Fact]
    public async Task ContactList_IsLoadedPageByPage()
    {
        await _factory.DisposeAsync();
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
        {
            ["EstrichOne:Lexware:MinRequestInterval"] = "00:00:00", ["EstrichOne:Lexware:ContactPageSize"] = "3",
        }, services => services.AddHttpClient(LexwareClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _lexware));
        _admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        await ConfigureAsync();
        for (var i = 0; i < 8; i++)
        {
            _lexware.AddContact($"Kunde {i}");
        }

        (await SendAsync<LexwareContactListResponse>(_admin, HttpMethod.Get, "/api/v1/lexware/contacts")).Total.ShouldBe(8);
        _lexware.CallsTo("GET", "/v1/contacts").Select(c => c.Query).ShouldBe(
            ["customer=true&page=0&size=3", "customer=true&page=1&size=3", "customer=true&page=2&size=3"]);
    }

    // ---------- „Mit Lexware Office abgleichen“ ----------

    [Fact]
    public async Task SyncFlag_DefaultFollowsModuleAndSetting_AndIsCarriedByRestAndAppSync()
    {
        // Modul aus: neue Kunden bleiben nur in EstrichONE, die Clients sehen keine Vorgabe.
        var before = await CustomerAsync("Vor dem Einschalten GmbH");
        (await SendAsync<JsonObject>(_admin, HttpMethod.Get, $"/api/v1/contacts/{before}"))["lexwareSync"]!.GetValue<bool>().ShouldBeFalse();
        (await SendAsync<ModulesResponse>(_admin, HttpMethod.Get, "/api/v1/settings/modules")).LexwareSyncDefault.ShouldBeFalse();

        // Modul an: Vorgabe aus den Einstellungen (zunächst ja).
        await ConfigureAsync();
        (await SendAsync<ModulesResponse>(_admin, HttpMethod.Get, "/api/v1/settings/modules")).LexwareSyncDefault.ShouldBeTrue();
        var byDefault = await CustomerAsync("Nach dem Einschalten GmbH");
        (await SendAsync<JsonObject>(_admin, HttpMethod.Get, $"/api/v1/contacts/{byDefault}"))["lexwareSync"]!.GetValue<bool>().ShouldBeTrue();
        (await SendAsync<JsonObject>(_admin, HttpMethod.Get, $"/api/v1/contacts/{before}"))["lexwareSync"]!.GetValue<bool>().ShouldBeFalse();

        // Ausdrücklich „einseitiger Kunde“ beim Anlegen; beim Ändern ohne Angabe unverändert, mit Angabe gesetzt.
        var oneSided = Guid.CreateVersion7();
        var created = await SendAsync<JsonObject>(_admin, HttpMethod.Post, "/api/v1/contacts",
            new { id = oneSided, kind = "person", name = "Privat Einseitig", lexwareSync = false }, HttpStatusCode.Created);
        created["lexwareSync"]!.GetValue<bool>().ShouldBeFalse();
        var version = created["version"]!.GetValue<long>();
        var kept = await SendAsync<JsonObject>(_admin, HttpMethod.Put, $"/api/v1/contacts/{oneSided}", new { version, kind = "person", name = "Privat Einseitig", city = "Rosenheim" });
        kept["lexwareSync"]!.GetValue<bool>().ShouldBeFalse();
        var switched = await SendAsync<JsonObject>(_admin, HttpMethod.Put, $"/api/v1/contacts/{oneSided}",
            new { version = kept["version"]!.GetValue<long>(), kind = "person", name = "Privat Einseitig", city = "Rosenheim", lexwareSync = true });
        switched["lexwareSync"]!.GetValue<bool>().ShouldBeTrue();

        // Vorgabe umstellen: neue Kunden bleiben standardmäßig nur in EstrichONE.
        var settings = await SendAsync<LexwareSettingsResponse>(_admin, HttpMethod.Put, "/api/v1/settings/integrations/lexware", new { newCustomersSync = false });
        settings.NewCustomersSync.ShouldBeFalse();
        settings.HasApiKey.ShouldBeTrue();
        (await SendAsync<ModulesResponse>(_admin, HttpMethod.Get, "/api/v1/settings/modules")).LexwareSyncDefault.ShouldBeFalse();
        var off = await CustomerAsync("Vorgabe Aus GmbH");
        (await SendAsync<JsonObject>(_admin, HttpMethod.Get, $"/api/v1/contacts/{off}"))["lexwareSync"]!.GetValue<bool>().ShouldBeFalse();

        // App (Outbox): das Feld wird mitgetragen; ältere Apps ohne Feld bekommen die Vorgabe.
        var withFlag = Guid.CreateVersion7();
        var legacy = Guid.CreateVersion7();
        var results = (await _admin.PushAsync(
            SyncClient.Op("contact.create", withFlag, new { name = "Aus der App mit Schalter", kind = "company", lexwareSync = true }),
            SyncClient.Op("contact.create", legacy, new { name = "Aus einer älteren App" }))).Results;
        results.ShouldAllBe(r => r.Status == SyncOperationStatus.Applied);
        results[0].Entity!["lexwareSync"]!.GetValue<bool>().ShouldBeTrue();
        results[1].Entity!["lexwareSync"]!.GetValue<bool>().ShouldBeFalse();
        var update = (await _admin.PushAsync(SyncClient.Op("contact.update", legacy, new
        {
            changes = new { lexwareSync = true }, @base = new { lexwareSync = false },
        }))).Results.Single();
        update.Status.ShouldBe(SyncOperationStatus.Applied, update.Message);
        update.Entity!["lexwareSync"]!.GetValue<bool>().ShouldBeTrue();
    }

    [Fact]
    public async Task SyncFlagOff_CustomerStaysInEstrichOne_UntilSwitchedOnDeliberately()
    {
        await ConfigureAsync();
        var lexContact = _lexware.AddContact("Huber Bau GmbH", email: "buero@huber-bau.example");
        var customer = Guid.CreateVersion7();
        await SendAsync<JsonObject>(_admin, HttpMethod.Post, "/api/v1/contacts",
            new { id = customer, kind = "company", name = "Huber Bau GmbH", email = "buero@huber-bau.example", lexwareSync = false }, HttpStatusCode.Created);
        var other = await CustomerAsync("Zimmerei Lang");
        var bauakte = await BauakteAsync("EFH Huber", customer);

        // Weder verknüpfen noch anlegen: klare Meldung, kein schreibender Aufruf bei Lexware.
        var status = await SendAsync<CustomerLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/lexware/customers/{customer}");
        status.SyncEnabled.ShouldBeFalse();
        foreach (var (path, body) in new (string, object)[]
                 {
                     ($"/api/v1/lexware/customers/{customer}/link", new { lexwareContactId = lexContact }),
                     ($"/api/v1/lexware/customers/{customer}/create", new { createAnyway = true }),
                 })
        {
            var problem = await ProblemAsync(_admin, HttpMethod.Post, path, HttpStatusCode.Conflict, body);
            problem.ShouldStartWith("LEXWARE_SYNC_DISABLED");
            problem.ShouldContain("bleibt nur in EstrichONE");
        }

        _lexware.Calls.ShouldNotContain(c => c.Method == "POST");
        var view = await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware");
        view.Customer!.SyncEnabled.ShouldBeFalse();

        // Abgleichübersicht: eigene Gruppe „bewusst nur in EstrichONE“, nicht als fehlend und nicht als Dublette vorgeschlagen.
        var overview = await SendAsync<LexwareReconciliationResponse>(_admin, HttpMethod.Get, "/api/v1/settings/integrations/lexware/reconciliation");
        overview.Excluded.ShouldBe(1);
        overview.ExcludedItems!.ShouldHaveSingleItem().Id.ShouldBe(customer);
        overview.OnlyEstrichOneItems.ShouldHaveSingleItem().Id.ShouldBe(other);
        overview.Suggestions.ShouldBeEmpty();
        overview.UniqueMatches.ShouldBe(0);

        // Massenaktion: Abgleich für ausgewählte Kunden einschalten; danach erscheint der eindeutige Treffer. Übertragen wird dabei nichts.
        var enabled = await SendAsync<EnableLexwareSyncResponse>(_admin, HttpMethod.Post,
            "/api/v1/settings/integrations/lexware/reconciliation/enable-sync", new { contactIds = new[] { customer, other } });
        enabled.Changed.ShouldBe(1);
        _lexware.Calls.ShouldNotContain(c => c.Method == "POST");
        var after = await SendAsync<LexwareReconciliationResponse>(_admin, HttpMethod.Get, "/api/v1/settings/integrations/lexware/reconciliation");
        after.Excluded.ShouldBe(0);
        after.Suggestions.ShouldHaveSingleItem().Unique.ShouldBeTrue();

        // Verknüpfen geht jetzt; beim Ausschalten bleibt die Verknüpfung bestehen, bis sie gelöst wird.
        await SendAsync<CustomerLexwareResponse>(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{customer}/link", new { lexwareContactId = lexContact });
        var offAgain = await SendAsync<CustomerLexwareResponse>(_admin, HttpMethod.Put, $"/api/v1/lexware/customers/{customer}/sync", new { enabled = false });
        offAgain.SyncEnabled.ShouldBeFalse();
        offAgain.Link!.LexwareContactId.ShouldBe(lexContact);
        (await SendAsync<CustomerVouchersResponse>(_admin, HttpMethod.Get, $"/api/v1/lexware/customers/{customer}/vouchers")).Linked.ShouldBeTrue();
        var linked = await SendAsync<LexwareReconciliationResponse>(_admin, HttpMethod.Get, "/api/v1/settings/integrations/lexware/reconciliation");
        linked.Linked.ShouldBe(1);
        linked.LinkedItems[0].Customer.SyncEnabled.ShouldBeFalse();
        linked.Excluded.ShouldBe(0);

        // Nach dem Lösen ist der Kunde wieder nur in EstrichONE.
        using var unlink = await _admin.DeleteAsync(new Uri($"/api/v1/lexware/customers/{customer}/link", UriKind.Relative), Ct);
        unlink.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync<CustomerVouchersResponse>(_admin, HttpMethod.Get, $"/api/v1/lexware/customers/{customer}/vouchers")).Linked.ShouldBeFalse();
        var back = await SendAsync<CustomerLexwareResponse>(_admin, HttpMethod.Put, $"/api/v1/lexware/customers/{customer}/sync", new { enabled = true });
        back.SyncEnabled.ShouldBeTrue();
        (await DbAsync(db => db.Set<AuditEvent>().AsNoTracking().Where(e => e.Action.StartsWith("lexware.sync")).Select(e => e.Action).ToListAsync(Ct)))
            .ShouldBe(["lexware.sync_enabled_bulk", "lexware.sync_changed", "lexware.sync_changed"], ignoreOrder: true);
    }

    // ---------- Rechnungen und Angebote (nur lesend) ----------

    [Fact]
    public async Task CustomerVouchers_ShowInvoicesAndQuotationsOfLinkedContact_ReadOnlyWithShortCache()
    {
        await ConfigureAsync();
        var lexContact = _lexware.AddContact("Huber Bau GmbH");
        var customer = await CustomerAsync("Huber Bau GmbH");
        var bauakte = await BauakteAsync("EFH Huber", customer);

        // Nicht verknüpft: leere Liste, kein Aufruf.
        var none = await SendAsync<CustomerVouchersResponse>(_admin, HttpMethod.Get, $"/api/v1/lexware/customers/{customer}/vouchers");
        none.Linked.ShouldBeFalse();
        none.Vouchers.ShouldBeEmpty();
        _lexware.CallsTo("GET", "/v1/voucherlist").ShouldBeEmpty();
        await SendAsync<CustomerLexwareResponse>(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{customer}/link", new { lexwareContactId = lexContact });

        var draft = _lexware.AddVoucher("invoice", "draft", null, "2026-11-20", null, 500m, 595m, lexContact, "Huber Bau GmbH");
        var overdue = _lexware.AddVoucher("invoice", "open", "RE1042", "2026-10-01", "2020-01-31", 1000m, 1190m, lexContact, "Huber Bau GmbH");
        var paid = _lexware.AddVoucher("invoice", "paid", "RE0990", "2026-09-01", "2026-09-15", 2000m, 2380m, lexContact, "Huber Bau GmbH");
        var accepted = _lexware.AddVoucher("quotation", "accepted", "AG0311", "2026-08-01", "2026-08-31", 3000m, 3570m, lexContact, "Huber Bau GmbH");
        var rejected = _lexware.AddVoucher("quotation", "rejected", "AG0290", "2026-07-01", "2026-07-31", 4000m, 4760m, lexContact, "Huber Bau GmbH");
        var credit = _lexware.AddVoucher("creditnote", "voided", "GS0007", "2026-06-01", null, 100m, 119m, lexContact, "Huber Bau GmbH");
        _lexware.AddVoucher("invoice", "open", "RE7777", "2026-11-01", "2099-01-01", 1m, 1.19m, _lexware.AddContact("Fremd GmbH"), "Fremd GmbH");
        await SendAsync<LexwareVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/bauakten/{bauakte}/lexware/vouchers/assign",
            new { lexwareId = paid, type = "invoice" }, HttpStatusCode.Created);

        var list = await SendAsync<CustomerVouchersResponse>(_admin, HttpMethod.Get, $"/api/v1/lexware/customers/{customer}/vouchers");
        list.Linked.ShouldBeTrue();
        list.Vouchers.Select(v => (v.LexwareId, v.Type, v.Status)).ShouldBe(
        [
            (draft, "invoice", "draft"), (overdue, "invoice", "overdue"), (paid, "invoice", "paid"), (accepted, "quotation", "accepted"),
            (rejected, "quotation", "rejected"), (credit, "creditnote", "voided"),
        ]);
        list.Vouchers[1].GrossAmount.ShouldBe(1190m);
        list.Vouchers[1].DueDate.ShouldBe(new DateOnly(2020, 1, 31));
        list.Vouchers[0].Url.ShouldBe($"https://app.lexware.de/permalink/invoices/edit/{draft}");
        list.Vouchers[3].Url.ShouldBe($"https://app.lexware.de/permalink/quotations/view/{accepted}");
        list.Vouchers[5].Url.ShouldBe($"https://app.lexware.de/permalink/credit-notes/view/{credit}");
        list.Vouchers[2].AssignedBauakteId.ShouldBe(bauakte);
        list.Vouchers[0].AssignedBauakteId.ShouldBeNull();
        var query = _lexware.CallsTo("GET", "/v1/voucherlist").ShouldHaveSingleItem().Query;
        query.ShouldContain($"contactId={lexContact}");
        query.ShouldContain("voucherType=invoice,quotation,creditnote,orderconfirmation");

        // Kurzer Cache; „Aktualisieren“ und der Ablauf des Caches laden neu.
        await SendAsync<CustomerVouchersResponse>(_admin, HttpMethod.Get, $"/api/v1/lexware/customers/{customer}/vouchers");
        _lexware.CallsTo("GET", "/v1/voucherlist").Count().ShouldBe(1);
        await SendAsync<CustomerVouchersResponse>(_admin, HttpMethod.Get, $"/api/v1/lexware/customers/{customer}/vouchers?refresh=true");
        _lexware.CallsTo("GET", "/v1/voucherlist").Count().ShouldBe(2);
        _factory.Clock.Advance(TimeSpan.FromMinutes(3));
        await SendAsync<CustomerVouchersResponse>(_admin, HttpMethod.Get, $"/api/v1/lexware/customers/{customer}/vouchers");
        _lexware.CallsTo("GET", "/v1/voucherlist").Count().ShouldBe(3);

        // Lexware lehnt ab bzw. ist nicht erreichbar: verständliche Meldung, kein Serverfehler.
        _lexware.Raw["/v1/voucherlist"] = (402, "{}");
        (await ProblemAsync(_admin, HttpMethod.Get, $"/api/v1/lexware/customers/{customer}/vouchers?refresh=true", HttpStatusCode.FailedDependency))
            .ShouldStartWith("LEXWARE_NO_API_ACCESS");
        _lexware.Raw["/v1/voucherlist"] = (503, "");
        (await ProblemAsync(_admin, HttpMethod.Get, $"/api/v1/lexware/customers/{customer}/vouchers?refresh=true", HttpStatusCode.ServiceUnavailable))
            .ShouldStartWith("LEXWARE_UNAVAILABLE");

        // Über alle Aufrufe hinweg wurde in Lexware nichts geschrieben (auch die Verknüpfung liest den Kontakt nur).
        _lexware.Calls.ShouldAllBe(c => c.Method == "GET");
    }

    [Fact]
    public async Task BauakteVouchers_SuggestByBauakteNumber_AssignAfterConfirmation_StatusSync()
    {
        await ConfigureAsync();
        var lexContact = _lexware.AddContact("Huber Bau GmbH");
        var customer = await CustomerAsync("Huber Bau GmbH");
        var bauakte = await BauakteAsync("EFH Huber, Neubau", customer);
        var number = (await SendAsync<JsonObject>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}"))["number"]!.GetValue<string>();
        await SendAsync<CustomerLexwareResponse>(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{customer}/link", new { lexwareContactId = lexContact });

        var quotation = _lexware.AddVoucher("quotation", "open", "AG0311", "2026-08-01", "2026-08-31", 3000m, 3570m, lexContact, "Huber Bau GmbH",
            title: $"Angebot Estricharbeiten, Bauakte {number}");
        var invoice = _lexware.AddVoucher("invoice", "draft", null, "2026-11-20", null, 4629.25m, 5508.81m, lexContact, "Huber Bau GmbH",
            introduction: $"Bauvorhaben EFH Huber\nBauakte: {number.ToLowerInvariant()}");
        var unrelated = _lexware.AddVoucher("invoice", "paid", "RE0500", "2026-03-01", "2026-03-15", 100m, 119m, lexContact, "Huber Bau GmbH",
            introduction: "Reparatur Garage");

        // Zuordnungsdialog: Belege des Kunden, Vorschläge über die Bauaktennummer zuerst. Zugeordnet wird dabei nichts.
        var candidates = await SendAsync<BauakteVoucherCandidatesResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware/candidates");
        candidates.CustomerLinked.ShouldBeTrue();
        candidates.Vouchers.Select(v => (v.LexwareId, v.Suggested)).ShouldBe([(invoice, true), (quotation, true), (unrelated, false)]);
        (await DbAsync(db => db.Set<LexwareVoucherLink>().CountAsync(Ct))).ShouldBe(0);
        // Die Texte der Belege werden dafür einzeln gelesen und zwischengespeichert.
        var detailCalls = _lexware.Calls.Count(c => c.Path.StartsWith("/v1/invoices/", StringComparison.Ordinal) || c.Path.StartsWith("/v1/quotations/", StringComparison.Ordinal));
        detailCalls.ShouldBe(3);
        await SendAsync<BauakteVoucherCandidatesResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware/candidates");
        _lexware.Calls.Count(c => c.Path.StartsWith("/v1/invoices/", StringComparison.Ordinal) || c.Path.StartsWith("/v1/quotations/", StringComparison.Ordinal))
            .ShouldBe(detailCalls);

        // Nach Bestätigung zuordnen: Rechnung (Entwurf) und Angebot.
        var draft = await SendAsync<LexwareVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/bauakten/{bauakte}/lexware/vouchers/assign",
            new { lexwareId = invoice, type = "invoice" }, HttpStatusCode.Created);
        (draft.Type, draft.Status, draft.NetAmount, draft.GrossAmount, draft.ContactName).ShouldBe(("invoice", "draft", 4629.25m, 5508.81m, "Huber Bau GmbH"));
        draft.Url.ShouldBe($"https://app.lexware.de/permalink/invoices/edit/{invoice}");
        var offer = await SendAsync<LexwareVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/bauakten/{bauakte}/lexware/vouchers/assign",
            new { lexwareId = quotation, type = "quotation" }, HttpStatusCode.Created);
        (offer.Type, offer.Status, offer.Number, offer.DueDate).ShouldBe(("quotation", "open", "AG0311", new DateOnly(2026, 8, 31)));
        (await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/bauakten/{bauakte}/lexware/vouchers/assign", HttpStatusCode.UnprocessableEntity,
            new { lexwareId = unrelated, type = "salesvoucher" })).ShouldStartWith("LEXWARE_UNKNOWN_VOUCHER_TYPE");

        // Anzeige innerhalb des kurzen Caches: kein weiterer Aufruf.
        var before = _lexware.Calls.Count;
        var list = await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware");
        list.CanAssign.ShouldBeTrue();
        list.Vouchers.Select(v => v.Id).ShouldBe([draft.Id, offer.Id]);
        _lexware.Calls.Count.ShouldBe(before);

        // In Lexware abgeschlossen bzw. angenommen: „Aktualisieren“ holt den Stand sofort.
        var remote = _lexware.Vouchers[invoice];
        remote["voucherStatus"] = "open";
        remote["voucherNumber"] = "RE1042";
        remote["dueDate"] = "2099-01-31T00:00:00.000+01:00";
        _lexware.Vouchers[quotation]["voucherStatus"] = "accepted";
        var refreshed = (await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware?refresh=true")).Vouchers;
        (refreshed[0].Status, refreshed[0].Number, refreshed[0].DueDate).ShouldBe(("open", "RE1042", new DateOnly(2099, 1, 31)));
        refreshed[0].Url.ShouldBe($"https://app.lexware.de/permalink/invoices/view/{invoice}");
        refreshed[1].Status.ShouldBe("accepted");

        // Fälligkeit überschritten: überfällig wird abgeleitet (nur bei Rechnungen). Abgleich bei der Anzeige erst nach Ablauf des Caches.
        remote["dueDate"] = "2020-01-31T00:00:00.000+01:00";
        (await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware")).Vouchers[0].Status.ShouldBe("open");
        _factory.Clock.Advance(TimeSpan.FromMinutes(3));
        var later = (await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware")).Vouchers;
        later[0].Status.ShouldBe("overdue");
        later[1].Status.ShouldBe("accepted");
        remote["voucherStatus"] = "paid";
        (await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware?refresh=true")).Vouchers[0].Status.ShouldBe("paid");

        // Schlüssel ungültig: gespeicherter Stand mit Hinweis statt Fehler. In Lexware gelöscht: Hinweis am Beleg.
        _lexware.Raw["/v1/invoices/"] = (401, "{}");
        var stale = await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware?refresh=true");
        stale.SyncError!.ShouldContain("API-Schlüssel");
        stale.Vouchers[0].Status.ShouldBe("paid");
        _lexware.Raw["/v1/invoices/"] = (404, "{}");
        var gone = await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware?refresh=true");
        gone.SyncError.ShouldBeNull();
        gone.Vouchers[0].SyncError.ShouldBe("In Lexware Office nicht mehr vorhanden.");

        // Drosselung beim Lesen: Wiederholung, dann Erfolg.
        _lexware.Raw.Clear();
        _lexware.Throttle["/v1/quotations/"] = 2;
        (await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{bauakte}/lexware?refresh=true")).SyncError.ShouldBeNull();

        (await DbAsync(db => db.Set<AuditEvent>().AsNoTracking().CountAsync(e => e.Action == "lexware.voucher_assigned", Ct))).ShouldBe(2);
        // EstrichONE hat in Lexware Office keinen Beleg angelegt oder geändert.
        _lexware.Calls.ShouldAllBe(c => c.Method == "GET");
        _lexware.Vouchers.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Voucher_IsFoundByNumber_AssignedOnce_AndCanBeReleased()
    {
        await ConfigureAsync();
        var first = await BauakteAsync("EFH Huber", null);
        var second = await BauakteAsync("MFH Lang", null);
        var invoice = _lexware.AddVoucher("invoice", "open", "RE0815", "2026-10-01", "2026-10-15", 1000m, 1190m, null, "Huber Bau GmbH");

        // Ohne Kunde bzw. ohne Verknüpfung: keine Belegliste, aber die Suche über die Nummer.
        var empty = await SendAsync<BauakteVoucherCandidatesResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{first}/lexware/candidates");
        empty.CustomerLinked.ShouldBeFalse();
        empty.Vouchers.ShouldBeEmpty();
        var hit = (await SendAsync<BauakteVoucherCandidatesResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{first}/lexware/candidates?number=RE0815"))
            .Vouchers.ShouldHaveSingleItem();
        (hit.LexwareId, hit.Type, hit.Status, hit.GrossAmount, hit.ContactName).ShouldBe((invoice, "invoice", "overdue", 1190m, "Huber Bau GmbH"));
        hit.AssignedBauakteId.ShouldBeNull();
        _lexware.CallsTo("GET", "/v1/voucherlist").ShouldHaveSingleItem().Query.ShouldContain("voucherNumber=RE0815");
        (await SendAsync<BauakteVoucherCandidatesResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{first}/lexware/candidates?number=GIBTSNICHT"))
            .Vouchers.ShouldBeEmpty();

        var assigned = await SendAsync<LexwareVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/bauakten/{first}/lexware/vouchers/assign",
            new { lexwareId = invoice, type = "invoice" }, HttpStatusCode.Created);
        (assigned.Number, assigned.NetAmount, assigned.Status).ShouldBe(("RE0815", 1000m, "overdue"));

        // Ein Beleg gehört zu genau einer Bauakte.
        var conflict = await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/bauakten/{second}/lexware/vouchers/assign", HttpStatusCode.Conflict,
            new { lexwareId = invoice, type = "invoice" });
        conflict.ShouldStartWith("LEXWARE_ALREADY_LINKED");
        (await SendAsync<BauakteVoucherCandidatesResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{second}/lexware/candidates?number=RE0815"))
            .Vouchers.ShouldHaveSingleItem().AssignedBauakteId.ShouldBe(first);
        (await ProblemAsync(_admin, HttpMethod.Post, $"/api/v1/bauakten/{second}/lexware/vouchers/assign", HttpStatusCode.FailedDependency,
            new { lexwareId = Guid.NewGuid().ToString(), type = "invoice" })).ShouldStartWith("LEXWARE_NOT_FOUND");

        using var release = await _admin.DeleteAsync(new Uri($"/api/v1/bauakten/{first}/lexware/vouchers/{assigned.Id}", UriKind.Relative), Ct);
        release.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await SendAsync<LexwareVoucherResponse>(_admin, HttpMethod.Post, $"/api/v1/bauakten/{second}/lexware/vouchers/assign",
            new { lexwareId = invoice, type = "invoice" }, HttpStatusCode.Created);
        _lexware.Vouchers.ContainsKey(invoice).ShouldBeTrue();
        _lexware.Calls.ShouldAllBe(c => c.Method == "GET");
    }
}
