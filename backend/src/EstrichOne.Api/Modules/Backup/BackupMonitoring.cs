using EstrichOne.Api.Modules.Backup.Exports;

namespace EstrichOne.Api.Modules.Backup;

internal sealed class BackupMonitoring(BackupStatusReader reader, BackupTargetService targets) : IBackupMonitoring
{
    public async Task<BackupMonitor> GetAsync(CancellationToken cancellationToken)
    {
        var status = reader.Read();
        var list = await targets.ListAsync(live: true, cancellationToken);
        return new BackupMonitor(status.Configured, status.Healthy, status.Summary, status.LastSuccess?.At, status.LastRun?.Status,
            [.. list.Select(t => new BackupTargetMonitor(t.Name, t.Address, t.Remote is not null, t.LastRun?.Status, t.LastRun?.At,
                t.Remote?.DiskFreeBytes, t.Remote?.DiskTotalBytes, t.Remote?.Bytes, t.LastError))]);
    }
}
