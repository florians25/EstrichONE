using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Planning.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

internal sealed class BauakteAssignments(EstrichOneDbContext db) : IBauakteAssignments
{
    public async Task<IReadOnlyList<BauakteAssignmentInfo>> ListAsync(Guid bauakteId, CancellationToken cancellationToken)
    {
        var assignments = await db.Set<Assignment>().AsNoTracking().Where(a => a.BauakteId == bauakteId).OrderBy(a => a.Day).ToListAsync(cancellationToken);
        var crewIds = assignments.Select(a => a.CrewId).OfType<Guid>().Distinct().ToList();
        var crews = await db.Set<Crew>().AsNoTracking().Where(c => crewIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
        return assignments.Select(a =>
        {
            var people = new HashSet<Guid>(a.UserIds);
            if (a.CrewId is { } crewId && crews.TryGetValue(crewId, out var crew))
            {
                people.UnionWith(crew.MemberUserIds);
                if (crew.LeaderUserId is { } leader)
                {
                    people.Add(leader);
                }
            }

            people.ExceptWith(a.ExcludedUserIds);
            decimal? hours = a.StartTime is { } start && a.EndTime is { } end && end > start ? Math.Round((decimal)(end - start).TotalHours, 2) : null;
            var kind = a.Kind.ToString();
            return new BauakteAssignmentInfo(a.Id, a.Day, char.ToLowerInvariant(kind[0]) + kind[1..], a.Activity, people.Count, hours);
        }).ToList();
    }
}
