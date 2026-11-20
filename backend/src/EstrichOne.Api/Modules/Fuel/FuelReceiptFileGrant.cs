using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Documentation.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Fuel;

/// <summary>Belegfotos sehen außer dem Erfasser alle, die Tankbelege prüfen (<c>fuel.manage</c>).</summary>
internal sealed class FuelReceiptFileGrant(EstrichOneDbContext db, IUserPermissions permissions) : IFileReadGrant
{
    public async Task<bool> CanReadAsync(Guid fileId, CancellationToken cancellationToken) =>
        await db.Set<FuelReceipt>().AnyAsync(r => r.ImageFileId == fileId, cancellationToken)
        && await permissions.HasAsync(Permissions.FuelManage, cancellationToken);
}
