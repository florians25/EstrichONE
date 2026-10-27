using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Notifications.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Notifications;

/// <summary>Weckt den Versand sofort nach neuen Benachrichtigungen (sonst prüft er alle 30 Sekunden).</summary>
internal sealed class PushSignal : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Dispose() => _signal.Dispose();

    public void Wake()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Schon geweckt.
            }
        }
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}

internal sealed partial class Notifier(EstrichOneDbContext db, ICurrentUser currentUser, TimeProvider clock, PushSignal signal, ILogger<Notifier> logger)
    : INotifier
{
    public async Task NotifyAsync(IEnumerable<Guid> userIds, NotificationRequest notification, CancellationToken cancellationToken)
    {
        try
        {
            var actor = Actor();
            var ids = userIds.Where(id => id != Guid.Empty && id != actor).Distinct().ToList();
            if (ids.Count == 0)
            {
                return;
            }

            var active = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync(cancellationToken);
            if (notification.DedupKey is { } key)
            {
                var existing = await db.Set<Notification>().AsNoTracking().Where(n => active.Contains(n.UserId) && n.DedupKey == key)
                    .Select(n => n.UserId).ToListAsync(cancellationToken);
                active = active.Except(existing).ToList();
            }

            if (active.Count == 0)
            {
                return;
            }

            // Eigener Kontext-Zustand: nur die neuen Einträge speichern, nichts vom Aufrufer mitnehmen.
            var now = clock.GetUtcNow();
            var rows = active.Select(id => Notification.Create(id, notification, now, actor)).ToList();
            db.AddRange(rows);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                foreach (var row in rows)
                {
                    db.Entry(row).State = EntityState.Detached;
                }
            }

            signal.Wake();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Benachrichtigung ist Zusatz – die fachliche Änderung ist gespeichert.
            LogFailed(logger, exception, notification.Category.ToString());
        }
    }

    private Guid? Actor()
    {
        try
        {
            return currentUser.UserId;
        }
        catch (InvalidOperationException)
        {
            return null; // Hintergrunddienst ohne angemeldeten Benutzer.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Benachrichtigung ({Category}) nicht angelegt")]
    private static partial void LogFailed(ILogger logger, Exception exception, string category);
}

/// <summary>Stellt neue Benachrichtigungen als Push zu (Web Push, APNs) und vermerkt den Zustellstatus.</summary>
internal sealed partial class NotificationDispatcher(IServiceScopeFactory scopes, PushSignal signal, TimeProvider clock, ILogger<NotificationDispatcher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (await DispatchBatchAsync(stoppingToken) > 0)
                {
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogBatchFailed(logger, exception);
            }

            try
            {
                await signal.WaitAsync(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Bis zu 50 wartende Benachrichtigungen zustellen; gibt die Anzahl zurück.</summary>
    public async Task<int> DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        List<Notification> pending;
        try
        {
            pending = await db.Set<Notification>().Where(n => n.PushState == PushState.Pending).OrderBy(n => n.CreatedAt).Take(50).ToListAsync(cancellationToken);
        }
        catch (Npgsql.PostgresException exception) when (exception.SqlState == Npgsql.PostgresErrorCodes.UndefinedTable)
        {
            return 0; // Datenbank noch nicht migriert.
        }

        if (pending.Count == 0)
        {
            return 0;
        }

        var keys = scope.ServiceProvider.GetRequiredService<PushKeys>();
        var gateway = scope.ServiceProvider.GetRequiredService<PushGateway>();
        var config = await keys.ConfigAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var users = pending.Select(n => n.UserId).Distinct().ToList();
        var devices = await db.Set<PushDevice>().Where(d => users.Contains(d.UserId)).ToListAsync(cancellationToken);
        var unread = await db.Set<Notification>().AsNoTracking().Where(n => users.Contains(n.UserId) && n.ReadAt == null)
            .GroupBy(n => n.UserId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        foreach (var notification in pending)
        {
            if (now - notification.CreatedAt > TimeSpan.FromHours(24))
            {
                notification.SetPush(PushState.Expired, now);
                continue;
            }

            var targets = devices.Where(d => d.UserId == notification.UserId && db.Entry(d).State != EntityState.Deleted).ToList();
            if (targets.Count == 0)
            {
                notification.SetPush(PushState.NoDevice, now);
                continue;
            }

            var message = new PushMessage(notification.Id,
                config.ShowContent ? notification.Title : "Neue Benachrichtigung",
                config.ShowContent ? Short(notification.Body) : "In EstrichONE öffnen",
                notification.Link, notification.Category.ToString().ToLowerInvariant(), notification.Urgency.ToString().ToLowerInvariant(),
                unread.GetValueOrDefault(notification.UserId));
            var delivered = 0;
            var notConfigured = 0;
            string? lastError = null;
            foreach (var device in targets)
            {
                var (result, error) = await gateway.SendAsync(device, message, config, cancellationToken);
                switch (result)
                {
                    case PushResult.Delivered:
                        delivered++;
                        device.Succeeded(now);
                        break;
                    case PushResult.Gone:
                        db.Remove(device);
                        lastError = error;
                        break;
                    case PushResult.NotConfigured:
                        notConfigured++;
                        lastError = error;
                        break;
                    default:
                        device.Failed(error ?? "Fehler");
                        lastError = error;
                        break;
                }
            }

            notification.SetPush(delivered > 0 ? PushState.Sent : notConfigured == targets.Count ? PushState.InAppOnly : PushState.Failed, now,
                delivered > 0 ? null : lastError);
        }

        await db.SaveChangesAsync(cancellationToken);
        return pending.Count;
    }

    /// <summary>Text der Pushnachricht kurz halten (Sperrbildschirm); vollständig im Postfach.</summary>
    private static string? Short(string? body) => body is null ? null : body.Length > 180 ? body[..177] + "…" : body;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Versand der Pushnachrichten fehlgeschlagen")]
    private static partial void LogBatchFailed(ILogger logger, Exception exception);
}

/// <summary>Meldet einmal je Tag fällige Fristen (heute oder morgen) aller Quellen ins Postfach.</summary>
internal sealed partial class DeadlineScanner(IServiceScopeFactory scopes, TimeProvider clock, ILogger<DeadlineScanner> logger) : BackgroundService
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), clock, stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
            do
            {
                try
                {
                    await ScanAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogScanFailed(logger, exception);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Herunterfahren.
        }
    }

    public async Task<int> ScanAsync(CancellationToken cancellationToken)
    {
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Berlin);
        if (local.Hour < 6)
        {
            return 0; // Nicht nachts melden.
        }

        var today = DateOnly.FromDateTime(local.DateTime);
        await using var scope = scopes.CreateAsyncScope();
        var notifier = scope.ServiceProvider.GetRequiredService<INotifier>();
        var count = 0;
        foreach (var source in scope.ServiceProvider.GetServices<IDeadlineSource>())
        {
            foreach (var item in await source.DueAsync(today.AddDays(1), cancellationToken))
            {
                var when = item.DueDate < today ? "überfällig seit " + item.DueDate.ToString("dd.MM.", System.Globalization.CultureInfo.GetCultureInfo("de-DE"))
                    : item.DueDate == today ? "heute fällig" : "morgen fällig";
                await notifier.NotifyAsync([item.UserId], new NotificationRequest(NotificationCategory.Deadline, item.Title, $"Frist: {when}", item.Link,
                    item.ObjectType, item.ObjectId, item.DueDate <= today ? NotificationUrgency.High : NotificationUrgency.Normal,
                    $"deadline:{item.Key}:{item.DueDate:yyyy-MM-dd}"), cancellationToken);
                count++;
            }
        }

        return count;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fristenprüfung fehlgeschlagen")]
    private static partial void LogScanFailed(ILogger logger, Exception exception);
}
