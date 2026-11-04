namespace EstrichOne.Api.Modules.Backup.Exports;

/// <param name="Status"><c>requested</c> (noch nicht abgeholt), <c>running</c>, <c>succeeded</c> oder <c>failed</c>.</param>
/// <param name="Files">Gesicherte bzw. zurückgeholte Dateien.</param>
public sealed record OffloadBackupResult(string RequestId, string Status, string? Message, DateTimeOffset At, int? Files);

/// <summary>
/// Sicherungskopie ausgelagerter Originale (docs/speicher.md, „Auslagerung“): Bevor ein Original lokal gelöscht wird, legt der
/// Backup-Dienst einen eigenen, dauerhaft aufbewahrten Snapshot (Tag <c>estrichone-auslagerung</c>) mit genau diesen Dateien
/// in allen Sicherungszielen an. Rückweg bei Verlust des Storage: dieselben Dateien aus diesen Snapshots zurückspielen.
/// Austausch über das gemeinsame Statusverzeichnis mit dem Backup-Dienst (<c>requests/offload-*.list</c>, <c>offload-*.json</c>).
/// </summary>
public interface IOffloadBackup
{
    /// <summary>Backup-Dienst eingerichtet (gemeinsames Statusverzeichnis vorhanden).</summary>
    bool Available { get; }

    /// <summary>Anforderung für Pfade relativ zur Dateiablage (<c>originals/JJJJ/MM/&lt;id&gt;</c>); false, wenn schon eine offen ist oder kein Dienst.</summary>
    bool RequestCopy(string requestId, IReadOnlyCollection<string> relativePaths);

    OffloadBackupResult? CopyResult(string requestId);

    /// <summary>Originale aus den Auslagerungs-Snapshots zurück in die Dateiablage spielen (vorhandene Dateien bleiben unverändert).</summary>
    bool RequestRecovery(string requestId, IReadOnlyCollection<string> relativePaths);

    OffloadBackupResult? RecoveryResult(string requestId);
}
