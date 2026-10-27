using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Archive;
using EstrichOne.Api.Modules.Backup;
using EstrichOne.Api.Modules.Notifications;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Backup;

/// <summary>Backup Center: Status-Banner, Meldungen an Admins, Zeitplan, Wiederherstellung, Notfallblatt, Backup-Ziele, die ziehen.</summary>
public sealed class BackupCenterTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly string _status = Path.Combine(Path.GetTempPath(), "estrichone-backup-center", Guid.NewGuid().ToString("N"));
    private readonly string _repository = Path.Combine(Path.GetTempPath(), "estrichone-backup-repo", Guid.NewGuid().ToString("N"));
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_status);
        Directory.CreateDirectory(Path.Combine(_repository, "snapshots"));
        Directory.CreateDirectory(Path.Combine(_repository, "data", "ab"));
        await File.WriteAllTextAsync(Path.Combine(_repository, "config"), "verschlüsselt", Ct);
        await File.WriteAllTextAsync(Path.Combine(_repository, "snapshots", new string('1', 64)), "s", Ct);
        await File.WriteAllTextAsync(Path.Combine(_repository, "data", "ab", "ab" + new string('2', 62)), "pack", Ct);
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
        {
            ["EstrichOne:Backup:StatusPath"] = _status,
            ["EstrichOne:Backup:RepositoryPath"] = _repository,
            ["EstrichOne:Backup:RepositoryPassword"] = "GeheimerSchluessel1234",
            ["EstrichOne:Backup:Repository"] = "/repository",
            ["EstrichOne:Health:Worker"] = "false",
            ["EstrichOne:Archive:Worker"] = "false",
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(_status, recursive: true);
        Directory.Delete(_repository, recursive: true);
    }

    private void Write(string name, string status, DateTimeOffset at, string extra = "") =>
        File.WriteAllText(Path.Combine(_status, name + ".json"),
            $$"""{"status":"{{status}}","message":"Sicherung {{status}}","at":"{{at.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}}"{{extra}}}""");

    private SystemHealthMonitor Monitor => _factory.Services.GetRequiredService<SystemHealthMonitor>();

    [Fact]
    public async Task Banner_RotNurWennEingerichtetUndVeraltet_TextExakt()
    {
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);

        // Noch nie gesichert, kein Ziel: gilt als nicht eingerichtet – kein Banner.
        var none = await worker.GetFromJsonAsync<HealthBannerResponse>("/api/v1/system/health-banner", TestJson.Options, Ct);
        none!.Banners.ShouldBeEmpty();
        none.Backup.Configured.ShouldBeFalse();
        none.Archive.Configured.ShouldBeFalse();

        var at = new DateTimeOffset(2030, 1, 15, 21, 30, 0, TimeSpan.Zero);
        _factory.Clock.SetUtcNow(at.AddHours(2));
        Write("lastSuccess", "succeeded", at);
        Write("lastRun", "succeeded", at);
        (await Monitor.RefreshAsync(notify: false, Ct)).Banners.ShouldBeEmpty();

        _factory.Clock.SetUtcNow(at.AddHours(27));
        var stale = await Monitor.RefreshAsync(notify: false, Ct);
        stale.Backup.Severity.ShouldBe("critical");
        stale.Banners.Single().ShouldBe(new HealthBanner("backup", "critical",
            "ACHTUNG: Backup-Server ausgefallen oder nicht erreichbar (Letztes Backup 15.01.2030 22:30 Uhr). Bitte geben Sie umgehend Ihrem Admin Bescheid."));

        using var later = await _factory.CreateClientAsAsync("polier", StandardRoles.SiteWorkerId);
        var cached = await later.GetFromJsonAsync<HealthBannerResponse>("/api/v1/system/health-banner", TestJson.Options, Ct);
        cached!.Banners.Single().Severity.ShouldBe("critical");
        using var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync(new Uri("/api/v1/system/health-banner", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task NachJederSicherung_PushAnAdmins_EinmalJeLauf_FehlerDeutlich()
    {
        using var admin = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        var now = _factory.Clock.GetUtcNow();
        Write("lastSuccess", "succeeded", now.AddMinutes(-1));
        Write("lastRun", "succeeded", now.AddMinutes(-1), ""","durationSeconds":95,"targets":2,"kind":"full" """);
        await Monitor.RefreshAsync(notify: true, Ct);
        await Monitor.RefreshAsync(notify: true, Ct);

        _factory.Clock.Advance(TimeSpan.FromHours(1));
        Write("lastRun", "failed", _factory.Clock.GetUtcNow());
        await Monitor.RefreshAsync(notify: true, Ct);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var titles = await db.Set<Notification>().AsNoTracking().OrderBy(n => n.CreatedAt).Select(n => new { n.Title, n.Body, n.Urgency }).ToListAsync(Ct);
        titles.Count(t => t.Title == "Sicherung erfolgreich").ShouldBe(1);
        titles.Single(t => t.Title == "Sicherung erfolgreich").Body!.ShouldContain("Vollsicherung · 2 min · 2 Ziele");
        titles.Single(t => t.Title == "Sicherung fehlgeschlagen").Urgency.ShouldBe(Modules.Notifications.Exports.NotificationUrgency.High);
    }

    [Fact]
    public async Task Zeitplan_WirdGeprueft_UndAlsEnvFuerDenDienstGeschrieben()
    {
        using var admin = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        using var bad = await admin.PutAsJsonAsync("/api/v1/backup/settings", new BackupPreferences(DailyTime: "25:00", KeepDaily: 1), Ct);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using var ok = await admin.PutAsJsonAsync("/api/v1/backup/settings", new BackupPreferences(DailyTime: "23:15", IncrementalEveryHours: 4, NotifyOnSuccess: false), Ct);
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        var env = await File.ReadAllTextAsync(Path.Combine(_status, "settings.env"), Ct);
        env.ShouldContain("BACKUP_TIME=23:15");
        env.ShouldContain("BACKUP_INTERVAL_HOURS=4");
        (await admin.GetFromJsonAsync<BackupPreferences>("/api/v1/backup/settings", TestJson.Options, Ct))!.NotifyOnSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task Wiederherstellung_BrauchtBestaetigung_SperrtAlle_BisDieApiAbschliesst()
    {
        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        using var noConfirm = await admin.PostAsJsonAsync("/api/v1/backup/restore", new RestoreRequest("abcdef12"), Ct);
        (await noConfirm.ReadProblemAsync()).Code.ShouldBe("RESTORE_REJECTED");
        using var evil = await admin.PostAsJsonAsync("/api/v1/backup/restore", new RestoreRequest("abc; rm -rf /", Confirmation: "WIEDERHERSTELLEN"), Ct);
        evil.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        using var accepted = await admin.PostAsJsonAsync("/api/v1/backup/restore", new RestoreRequest("abcdef12", Confirmation: "WIEDERHERSTELLEN"), Ct);
        accepted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await File.ReadAllTextAsync(Path.Combine(_status, "requests", "restore.env"), Ct)).ShouldContain("RESTORE_SNAPSHOT=abcdef12");

        using var blocked = await admin.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct);
        blocked.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await blocked.ReadProblemAsync()).Code.ShouldBe("MAINTENANCE");
        (await admin.GetAsync(new Uri("/api/v1/backup/restore", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Backup-Dienst meldet „restored“ → API aktualisiert das Schema und gibt frei.
        var progress = await File.ReadAllTextAsync(Path.Combine(_status, "restore.json"), Ct);
        await File.WriteAllTextAsync(Path.Combine(_status, "restore.json"), progress.Replace("\"requested\"", "\"restored\"", StringComparison.Ordinal), Ct);
        for (var i = 0; i < 40 && !(await File.ReadAllTextAsync(Path.Combine(_status, "restore.json"), Ct)).Contains("completed", StringComparison.Ordinal); i++)
        {
            await Task.Delay(500, Ct);
        }

        (await admin.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Notfallblatt_IstPdf_NurMitBackupRecht()
    {
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var admin = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        (await worker.GetAsync(new Uri("/api/v1/backup/emergency-sheet", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var pdf = await admin.GetByteArrayAsync(new Uri("/api/v1/backup/emergency-sheet", UriKind.Relative), Ct);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
        EmergencySheetService.Group("GeheimerSchluessel1234").ShouldBe("Gehe imer Schl uess el12 34");
    }

    [Fact]
    public async Task ZielZieht_KoppelnMitCode_LiestNurDasRepository_MeldetSich()
    {
        using var admin = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        using var created = await admin.PostAsJsonAsync("/api/v1/backup/mirrors", new CreateBackupMirrorRequest("NAS zuhause"), Ct);
        var mirror = (await created.Content.ReadFromJsonAsync<CreateBackupMirrorResponse>(TestJson.Options, Ct))!;
        mirror.Command.ShouldContain($"ESTRICHONE_PULL_CODE={mirror.Code}");

        using var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync(new Uri("/api/v1/backup/mirror/repo/config", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var wrong = await anonymous.PostAsJsonAsync("/api/v1/backup/mirror/pair", new MirrorPairRequest("AAAA-BBBB-CCCC-DDDD", null), Ct);
        wrong.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var pair = await anonymous.PostAsJsonAsync("/api/v1/backup/mirror/pair", new MirrorPairRequest(mirror.Code.ToLowerInvariant(), "Keller"), Ct);
        var token = (await pair.Content.ReadFromJsonAsync<MirrorPairResponse>(TestJson.Options, Ct))!.Token;
        using var again = await anonymous.PostAsJsonAsync("/api/v1/backup/mirror/pair", new MirrorPairRequest(mirror.Code, null), Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var mirrorClient = _factory.CreateClient();
        mirrorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await mirrorClient.GetStringAsync(new Uri("/api/v1/backup/mirror/repo/config", UriKind.Relative), Ct)).ShouldBe("verschlüsselt");
        var data = await mirrorClient.GetFromJsonAsync<List<MirrorFile>>("/api/v1/backup/mirror/repo/data/", TestJson.Options, Ct);
        data!.Single().Name.ShouldBe("ab" + new string('2', 62));
        (await mirrorClient.GetStringAsync(new Uri($"/api/v1/backup/mirror/repo/data/{data![0].Name}", UriKind.Relative), Ct)).ShouldBe("pack");
        (await mirrorClient.GetAsync(new Uri("/api/v1/backup/mirror/repo/data/..%2Fconfig", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await mirrorClient.GetAsync(new Uri("/api/v1/backup/mirror/repo/locks/", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var report = await mirrorClient.PostAsJsonAsync("/api/v1/backup/mirror/report",
            new BackupMirrorReport(1, 4096, _factory.Clock.GetUtcNow(), 1_000_000, 2_000_000, "0.1.0", null), Ct);
        report.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var list = await admin.GetFromJsonAsync<BackupMirrorsResponse>("/api/v1/backup/mirrors", TestJson.Options, Ct);
        list!.Mirrors.Single().ShouldSatisfyAllConditions(m => m.Paired.ShouldBeTrue(), m => m.Reachable.ShouldBeTrue(), m => m.TargetName!.ShouldBe("Keller"));

        // Zählt als eingerichtetes Backup-Ziel für das Banner: meldet sich der Spiegel nicht mehr, wird es rot.
        _factory.Clock.Advance(TimeSpan.FromHours(4));
        Write("lastSuccess", "succeeded", _factory.Clock.GetUtcNow());
        (await Monitor.RefreshAsync(notify: false, Ct)).Backup.ShouldSatisfyAllConditions(b => b.Configured.ShouldBeTrue(), b => b.Reachable.ShouldBeFalse());
    }
}
