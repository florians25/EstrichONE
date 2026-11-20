using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>
/// Lesen: Mit <c>bauakten.read</c> (oder <c>bauakten.read_all</c>) alle Bauakten. Schreiben: nur zugeteilte
/// (zuständig, Beteiligter, eingeplant über Einsatz oder Kolonne) – mit <c>bauakten.edit_all</c> alle.
/// Jede Liste, jede Detailabfrage, die Synchronisation und die Dateiauslieferung nutzen diese Filter.
/// </summary>
internal sealed class BauakteAccess(EstrichOneDbContext db, ICurrentUser currentUser, IUserPermissions permissions,
    IEnumerable<IBauakteVisibilitySource> visibilitySources) : IBauakteAccess
{
    public async Task<IQueryable<Bauakte>> VisibleAsync(IQueryable<Bauakte> query, CancellationToken cancellationToken) =>
        await CanSeeAsync(cancellationToken) ? query : query.Where(_ => false);

    public async Task<IQueryable<Bauakte>> AssignedAsync(IQueryable<Bauakte> query, CancellationToken cancellationToken) =>
        await CanSeeAsync(cancellationToken) ? OnlyAssigned(query) : query.Where(_ => false);

    public async Task<IQueryable<Bauakte>> SyncScopeAsync(IQueryable<Bauakte> query, CancellationToken cancellationToken)
    {
        var granted = await permissions.GetAsync(cancellationToken);
        if (granted.Contains(Permissions.BauaktenReadAll))
        {
            return query;
        }

        return granted.Contains(Permissions.BauaktenRead) ? OnlyAssigned(query) : query.Where(_ => false);
    }

    public async Task<IQueryable<Bauakte>> WritableAsync(IQueryable<Bauakte> query, CancellationToken cancellationToken)
    {
        if (!await CanSeeAsync(cancellationToken))
        {
            return query.Where(_ => false);
        }

        return await CanEditAllAsync(cancellationToken) ? query : OnlyAssigned(query);
    }

    public async Task<BauakteWriteCheck> CheckWriteAsync(Guid bauakteId, CancellationToken cancellationToken)
    {
        if (!await CanReadAsync(bauakteId, cancellationToken))
        {
            return BauakteWriteCheck.NotFound;
        }

        return await (await WritableAsync(db.Set<Bauakte>(), cancellationToken)).AnyAsync(b => b.Id == bauakteId, cancellationToken)
            ? BauakteWriteCheck.Allowed
            : BauakteWriteCheck.NotAssigned;
    }

    public async Task<bool> CanEditAllAsync(CancellationToken cancellationToken) =>
        await permissions.HasAsync(Permissions.BauaktenEditAll, cancellationToken);

    public async Task<bool> CanReadAsync(Guid bauakteId, CancellationToken cancellationToken) =>
        await (await VisibleAsync(db.Set<Bauakte>(), cancellationToken)).AnyAsync(b => b.Id == bauakteId, cancellationToken);

    private async Task<bool> CanSeeAsync(CancellationToken cancellationToken)
    {
        var granted = await permissions.GetAsync(cancellationToken);
        return granted.Contains(Permissions.BauaktenRead) || granted.Contains(Permissions.BauaktenReadAll);
    }

    private IQueryable<Bauakte> OnlyAssigned(IQueryable<Bauakte> query)
    {
        var userId = currentUser.UserId;
        var extra = visibilitySources.Select(source => source.AdditionalVisibleIds(userId)).ToList();
        if (extra.Count == 0)
        {
            return query.Where(bauakte => bauakte.ResponsibleUserId == userId
                || db.Set<BauakteMember>().Any(member => member.BauakteId == bauakte.Id && member.UserId == userId));
        }

        var additional = extra.Aggregate((left, right) => left.Union(right));
        return query.Where(bauakte => bauakte.ResponsibleUserId == userId
            || db.Set<BauakteMember>().Any(member => member.BauakteId == bauakte.Id && member.UserId == userId)
            || additional.Contains(bauakte.Id));
    }
}
