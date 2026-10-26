namespace EstrichOne.Api.Modules.Backup.Exports;

public sealed record BackupTargetMonitor(string Name, string Address, bool Reachable, string? LastRunStatus, DateTimeOffset? LastRunAt,
    long? DiskFreeBytes, long? DiskTotalBytes, long? RepositoryBytes, string? Error);

public sealed record BackupMonitor(bool Configured, bool Healthy, string Summary, DateTimeOffset? LastSuccessAt, string? LastRunStatus,
    IReadOnlyList<BackupTargetMonitor> Targets);

/// <summary>Zustand von Sicherung und Backup-Zielen für die Überwachung (System → Überwachung).</summary>
public interface IBackupMonitoring
{
    Task<BackupMonitor> GetAsync(CancellationToken cancellationToken);
}
