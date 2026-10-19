using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Backup;
using EstrichOne.Api.Tests.Support;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace EstrichOne.Api.Tests.Backup;

public sealed class BackupStatusTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "estrichone-backup-status", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));

    public BackupStatusTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void WithoutStatusPath_IsNotConfigured() =>
        Reader(statusPath: null).Read().Configured.ShouldBeFalse();

    [Fact]
    public void WithoutSuccessfulBackup_IsUnhealthy()
    {
        var status = Reader().Read();

        status.Configured.ShouldBeTrue();
        status.Healthy.ShouldBeFalse();
    }

    [Fact]
    public void RecentBackup_IsHealthy_OldBackupIsNot()
    {
        File.WriteAllText(Path.Combine(_dir, "lastSuccess.json"),
            """{"status":"succeeded","message":"ok","at":"2026-10-19T20:30:00Z","durationSeconds":42}""");
        File.WriteAllText(Path.Combine(_dir, "lastRestoreTest.json"),
            """{"status":"succeeded","message":"ok","at":"2026-10-18T03:00:00Z","migrations":9,"tables":{"bauakten":{"restored":3,"live":3}}}""");

        var fresh = Reader().Read();
        fresh.Healthy.ShouldBeTrue();
        fresh.LastSuccess!.Details!["durationSeconds"]!.GetValue<int>().ShouldBe(42);
        fresh.LastRestoreTest!.Status.ShouldBe("succeeded");

        _clock.Advance(TimeSpan.FromDays(2));
        var old = Reader().Read();
        old.Healthy.ShouldBeFalse();
        old.Summary.ShouldContain("Stunden alt");
    }

    [Fact]
    public void RequestRun_LeavesRequestFileForTheBackupService()
    {
        var reader = Reader();

        reader.RequestRun().ShouldBeTrue();

        File.Exists(Path.Combine(_dir, "requests", "backup-now")).ShouldBeTrue();
        reader.Read().RunRequested.ShouldBeTrue();
    }

    private BackupStatusReader Reader(string? statusPath = "") =>
        new(Options.Create(new BackupSettings { StatusPath = statusPath == "" ? _dir : statusPath }), _clock);
}

public sealed class BackupEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Status_RequiresBackupPermission()
    {
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var it = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);

        (await worker.GetAsync(new Uri("/api/v1/backup/status", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var status = await it.GetFromJsonAsync<BackupStatusResponse>("/api/v1/backup/status", TestJson.Options, TestContext.Current.CancellationToken);
        status!.Configured.ShouldBeFalse();
        using var run = await it.PostAsync(new Uri("/api/v1/backup/run", UriKind.Relative), null, TestContext.Current.CancellationToken);
        (await run.ReadProblemAsync()).Code.ShouldBe("BACKUP_NOT_CONFIGURED");
    }
}
