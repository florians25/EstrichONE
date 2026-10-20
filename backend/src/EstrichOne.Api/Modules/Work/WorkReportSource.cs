using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Work.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Work;

internal sealed class WorkReportSource(EstrichOneDbContext db) : IWorkReportSource
{
    public async Task<IReadOnlyList<WorkReportRow>> ListAsync(Guid bauakteId, CancellationToken cancellationToken)
    {
        var items = await db.Set<WorkItem>().AsNoTracking().Where(w => w.BauakteId == bauakteId).ToListAsync(cancellationToken);
        var userIds = items.Where(w => w.AssigneeUserId != null).Select(w => w.AssigneeUserId!.Value).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        return items
            .OrderBy(w => w.Status == WorkItemStatus.Done).ThenByDescending(w => w.Kind == WorkItemKind.Defect).ThenBy(w => w.CreatedAt)
            .Select(w => new WorkReportRow(w.Kind == WorkItemKind.Defect, w.Title, w.Description,
                w.Status switch { WorkItemStatus.Done => "erledigt", WorkItemStatus.InProgress => "in Arbeit", _ => "offen" },
                w.DueDate, w.AssigneeUserId is { } a ? names.GetValueOrDefault(a) : null, w.CreatedAt, w.CompletedAt))
            .ToList();
    }
}
