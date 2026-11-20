using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Planning.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>Geräte und Fahrzeuge für die Tankbelege, dazu das heutige Dispo-Fahrzeug einer Person.</summary>
internal sealed class VehicleDirectory(EstrichOneDbContext db, PlanningService planning) : IVehicleDirectory
{
    public async Task<IReadOnlyList<VehicleInfo>> ListAsync(bool includeInactive, CancellationToken cancellationToken)
    {
        var query = db.Set<Resource>().AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(r => r.IsActive);
        }

        var resources = await query.OrderBy(r => r.Kind).ThenBy(r => r.Name).ToListAsync(cancellationToken);
        return resources.Select(Info).ToList();
    }

    public async Task<Guid?> VehicleOfDayAsync(Guid userId, DateOnly day, CancellationToken cancellationToken)
    {
        var range = await planning.RangeAsync(day, day, userId, cancellationToken);
        var planned = range.Assignments.OrderBy(a => a.StartTime ?? TimeOnly.MaxValue).ThenBy(a => a.Sequence)
            .SelectMany(a => a.Resources).FirstOrDefault(r => r.Kind == ResourceKind.Vehicle);
        if (planned is not null)
        {
            return planned.Id;
        }

        // Ohne Einsatz: Fahrzeug, das der eigenen Kolonne fest zugeordnet ist.
        var crews = await db.Set<Crew>().AsNoTracking().Where(c => c.IsActive && (c.LeaderUserId == userId || c.MemberUserIds.Contains(userId)))
            .Select(c => c.Id).ToListAsync(cancellationToken);
        return await db.Set<Resource>().AsNoTracking()
            .Where(r => r.IsActive && r.Kind == ResourceKind.Vehicle && r.CrewId != null && crews.Contains(r.CrewId.Value))
            .OrderBy(r => r.Name).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(cancellationToken);
    }

    private static VehicleInfo Info(Resource r) => new(r.Id, r.Kind switch
    {
        ResourceKind.Vehicle => "vehicle",
        ResourceKind.Pump => "pump",
        _ => "machine",
    }, r.Name, r.Identifier, r.InventoryNumber, r.TankCapacityLiters, r.IsActive);
}
