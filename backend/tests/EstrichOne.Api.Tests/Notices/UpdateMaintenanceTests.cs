using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Notices;
using EstrichOne.Api.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace EstrichOne.Api.Tests.Notices;

/// <summary>Geordneter Update-Ablauf: Ankündigung mit Countdown, Sperre und Abmeldung im Update, Freigabe danach.</summary>
public sealed class UpdateMaintenanceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "estrichone-update", Guid.NewGuid().ToString("N"), UpdateMaintenanceReader.FileName);
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string> { ["EstrichOne:Notices:UpdateStatePath"] = _file });
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
        Directory.Delete(Path.GetDirectoryName(_file)!, recursive: true);
    }

    private void Write(string phase, int phaseSeconds = 60, string id = "u1", DateTimeOffset? expires = null)
    {
        var now = _factory.Clock.GetUtcNow();
        File.WriteAllText(_file, JsonSerializer.Serialize(new
        {
            id, phase, fromVersion = "1.0.15", targetVersion = "1.0.16", startedAt = now, phaseStartedAt = now,
            phaseEndsAt = now.AddSeconds(phaseSeconds), expiresAt = expires ?? now.AddHours(2), source = "update.sh",
        }, TestJson.Options));
    }

    private Task<HttpResponseMessage> LoginAsync(string login) => _factory.CreateClient().LoginAsync(login);

    [Fact]
    public async Task Ankuendigung_ZeigtCountdown_ArbeitGehtWeiter_NeueAnmeldungGesperrt()
    {
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        Write(UpdatePhases.Announce);

        using var anonymous = _factory.CreateClient();
        var notices = await anonymous.GetFromJsonAsync<SystemNoticesResponse>("/api/v1/system/notices", TestJson.Options, Ct);
        notices!.Update.ShouldNotBeNull();
        notices.Update.Phase.ShouldBe("announce");
        notices.Update.TargetVersion.ShouldBe("1.0.16");
        notices.Update.Blocking.ShouldBeFalse();
        notices.Update.PhaseEndsAt!.Value.ShouldBe(_factory.Clock.GetUtcNow().AddSeconds(60), TimeSpan.FromSeconds(1));

        // Arbeiten (und Speichern) ist noch möglich; jede Antwort trägt die Phase.
        using var work = await worker.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct);
        work.StatusCode.ShouldBe(HttpStatusCode.OK);
        work.Headers.GetValues(MaintenanceGate.UpdateHeader).ShouldBe(["announce"]);

        using var login = await LoginAsync("chefin");
        login.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var problem = await login.ReadProblemAsync();
        problem.Code.ShouldBe("MAINTENANCE");
    }

    [Fact]
    public async Task UpdatePhase_SperrtAlle_BeendetSitzungen_FreigabeErfordertNeueAnmeldung()
    {
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        Write(UpdatePhases.Update);

        using var blocked = await worker.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct);
        blocked.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        using (var body = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync(Ct)))
        {
            body.RootElement.GetProperty("code").GetString().ShouldBe("MAINTENANCE");
            body.RootElement.GetProperty("reason").GetString().ShouldBe("update");
            body.RootElement.GetProperty("phase").GetString().ShouldBe("update");
        }

        // Auch Administratoren und Anmeldung sind gesperrt; Hinweise und Serverinfo bleiben lesbar.
        (await admin.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await LoginAsync("chefin")).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        using var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync(new Uri("/api/v1/server-info", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var notices = await anonymous.GetFromJsonAsync<SystemNoticesResponse>("/api/v1/system/notices", TestJson.Options, Ct);
        notices!.Update!.Blocking.ShouldBeTrue();

        Write(UpdatePhases.Finish);
        (await worker.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        // Freigabe: Stand entfernt – alte Sitzungen sind beendet, neue Anmeldung klappt.
        File.Delete(_file);
        (await worker.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await admin.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var again = await LoginAsync("chefin");
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetFromJsonAsync<SystemNoticesResponse>("/api/v1/system/notices", TestJson.Options, Ct))!.Update.ShouldBeNull();
    }

    [Fact]
    public async Task Erneuern_NachDemUpdate_MeldetSitzungBeimUpdateBeendet()
    {
        await _factory.CreateUserAsync("monteur", roleIds: StandardRoles.SiteWorkerId);
        using var client = _factory.CreateClient();
        var tokens = await client.LoginOkAsync("monteur");
        Write(UpdatePhases.Update);
        // Während des Updates: Erneuern gesperrt (503), die Sitzung endet nicht auf dem Gerät.
        using (var blocked = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken }, Ct))
        {
            blocked.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        }

        File.Delete(_file);
        using var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken }, Ct);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await refresh.ReadProblemAsync()).Code.ShouldBe("SESSION_ENDED_UPDATE");
    }

    [Fact]
    public async Task VerwaisterStand_WirdIgnoriert()
    {
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        Write(UpdatePhases.Update, expires: _factory.Clock.GetUtcNow().AddMinutes(-1));
        using var response = await worker.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains(MaintenanceGate.UpdateHeader).ShouldBeFalse();
    }
}

public sealed class UpdateMaintenanceReaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "estrichone-update-reader", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 12, 10, 0, 0, TimeSpan.Zero));

    public UpdateMaintenanceReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private UpdateMaintenanceReader Reader(string? path = null, string? statusPath = null) => new(
        Options.Create(new NoticeSettings { UpdateStatePath = path }),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["EstrichOne:Backup:StatusPath"] = statusPath }).Build(),
        _clock, NullLogger<UpdateMaintenanceReader>.Instance);

    [Fact]
    public void Pfad_StandardNebenDemBackupStatus()
    {
        Reader(statusPath: "/status").Path.ShouldBe("/status/update-maintenance.json");
        Reader().Path.ShouldBeNull();
        Reader().Current().ShouldBeNull();
    }

    [Fact]
    public void Liest_Phasen_IgnoriertUngueltiges_UndFolgtAenderungen()
    {
        var file = Path.Combine(_dir, UpdateMaintenanceReader.FileName);
        var reader = Reader(statusPath: _dir);
        reader.Current().ShouldBeNull();

        File.WriteAllText(file, """{"id":"a","phase":"announce","targetVersion":"2.0.0","startedAt":"2026-11-12T10:00:00Z","phaseEndsAt":"2026-11-12T10:01:00Z"}""");
        reader.Current()!.ShouldSatisfyAllConditions(s => s.Phase.ShouldBe("announce"), s => s.Blocking.ShouldBeFalse());

        File.WriteAllText(file, """{"id":"a","phase":"finish","targetVersion":"2.0.0","startedAt":"2026-11-12T10:00:00Z","expiresAt":"2026-11-12T11:00:00Z"}""");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));
        reader.Current()!.Blocking.ShouldBeTrue();

        _clock.Advance(TimeSpan.FromHours(2));
        reader.Current().ShouldBeNull();

        File.WriteAllText(file, """{"id":"a","phase":"irgendwas","startedAt":"2026-11-12T10:00:00Z"}""");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(10));
        reader.Current().ShouldBeNull();
        File.WriteAllText(file, "kein json");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(15));
        reader.Current().ShouldBeNull();
    }
}
