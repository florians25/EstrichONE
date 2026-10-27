using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Notifications.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Work;

/// <summary>Offene, zugewiesene Aufgaben und Mängel mit Frist für die tägliche Fristen-Benachrichtigung.</summary>
internal sealed class WorkDeadlines(EstrichOneDbContext db) : IDeadlineSource
{
    public async Task<IReadOnlyList<DeadlineItem>> DueAsync(DateOnly until, CancellationToken cancellationToken)
    {
        var from = until.AddDays(-7); // Überfälliges nur eine Woche lang melden (je Tag höchstens einmal).
        var items = await db.Set<WorkItem>().AsNoTracking()
            .Where(w => w.AssigneeUserId != null && w.DueDate != null && w.DueDate <= until && w.DueDate >= from
                && w.Status != WorkItemStatus.Done)
            .Select(w => new { w.Id, w.AssigneeUserId, w.DueDate, w.Title, w.Kind, w.BauakteId })
            .Take(2000).ToListAsync(cancellationToken);
        return items.Select(w => new DeadlineItem(w.AssigneeUserId!.Value, $"work:{w.Id}", w.DueDate!.Value,
            (w.Kind == WorkItemKind.Defect ? "Mangel: " : "Aufgabe: ") + w.Title,
            w.BauakteId is { } b ? $"/bauakten/{b}/aufgaben/{w.Id}" : "/start", "workItem", w.Id)).ToList();
    }
}
