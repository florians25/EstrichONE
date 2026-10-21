using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EstrichOne.Hosting.Tests;

public sealed class TenantTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string> PlanIdAsync(HttpClient client, string name = "Starter")
    {
        var plans = await client.GetFromJsonAsync<JsonElement>("/api/plans", Ct);
        return plans.EnumerateArray().First(p => p.GetProperty("name").GetString() == name).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> WaitAsync(HttpClient client, string operationId)
    {
        for (var i = 0; i < 200; i++)
        {
            var operation = await client.GetFromJsonAsync<JsonElement>($"/api/operations/{operationId}", Ct);
            var status = operation.GetProperty("status").GetString();
            if (status is "Succeeded" or "Failed")
            {
                return operation;
            }

            await Task.Delay(50, Ct);
        }

        throw new TimeoutException("Vorgang nicht abgeschlossen.");
    }

    private static async Task<(string Id, JsonElement Operation)> CreateAsync(HttpClient client, string slug, string company = "Estrichbau Müller GmbH", string? version = null)
    {
        var response = await client.PostAsJsonAsync("/api/tenants", new
        {
            companyName = company,
            customerNumber = "K-1001",
            contactName = "Max Müller",
            contactEmail = "info@mueller.de",
            slug,
            planId = await PlanIdAsync(client),
            version,
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var operation = await WaitAsync(client, created.GetProperty("openOperation").GetProperty("id").GetString()!);
        return (created.GetProperty("id").GetString()!, operation);
    }

    private static async Task<JsonElement> ActionAsync(HttpClient client, string id, string action, object? body = null, HttpStatusCode expected = HttpStatusCode.Accepted)
    {
        var response = await client.PostAsJsonAsync($"/api/tenants/{id}/actions/{action}", body ?? new { }, Ct);
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(Ct));
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return expected == HttpStatusCode.Accepted ? await WaitAsync(client, json.GetProperty("id").GetString()!) : json;
    }

    private static async Task<JsonElement> DetailAsync(HttpClient client, string id) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/tenants/{id}", Ct);

    [Fact]
    public async Task Anlegen_MitUngueltigenDaten_LiefertFeldfehler()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var response = await client.PostAsJsonAsync("/api/tenants", new
        {
            companyName = "Firma \"X\"",
            contactEmail = "kaputt",
            slug = "a; rm -rf /",
            domain = "*.boese.de",
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var fields = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("fields");
        foreach (var field in new[] { "companyName", "contactEmail", "slug", "domain", "planId" })
        {
            fields.TryGetProperty(field, out _).ShouldBeTrue(field);
        }

        Directory.Exists(Path.Combine(console.Root, "instances")).ShouldBeFalse();
    }

    [Fact]
    public async Task Bereitstellen_LegtInstanzAn_UndTraegtSieImProxyEin()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var (id, operation) = await CreateAsync(client, "mueller");
        operation.GetProperty("status").GetString().ShouldBe("Succeeded", operation.GetProperty("log").GetString());
        operation.GetProperty("log").GetString()!.ShouldContain("Einrichtungscode");

        var directory = Path.Combine(console.Root, "instances", "mueller");
        File.Exists(Path.Combine(directory, ".env")).ShouldBeTrue();
        File.Exists(Path.Combine(directory, "compose.hosting.yml")).ShouldBeTrue();
        var caddyfile = await File.ReadAllTextAsync(Path.Combine(console.Root, "proxy", "Caddyfile"), Ct);
        caddyfile.ShouldContain("mueller.hosting.test {");
        caddyfile.ShouldContain("reverse_proxy estrichone-mueller-caddy-1:8181");
        File.Exists(Path.Combine(console.Root, "proxy", "maintenance", "index.html")).ShouldBeTrue();
        console.Docker.Networks.ShouldContain("estrichone-mueller-edge");
        console.Docker.IsConnected("estrichone-mueller-edge", "estrichone-hosting-proxy").ShouldBeTrue();
        console.Docker.IsConnected("estrichone-mueller-edge", "estrichone-hosting-console").ShouldBeTrue();
        console.Docker.Calls.ShouldContain(c => c.StartsWith("estrichone-mueller up -d --wait", StringComparison.Ordinal));

        var detail = await DetailAsync(client, id);
        var summary = detail.GetProperty("summary");
        summary.GetProperty("state").GetString().ShouldBe("Running");
        summary.GetProperty("health").GetString().ShouldBe("Healthy");
        summary.GetProperty("url").GetString().ShouldBe("https://mueller.hosting.test");
        detail.GetProperty("status").GetProperty("setupCode").GetString()!.ShouldMatch("^[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}$");
        detail.GetProperty("status").GetProperty("containers").GetArrayLength().ShouldBe(4);

        // Kürzel und Domain sind eindeutig.
        var duplicate = await client.PostAsJsonAsync("/api/tenants", new
        {
            companyName = "Andere Firma",
            contactEmail = "a@b.de",
            slug = "mueller",
            planId = await PlanIdAsync(client),
        }, Ct);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var suggestion = await client.GetFromJsonAsync<JsonElement>("/api/tenants/suggest?company=Estrichbau%20M%C3%BCller", Ct);
        suggestion.GetProperty("slug").GetString().ShouldBe("estrichbau-mueller");
        suggestion.GetProperty("domain").GetString().ShouldBe("estrichbau-mueller.hosting.test");
    }

    [Fact]
    public async Task Lebenszyklus_StoppenStartenSperrenAktualisierenSichern()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var (id, _) = await CreateAsync(client, "nord", "Estrich Nord", "0.1.0-preview.3");

        (await ActionAsync(client, id, "stop")).GetProperty("status").GetString().ShouldBe("Succeeded");
        (await DetailAsync(client, id)).GetProperty("summary").GetProperty("state").GetString().ShouldBe("Stopped");
        (await File.ReadAllTextAsync(Path.Combine(console.Root, "proxy", "Caddyfile"), Ct)).ShouldContain("# Mandant nord – Wartungsseite");
        await ActionAsync(client, id, "stop", expected: HttpStatusCode.BadRequest);

        (await ActionAsync(client, id, "start")).GetProperty("status").GetString().ShouldBe("Succeeded");
        (await ActionAsync(client, id, "restart")).GetProperty("status").GetString().ShouldBe("Succeeded");

        await ActionAsync(client, id, "lock", new { reason = "" }, HttpStatusCode.BadRequest);
        (await ActionAsync(client, id, "lock", new { reason = "Zahlungsverzug" })).GetProperty("status").GetString().ShouldBe("Succeeded");
        var locked = (await DetailAsync(client, id)).GetProperty("summary");
        locked.GetProperty("state").GetString().ShouldBe("Locked");
        locked.GetProperty("lockReason").GetString().ShouldBe("Zahlungsverzug");
        (await File.ReadAllTextAsync(Path.Combine(console.Root, "proxy", "Caddyfile"), Ct)).ShouldContain("error 503");
        (await ActionAsync(client, id, "unlock")).GetProperty("status").GetString().ShouldBe("Succeeded");
        (await DetailAsync(client, id)).GetProperty("summary").GetProperty("state").GetString().ShouldBe("Running");

        var dashboard = await client.GetFromJsonAsync<JsonElement>("/api/dashboard", Ct);
        dashboard.GetProperty("outdated").GetInt32().ShouldBe(1);

        var failed = await ActionAsync(client, id, "update", new { version = "0.2.0-fehler" });
        failed.GetProperty("status").GetString().ShouldBe("Failed");
        var afterFailure = (await DetailAsync(client, id)).GetProperty("summary");
        afterFailure.GetProperty("version").GetString().ShouldBe("0.1.0-preview.3");
        afterFailure.GetProperty("state").GetString().ShouldBe("Failed");
        (await File.ReadAllTextAsync(Path.Combine(console.Root, "instances", "nord", ".env"), Ct)).ShouldContain("ESTRICHONE_VERSION=0.1.0-preview.3");

        await ActionAsync(client, id, "update", new { version = "latest" }, HttpStatusCode.BadRequest);
        (await ActionAsync(client, id, "update", new { version = "0.1.0-preview.4" })).GetProperty("status").GetString().ShouldBe("Succeeded");
        var updated = (await DetailAsync(client, id)).GetProperty("summary");
        updated.GetProperty("version").GetString().ShouldBe("0.1.0-preview.4");
        updated.GetProperty("state").GetString().ShouldBe("Running");
        updated.GetProperty("outdated").GetBoolean().ShouldBeFalse();

        (await ActionAsync(client, id, "backup")).GetProperty("status").GetString().ShouldBe("Succeeded");
        var backup = (await DetailAsync(client, id)).GetProperty("status").GetProperty("backup");
        backup.GetProperty("healthy").GetBoolean().ShouldBeTrue();

        var logs = await client.GetFromJsonAsync<JsonElement>($"/api/tenants/{id}/logs?service=api&lines=50&filter=Einrichtungscode", Ct);
        logs.GetProperty("count").GetInt32().ShouldBe(1);
        (await client.GetAsync($"/api/tenants/{id}/logs?service=--help", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var wrongPassword = await client.PostAsJsonAsync($"/api/tenants/{id}/backup-password", new { password = "falsch" }, Ct);
        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var secret = await client.PostAsJsonAsync($"/api/tenants/{id}/backup-password", new { password = ConsoleApp.Password }, Ct);
        (await secret.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("password").GetString()!.Length.ShouldBe(48);

        var audit = await client.GetFromJsonAsync<JsonElement>("/api/audit?q=nord&take=200", Ct);
        var actions = audit.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("action").GetString()).ToList();
        actions.ShouldContain("Mandant angelegt");
        actions.ShouldContain("Sperren abgeschlossen");
        actions.ShouldContain("Aktualisieren fehlgeschlagen");
        actions.ShouldContain("Sicherungsschlüssel angezeigt");
    }

    [Fact]
    public async Task AlleAktualisieren_ReihtNurVeralteteEin()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        await CreateAsync(client, "alt-eins", "Alt Eins", "0.1.0-preview.2");
        await CreateAsync(client, "alt-zwei", "Alt Zwei", "0.1.0-preview.3");
        await CreateAsync(client, "aktuell", "Aktuell", "0.1.0-preview.4");
        var response = await client.PostAsJsonAsync("/api/tenants/update-all", new { version = "0.1.0-preview.4" }, Ct);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("queued").GetInt32().ShouldBe(2);
        for (var i = 0; i < 100; i++)
        {
            var tenants = await client.GetFromJsonAsync<JsonElement>("/api/tenants", Ct);
            if (tenants.EnumerateArray().All(t => t.GetProperty("version").GetString() == "0.1.0-preview.4" && t.GetProperty("openOperation").ValueKind == JsonValueKind.Null))
            {
                return;
            }

            await Task.Delay(50, Ct);
        }

        throw new TimeoutException("Nicht alle Instanzen aktualisiert.");
    }

    [Fact]
    public async Task Stammdaten_DomainAenderung_KonfiguriertNeu()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var (id, _) = await CreateAsync(client, "sued", "Estrich Süd");
        var response = await client.PutAsJsonAsync($"/api/tenants/{id}", new
        {
            companyName = "Estrich Süd GmbH",
            contactEmail = "neu@sued.de",
            domain = "estrichone.sued.de",
            planId = await PlanIdAsync(client, "Business"),
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var operations = await client.GetFromJsonAsync<JsonElement>($"/api/tenants/{id}/operations", Ct);
        var reconfigure = operations.EnumerateArray().First(o => o.GetProperty("kind").GetString() == "Reconfigure");
        (await WaitAsync(client, reconfigure.GetProperty("id").GetString()!)).GetProperty("status").GetString().ShouldBe("Succeeded");
        var env = await File.ReadAllTextAsync(Path.Combine(console.Root, "instances", "sued", ".env"), Ct);
        env.ShouldContain("ESTRICHONE_PUBLIC_URL=https://estrichone.sued.de");
        env.ShouldContain("ESTRICHONE_SERVER_NAME=\"Estrich Süd GmbH\"");
        (await File.ReadAllTextAsync(Path.Combine(console.Root, "instances", "sued", "compose.hosting.yml"), Ct)).ShouldContain("mem_limit: 2048m");
        (await File.ReadAllTextAsync(Path.Combine(console.Root, "proxy", "Caddyfile"), Ct)).ShouldContain("estrichone.sued.de {");
    }

    [Fact]
    public async Task Loeschen_BrauchtDoppelteBestaetigung_UndArchiviertEndsicherung()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var (id, _) = await CreateAsync(client, "weg-damit", "Weg GmbH");

        await ActionAsync(client, id, "delete", new { confirm = true, confirmSlug = "falsch" }, HttpStatusCode.BadRequest);
        await ActionAsync(client, id, "delete", new { confirm = false, confirmSlug = "weg-damit" }, HttpStatusCode.BadRequest);
        (await ActionAsync(client, id, "delete", new { confirm = true, confirmSlug = "weg-damit", finalBackup = true }))
            .GetProperty("status").GetString().ShouldBe("Succeeded");

        Directory.Exists(Path.Combine(console.Root, "instances", "weg-damit")).ShouldBeFalse();
        var archive = Directory.GetDirectories(Path.Combine(console.Root, "archive")).Single();
        Path.GetFileName(archive).ShouldStartWith("weg-damit-");
        File.Exists(Path.Combine(archive, "instanz.env")).ShouldBeTrue();
        File.Exists(Path.Combine(archive, "backup-repository", "config")).ShouldBeTrue();
        console.Docker.Networks.ShouldNotContain("estrichone-weg-damit-edge");
        (await File.ReadAllTextAsync(Path.Combine(console.Root, "proxy", "Caddyfile"), Ct)).ShouldNotContain("weg-damit");
        (await client.GetAsync($"/api/tenants/{id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetFromJsonAsync<JsonElement>("/api/tenants", Ct)).GetArrayLength().ShouldBe(0);

        // Das Kürzel ist wieder frei.
        var (_, again) = await CreateAsync(client, "weg-damit", "Weg GmbH");
        again.GetProperty("status").GetString().ShouldBe("Succeeded");
    }

    [Fact]
    public async Task Tarife_GrenzenAendern_WendetSieAufInstanzenAn()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var planId = await PlanIdAsync(client);
        await CreateAsync(client, "tarif-test", "Tarif Test");
        (await client.DeleteAsync($"/api/plans/{planId}", Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var invalid = await client.PutAsJsonAsync($"/api/plans/{planId}", new { name = "Starter", memoryMb = 100 }, Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var response = await client.PutAsJsonAsync($"/api/plans/{planId}", new { name = "Starter", maxUsers = 5, storageGb = 20, memoryMb = 1536, cpuLimit = 1.25 }, Ct);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("reconfigured").GetInt32().ShouldBe(1);
        for (var i = 0; i < 100; i++)
        {
            var overlay = await File.ReadAllTextAsync(Path.Combine(console.Root, "instances", "tarif-test", "compose.hosting.yml"), Ct);
            if (overlay.Contains("mem_limit: 1536m", StringComparison.Ordinal) && overlay.Contains("cpus: 1.25", StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(50, Ct);
        }

        throw new TimeoutException("Grenzen nicht übernommen.");
    }

    [Fact]
    public async Task Uebersicht_UndAuditExport_Funktionieren()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        await CreateAsync(client, "kpi-test", "KPI Test");
        var dashboard = await client.GetFromJsonAsync<JsonElement>("/api/dashboard", Ct);
        dashboard.GetProperty("tenants").GetProperty("total").GetInt32().ShouldBe(1);
        dashboard.GetProperty("tenants").GetProperty("running").GetInt32().ShouldBe(1);
        dashboard.GetProperty("system").GetProperty("simulation").GetBoolean().ShouldBeTrue();
        var csv = await client.GetStringAsync("/api/audit/export", Ct);
        csv.ShouldContain("Zeitpunkt (UTC);Benutzer;Aktion");
        csv.ShouldContain("Mandant angelegt");
        var versions = await client.GetFromJsonAsync<JsonElement>("/api/versions", Ct);
        versions.GetProperty("versions").EnumerateArray().Select(v => v.GetString()).ShouldContain("0.1.0-preview.4");
    }
}
