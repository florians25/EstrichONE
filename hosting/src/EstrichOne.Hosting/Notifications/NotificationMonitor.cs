using System.Collections.Concurrent;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Docker;
using EstrichOne.Hosting.Infrastructure;
using EstrichOne.Hosting.Status;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Notifications;

/// <summary>Ein Befund, der eine Benachrichtigung auslöst.</summary>
public sealed record Finding(string Key, string Severity, string Title, string Text, Guid? TenantId, string? TenantSlug);

/// <summary>Eingangsdaten je Instanz für die Bewertung.</summary>
public sealed record TenantObservation(Tenant Tenant, TenantStatus Status, CertificateInfo? Certificate, bool OperationOpen);

/// <summary>
/// Überwacht alle Instanzen und den Server; neue Probleme werden als Benachrichtigung gespeichert und per E-Mail/Webhook
/// zugestellt, behobene automatisch geschlossen. Ein Befund muss zwei Prüfungen hintereinander bestehen (kein Flattern
/// beim Neustart einer Instanz).
/// </summary>
public sealed class NotificationMonitor(IServiceScopeFactory scopes, StatusService status, CertificateService certificates, IDockerHost docker,
    IOptions<HostingOptions> options, TimeProvider clock, ILogger<NotificationMonitor> logger) : BackgroundService
{
    /// <summary>Unter diesem Anteil freien Speichers auf dem Datenträger der Instanzen wird gewarnt.</summary>
    public const double DiskFreeWarning = 0.10;

    private readonly ConcurrentDictionary<string, int> seen = new(StringComparer.Ordinal);

    public static IReadOnlyList<Finding> Evaluate(IEnumerable<TenantObservation> observations, double? diskFreeRatio, bool dockerReachable, string? dockerError)
    {
        var findings = new List<Finding>();
        if (!dockerReachable)
        {
            findings.Add(new Finding("host:docker", "critical", "Docker ist nicht erreichbar", dockerError ?? "Der Docker-Daemon antwortet nicht.", null, null));
        }

        if (diskFreeRatio is { } ratio && ratio < DiskFreeWarning)
        {
            findings.Add(new Finding("host:disk", ratio < 0.03 ? "critical" : "warning", "Speicherplatz auf dem Server knapp",
                $"Nur noch {ratio:P0} frei. Alte Archive löschen oder Datenträger vergrößern.", null, null));
        }

        foreach (var (tenant, current, certificate, operationOpen) in observations)
        {
            var name = $"{tenant.CompanyName} ({tenant.Slug})";
            if (tenant.State == TenantState.Failed && !operationOpen)
            {
                findings.Add(new Finding($"tenant:{tenant.Id}:failed", "critical", $"{name}: Vorgang fehlgeschlagen",
                    tenant.LastError ?? "Der letzte Vorgang ist fehlgeschlagen.", tenant.Id, tenant.Slug));
            }

            var monitored = tenant.State == TenantState.Running && !operationOpen && current.CollectedAt != DateTime.MinValue;
            if (monitored && current.Health == HealthLevel.Unhealthy)
            {
                findings.Add(new Finding($"tenant:{tenant.Id}:health", "critical", $"{name}: Instanz gestört",
                    string.Join(" ", current.Problems.DefaultIfEmpty("Die Instanz meldet einen Fehler.")), tenant.Id, tenant.Slug));
            }

            if (monitored && current.Problems.FirstOrDefault(p => p.StartsWith("Sicherung:", StringComparison.Ordinal)) is { } backupProblem)
            {
                findings.Add(new Finding($"tenant:{tenant.Id}:backup", "warning", $"{name}: Sicherung nicht aktuell", backupProblem, tenant.Id, tenant.Slug));
            }

            if (monitored && current.Problems.FirstOrDefault(p => p.Contains("kontingent", StringComparison.OrdinalIgnoreCase)) is { } storageProblem)
            {
                findings.Add(new Finding($"tenant:{tenant.Id}:storage", "warning", $"{name}: Speicherkontingent überschritten", storageProblem, tenant.Id, tenant.Slug));
            }

            if (tenant.State is TenantState.Running or TenantState.Locked && certificate is { State: "expiring" or "invalid" or "missing" })
            {
                findings.Add(new Finding($"tenant:{tenant.Id}:certificate", certificate.State == "expiring" ? "warning" : "critical",
                    $"{name}: Zertifikat {(certificate.State == "expiring" ? "läuft bald ab" : "fehlerhaft")}",
                    $"{tenant.Domain}: {certificate.Error}", tenant.Id, tenant.Slug));
            }
        }

        return findings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(10, options.Value.NotificationIntervalSeconds)));
        do
        {
            try
            {
                await RunOnceAsync(confirmations: 2, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Überwachung für Benachrichtigungen fehlgeschlagen.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Eine Prüfung: Befunde erheben, neue melden, behobene schließen. Gibt die Zahl neuer Benachrichtigungen zurück.</summary>
    public async Task<int> RunOnceAsync(int confirmations, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HostingDbContext>();
        var tenants = await db.Tenants.AsNoTracking().Where(t => t.State != TenantState.Deleted && t.State != TenantState.Deleting).ToListAsync(cancellationToken);
        var open = await db.Operations.AsNoTracking()
            .Where(o => o.Status == OperationStatus.Queued || o.Status == OperationStatus.Running).Select(o => o.TenantId).ToListAsync(cancellationToken);
        double? diskFree = null;
        try
        {
            var drive = new DriveInfo(options.Value.Root);
            diskFree = drive.TotalSize > 0 ? (double)drive.AvailableFreeSpace / drive.TotalSize : null;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            logger.LogDebug(exception, "Speicherplatz nicht ermittelbar.");
        }

        var system = await docker.GetSystemInfoAsync(cancellationToken);
        var findings = Evaluate(tenants.Select(t => new TenantObservation(t, status.Get(t.Slug), certificates.Get(t.Domain), open.Contains(t.Id))),
            options.Value.IsFake ? null : diskFree, system.Reachable, system.Error);

        var current = findings.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in seen.Keys.Where(k => !current.Contains(k)).ToList())
        {
            seen.TryRemove(key, out _);
        }

        var settings = await NotificationSettings.LoadAsync(scope.ServiceProvider.GetRequiredService<ConsoleSettings>(), cancellationToken);
        var sender = scope.ServiceProvider.GetRequiredService<NotificationSender>();
        var active = await db.Notifications.Where(n => n.ResolvedAt == null).ToListAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var raised = 0;
        foreach (var finding in findings)
        {
            var count = seen.AddOrUpdate(finding.Key, 1, (_, c) => c + 1);
            var existing = active.FirstOrDefault(n => n.Key == finding.Key);
            if (existing is not null)
            {
                existing.Text = finding.Text;
                existing.Severity = finding.Severity;
                continue;
            }

            if (count < confirmations)
            {
                continue;
            }

            var notification = new Notification
            {
                Key = finding.Key, Severity = finding.Severity, Title = finding.Title, Text = finding.Text,
                TenantId = finding.TenantId, TenantSlug = finding.TenantSlug, RaisedAt = now,
            };
            var delivery = await sender.SendAsync(settings, $"[EstrichONE Hosting] {finding.Title}", finding.Text, finding.Severity,
                finding.TenantSlug, resolved: false, cancellationToken);
            notification.Delivery = delivery.Summary;
            db.Notifications.Add(notification);
            raised++;
        }

        foreach (var notification in active.Where(n => !current.Contains(n.Key)))
        {
            notification.ResolvedAt = now;
            var delivery = await sender.SendAsync(settings, $"[EstrichONE Hosting] Behoben: {notification.Title}",
                $"{notification.Title} ist behoben (seit {notification.RaisedAt:dd.MM.yyyy HH:mm} UTC gemeldet).", "info",
                notification.TenantSlug, resolved: true, cancellationToken);
            notification.Delivery = $"{notification.Delivery} · behoben: {delivery.Summary}";
        }

        await db.SaveChangesAsync(cancellationToken);
        return raised;
    }
}
