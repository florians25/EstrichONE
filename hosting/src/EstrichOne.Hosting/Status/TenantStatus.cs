using EstrichOne.Hosting.Docker;

namespace EstrichOne.Hosting.Status;

public enum HealthLevel
{
    Unknown,
    Healthy,
    Degraded,
    Unhealthy,
    Stopped,
}

public sealed record ContainerStatus(
    string Service,
    string Name,
    string State,
    string? Health,
    string Status,
    string Image,
    int? ExitCode,
    double? CpuPercent,
    long? MemoryBytes,
    long? MemoryLimitBytes);

/// <summary>Zuletzt erhobener Betriebszustand einer Instanz.</summary>
public sealed record TenantStatus(
    string Slug,
    DateTime CollectedAt,
    HealthLevel Health,
    IReadOnlyList<ContainerStatus> Containers,
    ProbeResult? Probe,
    string? RunningVersion,
    bool? SetupRequired,
    string? SetupCode,
    double CpuPercent,
    long MemoryBytes,
    long? DiskBytes,
    DateTime? DiskMeasuredAt,
    int? ActiveUsers,
    BackupState? Backup,
    IReadOnlyList<string> Problems,
    string? Error)
{
    public static TenantStatus Empty(string slug) =>
        new(slug, DateTime.MinValue, HealthLevel.Unknown, [], null, null, null, null, 0, 0, null, null, null, null, [], null);
}

/// <summary>Gemessene Nutzung (teuer, daher seltener): Speicherplatz und aktive Benutzer.</summary>
public sealed record UsageSample(long? DiskBytes, int? ActiveUsers, DateTime MeasuredAt);

public static class HealthEvaluator
{
    /// <summary>Dienste, die bei einer laufenden Instanz laufen müssen (migrate beendet sich nach dem Schema-Update).</summary>
    public static readonly IReadOnlyList<string> RequiredServices = ["postgres", "api", "backup", "caddy"];

    public static (HealthLevel Level, List<string> Problems) Evaluate(
        bool expectedRunning,
        IReadOnlyList<ContainerInfo> containers,
        ProbeResult? probe,
        BackupState? backup,
        bool backupExpected,
        long? diskBytes,
        long storageLimitBytes,
        int? activeUsers,
        int maxUsers)
    {
        var problems = new List<string>();
        var level = HealthLevel.Healthy;
        void Raise(HealthLevel to, string problem)
        {
            problems.Add(problem);
            if (to > level)
            {
                level = to;
            }
        }

        var running = containers.Where(c => c.State == "running").ToList();
        if (!expectedRunning)
        {
            return (running.Count == 0 ? HealthLevel.Stopped : HealthLevel.Degraded,
                running.Count == 0 ? problems : [$"{running.Count} Container laufen trotz angehaltener Instanz."]);
        }

        if (containers.Count == 0)
        {
            Raise(HealthLevel.Unhealthy, "Keine Container vorhanden.");
        }

        foreach (var service in RequiredServices)
        {
            var container = containers.FirstOrDefault(c => c.Service == service);
            if (container is null || container.State != "running")
            {
                if (containers.Count > 0)
                {
                    Raise(HealthLevel.Unhealthy, $"Dienst {service} läuft nicht{(container is null ? string.Empty : $" ({container.Status})")}.");
                }
            }
            else if (container.Health == "unhealthy")
            {
                Raise(HealthLevel.Unhealthy, $"Dienst {service} meldet „unhealthy“.");
            }
            else if (container.Health == "starting")
            {
                Raise(HealthLevel.Degraded, $"Dienst {service} startet noch.");
            }
        }

        var migrate = containers.FirstOrDefault(c => c.Service == "migrate");
        if (migrate is { State: "exited", ExitCode: not 0 and not null })
        {
            Raise(HealthLevel.Unhealthy, $"Datenbank-Migration fehlgeschlagen (Exit-Code {migrate.ExitCode}).");
        }

        if (probe is not null && containers.Count > 0)
        {
            if (!probe.Reachable)
            {
                Raise(HealthLevel.Unhealthy, $"Instanz nicht erreichbar: {probe.Error}");
            }
            else if (probe.HealthStatusCode != 200)
            {
                Raise(HealthLevel.Unhealthy, $"/health/ready meldet HTTP {probe.HealthStatusCode} ({probe.HealthText}).");
            }
            else if (string.Equals(probe.HealthText, "Degraded", StringComparison.OrdinalIgnoreCase) && backupExpected)
            {
                // Die Suite meldet „Degraded“ vor allem bei veralteter Sicherung – bei neuen Instanzen (noch keine
                // nächtliche Sicherung möglich) ist das kein Befund.
                Raise(HealthLevel.Degraded, "/health/ready meldet „Degraded“ (z. B. Sicherung veraltet).");
            }
        }

        if (backupExpected && backup is not null && !backup.Healthy)
        {
            Raise(HealthLevel.Degraded, $"Sicherung: {backup.Summary}");
        }

        if (storageLimitBytes > 0 && diskBytes > storageLimitBytes)
        {
            Raise(HealthLevel.Degraded, "Speicherkontingent des Tarifs überschritten.");
        }

        if (maxUsers > 0 && activeUsers > maxUsers)
        {
            Raise(HealthLevel.Degraded, $"Mehr aktive Benutzer ({activeUsers}) als im Tarif enthalten ({maxUsers}).");
        }

        return (level, problems);
    }
}
