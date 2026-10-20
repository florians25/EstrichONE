using EstrichOne.Hosting.Docker;
using EstrichOne.Hosting.Status;

namespace EstrichOne.Hosting.Tests;

public sealed class DockerParsingTests
{
    [Fact]
    public void ParseContainers_ErkenntZustandHealthUndExitCode()
    {
        var output = string.Join('\n',
            "aaa\testrichone-m-api-1\tapi\trunning\tUp 2 minutes (healthy)\tghcr.io/x/estrichone-api:1",
            "bbb\testrichone-m-migrate-1\tmigrate\texited\tExited (0) 3 minutes ago\tghcr.io/x/estrichone-api:1",
            "ccc\testrichone-m-caddy-1\tcaddy\trunning\tUp 5 seconds (health: starting)\tghcr.io/x/estrichone-web:1",
            "ddd\testrichone-m-backup-1\tbackup\texited\tExited (137) 1 hour ago\tghcr.io/x/estrichone-backup:1",
            "kaputt");
        var containers = DockerCliHost.ParseContainers(output);
        containers.Count.ShouldBe(4);
        containers.Single(c => c.Service == "api").Health.ShouldBe("healthy");
        containers.Single(c => c.Service == "migrate").ExitCode.ShouldBe(0);
        containers.Single(c => c.Service == "caddy").Health.ShouldBe("starting");
        containers.Single(c => c.Service == "backup").ExitCode.ShouldBe(137);
    }

    [Fact]
    public void ParseUsage_LiestCpuUndSpeicher()
    {
        var usage = DockerCliHost.ParseUsage("aaa\testrichone-m-api-1\t1.25%\t180.5MiB / 2GiB\nbbb\tx\t0.00%\t800kB / 1.944GiB");
        usage[0].CpuPercent.ShouldBe(1.25);
        usage[0].MemoryBytes.ShouldBe((long)(180.5 * 1024 * 1024));
        usage[0].MemoryLimitBytes.ShouldBe(2L * 1024 * 1024 * 1024);
        usage[1].MemoryBytes.ShouldBe(800_000);
    }

    [Theory]
    [InlineData("0B", 0)]
    [InlineData("512B", 512)]
    [InlineData("1KiB", 1024)]
    [InlineData("1.5GB", 1_500_000_000)]
    [InlineData("unsinn", 0)]
    public void ParseSize_Einheiten(string text, long bytes) => DockerCliHost.ParseSize(text).ShouldBe(bytes);

    private static ContainerInfo C(string service, string state = "running", string? health = null, int? exit = null) =>
        new(service, $"x-{service}-1", service, state, health, state, "img", exit);

    private static readonly ProbeResult Ok = new(true, 200, "Healthy", "0.1.0", "X", false, null);

    [Fact]
    public void Evaluate_AllesLaeuft_IstGesund()
    {
        var (level, problems) = HealthEvaluator.Evaluate(true,
            [C("postgres", health: "healthy"), C("migrate", "exited", exit: 0), C("api", health: "healthy"), C("backup"), C("caddy", health: "healthy")],
            Ok, null, false, 1000, 0, 3, 5);
        level.ShouldBe(HealthLevel.Healthy);
        problems.ShouldBeEmpty();
    }

    [Fact]
    public void Evaluate_FehlenderDienstUndMigration_IstFehler()
    {
        var (level, problems) = HealthEvaluator.Evaluate(true,
            [C("postgres"), C("migrate", "exited", exit: 1), C("api", "exited", exit: 139), C("caddy")], Ok, null, false, null, 0, null, 0);
        level.ShouldBe(HealthLevel.Unhealthy);
        problems.ShouldContain(p => p.Contains("api", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("backup", StringComparison.Ordinal));
        problems.ShouldContain(p => p.Contains("Migration", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_LimitsUndSicherung_SindWarnungen()
    {
        var backup = new BackupState(true, false, "Die letzte erfolgreiche Sicherung ist 40 Stunden alt.", null, null, null, null, false);
        var (level, problems) = HealthEvaluator.Evaluate(true,
            [C("postgres"), C("api"), C("backup"), C("caddy")], Ok, backup, true, 11L * 1024 * 1024 * 1024, 10L * 1024 * 1024 * 1024, 6, 5);
        level.ShouldBe(HealthLevel.Degraded);
        problems.Count.ShouldBe(3);
    }

    [Fact]
    public void Evaluate_DegradedBeiNeuerInstanz_IstKeinBefund()
    {
        var degraded = new ProbeResult(true, 200, "Degraded", "0.1.0", "X", true, null);
        var containers = new[] { C("postgres"), C("api"), C("backup"), C("caddy") };
        HealthEvaluator.Evaluate(true, containers, degraded, null, false, null, 0, null, 0).Level.ShouldBe(HealthLevel.Healthy);
        HealthEvaluator.Evaluate(true, containers, degraded, null, true, null, 0, null, 0).Level.ShouldBe(HealthLevel.Degraded);
    }

    [Fact]
    public void Evaluate_NichtErreichbar_IstFehler()
    {
        var (level, _) = HealthEvaluator.Evaluate(true, [C("postgres"), C("api"), C("backup"), C("caddy")],
            new ProbeResult(false, null, null, null, null, null, "Connection refused"), null, false, null, 0, null, 0);
        level.ShouldBe(HealthLevel.Unhealthy);
    }

    [Fact]
    public void Evaluate_Angehalten_IstGestoppt() =>
        HealthEvaluator.Evaluate(false, [C("api", "exited", exit: 0)], null, null, false, null, 0, null, 0).Level.ShouldBe(HealthLevel.Stopped);

    [Fact]
    public void BackupState_LiestStatusdateien()
    {
        using var temp = new TempDirectory();
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        File.WriteAllText(Path.Combine(temp.Path, "lastSuccess.json"),
            "{\"status\":\"succeeded\",\"message\":\"Sicherung erfolgreich\",\"at\":\"2026-10-20T01:00:00Z\",\"durationSeconds\":12}");
        File.WriteAllText(Path.Combine(temp.Path, "lastRun.json"), "kaputt");
        var state = BackupState.Read(temp.Path, now);
        state.Configured.ShouldBeTrue();
        state.Healthy.ShouldBeTrue();
        state.LastSuccess!.DurationSeconds.ShouldBe(12);
        state.LastRun.ShouldBeNull();
        BackupState.Read(temp.Path, now.AddHours(40)).Healthy.ShouldBeFalse();
        BackupState.Read(Path.Combine(temp.Path, "fehlt"), now).Configured.ShouldBeFalse();
    }
}
