namespace EstrichOne.Api.Modules.Backup.Exports;

/// <param name="Pulls">Ziel zieht selbst (zuhause hinter NAT): erreichbar = hat sich innerhalb von 3 Stunden gemeldet.</param>
public sealed record BackupTargetMonitor(string Name, string Address, bool Reachable, string? LastRunStatus, DateTimeOffset? LastRunAt,
    long? DiskFreeBytes, long? DiskTotalBytes, long? RepositoryBytes, string? Error, bool Pulls = false);

/// <summary>Letzter Lauf des Backup-Dienstes (für Pushnachrichten nach jeder Sicherung).</summary>
public sealed record BackupRunMonitor(string Status, string Message, DateTimeOffset At, int? DurationSeconds, int? Targets, string? Kind);

public sealed record BackupMonitor(bool Configured, bool Healthy, string Summary, DateTimeOffset? LastSuccessAt, string? LastRunStatus,
    IReadOnlyList<BackupTargetMonitor> Targets, BackupRunMonitor? LastRun = null, bool NotifyAdmins = true, bool NotifyOnSuccess = true);

/// <summary>Laufende Wiederherstellung des ganzen Systems (API sperrt dann alle fachlichen Aufrufe).</summary>
public sealed record RestoreMonitor(string Status, string Phase, int Percent, string Snapshot, DateTimeOffset At);

/// <summary>Zustand von Sicherung und Backup-Zielen für die Überwachung (System → Überwachung, Status-Banner).</summary>
public interface IBackupMonitoring
{
    /// <summary>Mit Live-Abfrage der gekoppelten Ziele (bis 15 s je Ziel) – nicht im Anfragepfad häufiger Aufrufe verwenden.</summary>
    Task<BackupMonitor> GetAsync(CancellationToken cancellationToken);

    /// <summary>Wiederherstellung des ganzen Systems läuft (aus dem Statusverzeichnis, ohne Netz).</summary>
    RestoreMonitor? ActiveRestore();
}
