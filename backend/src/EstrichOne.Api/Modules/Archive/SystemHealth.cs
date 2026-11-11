using System.Globalization;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Backup.Exports;
using EstrichOne.Api.Modules.Documentation.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Notifications.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Archive;

/// <param name="Severity"><c>ok</c> oder <c>critical</c> (rotes Banner).</param>
/// <param name="Reachable">Alle eingerichteten Backup-Ziele erreichbar und der letzte Lauf nicht fehlgeschlagen.</param>
public sealed record BackupHealth(bool Configured, bool Reachable, DateTimeOffset? LastSuccessAt, string Severity, string? Message, bool RestoreRunning);

/// <param name="Severity"><c>ok</c> oder <c>warning</c> (gelbes Banner).</param>
public sealed record ArchiveHealth(bool Configured, bool Reachable, DateTimeOffset? LastSuccessAt, string Severity, string? Message);

/// <param name="Key"><c>backup</c>, <c>archive</c> oder <c>offload</c> (Storage der Speicher-Auslagerung).</param>
/// <param name="Severity"><c>critical</c> (rot) oder <c>warning</c> (gelb).</param>
/// <param name="AdminOnly">Nur für die Verwaltung (Recht settings.manage); anderen Benutzern wird das Banner nicht ausgeliefert.</param>
public sealed record HealthBanner(string Key, string Severity, string Text, bool AdminOnly = false);

/// <summary>Status für die Banner aller Clients (Web-Shell, iOS, Android). <c>Banners</c> ist leer, wenn alles in Ordnung ist.</summary>
public sealed record HealthBannerResponse(BackupHealth Backup, ArchiveHealth Archive, IReadOnlyList<HealthBanner> Banners, DateTimeOffset CheckedAt);

/// <summary>
/// Merkt sich je überwachtem Server (Archivserver, jedes Backup-Ziel) den Zeitpunkt des ersten Fehlschlags einer
/// ununterbrochenen Störung; jeder erfolgreiche Kontakt setzt ihn zurück. Gemeldet wird ein Ausfall erst, wenn er die
/// Schwelle aus den Einstellungen (<see cref="MonitoringSettings.OutageGraceMinutes"/>, Standard 3 Minuten) erreicht –
/// kurze Aussetzer (Neustart, Netzwackler) lösen weder Banner noch Benachrichtigung aus. Nur im Speicher: Nach einem
/// Neustart der API beginnt die Frist neu.
/// </summary>
public sealed class OutageTracker(TimeProvider clock)
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> _since = new(StringComparer.Ordinal);

    /// <summary>Ergebnis einer Prüfung eintragen; liefert den Beginn der laufenden Störung (<c>null</c> = erreichbar).</summary>
    public DateTimeOffset? Observe(string key, bool reachable)
    {
        if (reachable)
        {
            _since.TryRemove(key, out _);
            return null;
        }

        return _since.GetOrAdd(key, clock.GetUtcNow());
    }

    private TimeSpan _grace = TimeSpan.FromMinutes(MonitoringSettings.DefaultOutageGraceMinutes);

    /// <summary>Mindestens ein Server ist nicht erreichbar, die Störung liegt aber noch innerhalb der Meldefrist.</summary>
    public bool Pending => _since.Values.Any(at => clock.GetUtcNow() - at < _grace);

    public DateTimeOffset? Since(string key) => _since.TryGetValue(key, out var since) ? since : null;

    /// <summary>Schlüssel mit diesem Präfix vergessen, die nicht mehr überwacht werden (z. B. entkoppelte Backup-Ziele).</summary>
    public void Retain(string prefix, IEnumerable<string> keys)
    {
        var keep = keys.ToHashSet(StringComparer.Ordinal);
        foreach (var key in _since.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && !keep.Contains(k)))
        {
            _since.TryRemove(key, out _);
        }
    }

    /// <summary>Störung dauert ununterbrochen mindestens <paramref name="grace"/> (dann melden).</summary>
    public bool Exceeded(DateTimeOffset? since, TimeSpan grace)
    {
        _grace = grace;
        return since is { } at && clock.GetUtcNow() - at >= grace;
    }

    public const string ArchiveKey = "archive";

    public static string BackupKey(BackupTargetMonitor target) => $"backup:{target.Name}|{target.Address}";

    internal static async Task<TimeSpan> GraceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var settings = services.GetService<ISystemSettings>() is { } store ? (await store.GetAsync(cancellationToken)).Monitoring : new MonitoringSettings();
        return TimeSpan.FromMinutes(settings.OutageGraceMinutes);
    }
}

/// <summary>
/// Ermittelt den Zustand von Backup-Server und Archivserver (mit Live-Abfrage) und hält ihn für alle Clients vor;
/// benachrichtigt Administratoren (Recht backup.manage) nach jeder Sicherung und bei Ausfällen (einmal, dann täglich).
/// Nicht erreichbare Server gelten erst nach der Frist aus <see cref="OutageTracker"/> als ausgefallen.
/// </summary>
public sealed class SystemHealthMonitor(IServiceScopeFactory scopes, TimeProvider clock, OutageTracker outages) : IDisposable
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

    /// <summary>Nach Koppeln/Entkoppeln: nächste Abfrage prüft neu (z. B. „Abschließen &amp; Archivieren“ sofort anbieten).</summary>
    public void Invalidate() => _current = null;

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
            // Nicht erreichbare Ziele erst nach der Frist melden (fehlgeschlagener Lauf und veraltete Sicherung sofort).
            var grace = await OutageTracker.GraceAsync(services, cancellationToken);
            var targetDown = false;
            foreach (var target in backup.Targets)
            {
                targetDown |= outages.Exceeded(outages.Observe(OutageTracker.BackupKey(target), target.Reachable), grace);
            }

            outages.Retain("backup:", backup.Targets.Select(OutageTracker.BackupKey));
            var backupCritical = backupConfigured && restore is null && (targetDown || backup.LastRunStatus == "failed" || backupStale);
            var backupText = backupCritical
                ? $"Backup-Ziel nicht erreichbar. Letzte erfolgreiche Sicherung: {Local(backup.LastSuccessAt)}. Bitte Backup-Ziele und Backup-Dienst unter System › Sicherung prüfen."
                : null;

            var archiveService = services.GetRequiredService<ArchiveService>();
            var db = services.GetRequiredService<EstrichOneDbContext>();
            var connection = await archiveService.ConnectionAsync(cancellationToken);
            DateTimeOffset? lastArchived = null;
            var archiveReachable = true;
            DateTimeOffset? archiveDownSince = null;
            if (connection is not null)
            {
                archiveReachable = await archiveService.ServerStatusAsync(connection, cancellationToken) is not null;
                archiveDownSince = outages.Observe(OutageTracker.ArchiveKey, archiveReachable);
                lastArchived = await db.Set<ArchiveRecord>().Where(r => r.ArchivedAt != null).MaxAsync(r => r.ArchivedAt, cancellationToken);
            }
            else
            {
                outages.Observe(OutageTracker.ArchiveKey, reachable: true);
            }

            // „Reachable“ bleibt der Live-Zustand (für „Abschließen & Archivieren“); gemeldet wird erst nach der Frist.
            var archiveWarning = connection is not null && !archiveReachable && outages.Exceeded(archiveDownSince, grace);
            var archiveText = archiveWarning
                ? $"Archivserver nicht erreichbar. Letzte Archivierung: {Local(lastArchived)}. Dokumente werden zurückgehalten; bitte Archivserver unter System › Archiv prüfen."
                : null;

            // Ausfälle von Backup- und Archivserver sehen nur Administratoren (Benutzer verunsichert das nur; Admins bekommen zusätzlich Push).
            var banners = new List<HealthBanner>();
            if (backupText is not null) banners.Add(new HealthBanner("backup", "critical", backupText, AdminOnly: true));
            if (archiveText is not null) banners.Add(new HealthBanner("archive", "warning", archiveText, AdminOnly: true));
            // Speicher-Auslagerung: gelb und nur für die Verwaltung (Benutzer bekommen beim Öffnen eine klare Meldung).
            if (services.GetService<IOffloadHealth>() is { } offloadHealth && await offloadHealth.GetAsync(cancellationToken) is { Configured: true } offload)
            {
                if (offload.Missing > 0)
                {
                    banners.Add(new HealthBanner("offload", "warning",
                        $"{offload.Missing} ausgelagerte Original(e) fehlen auf dem Storage. Wiederherstellung unter System › Speicher-Auslagerung › „Aus Sicherung zurückholen“.", AdminOnly: true));
                }
                else if (!offload.Reachable)
                {
                    banners.Add(new HealthBanner("offload", "warning",
                        $"Storage „{offload.StorageName}“ (Speicher-Auslagerung) nicht erreichbar. {offload.OffloadedFiles} ausgelagerte Originale können derzeit nicht geöffnet werden; es gehen keine Daten verloren.",
                        AdminOnly: true));
                }
            }
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
                    $"{run.Message} · {Local(run.At)}", link, "backup", null, NotificationUrgency.High,
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
            await notifier.NotifyAsync(admins, new NotificationRequest(NotificationCategory.System, "Backup-Ziel nicht erreichbar",
                $"Letzte erfolgreiche Sicherung: {Local(health.Backup.LastSuccessAt)}", link, "backup", null,
                NotificationUrgency.High, $"backup:outage:{anchor:O}:{day}", NotificationEvents.BackupTargetDown), cancellationToken);
            _backupOutageNotified = true;
        }
        else if (_backupOutageNotified)
        {
            _backupOutageNotified = false;
            await notifier.NotifyAsync(admins, new NotificationRequest(NotificationCategory.System, "Backup-Ziel wieder erreichbar",
                $"Letzte erfolgreiche Sicherung: {Local(health.Backup.LastSuccessAt)}", link, "backup", null, NotificationUrgency.Low, Event: NotificationEvents.BackupTargetDown), cancellationToken);
        }

        if (health.Archive.Severity == "warning")
        {
            var anchor = archiveContact ?? DateTimeOffset.UnixEpoch;
            var day = (int)Math.Max(0, (now - anchor).TotalDays);
            await notifier.NotifyAsync(admins, new NotificationRequest(NotificationCategory.System, "Archivserver nicht erreichbar",
                $"Letzte Archivierung: {Local(health.Archive.LastSuccessAt)}. Dokumente werden nach Wiederherstellung der Verbindung übertragen.", "/system/archiv",
                "archive", null, NotificationUrgency.Normal, $"archive:outage:{anchor:O}:{day}", NotificationEvents.ArchiveDown), cancellationToken);
            _archiveOutageNotified = true;
        }
        else if (_archiveOutageNotified)
        {
            _archiveOutageNotified = false;
            await notifier.NotifyAsync(admins, new NotificationRequest(NotificationCategory.System, "Archivserver wieder erreichbar", null, "/system/archiv",
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

/// <summary>
/// Prüft jede Minute live (Backup-Ziele, Archivserver); solange eine Störung noch innerhalb der Meldefrist liegt, alle
/// 30 Sekunden – so kommt die Meldung zeitnah nach Ablauf der Frist (Standard 3 Minuten), kurze Aussetzer bleiben still.
/// </summary>
internal sealed partial class SystemHealthWorker(SystemHealthMonitor monitor, OutageTracker outages, ILogger<SystemHealthWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan PendingInterval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await monitor.RefreshAsync(notify: true, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogFailed(logger, exception.Message);
                }

                await Task.Delay(outages.Pending ? PendingInterval : Interval, stoppingToken);
            }
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
        api.MapGet("/system/health-banner", async (SystemHealthMonitor monitor, ICurrentUser user, IRoleAssignments roles, CancellationToken ct) =>
            {
                var health = await monitor.GetAsync(ct);
                var admin = (await roles.GetUserIdsWithPermissionAsync(Permissions.SettingsManage, ct)).Contains(user.UserId)
                    || (await roles.GetUserIdsWithPermissionAsync(Permissions.BackupManage, ct)).Contains(user.UserId);
                if (!admin)
                {
                    // Benutzer: keine Ausfallhinweise (auch nicht über Schweregrad/Text ableitbar); Erreichbarkeit bleibt für
                    // „Abschließen & Archivieren“ erhalten.
                    health = health with
                    {
                        Banners = [.. health.Banners.Where(b => !b.AdminOnly)],
                        Backup = health.Backup with { Severity = "ok", Message = null },
                        Archive = health.Archive with { Severity = "ok", Message = null },
                    };
                }

                return TypedResults.Ok(health);
            })
            .WithTags("Überwachung").WithName("GetHealthBanner")
            .WithSummary("Status-Banner für alle Clients: Backup-Server (rot), Archivserver (gelb), Storage der Auslagerung (gelb) – Ausfallhinweise nur für Administratoren, nur wenn eingerichtet. Bis 10 Minuten zwischengespeichert.")
            .RequireAuthorization();
    }
}
