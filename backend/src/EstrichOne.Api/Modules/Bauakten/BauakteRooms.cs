using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

internal sealed class BauakteRooms(EstrichOneDbContext db) : IBauakteRooms
{
    public Task<bool> BelongsToAsync(Guid bauakteId, Guid roomId, CancellationToken cancellationToken) =>
        db.Set<Room>().AnyAsync(room => room.Id == roomId && room.BauakteId == bauakteId, cancellationToken);

    public async Task<IReadOnlyList<BauakteRoomArea>> AreasAsync(Guid bauakteId, CancellationToken cancellationToken)
    {
        var rooms = await db.Set<Room>().AsNoTracking().Where(r => r.BauakteId == bauakteId).OrderBy(r => r.Floor).ThenBy(r => r.Name)
            .ToListAsync(cancellationToken);
        var typeIds = rooms.Select(r => r.ScreedTypeId).OfType<Guid>().Distinct().ToList();
        var types = await db.Set<ScreedType>().AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        return rooms.Select(r => new BauakteRoomArea(r.Id, r.Name, r.Floor, r.AreaSquareMeters,
            r.ScreedTypeId is { } id ? types.GetValueOrDefault(id) : null, r.ThicknessMillimeters, r.Status is RoomStatus.Laid or RoomStatus.Ready)).ToList();
    }
}
