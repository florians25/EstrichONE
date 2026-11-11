using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EstrichOne.Hosting.Domain;
using EstrichOne.Hosting.Status;

namespace EstrichOne.Hosting.Tests;

/// <summary>Rolle „nur lesen“, Passwortregeln, Bereitschaftsprüfung nach dem Update, Apps und Module je Instanz.</summary>
public sealed class RollenUndSuiteTests
{
    private const string ViewerPassword = "Lese-Zugang-Bereitschaft-7";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<JsonElement> WaitAsync(HttpClient client, string operationId)
    {
        for (var i = 0; i < 400; i++)
        {
            var operation = await client.GetFromJsonAsync<JsonElement>($"/api/operations/{operationId}", Ct);
            if (operation.GetProperty("status").GetString() is "Succeeded" or "Failed")
            {
                return operation;
            }

            await Task.Delay(50, Ct);
        }

        throw new TimeoutException("Vorgang nicht abgeschlossen.");
    }

    private static async Task<string> CreateAsync(HttpClient client, string slug, string version = "0.1.0-preview.4")
    {
        var plans = await client.GetFromJsonAsync<JsonElement>("/api/plans", Ct);
        var response = await client.PostAsJsonAsync("/api/tenants", new
        {
            companyName = $"Estrich {slug} GmbH", contactEmail = "info@estrich.de", slug,
            planId = plans[0].GetProperty("id").GetString(), version,
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        (await WaitAsync(client, created.GetProperty("openOperation").GetProperty("id").GetString()!)).GetProperty("status").GetString().ShouldBe("Succeeded");
        return created.GetProperty("id").GetString()!;
    }

    private static async Task<HttpClient> LoginAsync(ConsoleApp console, string userName, string password)
    {
        var client = console.Client();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { userName, password }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync(Ct));
        await ConsoleApp.AddCsrfAsync(client);
        return client;
    }

    [Theory]
    [InlineData("kurz-1", false)]
    [InlineData("aaaaaaaaaaaaaaaa", false)]
    [InlineData("123456789012", false)]
    [InlineData("Passwort2024!", false)]
    [InlineData("Estrichone123", false)]
    [InlineData("leitstand-mueller-2026", true)]
    [InlineData("Start-Passwort-123", true)]
    [InlineData("Sonne Mond Estrich Kelle", true)]
    public void Passwortregeln(string password, bool valid) =>
        (Validation.CheckPassword(password) is null).ShouldBe(valid);

    [Fact]
    public void Passwort_DarfNichtDerBenutzernameSein() =>
        Validation.CheckPassword("leitstand2026!", "leitstand").ShouldNotBeNull();

    [Fact]
    public async Task NurLesen_SiehtAlles_AendertNichts()
    {
        await using var console = await ConsoleApp.StartAsync();
        var admin = await console.AdminClientAsync();
        var id = await CreateAsync(admin, "lesen");

        var created = await admin.PostAsJsonAsync("/api/admins", new { userName = "leitstand", displayName = "Leitstand", password = ViewerPassword, role = "Viewer" }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var account = await created.Content.ReadFromJsonAsync<JsonElement>(Ct);
        account.GetProperty("role").GetString().ShouldBe("Viewer");
        account.GetProperty("mustChangePassword").GetBoolean().ShouldBeTrue();

        var viewer = await LoginAsync(console, "leitstand", ViewerPassword);
        // Zuerst eigenes Passwort (Startpasswort vergibt ein Administrator).
        (await viewer.GetAsync("/api/tenants", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync("/api/account/password", new { currentPassword = ViewerPassword, newPassword = "eigener-lese-zugang-42" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        viewer = await LoginAsync(console, "leitstand", "eigener-lese-zugang-42");

        var state = await viewer.GetFromJsonAsync<JsonElement>("/api/auth/state", Ct);
        state.GetProperty("user").GetProperty("role").GetString().ShouldBe("Viewer");
        foreach (var path in new[] { "/api/tenants", $"/api/tenants/{id}", "/api/dashboard", "/api/audit", $"/api/tenants/{id}/logs?service=api", "/api/admins" })
        {
            (await viewer.GetAsync(path, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK, path);
        }

        (await viewer.PostAsync($"/api/tenants/{id}/refresh", null, Ct)).IsSuccessStatusCode.ShouldBeTrue();
        var denied = await viewer.PostAsJsonAsync($"/api/tenants/{id}/actions/restart", new { }, Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await denied.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("READ_ONLY");
        (await viewer.PostAsJsonAsync($"/api/tenants/{id}/backup-password", new { password = "eigener-lese-zugang-42" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync($"/api/tenants/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync("/api/admins", new { userName = "zweiter", password = ViewerPassword }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.PutAsJsonAsync("/api/account/preferences", new { theme = "sap_horizon_dark" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Rollen: Administrator stuft hoch; der letzte Administrator bleibt Administrator.
        var admins = await admin.GetFromJsonAsync<JsonElement>("/api/admins", Ct);
        var adminId = admins.EnumerateArray().First(a => a.GetProperty("userName").GetString() == "admin").GetProperty("id").GetString();
        var viewerId = account.GetProperty("id").GetString();
        (await admin.PutAsJsonAsync($"/api/admins/{adminId}/role", new { role = "Viewer" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.PutAsJsonAsync($"/api/admins/{viewerId}/role", new { role = "Administrator" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        // Rollenwechsel beendet die Sitzung (neu anmelden), danach darf das Konto ändern.
        (await viewer.GetAsync("/api/tenants", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        viewer = await LoginAsync(console, "leitstand", "eigener-lese-zugang-42");
        (await viewer.PostAsJsonAsync($"/api/tenants/{id}/actions/restart", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var audit = await admin.GetFromJsonAsync<JsonElement>("/api/audit?q=Rolle", Ct);
        audit.GetProperty("total").GetInt32().ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Aktualisierung_OhneBereitschaft_WirdZurueckgenommen()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var id = await CreateAsync(client, "bereit");
        var response = await client.PostAsJsonAsync($"/api/tenants/{id}/actions/update", new { version = "0.2.0-ungesund", backupFirst = true }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var update = await WaitAsync(client, (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!);
        update.GetProperty("status").GetString().ShouldBe("Failed");
        update.GetProperty("log").GetString()!.ShouldContain("Bereitschaftsprüfung fehlgeschlagen");
        update.GetProperty("error").GetString()!.ShouldContain("Automatisch auf 0.1.0-preview.4 zurückgesetzt");
        console.Docker.Restores.Count.ShouldBe(1);
        var summary = (await client.GetFromJsonAsync<JsonElement>($"/api/tenants/{id}", Ct)).GetProperty("summary");
        summary.GetProperty("version").GetString().ShouldBe("0.1.0-preview.4");
        summary.GetProperty("state").GetString().ShouldBe("Running");

        var ok = await client.PostAsJsonAsync($"/api/tenants/{id}/actions/update", new { version = "0.1.0-preview.5", backupFirst = false }, Ct);
        var done = await WaitAsync(client, (await ok.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!);
        done.GetProperty("status").GetString().ShouldBe("Succeeded");
        done.GetProperty("log").GetString()!.ShouldContain("Bereitschaftsprüfung bestanden");
    }

    [Fact]
    public async Task AppsUndModule_JeInstanz_UndInDerUebersicht()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var neu = await CreateAsync(client, "apps-neu", "0.1.0-preview.5");
        var alt = await CreateAsync(client, "apps-alt", "0.1.0-preview.3");

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/tenants/{neu}", Ct);
        var suite = detail.GetProperty("suite");
        suite.GetProperty("android").GetProperty("latestVersion").GetString().ShouldBe("0.1.0");
        suite.GetProperty("android").GetProperty("packageAvailable").GetBoolean().ShouldBeTrue();
        suite.GetProperty("ios").GetProperty("downloadKind").GetString().ShouldBe("store");
        // preview.5 meldet die Module selbst (Schnittstelle der Suite, mit Kernmodulen).
        suite.GetProperty("modulesSource").GetString().ShouldBe(ModuleSources.Suite);
        suite.GetProperty("modulesNote").ValueKind.ShouldBe(JsonValueKind.Null);
        var modules = suite.GetProperty("modules").EnumerateArray().ToList();
        modules.Count(m => m.GetProperty("core").GetBoolean()).ShouldBe(5);
        modules.Count(m => !m.GetProperty("core").GetBoolean()).ShouldBe(SuiteModules.Catalog.Count);
        detail.GetProperty("latestVersion").GetString().ShouldBe("0.1.0-preview.5");

        var old = (await client.GetFromJsonAsync<JsonElement>($"/api/tenants/{alt}", Ct)).GetProperty("suite");
        old.GetProperty("android").ValueKind.ShouldBe(JsonValueKind.Null);
        old.GetProperty("appsNote").GetString()!.ShouldContain("1.0.13");
        // preview.3 kennt die Schnittstelle nicht: Rückfall auf die Datenbank, mit Hinweis.
        old.GetProperty("modulesSource").GetString().ShouldBe(ModuleSources.Database);
        old.GetProperty("modulesNote").GetString()!.ShouldContain("Datenbank");
        old.GetProperty("modules").GetArrayLength().ShouldBeGreaterThanOrEqualTo(SuiteModules.Catalog.Count);

        var dashboard = await client.GetFromJsonAsync<JsonElement>("/api/dashboard", Ct);
        dashboard.GetProperty("latestVersion").GetString().ShouldBe("0.1.0-preview.5");
        dashboard.GetProperty("behindLatest").GetInt32().ShouldBe(1);
        dashboard.GetProperty("apps").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public void Module_AusDenEinstellungen()
    {
        var modules = SuiteModules.Parse("{\"helpdesk\":false,\"orders\":true,\"zukunft\":false}");
        modules.First(m => m.Key == "helpdesk").Enabled.ShouldBeFalse();
        modules.First(m => m.Key == "orders").Enabled.ShouldBeTrue();
        modules.First(m => m.Key == "planning").Enabled.ShouldBeTrue();
        modules[^1].Key.ShouldBe("zukunft");
        SuiteModules.Parse("").All(m => m.Enabled).ShouldBeTrue();
    }

    [Theory]
    [InlineData("1.0.14", false)]
    [InlineData("1.0.15", true)]
    [InlineData("1.1.0", true)]
    [InlineData("0.1.0-preview.4", false)]
    [InlineData("0.1.0-preview.5", true)]
    public void Simulation_KenntModulSchnittstelleAbVersion(string version, bool expected) =>
        FakeInstanceProbe.SupportsModuleApi(version).ShouldBe(expected);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public async Task ModulSchnittstelle_MitInstanzSchluessel_UndRueckfallBei404()
    {
        const string body = """{"modules":[{"key":"bauakten","area":"core","areaTitle":"Immer aktiv","title":"Bauakten","description":"x","enabled":true,"core":true,"requires":[]},{"key":"helpdesk","area":"service","areaTitle":"Kommunikation und Service","title":"Helpdesk","description":"y","enabled":false,"core":false,"requires":[]},{"key":"neu","area":"zukunft","title":"","enabled":true,"core":false,"requires":[]}]}""";
        var ok = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        var result = await new HttpInstanceProbe(new HttpClient(ok)).GetModulesAsync("firma", "geheim", Ct);
        ok.Last!.RequestUri!.ToString().ShouldEndWith("/api/v1/system/modules");
        ok.Last.Headers.Authorization!.ToString().ShouldBe("Bearer geheim");
        result.Modules!.Select(m => (m.Key, m.Title, m.Area, m.Enabled, m.Core)).ShouldBe(
        [
            ("bauakten", "Bauakten", "Immer aktiv", true, true),
            ("helpdesk", "Helpdesk", "Kommunikation und Service", false, false),
            ("neu", "neu", "zukunft", true, false),
        ]);

        var old = await new HttpInstanceProbe(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)))).GetModulesAsync("firma", "geheim", Ct);
        (old.Modules, old.NotSupported).ShouldBe((null, true));

        var denied = await new HttpInstanceProbe(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)))).GetModulesAsync("firma", "geheim", Ct);
        denied.NotSupported.ShouldBeFalse();
        denied.Error!.ShouldContain("401");
    }
}
