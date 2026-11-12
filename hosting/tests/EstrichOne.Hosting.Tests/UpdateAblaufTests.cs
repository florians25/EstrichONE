using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EstrichOne.Hosting.Tests;

/// <summary>Geordneter Update-Ablauf je Instanz: Ankündigung, Update, Abschluss; Fehlschlag mit Rücknahme; Phase am Vorgang.</summary>
public sealed class UpdateAblaufTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] ShortPhases =
        ["--Hosting:UpdateAnnounceSeconds=1", "--Hosting:UpdateMinimumSeconds=1", "--Hosting:UpdateFinishSeconds=1"];

    private static async Task<JsonElement> WaitAsync(HttpClient client, string operationId, Action<JsonElement>? onPoll = null)
    {
        for (var i = 0; i < 600; i++)
        {
            var operation = await client.GetFromJsonAsync<JsonElement>($"/api/operations/{operationId}", Ct);
            onPoll?.Invoke(operation);
            if (operation.GetProperty("status").GetString() is "Succeeded" or "Failed")
            {
                return operation;
            }

            await Task.Delay(50, Ct);
        }

        throw new TimeoutException("Vorgang nicht abgeschlossen.");
    }

    private static async Task<string> CreateAsync(HttpClient client, string slug)
    {
        var plans = await client.GetFromJsonAsync<JsonElement>("/api/plans", Ct);
        var response = await client.PostAsJsonAsync("/api/tenants", new
        {
            companyName = "Estrich Update GmbH", contactEmail = "info@update.de", slug, planId = plans[0].GetProperty("id").GetString(),
            version = "0.1.0-preview.4",
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        (await WaitAsync(client, created.GetProperty("openOperation").GetProperty("id").GetString()!)).GetProperty("status").GetString().ShouldBe("Succeeded");
        return created.GetProperty("id").GetString()!;
    }

    private static string StateFile(ConsoleApp console, string slug) =>
        Path.Combine(console.Root, "instances", slug, "data", "backup-status", "update-maintenance.json");

    [Fact]
    public async Task Aktualisierung_DurchlaeuftAnkuendigungUpdateAbschluss_UndGibtFrei()
    {
        await using var console = await ConsoleApp.StartAsync(settings: ShortPhases);
        var client = await console.AdminClientAsync();
        var id = await CreateAsync(client, "phasen");
        var file = StateFile(console, "phasen");

        // Was die Suite während der Compose-Aufrufe liest: Sicherung und Austausch laufen in der Update-Phase.
        var seen = new ConcurrentQueue<string>();
        console.Docker.OnCompose = (_, args) =>
        {
            if (args.Count > 0 && args[0] is "up" or "exec" && File.Exists(file))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                seen.Enqueue($"{args[0]}:{doc.RootElement.GetProperty("phase").GetString()}:{doc.RootElement.GetProperty("targetVersion").GetString()}");
            }
        };

        var started = DateTime.UtcNow;
        var response = await client.PostAsJsonAsync($"/api/tenants/{id}/actions/update", new { version = "0.1.0", backupFirst = true }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var phases = new ConcurrentQueue<string>();
        var operation = await WaitAsync(client, (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!, op =>
        {
            if (op.TryGetProperty("phase", out var p) && p.GetString() is { } phase && (phases.IsEmpty || phases.Last() != phase))
            {
                phases.Enqueue(phase);
            }
        });

        var log = operation.GetProperty("log").GetString()!;
        operation.GetProperty("status").GetString().ShouldBe("Succeeded", log);
        (DateTime.UtcNow - started).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(3));
        log.ShouldContain("Phase 1/3 – Ankündigung");
        log.ShouldContain("Phase 2/3 – Update");
        log.ShouldContain("Phase 3/3 – Abschluss");
        log.IndexOf("Phase 2/3", StringComparison.Ordinal).ShouldBeLessThan(log.IndexOf("Vorab-Sicherung", StringComparison.Ordinal));
        seen.ShouldContain("exec:update:0.1.0");
        seen.ShouldContain("up:update:0.1.0");
        phases.ShouldContain("announce");
        phases.ShouldContain("finish");
        operation.GetProperty("phase").GetString().ShouldBe("released");
        File.Exists(file).ShouldBeFalse();
    }

    [Fact]
    public async Task Fehlschlag_MitRuecknahme_ZeigtStatus_UndGibtFrei()
    {
        await using var console = await ConsoleApp.StartAsync(settings: ShortPhases);
        var client = await console.AdminClientAsync();
        var id = await CreateAsync(client, "fehler");
        var file = StateFile(console, "fehler");
        var seen = new ConcurrentQueue<string>();
        console.Docker.OnCompose = (_, args) =>
        {
            if (File.Exists(file))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                seen.Enqueue(doc.RootElement.GetProperty("phase").GetString()!);
            }
        };

        var response = await client.PostAsJsonAsync($"/api/tenants/{id}/actions/update", new { version = "0.2.0-startabbruch", backupFirst = true }, Ct);
        var operation = await WaitAsync(client, (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!);
        var log = operation.GetProperty("log").GetString()!;
        operation.GetProperty("status").GetString().ShouldBe("Failed", log);
        operation.GetProperty("error").GetString()!.ShouldContain("zurückgesetzt");
        seen.ShouldContain("rollback");
        log.ShouldContain("Update fehlgeschlagen");
        operation.GetProperty("phase").GetString().ShouldBe("released");
        File.Exists(file).ShouldBeFalse();
    }

    [Fact]
    public async Task HaengengebliebeneWartung_EndetMitNeustart()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var id = await CreateAsync(client, "haengt");
        var file = StateFile(console, "haengt");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, """{"id":"x","phase":"failed","startedAt":"2026-11-12T10:00:00Z"}""", Ct);

        var response = await client.PostAsJsonAsync($"/api/tenants/{id}/actions/restart", new { }, Ct);
        var operation = await WaitAsync(client, (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!);
        operation.GetProperty("status").GetString().ShouldBe("Succeeded");
        operation.GetProperty("log").GetString()!.ShouldContain("Wartungsbildschirm eines abgebrochenen Updates beendet");
        File.Exists(file).ShouldBeFalse();
    }
}
