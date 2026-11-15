using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Modules.AppUpdates;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.AppUpdates;

/// <summary>
/// „Was gibt's Neues“ nach der Anmeldung: Web nur „## Neu“, Apps nur „## Apps“; einmal pro Benutzer und Version,
/// geräteübergreifend gespeichert; neue Benutzer werden still als gesehen markiert.
/// </summary>
public sealed class WhatsNewTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string ServerVersion = "1.0.12";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "estrichone-whats-new", Guid.NewGuid().ToString("N"));
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(Path.Combine(_dir, "v1.0.9.md"), "# EstrichONE 1.0.9\n\n## Neu\n\n- Neun\n", Ct);
        await File.WriteAllTextAsync(Path.Combine(_dir, "v1.0.10.md"), "# EstrichONE 1.0.10\n\n## Verbessert\n\n- Zehn\n\n## Update\n\n- sudo update\n", Ct);
        await File.WriteAllTextAsync(Path.Combine(_dir, "v1.0.11.md"), "# EstrichONE 1.0.11\n\n## Apps\n\n- Elf in der App\n", Ct);
        await File.WriteAllTextAsync(Path.Combine(_dir, "v1.0.12.md"),
            "# EstrichONE 1.0.12\n\n## Neu\n\n- **Zwölf** im Web <script>alert(1)</script>\n\n## Apps\n\n- Zwölf in der App\n\n## Server aktualisieren\n\n```bash\nsudo /opt/estrichone/update.sh\n```\n", Ct);
        await File.WriteAllTextAsync(Path.Combine(_dir, "v1.0.13.md"), "# EstrichONE 1.0.13\n\n## Neu\n\n- Zukunft\n", Ct);
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
        {
            ["EstrichOne:AppUpdates:ReleaseNotesPath"] = _dir,
            ["EstrichOne:AppUpdates:ServerVersion"] = ServerVersion,
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_dir, recursive: true);
    }

    // Aufbereitung (ohne Server)

    [Fact]
    public void Web_NurAbschnittNeu_FettBleibt_LinksWerdenText()
    {
        var note = ReleaseNotes.ParseFor("1.0.26",
            "# EstrichONE 1.0.26\n\n## Neu\n\n- **Kennwerte** bearbeiten ([Handbuch](https://x))\n  mit `Code` und Fortsetzung\n- **offen\n\n## Apps\n\n- App\n\n## Server aktualisieren\n\n- sudo\n",
            ReleaseNoteAudience.Web);
        note.Title.ShouldBe("EstrichONE 1.0.26");
        note.Sections.Count.ShouldBe(1);
        note.Sections[0].Title.ShouldBe("Neu");
        note.Sections[0].Items.ShouldBe(["**Kennwerte** bearbeiten (Handbuch) mit Code und Fortsetzung", "offen"]);
    }

    [Fact]
    public void Web_OhneNeu_AlleAbschnitteFuerBenutzer_OhneAppsUndServer()
    {
        var note = ReleaseNotes.ParseFor("1.0.12", "## Module\n\n- M\n\n## Apps\n\n- A\n\n## Update\n\n- U\n", ReleaseNoteAudience.Web);
        note.Sections.Select(s => s.Title).ShouldBe(["Module"]);
    }

    [Fact]
    public void Apps_NurAppsAbschnitt_SonstNichts()
    {
        ReleaseNotes.ParseFor("1.0.20", "## Neu\n\n- Nur Web\n", ReleaseNoteAudience.Apps).Sections.ShouldBeEmpty();
        var both = ReleaseNotes.ParseFor("1.0.24", "## Neu\n\n- W\n\n## Apps\n\n- A\n\n## Android\n\n- D\n", ReleaseNoteAudience.Apps, "ios");
        both.Sections.Select(s => s.Title).ShouldBe(["Apps"]);
    }

    [Fact]
    public void Vorlage_PlatzhalterUndMehrzeiligeKommentareZaehlenNicht()
    {
        var note = ReleaseNotes.ParseFor("1.0.0", "## Neu\n\n- …\n\n## Apps\n\n<!-- Optional:\n     - kein Punkt -->\n\n- …\n", ReleaseNoteAudience.Apps);
        note.Sections.ShouldBeEmpty();
        ReleaseNotes.ParseFor("1.0.0", "## Neu\n\n- …\n", ReleaseNoteAudience.Web).Sections.ShouldBeEmpty();
    }

    [Fact]
    public void EchteVersionshinweise_ParsenOhneServerAnleitung()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "docs", "releases");
        if (!Directory.Exists(dir))
        {
            return;
        }

        // Vorabversionen (0.1.0-preview.*) erscheinen auf Servern ohne Vorabkennung nicht.
        foreach (var file in Directory.EnumerateFiles(dir, "v*.md").Where(f => !Path.GetFileName(f).Contains('-', StringComparison.Ordinal)))
        {
            var version = Path.GetFileNameWithoutExtension(file)[1..];
            var text = File.ReadAllText(file);
            foreach (var audience in new[] { ReleaseNoteAudience.Web, ReleaseNoteAudience.Apps })
            {
                var note = ReleaseNotes.ParseFor(version, text, audience);
                note.Sections.ShouldAllBe(s => !s.Title.Contains("Server", StringComparison.OrdinalIgnoreCase));
                note.Sections.SelectMany(s => s.Items).ShouldAllBe(i => !i.Contains("sudo ", StringComparison.Ordinal));
            }
        }
    }

    // Endpunkte

    [Fact]
    public async Task NeuerBenutzer_SiehtNichts_NachUpdate_EinmalDieNeuerungen()
    {
        var client = await _factory.CreateClientAsAsync("w.neu");

        var first = (await client.GetFromJsonAsync<JsonObject>("/api/v1/release-notes/whats-new", Ct))!;
        first["serverVersion"]!.GetValue<string>().ShouldBe(ServerVersion);
        first["lastSeenVersion"].ShouldBeNull();
        first["notes"]!.AsArray().ShouldBeEmpty();

        // Wie nach einem Server-Update: zuletzt gesehen war 1.0.8.
        (await client.PutAsJsonAsync("/api/v1/release-notes/seen", new { audience = "web", version = "1.0.8" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var after = (await client.GetFromJsonAsync<JsonObject>("/api/v1/release-notes/whats-new?audience=web", Ct))!;
        after["lastSeenVersion"]!.GetValue<string>().ShouldBe("1.0.8");
        var notes = after["notes"]!.AsArray();
        // Neueste zuerst; 1.0.11 hat nur „## Apps“, 1.0.13 ist neuer als der Server.
        notes.Select(n => n!["version"]!.GetValue<string>()).ShouldBe(["1.0.12", "1.0.10", "1.0.9"]);
        var item = notes[0]!["sections"]![0]!["items"]![0]!.GetValue<string>();
        item.ShouldStartWith("**Zwölf** im Web");

        // Erneut abrufen: noch nicht bestätigt, also weiterhin da.
        (await client.GetFromJsonAsync<JsonObject>("/api/v1/release-notes/whats-new", Ct))!["notes"]!.AsArray().Count.ShouldBe(3);

        // „Verstanden“ – danach nichts mehr, auch nicht auf einem anderen Gerät (zweite Anmeldung).
        (await client.PutAsJsonAsync("/api/v1/release-notes/seen", new { audience = "web" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var other = _factory.CreateClient().WithToken((await _factory.CreateClient().LoginOkAsync("w.neu")).AccessToken);
        (await other.GetFromJsonAsync<JsonObject>("/api/v1/release-notes/whats-new", Ct))!["notes"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Apps_EigenerStand_OhneAppsAbschnittWirdStillWeitergezaehlt()
    {
        var client = await _factory.CreateClientAsAsync("a.neu");
        await client.GetFromJsonAsync<JsonObject>("/api/v1/release-notes/whats-new?audience=apps&platform=ios", Ct);
        (await client.PutAsJsonAsync("/api/v1/release-notes/seen", new { audience = "apps", version = "1.0.10" }, Ct)).EnsureSuccessStatusCode();

        var apps = (await client.GetFromJsonAsync<JsonObject>("/api/v1/release-notes/whats-new?audience=apps&platform=ios", Ct))!;
        apps["notes"]!.AsArray().Select(n => n!["version"]!.GetValue<string>()).ShouldBe(["1.0.12", "1.0.11"]);
        // Das Web hat einen eigenen Stand (neu: still gesehen).
        (await client.GetFromJsonAsync<JsonObject>("/api/v1/release-notes/whats-new?audience=web", Ct))!["notes"]!.AsArray().ShouldBeEmpty();

        // Zwischen 1.0.12 und 1.0.12 nichts; ab 1.0.11 bis 1.0.12 gibt es für Apps 1.0.12.
        (await client.PutAsJsonAsync("/api/v1/release-notes/seen", new { audience = "apps", version = "1.0.9" }, Ct)).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync("/api/v1/release-notes/seen", new { audience = "apps", version = "1.0.12" }, Ct)).EnsureSuccessStatusCode();
        (await client.GetFromJsonAsync<JsonObject>("/api/v1/release-notes/whats-new?audience=apps", Ct))!["notes"]!.AsArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Liste_NeuesteZuerst_SinceUndLimit()
    {
        var client = await _factory.CreateClientAsAsync("l.liste");
        var recent = (await client.GetFromJsonAsync<JsonArray>("/api/v1/release-notes?limit=2", Ct))!;
        recent.Select(n => n!["version"]!.GetValue<string>()).ShouldBe(["1.0.12", "1.0.10"]);
        var since = (await client.GetFromJsonAsync<JsonArray>("/api/v1/release-notes?since=1.0.10&audience=apps", Ct))!;
        since.Select(n => n!["version"]!.GetValue<string>()).ShouldBe(["1.0.12", "1.0.11"]);
        // Server-Anleitung fehlt immer.
        since.ToJsonString().ShouldNotContain("update.sh");
    }

    [Theory]
    [InlineData("/api/v1/release-notes?since=abc")]
    [InlineData("/api/v1/release-notes?audience=desktop")]
    [InlineData("/api/v1/release-notes/whats-new?platform=windows")]
    public async Task UngueltigeAngaben_400(string url)
    {
        var client = await _factory.CreateClientAsAsync($"u.{Guid.NewGuid():N}"[..12]);
        (await client.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Gesehen_NichtNeuerAlsServer()
    {
        var client = await _factory.CreateClientAsAsync("g.zukunft");
        (await client.PutAsJsonAsync("/api/v1/release-notes/seen", new { version = "1.0.13" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync("/api/v1/release-notes/seen", new { version = "kaputt" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OhneAnmeldung_401()
    {
        var client = _factory.CreateClient();
        (await client.GetAsync("/api/v1/release-notes/whats-new", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
