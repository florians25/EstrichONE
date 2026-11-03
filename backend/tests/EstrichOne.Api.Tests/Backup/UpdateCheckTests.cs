using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Backup;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Backup;

/// <summary>Hinweis „Sicherheitsupdate verfügbar“: Release-Feed, Versionsregeln wie update.sh, Stand der Automatik vom Server.</summary>
public sealed class UpdateCheckTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "estrichone-updates", Guid.NewGuid().ToString("N"));
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Feed => Path.Combine(_dir, "feed.json");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "status"));
        await WriteFeedAsync(("v0.1.0-preview.23", true, "Bisher", false));
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
        {
            ["EstrichOne:Backup:StatusPath"] = Path.Combine(_dir, "status"),
            ["EstrichOne:Updates:Feed"] = new Uri(Feed).AbsoluteUri,
            ["EstrichOne:Updates:InstalledVersion"] = "0.1.0-preview.23",
            ["EstrichOne:Health:Worker"] = "false",
            ["EstrichOne:Archive:Worker"] = "false",
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_dir, recursive: true);
    }

    private Task WriteFeedAsync(params (string Tag, bool Pre, string Body, bool Draft)[] releases) =>
        File.WriteAllTextAsync(Feed, System.Text.Json.JsonSerializer.Serialize(releases.Select(r => new Dictionary<string, object>
        {
            ["tag_name"] = r.Tag, ["name"] = "EstrichONE " + r.Tag[1..], ["body"] = r.Body, ["prerelease"] = r.Pre, ["draft"] = r.Draft,
            ["html_url"] = "https://github.com/florians25/EstrichONE/releases/tag/" + r.Tag, ["published_at"] = "2026-11-01T10:00:00Z",
        })), Ct);

    [Theory]
    [InlineData("0.1.0-preview.9", "0.1.0-preview.10", -1)]
    [InlineData("0.1.0-preview.23", "0.1.0", -1)]
    [InlineData("0.1.0", "0.1.1", -1)]
    [InlineData("0.2.0", "0.1.9", 1)]
    [InlineData("v0.1.0", "0.1.0", 0)]
    [InlineData("0.1.0-alpha", "0.1.0-alpha.1", -1)]
    [InlineData("0.1.0-alpha.beta", "0.1.0-alpha.1", 1)]
    public void Versionsvergleich_WieSemanticVersioning(string a, string b, int expected)
    {
        SemanticVersion.Compare(a, b).ShouldBe(expected);
        SemanticVersion.Compare(b, a).ShouldBe(-expected);
    }

    [Fact]
    public void Kandidat_NurGleicheVersionslinie_VorabversionenNurAusVorabversion()
    {
        var releases = new[]
        {
            new UpdateRelease("0.2.0", false, true, null, null, null),
            new UpdateRelease("0.1.2-rc.1", true, false, null, null, null),
            new UpdateRelease("0.1.1", false, true, null, null, null),
            new UpdateRelease("0.1.0", false, false, null, null, null),
        };
        var (stable, security) = SemanticVersion.Candidate("0.1.0", releases);
        stable!.Version.ShouldBe("0.1.1");
        security.ShouldBeTrue();
        SemanticVersion.Candidate("0.1.1", releases).Release.ShouldBeNull();
        SemanticVersion.Candidate("0.1.1-rc.1", releases).Release!.Version.ShouldBe("0.1.2-rc.1");
    }

    [Fact]
    public async Task Status_NurFuerBackupAdmins_MeldetSicherheitsupdateUndAutomatik()
    {
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        (await worker.GetAsync("/api/v1/system/updates", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync("/api/v1/system/updates", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var admin = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        var none = await admin.GetFromJsonAsync<UpdateStatusResponse>("/api/v1/system/updates", TestJson.Options, Ct);
        none!.Installed.ShouldBe("0.1.0-preview.23");
        none.Available.ShouldBeNull();
        none.SecurityUpdate.ShouldBeFalse();
        none.Source.ShouldBe("feed");
        none.Auto.ShouldBeNull();

        // Neuer Feed: preview.24 behebt eine Lücke, preview.25 nur Fehler, 0.2.0 ist eine neue Versionslinie, ein Entwurf zählt nicht.
        await WriteFeedAsync(("v0.2.0", false, "Neue Funktionen", false), ("v0.1.0-preview.26", true, "## Sicherheitsupdate", true),
            ("v0.1.0-preview.25", true, "Fehlerbehebungen", false), ("v0.1.0-preview.24", true, "## Sicherheitsupdate\nAnmeldung gehärtet", false),
            ("v0.1.0-preview.23", true, "Bisher", false));
        await File.WriteAllTextAsync(Path.Combine(_dir, "status", "update.json"),
            """{"checkedAt":"2026-11-03T02:10:00Z","installed":"0.1.0-preview.23","available":"0.1.0-preview.25","securityUpdate":true,"autoUpdate":"sicherheit","window":"02:00-05:00","lastAutoUpdate":{"at":"2026-11-02T02:10:00Z","from":"0.1.0-preview.22","to":"0.1.0-preview.23","result":"succeeded","message":"Automatisch aktualisiert auf 0.1.0-preview.23."}}""",
            Ct);
        var cached = await admin.GetFromJsonAsync<UpdateStatusResponse>("/api/v1/system/updates", TestJson.Options, Ct);
        cached!.Available.ShouldBeNull(); // Feed zwischengespeichert
        cached.Auto!.Mode.ShouldBe("sicherheit");

        var status = await admin.GetFromJsonAsync<UpdateStatusResponse>("/api/v1/system/updates?refresh=true", TestJson.Options, Ct);
        status!.Available.ShouldBe("0.1.0-preview.25");
        status.SecurityUpdate.ShouldBeTrue();
        status.Latest.ShouldBe("0.2.0");
        status.ReleaseUrl.ShouldBe("https://github.com/florians25/EstrichONE/releases/tag/v0.1.0-preview.25");
        status.Command.ShouldBe("sudo /opt/estrichone/update.sh 0.1.0-preview.25");
        status.Auto!.Window.ShouldBe("02:00-05:00");
        status.Auto.Result.ShouldBe("succeeded");
        status.Auto.LastTo.ShouldBe("0.1.0-preview.23");

        // Feed weg (kein Internet): letzter Stand bleibt; ohne bisherigen Feed zählt die Prüfung durch update.sh.
        File.Delete(Feed);
        var offline = await admin.GetFromJsonAsync<UpdateStatusResponse>("/api/v1/system/updates?refresh=true", TestJson.Options, Ct);
        offline!.Available.ShouldBe("0.1.0-preview.25");
        offline.Source.ShouldBe("feed");
    }

    [Fact]
    public async Task OhneFeed_ZeigtDenStandVonUpdateSh()
    {
        await using var factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
        {
            ["EstrichOne:Backup:StatusPath"] = Path.Combine(_dir, "status"),
            ["EstrichOne:Updates:Enabled"] = "false",
            ["EstrichOne:Updates:InstalledVersion"] = "0.1.0-preview.23",
            ["EstrichOne:Health:Worker"] = "false",
            ["EstrichOne:Archive:Worker"] = "false",
        });
        await File.WriteAllTextAsync(Path.Combine(_dir, "status", "update.json"),
            """{"checkedAt":"2026-11-03T02:10:00Z","installed":"0.1.0-preview.23","available":"0.1.0-preview.24","securityUpdate":true,"autoUpdate":"aus","window":"02:00-05:00","lastAutoUpdate":null}""",
            Ct);
        using var admin = await factory.CreateClientAsAsync("it2", StandardRoles.ItAdministratorId);
        var status = await admin.GetFromJsonAsync<UpdateStatusResponse>("/api/v1/system/updates", TestJson.Options, Ct);
        status!.Source.ShouldBe("server");
        status.Available.ShouldBe("0.1.0-preview.24");
        status.SecurityUpdate.ShouldBeTrue();
        status.Auto!.Mode.ShouldBe("aus");
        status.Auto.Result.ShouldBeNull();
    }
}
