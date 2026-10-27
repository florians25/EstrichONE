using System.Globalization;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Backup.Exports;
using EstrichOne.Api.Modules.Notifications.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Archive;

/// <param name="Severity"><c>ok</c> oder <c>critical</c> (rotes Banner).</param>
/// <param name="Reachable">Alle eingerichteten Backup-Ziele erreichbar und der letzte Lauf nicht fehlgeschlagen.</param>
public sealed record BackupHealth(bool Configured, bool Reachable, DateTimeOffset? LastSuccessAt, string Severity, string? Message, bool RestoreRunning);

/// <param name="Severity"><c>ok</c> oder <c>warning</c> (gelbes Banner).</param>
public sealed record ArchiveHealth(bool Configured, bool Reachable, DateTimeOffset? LastSuccessAt, string Severity, string? Message);

/// <param name="Key"><c>backup</c> oder <c>archive</c>.</param>
/// <param name="Severity"><c>critical</c> (rot) oder <c>warning</c> (gelb).</param>
public sealed record HealthBanner(string Key, string Severity, string Text);

/// <summary>Status für die Banner aller Clients (Web-Shell, iOS, Android). <c>Banners</c> ist leer, wenn alles in Ordnung ist.</summary>
public sealed record HealthBannerResponse(BackupHealth Backup, ArchiveHealth Archive, IReadOnlyList<HealthBanner> Banners, DateTimeOffset CheckedAt);

/// <summary>
/// Ermittelt den Zustand von Backup-Server und Archivserver (mit Live-Abfrage) und hält ihn für alle Clients vor;
/// benachrichtigt Administratoren (Recht backup.manage) nach jeder Sicherung und bei Ausfällen (einmal, dann täglich).
/// </summary>
public sealed class SystemHealthMonitor(IServiceScopeFactory scopes, TimeProvider clock) : IDisposable
{
    /// <summary>Älter als das gilt das letzte Backup als ausgefallen (tägliche Sicherung + 2 Stunden Puffer).</summary>
    public static readonly TimeSpan BackupMaxAge = TimeSpan.FromHours(26);

    /// <summary>Zwischenstand höchstens so alt ausliefern; sonst beim Abruf neu ermitteln.</summary>
    public static readonly TimeSpan CacheAge = TimeSpan.FromMinutes(10);

    private readonly SemaphoreSlim _refresh = new(1, 1);
    private HealthBannerResponse? _current;
    private DateTimeOffset? _lastRunNotified;
    private bool _backupOutageNotified;
    private bool _archiveOutageNotified;

    public void Dispose() => _refresh.Dispose();

    public async Task<HealthBannerResponse> GetAsync(CancellationToken cancellationToken)
    {
        var current = _current;
        return current is not null && clock.GetUtcNow() - current.CheckedAt < CacheAge ? current : await RefreshAsync(notify: false, cancellationToken);
    }

    public async Task<HealthBannerResponse> RefreshAsync(bool notify, CancellationToken cancellationToken)
    {
        await _refresh.WaitAsync(cancellationToken);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var now = clock.GetUtcNow();
            var backup = await services.GetRequiredService<IBackupMonitoring>().GetAsync(cancellationToken);
            var restore = services.GetRequiredService<IBackupMonitoring>().ActiveRestore();

            // Eingerichtet = es gibt ein Backup-Ziel auf anderer Hardware oder der Dienst hat schon einmal erfolgreich gesichert.
            var backupConfigured = backup.Configured && (backup.Targets.Count > 0 || backup.LastSuccessAt is not null);
            var backupReachable = backup.Targets.All(t => t.Reachable) && backup.LastRunStatus != "failed";
            var backupStale = backup.LastSuccessAt is not { } success || now - success > BackupMaxAge;
            var backupCritical = backupConfigured && restore is null && (!backupReachable || backupStale);
            var backupText = backupCritical
                ? $"ACHTUNG: Backup-Server ausgefallen oder nicht erreichbar (Letztes Backup {Local(backup.LastSuccessAt)}). Bitte geben Sie umgehend Ihrem Admin Bescheid."
                : null;

            var archiveService = services.GetRequiredService<ArchiveService>();
            var db = services.GetRequiredService<EstrichOneDbContext>();
            var connection = await archiveService.ConnectionAsync(cancellationToken);
            DateTimeOffset? lastArchived = null;
            var archiveReachable = true;
            if (connection is not null)
            {
                archiveReachable = await archiveService.ServerStatusAsync(connection, cancellationToken) is not null;
                lastArchived = await db.Set<ArchiveRecord>().Where(r => r.ArchivedAt != null).MaxAsync(r => r.ArchivedAt, cancellationToken);
            }

            var archiveWarning = connection is not null && !archiveReachable;
            var archiveText = archiveWarning
                ? $"Hinweis: Archiv-Server ausgefallen oder nicht erreichbar (Letzte Archivierung {Local(lastArchived)}). Bitte informieren Sie Ihren Admin."
                : null;

            var banners = new List<HealthBanner>();
            if (backupText is not null) banners.Add(new HealthBanner("backup", "critical", backupText));
            if (archiveText is not null) banners.Add(new HealthBanner("archive", "warning", archiveText));
            var result = new HealthBannerResponse(
                new BackupHealth(backupConfigured, backupReachable, backup.LastSuccessAt, backupCritical ? "critical" : "ok", backupText, restore is not null),
                new ArchiveHealth(connection is not null, archiveReachable, lastArchived, archiveWarning ? "warning" : "ok", archiveText),
                banners, now);
            _current = result;

            if (notify)
            {
                await NotifyAsync(services, backup, result, connection?.LastContactAt, cancellationToken);
            }

            return result;
        }
        finally
        {
            _refresh.Release();
        }
    }

    private async Task NotifyAsync(IServiceProvider services, BackupMonitor backup, HealthBannerResponse health, DateTimeOffset? archiveContact,
        CancellationToken cancellationToken)
    {
        if (!backup.NotifyAdmins)
        {
            return;
        }

        var notifier = services.GetRequiredService<INotifier>();
        var admins = await services.GetRequiredService<IRoleAssignments>().GetUserIdsWithPermissionAsync(Permissions.BackupManage, cancellationToken);
        if (admins.Count == 0)
        {
            return;
        }

        var now = clock.GetUtcNow();
        const string link = "/system/sicherung";

        // Nach jeder Sicherung (Erfolg kurz, Fehler deutlich). Beim ersten Durchlauf nach dem Start nur ganz frische Läufe melden.
        if (backup.LastRun is { Status: "succeeded" or "failed" } run && run.At != _lastRunNotified
            && (_lastRunNotified is not null || now - run.At < TimeSpan.FromMinutes(15)))
        {
            _lastRunNotified = run.At;
            if (run.Status == "failed")
            {
                await notifier.NotifyAsync(admins, new NotificationRequest(NotificationCategory.System, "Sicherung fehlgeschlagen",
                    $"{run.Message} ({Local(run.At)}). Bitte im Backup Center prüfen.", link, "backup", null, NotificationUrgency.High,
                    $"backup:run:{run.At:O}", NotificationEvents.BackupFailed), cancellationToken);
            }
            else if (backup.NotifyOnSuccess)
            {
                var details = string.Join(" · ", new[]
                {
                    run.Kind == "incremental" ? "inkrementell" : "Vollsicherung",
                    run.DurationSeconds is { } d ? $"{Math.Max(1, (d + 59) / 60)} min" : null,
                    run.Targets is { } t ? $"{t} Ziel{(t == 1 ? "" : "e")}" : null,
                }.Where(v => v is not null));
                await notifier.NotifyAsync(admins, new NotificationRequest(NotificationCategory.System, "Sicherung erfolgreich",
                    $"{Local(run.At)} · {details}", link, "backup", null, NotificationUrgency.Low, $"backup:run:{run.At:O}", NotificationEvents.BackupSucceeded), cancellationToken);
            }
        }
        else if (backup.LastRun is { } seen && _lastRunNotified is null)
        {
            _lastRunNotified = seen.At;
        }

        // Ausfall: einmal, dann täglich erinnern (Schlüssel je Tag seit der letzten erfolgreichen Sicherung bzw. dem letzten Kontakt).
        if (health.Backup.Severity == "critical")
        {
            var anchor = health.Backup.LastSuccessAt ?? DateTimeOffset.UnixEpoch;
            var day = (int)Math.Max(0, (now - anchor).TotalDays);
            await notifier.NotifyAsync(admins, new NotificationRequest(NotificationCategory.System, "Backup-Server ausgefallen oder nicht erreichbar",
                $"Letztes Backup {Local(health.Backup.LastSuccessAt)}. Backup-Ziele und Backup-Dienst prüfen.", link, "backup", null,
                NotificationUrgency.High, $"backup:outage:{anchor:O}:{day}", NotificationEvents.BackupTargetDown), cancellationToken);
            _backupOutageNotified = true;
        }
        else if (_backupOutageNotified)
        {
            _backupOutageNotified = false;
            await notifier.NotifyAsync(admins, new NotificationRequest(NotificationCategory.System, "Backup wieder in Ordnung",
                $"Letztes Backup {Local(health.Backup.LastSuccessAt)}.", link, "backup", null, NotificationUrgency.Low, Event: NotificationEvents.BackupTargetDown), cancellationToken);
        }

        if (health.Archive.Severity == "warning")
        {
            var anchor = archiveContact ?? DateTimeOffset.UnixEpoch;
            var day = (int)Math.Max(0, (now - anchor).TotalDays);
            await notifier.NotifyAsync(admins, new NotificationRequest(NotificationCategory.System, "Archiv-Server nicht erreichbar",
                $"Letzte Archivierung {Local(health.Archive.LastSuccessAt)}. Dokumente werden zurückgehalten und später übertragen.", "/system/archiv",
                "archive", null, NotificationUrgency.Normal, $"archive:outage:{anchor:O}:{day}", NotificationEvents.ArchiveDown), cancellationToken);
            _archiveOutageNotified = true;
        }
        else if (_archiveOutageNotified)
        {
            _archiveOutageNotified = false;
            await notifier.NotifyAsync(admins, new NotificationRequest(NotificationCategory.System, "Archiv-Server wieder erreichbar", null, "/system/archiv",
                "archive", null, NotificationUrgency.Low, Event: NotificationEvents.ArchiveDown), cancellationToken);
        }
    }

    internal static string Local(DateTimeOffset? at)
    {
        if (at is not { } value)
        {
            return "noch keines";
        }

        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.Utc;
        }

        return TimeZoneInfo.ConvertTime(value, zone).ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture) + " Uhr";
    }
}

/// <summary>Prüft jede Minute (Live-Abfrage der Ziele alle 5 Minuten bzw. sofort nach einem Backup-Lauf).</summary>
internal sealed partial class SystemHealthWorker(SystemHealthMonitor monitor, ILogger<SystemHealthWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
            do
            {
                try
                {
                    await monitor.RefreshAsync(notify: true, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogFailed(logger, exception.Message);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Systemstatus (Backup/Archiv): {Error}")]
    private static partial void LogFailed(ILogger logger, string error);
}

internal static class SystemHealthEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/system/health-banner", async (SystemHealthMonitor monitor, CancellationToken ct) => TypedResults.Ok(await monitor.GetAsync(ct)))
            .WithTags("Überwachung").WithName("GetHealthBanner")
            .WithSummary("Status-Banner für alle Clients: Backup-Server (rot) und Archivserver (gelb), nur wenn eingerichtet. Bis 10 Minuten zwischengespeichert.")
            .RequireAuthorization();
    }
}
