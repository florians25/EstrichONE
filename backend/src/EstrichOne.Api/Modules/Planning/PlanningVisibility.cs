using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>
/// Eingeplante sehen ihre Bauakte: als Person im Einsatz, als Mitglied oder Leitung der eingeplanten Kolonne.
/// Berücksichtigt werden Einsätze der letzten 30 Tage und alle künftigen.
/// </summary>
internal sealed class PlanningVisibility(EstrichOneDbContext db, TimeProvider clock) : IBauakteVisibilitySource
{
    public const int PastDays = 30;

    public IQueryable<Guid> AdditionalVisibleIds(Guid userId)
    {
        var since = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-PastDays);
        return db.Set<Assignment>()
            .Where(a => a.Day >= since)
            .Where(a => a.UserIds.Contains(userId)
                || (a.CrewId != null && db.Set<Crew>().Any(c => c.Id == a.CrewId && (c.LeaderUserId == userId || c.MemberUserIds.Contains(userId)))))
            .Select(a => a.BauakteId);
    }

    public Task<bool> IsPlannedAsync(Guid bauakteId, CancellationToken cancellationToken)
    {
        var since = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-PastDays);
        return db.Set<Assignment>().AnyAsync(a => a.BauakteId == bauakteId && a.Day >= since, cancellationToken);
    }
}
