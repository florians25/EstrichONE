using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

internal sealed class BauakteSummaries(EstrichOneDbContext db) : IBauakteSummaries
{
    public async Task<IReadOnlyDictionary<Guid, BauakteSummary>> GetAsync(IReadOnlyCollection<Guid> bauakteIds, CancellationToken cancellationToken)
    {
        var ids = bauakteIds.Distinct().ToList();
        var customers = await (from b in db.Set<Bauakte>().AsNoTracking()
                               join c in db.Set<Contact>().AsNoTracking() on b.CustomerId equals c.Id
                               where ids.Contains(b.Id)
                               select new { b.Id, c.Name }).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        var rooms = await db.Set<Room>().AsNoTracking().Where(r => ids.Contains(r.BauakteId))
            .Select(r => new { r.BauakteId, r.AreaSquareMeters, r.ScreedTypeId }).ToListAsync(cancellationToken);
        var typeIds = rooms.Select(r => r.ScreedTypeId).OfType<Guid>().Distinct().ToList();
        var types = await db.Set<ScreedType>().AsNoTracking().Where(t => typeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Designation ?? t.Name, cancellationToken);

        return ids.ToDictionary(id => id, id =>
        {
            var own = rooms.Where(r => r.BauakteId == id).ToList();
            return new BauakteSummary(id, customers.GetValueOrDefault(id), own.Sum(r => r.AreaSquareMeters ?? 0),
                own.Select(r => r.ScreedTypeId).OfType<Guid>().Distinct().Where(types.ContainsKey).Select(t => types[t]).ToList());
        });
    }
}
