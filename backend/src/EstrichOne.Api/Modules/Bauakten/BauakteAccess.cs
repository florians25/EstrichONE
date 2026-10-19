using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>
/// Mit <c>bauakten.read_all</c>: alle Bauakten. Sonst nur Bauakten, in denen der Benutzer Mitglied oder zuständig ist.
/// (Sichtbarkeit über Einsätze der eigenen Kolonne folgt mit dem Planungsmodul.)
/// Jede Liste, jede Detailabfrage, die Synchronisation und die Dateiauslieferung nutzen diesen einen Filter.
/// </summary>
internal sealed class BauakteAccess(EstrichOneDbContext db, ICurrentUser currentUser, IUserPermissions permissions) : IBauakteAccess
{
    public async Task<IQueryable<Bauakte>> VisibleAsync(IQueryable<Bauakte> query, CancellationToken cancellationToken)
    {
        var granted = await permissions.GetAsync(cancellationToken);
        if (!granted.Contains(Permissions.BauaktenRead) && !granted.Contains(Permissions.BauaktenReadAll))
        {
            return query.Where(_ => false);
        }

        if (granted.Contains(Permissions.BauaktenReadAll))
        {
            return query;
        }

        var userId = currentUser.UserId;
        return query.Where(bauakte => bauakte.ResponsibleUserId == userId
            || db.Set<BauakteMember>().Any(member => member.BauakteId == bauakte.Id && member.UserId == userId));
    }

    public async Task<bool> CanReadAsync(Guid bauakteId, CancellationToken cancellationToken) =>
        await (await VisibleAsync(db.Set<Bauakte>(), cancellationToken)).AnyAsync(b => b.Id == bauakteId, cancellationToken);
}
