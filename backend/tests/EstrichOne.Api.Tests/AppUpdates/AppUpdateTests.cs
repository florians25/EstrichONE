using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Http;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.AppUpdates;
using EstrichOne.Api.Modules.Devices;
using EstrichOne.Api.Modules.Organization.Exports;
using EstrichOne.Api.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Tests.AppUpdates;

/// <summary>
/// App-Updates (docs/app-updates.md): Versionsregeln, Update-Prüfung, Auslieferung des Android-Pakets mit Prüfsumme,
/// Mindestversion mit 426 CLIENT_UPDATE_REQUIRED, App-Version der Geräte, „Neu in dieser Version“.
/// </summary>
public sealed class AppUpdateTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string ServerVersion = "1.0.12";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "estrichone-app-updates", Guid.NewGuid().ToString("N"));
    private readonly byte[] _apk = RandomNumberGenerator.GetBytes(300_000);
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Packages => Path.Combine(_dir, "packages");

    private string Notes => Path.Combine(_dir, "releases");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Packages);
        Directory.CreateDirectory(Notes);
        await File.WriteAllBytesAsync(Path.Combine(Packages, "EstrichONE-Android-v1.0.12.apk"), _apk, Ct);
        await File.WriteAllTextAsync(Path.Combine(Packages, AppPackageCatalog.ManifestFile), JsonSerializer.Serialize(new
        {
            version = ServerVersion, versionCode = 10012, file = "EstrichONE-Android-v1.0.12.apk", sha256 = Sha(_apk), size = _apk.Length,
            releaseSigned = true, signerSha256 = "ab12",
        }), Ct);
        await File.WriteAllTextAsync(Path.Combine(Notes, "v1.0.10.md"), "# EstrichONE 1.0.10\n\n## Bauakten\n\n- Zehn\n", Ct);
        await File.WriteAllTextAsync(Path.Combine(Notes, "v1.0.11.md"), "# EstrichONE 1.0.11\n\n## Apps\n\n- Elf in der App\n\n## Web\n\n- Nur Web\n", Ct);
        await File.WriteAllTextAsync(Path.Combine(Notes, "v1.0.12.md"),
            "# EstrichONE 1.0.12\n\n## Module\n\n- **Module** schaltbar ([Handbuch](x))\n  mit Fortsetzung\n\n## Update\n\n```bash\nsudo /opt/estrichone/update.sh 1.0.12\n```\n", Ct);
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
        {
            ["EstrichOne:AppUpdates:PackagePath"] = Packages,
            ["EstrichOne:AppUpdates:ReleaseNotesPath"] = Notes,
            ["EstrichOne:AppUpdates:ServerVersion"] = ServerVersion,
            ["EstrichOne:MinClientVersion"] = "1.0.0",
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_dir, recursive: true);
    }

    private static string Sha(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    // Regeln (ohne Server)

    [Theory]
    [InlineData("1.0.9", "1.0.10", -1)]
    [InlineData("1.1.0-preview.2", "1.1.0", -1)]
    [InlineData("v1.0.5+build.7", "1.0.5", 0)]
    [InlineData("2", "1.9.9", 1)]
    public void Versionsvergleich(string a, string b, int expected)
    {
        VersionComparer.Compare(a, b).ShouldBe(expected);
        VersionComparer.Compare(b, a).ShouldBe(-expected);
    }

    [Theory]
    [InlineData("android/1.0.5", "android", "1.0.5")]
    [InlineData("iOS/1.2.0 (45)", "ios", "1.2.0")]
    [InlineData("android/v1.1.0-preview.3", "android", "1.1.0-preview.3")]
    [InlineData("windows/1.0.0", null, null)]
    [InlineData("android/neu", null, null)]
    [InlineData("", null, null)]
    public void Kennung_DerApp(string header, string? platform, string? version)
    {
        var client = ClientIdentity.Parse(header);
        client?.Platform.ShouldBe(platform);
        client?.Version.ShouldBe(version);
        (client is null).ShouldBe(platform is null);
    }

    private static AndroidPackage Package(string version = "1.0.12") =>
        new(new AndroidPackageManifest(version, 10012, "a.apk", "00", 1, true, null), "a.apk", null);

    [Fact]
    public void Policy_HinweisSperreUndNeuerAlsServer()
    {
        var settings = new AppUpdateSettings();
        var android = AppUpdatePolicy.ForPlatform("android", "1.0.12", "1.0.0", settings, Package(), null);
        android.LatestVersion.ShouldBe("1.0.12");
        android.MinimumVersion.ShouldBe("1.0.0");
        android.Download.Kind.ShouldBe("package");

        AppUpdatePolicy.Evaluate("0.9.0", android, "1.0.12", recommend: true).ShouldBe(AppUpdateState.Required);
        AppUpdatePolicy.Evaluate("1.0.10", android, "1.0.12", recommend: true).ShouldBe(AppUpdateState.Recommended);
        AppUpdatePolicy.Evaluate("1.0.10", android, "1.0.12", recommend: false).ShouldBe(AppUpdateState.Current);
        AppUpdatePolicy.Evaluate("1.0.12", android, "1.0.12", recommend: true).ShouldBe(AppUpdateState.Current);
        AppUpdatePolicy.Evaluate("1.1.0", android, "1.0.12", recommend: true).ShouldBe(AppUpdateState.NewerThanServer);
    }

    [Fact]
    public void Policy_ErzwungeneMindestversion_NieUeberDerVerfuegbaren()
    {
        var enforce = new AppUpdateSettings { EnforceMinimum = true };
        AppUpdatePolicy.ForPlatform("android", "1.0.12", "1.0.0", enforce, Package(), null).MinimumVersion.ShouldBe("1.0.12");
        // Paket älter als der Server (z. B. Server ohne neues APK): Mindestversion = was bereitsteht.
        AppUpdatePolicy.ForPlatform("android", "1.0.12", "1.0.0", enforce, Package("1.0.11"), null).MinimumVersion.ShouldBe("1.0.11");
        // Ohne jede Bezugsquelle wird nichts erzwungen.
        var none = AppUpdatePolicy.ForPlatform("android", "1.0.12", "1.0.0", enforce, null, null);
        none.Download.Kind.ShouldBe("none");
        none.MinimumVersion.ShouldBe("1.0.0");
        // iOS: ohne Vorabkennung, Version aus den Einstellungen begrenzt die Sperre.
        var ios = AppUpdatePolicy.ForPlatform("ios", "1.1.0-preview.2", "1.0.0",
            enforce with { MinimumVersion = "1.1.0", IosLatestVersion = "1.0.12", IosUpdateUrl = "https://testflight.apple.com/join/abc" }, null, null);
        ios.LatestVersion.ShouldBe("1.0.12");
        ios.MinimumVersion.ShouldBe("1.0.12");
        ios.Download.ShouldBe(new AppDownload("store", "https://testflight.apple.com/join/abc"));
        // Technische Mindestversion des Servers gilt immer.
        AppUpdatePolicy.ForPlatform("android", "1.0.12", "1.0.5", new AppUpdateSettings(), null, null).MinimumVersion.ShouldBe("1.0.5");
    }

    [Fact]
    public void Policy_AndroidOhnePaket_LinkAufDasRelease()
    {
        var info = AppUpdatePolicy.ForPlatform("android", "1.0.12", "1.0.0", new AppUpdateSettings(), null, "https://example.org/v{version}/app.apk");
        info.Download.ShouldBe(new AppDownload("link", "https://example.org/v1.0.12/app.apk"));
        var own = AppUpdatePolicy.ForPlatform("android", "1.0.12", "1.0.0", new AppUpdateSettings { AndroidUpdateUrl = "https://mdm.firma.de/estrichone" }, Package(), null);
        own.Download.Kind.ShouldBe("link");
    }

    [Fact]
    public void Einstellungen_WerdenBereinigt()
    {
        var s = new AppUpdateSettings { MinimumVersion = " v1.0.5 ", IosUpdateUrl = "http://unsicher", AndroidUpdateUrl = "javascript:alert(1)", IosLatestVersion = "neu" }.Normalize();
        s.MinimumVersion.ShouldBe("1.0.5");
        s.IosUpdateUrl.ShouldBeNull();
        s.AndroidUpdateUrl.ShouldBeNull();
        s.IosLatestVersion.ShouldBeNull();
    }

    [Fact]
    public void Versionshinweise_NurInhalteFuerBenutzer()
    {
        var note = ReleaseNotes.Parse("1.0.12", "# EstrichONE 1.0.12\n\n## Module\n\n- **Fett** und [Link](https://x)\n  weiter\n\n## Server aktualisieren\n\n- sudo …\n\n```bash\n- kein Punkt\n```\n");
        note.Title.ShouldBe("EstrichONE 1.0.12");
        note.Sections.Count.ShouldBe(1);
        note.Sections[0].Items.ShouldBe(["Fett und Link weiter"]);

        var apps = ReleaseNotes.Parse("1.0.11", "## Web\n\n- W\n\n## iPhone und iPad\n\n- I\n\n## Android\n\n- A\n", "android");
        apps.Sections.Select(s => s.Title).ShouldBe(["Android"]);
    }

    [Fact]
    public void Paketpruefung_ErkenntBeschaedigteDatei()
    {
        var path = Path.Combine(_dir, "kaputt.apk");
        File.WriteAllBytes(path, [1, 2, 3]);
        AppPackageCatalog.Verify(path, new AndroidPackageManifest("1.0.0", 1, "kaputt.apk", Sha([1, 2, 4]), 3, false, null)).ShouldNotBeNull();
        AppPackageCatalog.Verify(path, new AndroidPackageManifest("1.0.0", 1, "kaputt.apk", Sha([1, 2, 3]), 4, false, null)).ShouldNotBeNull();
        AppPackageCatalog.Verify(path, new AndroidPackageManifest("1.0.0", 1, "kaputt.apk", Sha([1, 2, 3]), 3, false, null)).ShouldBeNull();
        AppPackageCatalog.Verify(Path.Combine(_dir, "fehlt.apk"), new AndroidPackageManifest("1.0.0", 1, "x", "00", 0, false, null)).ShouldNotBeNull();
    }

    [Fact]
    public void Paketkatalog_LiefertBeschaedigtesPaketNichtAus()
    {
        var dir = Path.Combine(_dir, "manipuliert");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "app.apk"), [9, 9, 9]);
        File.WriteAllText(Path.Combine(dir, AppPackageCatalog.ManifestFile),
            JsonSerializer.Serialize(new { version = "1.0.12", versionCode = 10012, file = "app.apk", sha256 = Sha([1]), size = 3 }));
        var catalog = new AppPackageCatalog(Options.Create(new AppUpdateOptions { PackagePath = dir }), NullLogger<AppPackageCatalog>.Instance);
        catalog.Android()!.IsValid.ShouldBeFalse();
        AppUpdatePolicy.ForPlatform("android", "1.0.12", "1.0.0", new AppUpdateSettings(), catalog.Android(), null).Download.Kind.ShouldBe("none");
    }

    // Über die API

    [Fact]
    public async Task Pruefung_AeltereApp_BekommtHinweisPaketUndNeuerungen()
    {
        using var client = _factory.CreateClient();
        var check = (await client.GetFromJsonAsync<JsonObject>("/api/v1/app-updates/check?platform=android&version=1.0.10", Ct))!;

        check["status"]!.GetValue<string>().ShouldBe("recommended");
        check["latestVersion"]!.GetValue<string>().ShouldBe("1.0.12");
        check["minimumVersion"]!.GetValue<string>().ShouldBe("1.0.0");
        check["download"]!["kind"]!.GetValue<string>().ShouldBe("package");
        check["download"]!["url"]!.GetValue<string>().ShouldBe("/api/v1/app-updates/android/package");
        check["download"]!["sha256"]!.GetValue<string>().ShouldBe(Sha(_apk));
        check["download"]!["releaseSigned"]!.GetValue<bool>().ShouldBeTrue();
        var notes = check["releaseNotes"]!.AsArray();
        notes.Select(n => n!["version"]!.GetValue<string>()).ShouldBe(["1.0.12", "1.0.11"]);
        notes[1]!["sections"]!.AsArray().Select(s => s!["title"]!.GetValue<string>()).ShouldBe(["Apps"]);

        var ios = (await client.GetFromJsonAsync<JsonObject>("/api/v1/app-updates/check?platform=ios&version=1.0.12", Ct))!;
        ios["status"]!.GetValue<string>().ShouldBe("current");
        ios["download"]!["kind"]!.GetValue<string>().ShouldBe("store");

        var newer = (await client.GetFromJsonAsync<JsonObject>("/api/v1/app-updates/check?platform=ios&version=1.1.0", Ct))!;
        newer["status"]!.GetValue<string>().ShouldBe("newerThanServer");

        using var invalid = await client.GetAsync(new Uri("/api/v1/app-updates/check?platform=windows&version=1", UriKind.Relative), Ct);
        (await invalid.ReadProblemAsync()).Code.ShouldBe("APP_VERSION_INVALID");
    }

    [Fact]
    public async Task NeuInDieserVersion_ZwischenZweiVersionen()
    {
        using var client = _factory.CreateClient();
        var notes = (await client.GetFromJsonAsync<JsonArray>("/api/v1/app-updates/release-notes?platform=ios&from=1.0.9&to=1.0.12", Ct))!;
        notes.Select(n => n!["version"]!.GetValue<string>()).ShouldBe(["1.0.12", "1.0.11", "1.0.10"]);
        var only = (await client.GetFromJsonAsync<JsonArray>("/api/v1/app-updates/release-notes?to=1.0.12", Ct))!;
        only.Count.ShouldBe(1);
        only[0]!["sections"]![0]!["items"]![0]!.GetValue<string>().ShouldBe("Module schaltbar (Handbuch) mit Fortsetzung");
    }

    [Fact]
    public async Task Paket_WirdMitPruefsummeUndFortsetzbarAusgeliefert()
    {
        using var client = _factory.CreateClient();
        using var full = await client.GetAsync(new Uri(AppUpdatePolicy.PackagePath, UriKind.Relative), Ct);
        full.StatusCode.ShouldBe(HttpStatusCode.OK);
        full.Content.Headers.ContentType!.MediaType.ShouldBe("application/vnd.android.package-archive");
        full.Headers.GetValues("X-Checksum-SHA256").Single().ShouldBe(Sha(_apk));
        Sha(await full.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(Sha(_apk));

        using var request = new HttpRequestMessage(HttpMethod.Get, AppUpdatePolicy.PackagePath);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(100_000, null);
        using var rest = await client.SendAsync(request, Ct);
        rest.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        (await rest.Content.ReadAsByteArrayAsync(Ct)).ShouldBe(_apk[100_000..]);
    }

    [Fact]
    public async Task Mindestversion_Erzwungen_Liefert426MitCode()
    {
        using var admin = await _factory.CreateClientAsAsync("it", StandardRoles.AdministratorId);
        await SaveSettingsAsync(admin, new JsonObject { ["enforceMinimum"] = true, ["recommendUpdates"] = true, ["message"] = "Bitte bis Freitag" });

        using var old = await _factory.CreateClientAsAsync("alt", StandardRoles.AdministratorId);
        old.DefaultRequestHeaders.Add(ClientIdentity.HeaderName, "android/1.0.10");
        using var blocked = await old.GetAsync(new Uri("/api/v1/settings", UriKind.Relative), Ct);
        blocked.StatusCode.ShouldBe(HttpStatusCode.UpgradeRequired);
        var problem = (await blocked.Content.ReadFromJsonAsync<JsonObject>(Ct))!;
        problem["code"]!.GetValue<string>().ShouldBe(ClientVersionGate.Code);
        problem["minimumVersion"]!.GetValue<string>().ShouldBe("1.0.12");

        // Serverinfo und Update-Prüfung bleiben erreichbar und nennen die Mindestversion.
        var info = (await old.GetFromJsonAsync<JsonObject>("/api/v1/server-info", Ct))!;
        info["minClientVersion"]!.GetValue<string>().ShouldBe("1.0.12");
        var check = (await old.GetFromJsonAsync<JsonObject>("/api/v1/app-updates/check", Ct))!;
        check["status"]!.GetValue<string>().ShouldBe("required");
        check["message"]!.GetValue<string>().ShouldBe("Bitte bis Freitag");
        (await old.GetAsync(new Uri(AppUpdatePolicy.PackagePath, UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Aktuelle App, Web (ohne Kennung) und iOS mit eigener verfügbarer Version arbeiten weiter.
        using var current = await _factory.CreateClientAsAsync("neu", StandardRoles.AdministratorId);
        current.DefaultRequestHeaders.Add(ClientIdentity.HeaderName, "android/1.0.12");
        (await current.GetAsync(new Uri("/api/v1/settings", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetAsync(new Uri("/api/v1/settings", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var web = (await admin.GetFromJsonAsync<JsonObject>("/api/v1/server-info", Ct))!;
        web["minClientVersion"]!.GetValue<string>().ShouldBe("1.0.0");
    }

    [Fact]
    public async Task Geraeteuebersicht_FuehrtAppVersionNachUndFordertZumUpdateAuf()
    {
        await _factory.CreateUserAsync("baustelle", roleIds: StandardRoles.SiteWorkerId);
        using var phone = _factory.CreateClient();
        var tokens = await phone.LoginOkAsync("baustelle", clientKind: "android");
        phone.WithToken(tokens.AccessToken);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var registered = await phone.PostAsJsonAsync("/api/v1/devices/register", new
        {
            name = "Pixel von Max", platform = "android", kind = "personal", publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
            appVersion = "1.0.9",
        }, Ct);
        var device = (await registered.Content.ReadFromJsonAsync<DeviceResponse>(TestJson.Options, Ct))!;

        // Nach dem Update meldet die App die neue Version mit jeder Anfrage.
        phone.DefaultRequestHeaders.Add(ClientIdentity.HeaderName, "android/1.0.11");
        (await phone.GetAsync(new Uri("/api/v1/devices/me", UriKind.Relative), Ct)).EnsureSuccessStatusCode();

        using var admin = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        var overview = (await admin.GetFromJsonAsync<JsonObject>("/api/v1/app-updates/overview", Ct))!;
        var row = overview["devices"]!.AsArray().Single(d => d!["id"]!.GetValue<Guid>() == device.Id)!;
        row["appVersion"]!.GetValue<string>().ShouldBe("1.0.11");
        row["state"]!.GetValue<string>().ShouldBe("recommended");
        row["assignedUserName"].ShouldNotBeNull();
        overview["counts"]!["recommended"]!.GetValue<int>().ShouldBe(1);
        overview["androidPackage"]!["valid"]!.GetValue<bool>().ShouldBeTrue();

        using var requested = await admin.PostAsync(new Uri($"/api/v1/app-updates/devices/{device.Id}/request-update", UriKind.Relative), null, Ct);
        requested.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var inbox = await phone.GetStringAsync(new Uri("/api/v1/notifications", UriKind.Relative), Ct);
        inbox.ShouldContain("Bitte EstrichONE auf Version 1.0.12 aktualisieren");

        phone.DefaultRequestHeaders.Remove(ClientIdentity.HeaderName);
        phone.DefaultRequestHeaders.Add(ClientIdentity.HeaderName, "android/1.0.12");
        (await phone.GetAsync(new Uri("/api/v1/devices/me", UriKind.Relative), Ct)).EnsureSuccessStatusCode();
        using var again = await admin.PostAsync(new Uri($"/api/v1/app-updates/devices/{device.Id}/request-update", UriKind.Relative), null, Ct);
        (await again.ReadProblemAsync()).Code.ShouldBe("APP_UP_TO_DATE");
    }

    private static async Task SaveSettingsAsync(HttpClient admin, JsonObject appUpdates)
    {
        var current = (await admin.GetFromJsonAsync<JsonObject>("/api/v1/settings", Ct))!;
        var settings = current["settings"]!.AsObject();
        settings["appUpdates"] = appUpdates;
        using var saved = await admin.PutAsJsonAsync("/api/v1/settings", new JsonObject
        {
            ["settings"] = settings.DeepClone(), ["version"] = current["version"]!.GetValue<long>(),
        }, Ct);
        saved.EnsureSuccessStatusCode();
        (await saved.Content.ReadFromJsonAsync<JsonObject>(Ct))!["settings"]!["appUpdates"]!["enforceMinimum"]!.GetValue<bool>()
            .ShouldBe(appUpdates["enforceMinimum"]?.GetValue<bool>() ?? false);
    }
}
