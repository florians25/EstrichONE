using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

internal sealed class BauakteRooms(EstrichOneDbContext db) : IBauakteRooms
{
    public Task<bool> BelongsToAsync(Guid bauakteId, Guid roomId, CancellationToken cancellationToken) =>
        db.Set<Room>().AnyAsync(room => room.Id == roomId && room.BauakteId == bauakteId, cancellationToken);
}
