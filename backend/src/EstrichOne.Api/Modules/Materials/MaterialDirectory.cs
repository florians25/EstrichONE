using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Materials.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Materials;

internal sealed class MaterialDirectory(EstrichOneDbContext db) : IMaterialDirectory
{
    public async Task<IReadOnlyList<MaterialSummary>> ListAsync(IReadOnlyCollection<Guid>? ids, CancellationToken cancellationToken)
    {
        var query = db.Set<Material>().AsNoTracking();
        query = ids is null ? query.Where(m => m.IsActive) : query.Where(m => ids.Contains(m.Id));
        return await query.OrderBy(m => m.Name).Select(m => new MaterialSummary(m.Id, m.Name, m.Number, m.Unit, m.IsActive)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SupplierSummary>> SuppliersAsync(CancellationToken cancellationToken) =>
        await db.Set<Supplier>().AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.Name).Select(s => new SupplierSummary(s.Id, s.Name, s.Number))
            .ToListAsync(cancellationToken);
}
