using System.Text.Json.Nodes;
using EstrichOne.Api.Modules.Backup.Exports;

namespace EstrichOne.Api.Modules.Backup;

internal sealed class BackupMonitoring(BackupStatusReader reader, BackupTargetService targets, BackupMirrorStore mirrors, BackupOperations operations)
    : IBackupMonitoring
{
    public async Task<BackupMonitor> GetAsync(CancellationToken cancellationToken)
    {
        var status = reader.Read();
        var list = await targets.ListAsync(live: true, cancellationToken);
        var monitors = list.Select(t => new BackupTargetMonitor(t.Name, t.Address, t.Remote is not null, t.LastRun?.Status, t.LastRun?.At,
                t.Remote?.DiskFreeBytes, t.Remote?.DiskTotalBytes, t.Remote?.Bytes, t.LastError))
            .Concat(mirrors.List().Where(m => m.Paired).Select(m => new BackupTargetMonitor(m.TargetName ?? m.Name, "zieht vom Server", m.Reachable,
                m.LastReport is null ? null : m.LastReport.Error is null ? "succeeded" : "failed", m.LastSeenAt, m.LastReport?.DiskFreeBytes,
                m.LastReport?.DiskTotalBytes, m.LastReport?.Bytes, m.Reachable ? m.LastReport?.Error : "Hat sich seit über 3 Stunden nicht gemeldet.", Pulls: true)))
            .ToList();
        var run = status.LastRun is { } r
            ? new BackupRunMonitor(r.Status, r.Message, r.At, Int(r.Details, "durationSeconds"), Int(r.Details, "targets"), r.Details?["kind"]?.GetValue<string>())
            : null;
        var preferences = operations.Preferences();
        return new BackupMonitor(status.Configured, status.Healthy, status.Summary, status.LastSuccess?.At, status.LastRun?.Status, monitors, run,
            preferences.NotifyAdmins, preferences.NotifyOnSuccess);
    }

    public RestoreMonitor? ActiveRestore() =>
        operations.Restore() is { Active: true } p ? new RestoreMonitor(p.Status, p.Phase, p.Percent, p.Snapshot, p.At) : null;

    private static int? Int(JsonNode? details, string key) =>
        details?[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;
}
