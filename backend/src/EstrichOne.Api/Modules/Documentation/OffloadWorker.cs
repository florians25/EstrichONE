using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Documentation.Exports;
using EstrichOne.Api.Modules.Notifications.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Documentation;

/// <summary>
/// Hintergrunddienst der Speicher-Auslagerung: jede Minute wartende Originale nach bestätigter Sicherungskopie lokal löschen,
/// zurückgeholte Kopien auf dem Storage aufräumen und manuelle Aufträge ausführen; alle 10 Minuten den Platz prüfen
/// (knapp → auslagern bis Schwelle + 5 %), nach Regeln alle 6 Stunden, Abgleich nach dem Start und alle 6 Stunden.
/// </summary>
internal sealed partial class OffloadWorker(IServiceScopeFactory scopes, OffloadProgress progress, TimeProvider clock, ILogger<OffloadWorker> logger)
    : BackgroundService
{
    public static readonly TimeSpan SpaceCheckInterval = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan RulesInterval = TimeSpan.FromHours(6);
    public static readonly TimeSpan ReconcileInterval = TimeSpan.FromHours(6);

    private DateTimeOffset _lastSpaceCheck = DateTimeOffset.MinValue;
    private DateTimeOffset _lastRules = DateTimeOffset.MinValue;
    private DateTimeOffset _lastReconcile = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception.Message);
                progress.Update(s => s.Running ? s with { Running = false, FinishedAt = clock.GetUtcNow(), Message = $"Abgebrochen: {exception.Message}" } : s);
            }

            try
            {
                await progress.WaitAsync(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        await progress.Gate.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var service = services.GetRequiredService<OffloadService>();
            var settings = await service.FindConnectionAsync(cancellationToken);
            if (settings is null)
            {
                progress.Requests.Clear();
                return;
            }

            var now = clock.GetUtcNow();
            var finalized = await service.FinalizeAsync(settings, cancellationToken);
            await service.CleanupRemoteAsync(settings, cancellationToken);

            while (progress.Requests.TryDequeue(out var request))
            {
                if (request.Recall)
                {
                    var recalled = await service.RecallAsync(settings, request.StorageKeys, request.BauakteId, cancellationToken);
                    await AuditAsync(services, "files.offload.recalled", request.UserId, recalled, request.BauakteId, cancellationToken);
                }
                else
                {
                    var uploaded = 0;
                    for (var round = 0; round < 25; round++)
                    {
                        var count = await service.OffloadAsync(settings, request.BauakteId, request.IgnoreRules, null, cancellationToken);
                        uploaded += count;
                        if (count == 0)
                        {
                            break;
                        }
                    }

                    if (uploaded == 0)
                    {
                        progress.Update(_ => new OffloadJobStatus(false, "offload", 0, 0, 0, 0, now, now,
                            settings.Paired ? "Keine Dateien entsprechen den Regeln (Alter, Arten, abgeschlossene Bauakten) bzw. Storage nicht erreichbar."
                                : "Kein Storage gekoppelt."));
                    }

                    await AuditAsync(services, "files.offload.started", request.UserId, uploaded, request.BauakteId, cancellationToken);
                    finalized += await service.FinalizeAsync(settings, cancellationToken);
                }
            }

            if (settings.Paired && now - _lastSpaceCheck >= SpaceCheckInterval)
            {
                _lastSpaceCheck = now;
                await CheckSpaceAsync(services, service, settings, cancellationToken);
            }

            if (settings.Paired && settings.AutoByRules && now - _lastRules >= RulesInterval)
            {
                _lastRules = now;
                for (var round = 0; round < 25 && await service.OffloadAsync(settings, null, false, null, cancellationToken) > 0; round++)
                {
                }

                finalized += await service.FinalizeAsync(settings, cancellationToken);
            }

            if (now - _lastReconcile >= ReconcileInterval)
            {
                _lastReconcile = now;
                await service.ReconcileAsync(settings, cancellationToken);
                await NotifyUnreachableAsync(services, service, settings, cancellationToken);
            }

            if (finalized > 0)
            {
                await NotifyCompletedAsync(services, service, finalized, cancellationToken);
            }
        }
        finally
        {
            progress.Gate.Release();
        }
    }

    private async Task CheckSpaceAsync(IServiceProvider services, OffloadService service, OffloadConnection settings, CancellationToken cancellationToken)
    {
        if (service.Disk() is not { } disk || !OffloadService.IsLow(settings, disk.Free, disk.Total))
        {
            if (settings.LowSpaceNotifiedAt is not null)
            {
                var tracked = await service.ConnectionAsync(cancellationToken);
                tracked.NotifiedLowSpace(null);
                await services.GetRequiredService<EstrichOneDbContext>().SaveChangesAsync(cancellationToken);
            }

            return;
        }

        var now = clock.GetUtcNow();
        var connection = await service.ConnectionAsync(cancellationToken);
        if (connection.LowSpaceNotifiedAt is not { } notified || now - notified > TimeSpan.FromDays(1))
        {
            connection.NotifiedLowSpace(now);
            await services.GetRequiredService<EstrichOneDbContext>().SaveChangesAsync(cancellationToken);
            var percent = disk.Free * 100.0 / disk.Total;
            await NotifyAsync(services, new NotificationRequest(NotificationCategory.System, "Speicher auf dem Server knapp",
                $"Noch {Gb(disk.Free)} frei ({percent:0.#} %). {(settings.AutoWhenLow ? "Originale werden automatisch auf den Storage ausgelagert." : "Automatische Auslagerung ist aus – bitte unter Speicher-Auslagerung handeln.")}",
                "/system/auslagerung", "offload", null, NotificationUrgency.High, $"offload:low:{now:yyyyMMdd}", NotificationEvents.OffloadLowSpace), cancellationToken);
        }

        if (!settings.AutoWhenLow)
        {
            return;
        }

        var wanted = OffloadService.BytesToFree(settings, disk.Free, disk.Total);
        long offloadedBytes = 0;
        for (var round = 0; round < 25 && offloadedBytes < wanted; round++)
        {
            var before = await PendingBytesAsync(services, cancellationToken);
            if (await service.OffloadAsync(settings, null, false, wanted - offloadedBytes, cancellationToken) == 0)
            {
                break;
            }

            offloadedBytes += await PendingBytesAsync(services, cancellationToken) - before;
        }

        var tracked2 = await service.ConnectionAsync(cancellationToken);
        tracked2.Ran(now, offloadedBytes > 0
            ? $"Platz knapp: {Gb(offloadedBytes)} zum Auslagern übertragen und geprüft."
            : "Platz knapp, aber keine Dateien entsprechen den Regeln (Alter, Arten, abgeschlossene Bauakten).");
        await services.GetRequiredService<EstrichOneDbContext>().SaveChangesAsync(cancellationToken);
    }

    private static Task<long> PendingBytesAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<EstrichOneDbContext>().Set<OffloadedBlob>().Where(b => b.State != OffloadState.Recalled).SumAsync(b => b.SizeBytes, cancellationToken);

    private async Task NotifyCompletedAsync(IServiceProvider services, OffloadService service, int finalized, CancellationToken cancellationToken)
    {
        var disk = service.Disk();
        var connection = await service.ConnectionAsync(cancellationToken);
        var message = $"{finalized} Original{(finalized == 1 ? "" : "e")} ausgelagert{(disk is { } d ? $", frei: {Gb(d.Free)}" : "")}.";
        connection.Ran(clock.GetUtcNow(), message);
        var db = services.GetRequiredService<EstrichOneDbContext>();
        services.GetRequiredService<IAuditLog>().Record("files.offload.completed", null, "offload", null,
            new Dictionary<string, string?> { ["files"] = finalized.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        await db.SaveChangesAsync(cancellationToken);
        await NotifyAsync(services, new NotificationRequest(NotificationCategory.System, "Dateien auf den Storage ausgelagert", message,
            "/system/auslagerung", "offload", null, NotificationUrgency.Low, $"offload:done:{clock.GetUtcNow():O}", NotificationEvents.OffloadCompleted), cancellationToken);
    }

    private async Task NotifyUnreachableAsync(IServiceProvider services, OffloadService service, OffloadConnection settings, CancellationToken cancellationToken)
    {
        var health = await services.GetRequiredService<IOffloadHealth>().GetAsync(cancellationToken);
        if (!health.Configured || (health.Reachable && health.Missing == 0) || health.OffloadedFiles + health.Missing == 0)
        {
            return;
        }

        var connection = await service.ConnectionAsync(cancellationToken);
        if (connection.UnreachableNotifiedAt is { } at && clock.GetUtcNow() - at < TimeSpan.FromDays(1))
        {
            return;
        }

        connection.NotifiedUnreachable(clock.GetUtcNow());
        await services.GetRequiredService<EstrichOneDbContext>().SaveChangesAsync(cancellationToken);
        var text = health.Reachable
            ? $"{health.Missing} ausgelagerte Original(e) fehlen auf dem Storage. Unter Speicher-Auslagerung „Aus Sicherung zurückholen“ wählen."
            : $"Storage „{settings.ServerName}“ nicht erreichbar – {health.OffloadedFiles} ausgelagerte Originale lassen sich gerade nicht öffnen. Nichts ist verloren.";
        await NotifyAsync(services, new NotificationRequest(NotificationCategory.System, "Storage der Auslagerung: Handlungsbedarf", text,
            "/system/auslagerung", "offload", null, NotificationUrgency.High, $"offload:down:{clock.GetUtcNow():yyyyMMdd}", NotificationEvents.OffloadUnreachable),
            cancellationToken);
    }

    private static async Task NotifyAsync(IServiceProvider services, NotificationRequest request, CancellationToken cancellationToken)
    {
        var admins = await services.GetRequiredService<IRoleAssignments>().GetUserIdsWithPermissionAsync(Permissions.SettingsManage, cancellationToken);
        if (admins.Count > 0)
        {
            await services.GetRequiredService<INotifier>().NotifyAsync(admins, request, cancellationToken);
        }
    }

    private static async Task AuditAsync(IServiceProvider services, string action, Guid? userId, int count, Guid? bauakteId, CancellationToken cancellationToken)
    {
        services.GetRequiredService<IAuditLog>().Record(action, userId, bauakteId is null ? "offload" : "bauakte", bauakteId?.ToString(),
            new Dictionary<string, string?> { ["files"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        await services.GetRequiredService<EstrichOneDbContext>().SaveChangesAsync(cancellationToken);
    }

    internal static string Gb(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024.0 * 1024 * 1024):0.0} GB".Replace('.', ',')
        : $"{bytes / (1024.0 * 1024):0} MB";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Speicher-Auslagerung: {Error}")]
    private static partial void LogFailed(ILogger logger, string error);
}

/// <summary>Zustand für das Status-Banner (live, kurze Zeitüberschreitung).</summary>
internal sealed class OffloadHealth(OffloadService service, EstrichOneDbContext db) : IOffloadHealth
{
    public async Task<OffloadHealthInfo> GetAsync(CancellationToken cancellationToken)
    {
        var connection = await service.FindConnectionAsync(cancellationToken);
        if (connection is not { Paired: true })
        {
            return new OffloadHealthInfo(false, true, null, null, 0, 0);
        }

        var offloaded = await db.Set<OffloadedBlob>().CountAsync(b => b.State == OffloadState.Offloaded, cancellationToken);
        var missing = await db.Set<OffloadedBlob>().CountAsync(b => b.State == OffloadState.Missing, cancellationToken);
        var reachable = await service.ServerStatusAsync(connection, cancellationToken) is not null;
        return new OffloadHealthInfo(true, reachable, connection.ServerName, connection.LastContactAt, offloaded, missing);
    }
}

/// <summary>Storage für die Fernaktualisierung (Auftrag und Anzeige im Backup-Modul).</summary>
internal sealed class OffloadUpdatablePeers(OffloadService service) : Backup.Exports.IUpdatablePeerSource
{
    public Task<IReadOnlyList<Backup.Exports.UpdatablePeer>> ListAsync(CancellationToken cancellationToken) => service.UpdatablePeersAsync(cancellationToken);
}

/// <summary>
/// Ausgelagerte bzw. zur Auslagerung übertragene Originale dürfen sich nicht mehr ändern (ihre Prüfsumme liegt auf dem Storage):
/// die Nachverdichtung behandelt sie wie archivierte Dateien.
/// </summary>
internal sealed class OffloadFrozenFiles(EstrichOneDbContext db) : IFileArchiveState
{
    public async Task<IReadOnlySet<Guid>> ArchivedOrQueuedAsync(IReadOnlyCollection<Guid> fileIds, CancellationToken cancellationToken) =>
        (await db.Set<FileRecord>().AsNoTracking()
            .Where(f => fileIds.Contains(f.Id) && db.Set<OffloadedBlob>().Any(b => b.StorageKey == f.StorageKey))
            .Select(f => f.Id).ToListAsync(cancellationToken)).ToHashSet();
}
