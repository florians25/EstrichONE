using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Notifications;
using EstrichOne.Hosting.Recovery;
using EstrichOne.Hosting.Status;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Hosting.Tests;

/// <summary>Anlage-Assistent mit erster Admin-Person, Aktualisierung mit Rücknahme, Wiederherstellung, Export, Archiv, Massenaktionen.</summary>
public sealed class BetriebTests
{
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

    private static async Task<string> CreateAsync(HttpClient client, string slug, object? firstAdmin = null)
    {
        var plans = await client.GetFromJsonAsync<JsonElement>("/api/plans", Ct);
        var body = new Dictionary<string, object?>
        {
            ["companyName"] = "Estrich Assistent GmbH",
            ["contactEmail"] = "info@assistent.de",
            ["slug"] = slug,
            ["planId"] = plans[0].GetProperty("id").GetString(),
            ["version"] = "0.1.0-preview.4",
        };
        if (firstAdmin is not null)
        {
            foreach (var property in JsonSerializer.SerializeToElement(firstAdmin).EnumerateObject())
            {
                body[property.Name] = property.Value.GetString();
            }
        }

        var response = await client.PostAsJsonAsync("/api/tenants", body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var operation = await WaitAsync(client, created.GetProperty("openOperation").GetProperty("id").GetString()!);
        operation.GetProperty("status").GetString().ShouldBe("Succeeded", operation.GetProperty("log").GetString());
        return created.GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> ActionAsync(HttpClient client, string id, string action, object? body = null)
    {
        var response = await client.PostAsJsonAsync($"/api/tenants/{id}/actions/{action}", body ?? new { }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        return await WaitAsync(client, (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!);
    }

    private static async Task<JsonElement> SummaryAsync(HttpClient client, string id) =>
        (await client.GetFromJsonAsync<JsonElement>($"/api/tenants/{id}", Ct)).GetProperty("summary");

    [Fact]
    public async Task Assistent_LegtErsteAdminPersonAn_UndZugangsbriefNurMitPasswort()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var invalid = await client.PostAsJsonAsync("/api/tenants", new
        {
            companyName = "X GmbH", contactEmail = "x@x.de", slug = "x-gmbh",
            planId = (await client.GetFromJsonAsync<JsonElement>("/api/plans", Ct))[0].GetProperty("id").GetString(),
            firstAdminUserName = "1 falsch", firstAdminName = "",
        }, Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var fields = (await invalid.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("fields");
        fields.TryGetProperty("firstAdminUserName", out _).ShouldBeTrue();
        fields.TryGetProperty("firstAdminName", out _).ShouldBeTrue();

        var id = await CreateAsync(client, "assistent", new { firstAdminName = "Petra Polier", firstAdminUserName = "petra", firstAdminEmail = "petra@assistent.de" });
        var probe = console.Services.GetRequiredService<FakeInstanceProbe>();
        probe.CompletedSetups.ShouldContainKey("assistent");
        var setup = probe.CompletedSetups["assistent"];
        setup.SetupCode.ShouldMatch("^[A-Z0-9]{4}(-[A-Z0-9]{4})+$");
        setup.CompanyName.ShouldBe("Estrich Assistent GmbH");
        setup.Administrator.UserName.ShouldBe("petra");
        (await SummaryAsync(client, id)).GetProperty("accessLetterAvailable").GetBoolean().ShouldBeTrue();

        var wrong = await client.PostAsJsonAsync($"/api/tenants/{id}/access-letter", new { password = "falsch" }, Ct);
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var letter = await (await client.PostAsJsonAsync($"/api/tenants/{id}/access-letter", new { password = ConsoleApp.Password }, Ct))
            .Content.ReadFromJsonAsync<JsonElement>(Ct);
        letter.GetProperty("userName").GetString().ShouldBe("petra");
        letter.GetProperty("url").GetString().ShouldBe("https://assistent.hosting.test");
        letter.GetProperty("initialPassword").GetString().ShouldBe(setup.Administrator.Password);
        letter.GetProperty("initialPassword").GetString()!.Length.ShouldBeGreaterThanOrEqualTo(16);
        var file = Path.Combine(console.Root, "instances", "assistent", AccessLetters.FileName);
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(file).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        (await client.DeleteAsync($"/api/tenants/{id}/access-letter", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        File.Exists(file).ShouldBeFalse();
        (await client.PostAsJsonAsync($"/api/tenants/{id}/access-letter", new { password = ConsoleApp.Password }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Erneut anstoßen: bereits eingerichtet → nichts zu tun.
        var again = await ActionAsync(client, id, "initial-setup");
        again.GetProperty("log").GetString()!.ShouldContain("bereits eingerichtet");
    }

    [Fact]
    public async Task Aktualisierung_MitVorabSicherung_LaesstSichZuruecknehmen()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var id = await CreateAsync(client, "rueck");
        (await client.PostAsJsonAsync($"/api/tenants/{id}/actions/rollback", new { confirm = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await ActionAsync(client, id, "update", new { version = "0.1.0-preview.5", backupFirst = true })).GetProperty("status").GetString().ShouldBe("Succeeded");
        var snapshots = await client.GetFromJsonAsync<JsonElement>($"/api/tenants/{id}/snapshots", Ct);
        snapshots.GetProperty("previousVersion").GetString().ShouldBe("0.1.0-preview.4");
        var rollbackSnapshot = snapshots.GetProperty("rollbackSnapshot").GetString()!;
        snapshots.GetProperty("items")[0].GetProperty("id").GetString().ShouldBe(rollbackSnapshot);

        (await client.PostAsJsonAsync($"/api/tenants/{id}/actions/rollback", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var rollback = await ActionAsync(client, id, "rollback", new { confirm = true });
        rollback.GetProperty("status").GetString().ShouldBe("Succeeded", rollback.GetProperty("log").GetString());
        console.Docker.Restores.ShouldContain($"estrichone-rueck {rollbackSnapshot}");
        var summary = await SummaryAsync(client, id);
        summary.GetProperty("version").GetString().ShouldBe("0.1.0-preview.4");
        summary.GetProperty("previousVersion").ValueKind.ShouldBe(JsonValueKind.Null);
        File.ReadAllText(Path.Combine(console.Root, "instances", "rueck", ".env")).ShouldContain("ESTRICHONE_VERSION=0.1.0-preview.4");
    }

    [Fact]
    public async Task Aktualisierung_StartetNicht_WirdAutomatischZurueckgenommen()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var id = await CreateAsync(client, "auto");
        var update = await ActionAsync(client, id, "update", new { version = "0.2.0-startabbruch", backupFirst = true });
        update.GetProperty("status").GetString().ShouldBe("Failed");
        update.GetProperty("error").GetString()!.ShouldContain("Automatisch auf 0.1.0-preview.4 zurückgesetzt");
        console.Docker.Restores.Count.ShouldBe(1);
        var summary = await SummaryAsync(client, id);
        summary.GetProperty("state").GetString().ShouldBe("Running");
        summary.GetProperty("version").GetString().ShouldBe("0.1.0-preview.4");
    }

    [Fact]
    public async Task Wiederherstellen_BrauchtBestaetigung_UndSpieltSnapshotZurueck()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var id = await CreateAsync(client, "wieder");
        (await client.PostAsJsonAsync($"/api/tenants/{id}/actions/restore", new { confirm = true, snapshot = "latest" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var failed = (await client.GetFromJsonAsync<JsonElement>($"/api/tenants/{id}/operations", Ct))[0];
        (await WaitAsync(client, failed.GetProperty("id").GetString()!)).GetProperty("error").GetString()!.ShouldContain("noch keine Sicherung");

        await ActionAsync(client, id, "backup");
        var snapshot = (await client.GetFromJsonAsync<JsonElement>($"/api/tenants/{id}/snapshots", Ct)).GetProperty("items")[0].GetProperty("id").GetString()!;
        (await client.PostAsJsonAsync($"/api/tenants/{id}/actions/restore", new { snapshot }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync($"/api/tenants/{id}/actions/restore", new { snapshot = "../../etc", confirm = true }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var restore = await ActionAsync(client, id, "restore", new { snapshot, confirm = true, backupFirst = true });
        restore.GetProperty("status").GetString().ShouldBe("Succeeded", restore.GetProperty("log").GetString());
        console.Docker.Restores.ShouldContain($"estrichone-wieder {snapshot}");
        console.Docker.Calls.ShouldContain(c => c.StartsWith("estrichone-wieder stop api caddy backup", StringComparison.Ordinal));
        (await client.GetFromJsonAsync<JsonElement>($"/api/tenants/{id}/snapshots", Ct)).GetProperty("items").GetArrayLength().ShouldBe(2);
        (await SummaryAsync(client, id)).GetProperty("state").GetString().ShouldBe("Running");
    }

    [Fact]
    public async Task Export_Archiv_UndMassenaktionen()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var a = await CreateAsync(client, "export-a");
        var b = await CreateAsync(client, "export-b");

        (await client.PostAsJsonAsync($"/api/tenants/{a}/export", new { password = ConsoleApp.Password }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var bulk = await client.PostAsJsonAsync("/api/tenants/bulk", new { tenantIds = new[] { a, b }, action = "backup" }, Ct);
        bulk.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await bulk.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("queued").GetArrayLength().ShouldBe(2);
        (await client.PostAsJsonAsync("/api/tenants/bulk", new { tenantIds = new[] { a }, action = "delete" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        foreach (var id in new[] { a, b })
        {
            var operation = (await client.GetFromJsonAsync<JsonElement>($"/api/tenants/{id}/operations", Ct))[0];
            (await WaitAsync(client, operation.GetProperty("id").GetString()!)).GetProperty("status").GetString().ShouldBe("Succeeded");
        }

        (await client.PostAsJsonAsync($"/api/tenants/{a}/export", new { password = "falsch" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var export = await (await client.PostAsJsonAsync($"/api/tenants/{a}/export", new { password = ConsoleApp.Password }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        var url = export.GetProperty("url").GetString()!;
        var download = await client.GetAsync(url, Ct);
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/gzip");
        var entries = new List<string>();
        await using (var gzip = new GZipStream(await download.Content.ReadAsStreamAsync(Ct), CompressionMode.Decompress))
        await using (var tar = new TarReader(gzip))
        {
            while (await tar.GetNextEntryAsync(cancellationToken: Ct) is { } entry)
            {
                entries.Add(entry.Name);
            }
        }

        entries.ShouldContain("instanz.env");
        entries.ShouldContain("backup-repository/config");
        entries.ShouldContain("compose.hosting.yml");
        (await client.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Löschen mit Endsicherung → Archiv; herunterladen und endgültig löschen.
        var delete = await client.PostAsJsonAsync($"/api/tenants/{b}/actions/delete", new { confirm = true, confirmSlug = "export-b", finalBackup = true }, Ct);
        await WaitAsync(client, (await delete.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!);
        var archives = await client.GetFromJsonAsync<JsonElement>("/api/archives", Ct);
        archives.GetArrayLength().ShouldBe(1);
        var name = archives[0].GetProperty("name").GetString()!;
        archives[0].GetProperty("slug").GetString().ShouldBe("export-b");
        archives[0].GetProperty("hasKey").GetBoolean().ShouldBeTrue();
        var archiveExport = await (await client.PostAsJsonAsync($"/api/archives/{name}/export", new { password = ConsoleApp.Password }, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        (await client.GetAsync(archiveExport.GetProperty("url").GetString(), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.DeleteAsync($"/api/archives/{name}?confirm=falsch", Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.DeleteAsync("/api/archives/..%2F..%2Fconsole?confirm=x", Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.DeleteAsync($"/api/archives/{name}?confirm={name}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<JsonElement>("/api/archives", Ct)).GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task InstallerAdmin_MussPasswortAendern_DannAssistentAbschliessen()
    {
        await using var console = await ConsoleApp.StartAsync(prepareRoot: root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "console"));
            File.WriteAllText(Path.Combine(root, "console", "initial-admin.json"),
                """{"userName":"chefin","password":"Start-Passwort-123","email":"chefin@hosting.test"}""");
        });
        File.Exists(Path.Combine(console.Root, "console", "initial-admin.json")).ShouldBeFalse();
        var client = console.Client();
        var state = await client.GetFromJsonAsync<JsonElement>("/api/auth/state", Ct);
        state.GetProperty("setupRequired").GetBoolean().ShouldBeFalse();
        (await client.PostAsJsonAsync("/api/auth/login", new { userName = "chefin", password = "Start-Passwort-123" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ConsoleApp.AddCsrfAsync(client);

        var wizard = await client.GetFromJsonAsync<JsonElement>("/api/setup-wizard", Ct);
        wizard.GetProperty("completed").GetBoolean().ShouldBeFalse();
        wizard.GetProperty("mustChangePassword").GetBoolean().ShouldBeTrue();
        wizard.GetProperty("notifications").GetProperty("emailTo").GetString().ShouldBe("chefin@hosting.test");
        // Startpasswort aktiv: alles außer Status, Abmelden und Passwort ändern wird serverseitig abgewiesen.
        var blocked = await client.PostAsJsonAsync("/api/setup-wizard/complete", new { defaultVersion = "0.1.0-preview.5" }, Ct);
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await blocked.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString().ShouldBe("PASSWORD_CHANGE_REQUIRED");
        foreach (var path in new[] { "/api/tenants", "/api/dashboard", "/api/admins", "/api/settings", "/api/archives", "/api/notifications" })
        {
            (await client.GetAsync(path, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
        }

        (await client.PostAsJsonAsync("/api/tenants", new { companyName = "X" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.PostAsJsonAsync("/api/account/totp/begin", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/auth/state", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await client.PostAsJsonAsync("/api/account/password", new { currentPassword = "Start-Passwort-123", newPassword = ConsoleApp.Password }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await ConsoleApp.AddCsrfAsync(client);
        (await client.GetFromJsonAsync<JsonElement>("/api/setup-wizard", Ct)).GetProperty("mustChangePassword").GetBoolean().ShouldBeFalse();
        (await client.GetAsync("/api/tenants", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var bad = await client.PostAsJsonAsync("/api/setup-wizard/complete", new { secondaryRepository = "rm -rf /", notifications = new { webhookUrl = "ftp://x" } }, Ct);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var badFields = (await bad.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("fields");
        badFields.TryGetProperty("secondaryRepository", out _).ShouldBeTrue();
        badFields.TryGetProperty("webhookUrl", out _).ShouldBeTrue();

        (await client.PostAsJsonAsync("/api/setup-wizard/complete", new
        {
            defaultVersion = "0.1.0-preview.5",
            secondaryRepository = "sftp:backup@sicherung.example.de:/estrichone",
            notifications = new { emailTo = "chefin@hosting.test", smtpHost = "smtp.example.de", smtpPort = 587, smtpFrom = "konsole@hosting.test", webhookUrl = "https://hooks.example.de/x" },
        }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        wizard = await client.GetFromJsonAsync<JsonElement>("/api/setup-wizard", Ct);
        wizard.GetProperty("completed").GetBoolean().ShouldBeTrue();
        wizard.GetProperty("defaultVersion").GetString().ShouldBe("0.1.0-preview.5");
        (await client.PostAsJsonAsync("/api/settings/notifications/test", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        await CreateAsync(client, "offsite");
        File.ReadAllText(Path.Combine(console.Root, "instances", "offsite", ".env"))
            .ShouldContain("BACKUP_REPOSITORY_2=sftp:backup@sicherung.example.de:/estrichone/offsite");
    }

    [Fact]
    public async Task InstallerAdmin_Reset_SetztPasswortUnd2FAZurueck()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        File.WriteAllText(Path.Combine(console.Root, "console", "initial-admin.json"), """{"userName":"admin","password":"Notfall-Passwort-1","reset":true}""");
        (await EstrichOne.Hosting.Setup.InitialAdmin.ApplyAsync(console.Services,
            console.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<EstrichOne.Hosting.Infrastructure.HostingOptions>>().Value, Ct))!
            .ShouldContain("zurückgesetzt");
        // Alte Sitzung ist beendet, das neue Startpasswort gilt.
        (await client.GetAsync("/api/tenants", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var fresh = console.Client();
        (await fresh.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = "Notfall-Passwort-1" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await fresh.GetAsync("/api/tenants", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Benachrichtigungen_MeldenProblemeUndSchliessenBehobene()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var id = await CreateAsync(client, "meldung");
        await using (var scope = console.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostingDbContext>();
            var tenant = await db.Tenants.FindAsync([Guid.Parse(id)], Ct);
            tenant!.State = TenantState.Failed;
            tenant.LastError = "Aktualisieren: Image nicht gefunden";
            await db.SaveChangesAsync(Ct);
        }

        var monitor = console.Services.GetRequiredService<NotificationMonitor>();
        (await monitor.RunOnceAsync(confirmations: 2, Ct)).ShouldBe(0);
        (await monitor.RunOnceAsync(confirmations: 2, Ct)).ShouldBe(1);
        (await monitor.RunOnceAsync(confirmations: 2, Ct)).ShouldBe(0);
        var list = await client.GetFromJsonAsync<JsonElement>("/api/notifications", Ct);
        list.GetProperty("open").GetInt32().ShouldBe(1);
        list.GetProperty("unread").GetInt32().ShouldBe(1);
        var item = list.GetProperty("items")[0];
        item.GetProperty("severity").GetString().ShouldBe("critical");
        item.GetProperty("text").GetString().ShouldBe("Aktualisieren: Image nicht gefunden");
        (await client.PostAsync("/api/notifications/read-all", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await ActionAsync(client, id, "start");
        await monitor.RunOnceAsync(confirmations: 2, Ct);
        (await client.GetFromJsonAsync<JsonElement>("/api/notifications", Ct)).GetProperty("open").GetInt32().ShouldBe(0);
        var all = await client.GetFromJsonAsync<JsonElement>("/api/notifications?all=true", Ct);
        all.GetProperty("items")[0].GetProperty("resolvedAt").ValueKind.ShouldNotBe(JsonValueKind.Null);
    }

    [Fact]
    public void Bewertung_ErkenntGestoerteInstanzZertifikatUndServer()
    {
        var tenant = new Tenant { Slug = "a", CompanyName = "A", Domain = "a.test", Version = "1.0.0", State = TenantState.Running, CreatedAt = DateTime.UtcNow.AddDays(-3) };
        var status = TenantStatus.Empty("a") with
        {
            CollectedAt = DateTime.UtcNow,
            Health = HealthLevel.Unhealthy,
            Problems = ["Dienst api läuft nicht.", "Sicherung: letzte erfolgreiche Sicherung vor 3 Tagen"],
        };
        var certificate = new CertificateInfo("a.test", DateTime.UtcNow, "expiring", DateTime.UtcNow.AddDays(5), "R11", 5, [], "Zertifikat läuft in 5 Tagen ab.");
        var findings = NotificationMonitor.Evaluate([new TenantObservation(tenant, status, certificate, false)], 0.02, false, "Socket fehlt");
        findings.Select(f => f.Key).ShouldBe(["host:docker", "host:disk", $"tenant:{tenant.Id}:health", $"tenant:{tenant.Id}:backup", $"tenant:{tenant.Id}:certificate"],
            ignoreOrder: true);
        findings.Single(f => f.Key == "host:disk").Severity.ShouldBe("critical");

        // Während eines laufenden Vorgangs (z. B. Aktualisierung) keine Meldung über den Zustand.
        NotificationMonitor.Evaluate([new TenantObservation(tenant, status, null, true)], 0.5, true, null).ShouldBeEmpty();

        // Frisch angelegt: fehlendes Zertifikat ist noch kein Befund.
        var fresh = new Tenant { Slug = "b", CompanyName = "B", Domain = "b.test", Version = "1.0.0", State = TenantState.Running };
        var missing = certificate with { Domain = "b.test", State = "missing", CheckedAt = DateTime.UtcNow };
        NotificationMonitor.Evaluate([new TenantObservation(fresh, TenantStatus.Empty("b") with { CollectedAt = DateTime.UtcNow, Health = HealthLevel.Healthy }, missing, false)],
            null, true, null).ShouldBeEmpty();
    }

    [Fact]
    public void Sicherungsliste_LiestResticJson()
    {
        using var temp = new TempDirectory();
        File.WriteAllText(Path.Combine(temp.Path, "snapshots.json"), """
            {"at":"2026-11-03T10:00:00Z","repositories":[{"key":"a1","label":"/repository","snapshots":[
              {"time":"2026-11-01T22:30:05.123456789+01:00","tree":"x","paths":["/tmp/estrichone-backup"],"hostname":"backup","tags":["estrichone"],"id":"4f1c2b3a9d8e7f60112233445566778899aabbccddeeff00112233445566778","short_id":"4f1c2b3a"},
              {"time":"2026-11-02T22:30:05Z","id":"5a6b7c8d00000000000000000000000000000000000000000000000000000000","short_id":"5a6b7c8d"},
              {"time":"kaputt","id":"zz"}]},
              {"key":"b2","label":"sftp:backup@***@host:/x","snapshots":[]}]}
            """);
        var list = SnapshotReader.Read(temp.Path);
        list.Count.ShouldBe(2);
        list[0].ShortId.ShouldBe("5a6b7c8d");
        list[1].Time.ShouldBe(new DateTime(2026, 11, 1, 21, 30, 5, DateTimeKind.Utc), TimeSpan.FromSeconds(1));
        SnapshotReader.IsSnapshotId("../../etc/passwd").ShouldBeFalse();
        SnapshotReader.IsSnapshotId("5a6b7c8d").ShouldBeTrue();
        SnapshotReader.Read(Path.Combine(temp.Path, "fehlt")).ShouldBeEmpty();
    }

    [Fact]
    public void Startpasswort_IstLangUndErfuelltSuiteRichtlinie()
    {
        var passwords = Enumerable.Range(0, 50).Select(_ => AccessLetters.GeneratePassword()).ToList();
        passwords.Distinct().Count().ShouldBe(50);
        passwords.ShouldAllBe(p => p.Length == 19 && p.Split('-').Length == 4);
    }
}
