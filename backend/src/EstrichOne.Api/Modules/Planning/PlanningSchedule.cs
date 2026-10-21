using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>Einsätze eines Tages für den automatischen Statuswechsel „Geplant → In Ausführung“ (Bauakten-Modul).</summary>
internal sealed class PlanningSchedule(EstrichOneDbContext db) : IBauakteScheduleSource
{
    public async Task<IReadOnlyList<Guid>> ScheduledOnAsync(DateOnly day, CancellationToken cancellationToken) =>
        await db.Set<Assignment>().AsNoTracking().Where(a => a.Day == day).Select(a => a.BauakteId).Distinct().ToListAsync(cancellationToken);
}
