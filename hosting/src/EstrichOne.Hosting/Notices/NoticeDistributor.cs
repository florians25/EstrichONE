using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Instances;
using EstrichOne.Hosting.Status;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Hosting.Notices;

/// <summary>Stand der Verteilung an eine Instanz.</summary>
public sealed record NoticeSyncState(bool Success, DateTime At, string? Error, string Hash, int Announcements, bool Maintenance);

/// <summary>
/// Verteilt Ankündigungen und Wartungsfenster an die Instanzen (Suite-Modul „Systemhinweise“): je Instanz wird der
/// vollständige Soll-Stand berechnet und per PUT mit dem Instanz-Schlüssel aus der .env gesetzt. Nicht erreichbare
/// Instanzen werden im Hintergrund erneut versucht, sobald sie wieder laufen.
/// </summary>
public sealed class NoticeDistributor(IServiceScopeFactory scopes, IInstanceProbe probe, InstanceFiles files, TimeProvider clock,
    ILogger<NoticeDistributor> logger) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, NoticeSyncState> states = new();
    private readonly SemaphoreSlim gate = new(1, 1);

    public IReadOnlyDictionary<Guid, NoticeSyncState> States => states;

    /// <summary>Soll-Stand einer Instanz im Format des Suite-Endpunkts.</summary>
    public static object BuildDocument(Guid tenantId, IEnumerable<Notice> notices, DateTime now)
    {
        var relevant = notices.Where(n => n.AppliesTo(tenantId) && (n.EndsAt is null || n.EndsAt > now)).ToList();
        var announcements = relevant.Where(n => n.Kind == NoticeKind.Announcement).OrderBy(n => n.StartsAt ?? n.CreatedAt).Take(20)
            .Select(n => new { id = n.Id.ToString("N"), title = n.Title, text = n.Text, severity = n.Severity, startsAt = Utc(n.StartsAt), endsAt = Utc(n.EndsAt) })
            .ToList();
        var maintenance = relevant.Where(n => n.Kind == NoticeKind.Maintenance).OrderBy(n => n.StartsAt ?? n.CreatedAt).FirstOrDefault();
        return new
        {
            announcements,
            maintenance = maintenance is null ? null : new
            {
                message = string.IsNullOrWhiteSpace(maintenance.Text) ? maintenance.Title : $"{maintenance.Title}: {maintenance.Text}",
                startsAt = Utc(maintenance.StartsAt) ?? new DateTimeOffset(maintenance.CreatedAt, TimeSpan.Zero),
                endsAt = Utc(maintenance.EndsAt),
                adminsMayWork = maintenance.AdminsMayWork,
            },
        };
    }

    private static DateTimeOffset? Utc(DateTime? value) => value is null ? null : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

    /// <summary>An alle (bzw. die angegebenen) laufenden Instanzen verteilen; <paramref name="force"/> auch bei unverändertem Stand.</summary>
    public async Task DistributeAsync(bool force, CancellationToken cancellationToken, IReadOnlyCollection<Guid>? only = null)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            List<Tenant> tenants;
            List<Notice> notices;
            await using (var scope = scopes.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostingDbContext>();
                tenants = await db.Tenants.AsNoTracking()
                    .Where(t => t.State == TenantState.Running || t.State == TenantState.Locked || t.State == TenantState.Failed)
                    .ToListAsync(cancellationToken);
                notices = await db.Notices.AsNoTracking().ToListAsync(cancellationToken);
            }

            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var tenant in tenants.Where(t => only is null || only.Contains(t.Id)))
            {
                var json = JsonSerializer.Serialize(BuildDocument(tenant.Id, notices, now), Json);
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
                if (!force && states.TryGetValue(tenant.Id, out var last) && last.Success && last.Hash == hash)
                {
                    continue;
                }

                var env = await files.ReadEnvAsync(tenant.Slug, cancellationToken);
                var token = env.GetValueOrDefault("ESTRICHONE_NOTICE_TOKEN");
                var document = JsonDocument.Parse(json).RootElement;
                var count = document.GetProperty("announcements").GetArrayLength();
                var hasMaintenance = document.GetProperty("maintenance").ValueKind != JsonValueKind.Null;
                if (string.IsNullOrEmpty(token))
                {
                    states[tenant.Id] = new NoticeSyncState(false, now, "Instanz hat noch keinen Schlüssel – „Neu konfigurieren“ bzw. nächster Vorgang legt ihn an.", hash, count, hasMaintenance);
                    continue;
                }

                var result = await probe.PushNoticesAsync(tenant.Slug, token, json, cancellationToken);
                states[tenant.Id] = new NoticeSyncState(result.Success, now, result.Error, hash, count, hasMaintenance);
                if (!result.Success)
                {
                    logger.LogWarning("Systemhinweise an {Slug} nicht übertragen: {Error}", tenant.Slug, result.Error);
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public void Forget(Guid tenantId) => states.TryRemove(tenantId, out _);

    public void Dispose() => gate.Dispose();
}

/// <summary>Gleicht die Systemhinweise jede Minute ab (z. B. nach Neustart einer Instanz oder wenn ein Zeitraum endet).</summary>
public sealed class NoticeSync(NoticeDistributor distributor, ILogger<NoticeSync> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await distributor.DistributeAsync(force: false, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Abgleich der Systemhinweise fehlgeschlagen.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
