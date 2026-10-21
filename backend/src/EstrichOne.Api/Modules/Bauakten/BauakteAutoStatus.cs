using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

internal sealed class BauakteAutoStatus(EstrichOneDbContext db, ISystemSettings settings, IAuditLog audit) : IBauakteAutoStatus
{
    public async Task<bool> TryStartAsync(Guid bauakteId, BauakteAutoTrigger trigger, CancellationToken cancellationToken)
    {
        var mode = (await settings.GetAsync(cancellationToken)).Bauakte.AutoStart;
        var enabled = mode switch
        {
            AutoStartTrigger.Both => true,
            AutoStartTrigger.FirstAssignmentDay => trigger == BauakteAutoTrigger.AssignmentDay,
            AutoStartTrigger.FirstSiteActivity => trigger is BauakteAutoTrigger.Photo or BauakteAutoTrigger.Diary or BauakteAutoTrigger.Inspection,
            _ => false,
        };
        if (!enabled)
        {
            return false;
        }

        var bauakte = await db.Set<Bauakte>().SingleOrDefaultAsync(b => b.Id == bauakteId, cancellationToken);
        if (bauakte is null || !BauakteStatusRules.CanAutoStart(bauakte.Status))
        {
            return false;
        }

        Change(bauakte, BauakteStatus.InProgress, trigger);
        return true;
    }

    public async Task<bool> TryCompleteAsync(Guid bauakteId, CancellationToken cancellationToken)
    {
        if (!(await settings.GetAsync(cancellationToken)).Bauakte.AutoCompleteAfterAcceptance)
        {
            return false;
        }

        var bauakte = await db.Set<Bauakte>().SingleOrDefaultAsync(b => b.Id == bauakteId, cancellationToken);
        if (bauakte is null || !BauakteStatusRules.CanAutoComplete(bauakte.Status))
        {
            return false;
        }

        Change(bauakte, BauakteStatus.Completed, BauakteAutoTrigger.Acceptance);
        return true;
    }

    private void Change(Bauakte bauakte, BauakteStatus to, BauakteAutoTrigger trigger)
    {
        var from = bauakte.Status;
        bauakte.ChangeStatus(to);
        // Ohne handelnde Person: der Verlauf zeigt „automatisch“ mit Auslöser.
        audit.Record($"bauakte.{BauakteHistoryActions.StatusChanged}", null, "bauakte", bauakte.Id.ToString(), new Dictionary<string, string?>
        {
            ["from"] = from.ToString(), ["to"] = to.ToString(), ["auto"] = "true", ["trigger"] = trigger.ToString(),
        });
    }
}

/// <summary>Prüft regelmäßig, welche geplanten Bauakten heute ihren ersten Einsatztag haben.</summary>
internal sealed class BauakteAutoStartJob(IServiceScopeFactory scopes, TimeProvider clock, ILogger<BauakteAutoStartJob> logger) : BackgroundService
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Kurz nach dem Start, dann alle 30 Minuten (auch nach Mitternacht schnell wirksam).
        await Task.Delay(TimeSpan.FromSeconds(30), clock, stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Automatischer Statuswechsel (Einsatztag) fehlgeschlagen");
            }

            await Task.Delay(TimeSpan.FromMinutes(30), clock, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<EstrichOneDbContext>();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Berlin).DateTime);
        var ids = new HashSet<Guid>();
        foreach (var source in services.GetServices<IBauakteScheduleSource>())
        {
            ids.UnionWith(await source.ScheduledOnAsync(today, cancellationToken));
        }

        var planned = await db.Set<Bauakte>().Where(b => ids.Contains(b.Id) && b.Status == BauakteStatus.Planned).Select(b => b.Id).ToListAsync(cancellationToken);
        var auto = services.GetRequiredService<IBauakteAutoStatus>();
        var changed = 0;
        foreach (var id in planned)
        {
            if (await auto.TryStartAsync(id, BauakteAutoTrigger.AssignmentDay, cancellationToken))
            {
                changed++;
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }
}
