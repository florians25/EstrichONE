using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Work;

/// <summary>Offene Mängel und überfällige Aufgaben der Bauakte.</summary>
internal sealed class WorkHints(EstrichOneDbContext db, ISystemSettings settings) : IBauakteHintSource
{
    public async Task<IReadOnlyList<BauakteHint>> GetHintsAsync(Bauakte bauakte, DateOnly today, CancellationToken cancellationToken)
    {
        var kinds = await WorkEndpoints.EnabledKindsAsync(settings, cancellationToken);
        var open = await db.Set<WorkItem>().AsNoTracking()
            .Where(w => w.BauakteId == bauakte.Id && w.Status != WorkItemStatus.Done && kinds.Contains(w.Kind))
            .Select(w => new { w.Kind, w.DueDate })
            .ToListAsync(cancellationToken);
        var hints = new List<BauakteHint>();
        var overdue = open.Count(w => w.DueDate < today);
        if (overdue > 0)
        {
            hints.Add(new("work_overdue", BauakteHintSeverity.Danger, overdue == 1 ? "1 Aufgabe ist überfällig." : $"{overdue} Aufgaben sind überfällig."));
        }

        var defects = open.Count(w => w.Kind == WorkItemKind.Defect);
        if (defects > 0)
        {
            hints.Add(new("defects_open", BauakteHintSeverity.Warning, defects == 1 ? "1 offener Mangel." : $"{defects} offene Mängel."));
        }

        return hints;
    }
}
