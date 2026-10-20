using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

internal sealed class BauakteReportSource(EstrichOneDbContext db) : IBauakteReportSource
{
    private static readonly Dictionary<RoomStatus, string> RoomStatusText = new()
    {
        [RoomStatus.Planned] = "geplant", [RoomStatus.Laid] = "eingebracht", [RoomStatus.Ready] = "belegreif",
    };

    public async Task<BauakteReportInfo?> GetAsync(Guid bauakteId, CancellationToken cancellationToken)
    {
        var bauakte = await db.Set<Bauakte>().AsNoTracking().Include(b => b.Members).SingleOrDefaultAsync(b => b.Id == bauakteId, cancellationToken);
        if (bauakte is null)
        {
            return null;
        }

        var customer = bauakte.CustomerId is { } customerId
            ? await db.Set<Contact>().AsNoTracking().Where(c => c.Id == customerId)
                .Select(c => new CustomerReportInfo(c.Name, c.CustomerNumber, c.Street, c.PostalCode, c.City, c.Phone, c.Email))
                .SingleOrDefaultAsync(cancellationToken)
            : null;
        var userIds = bauakte.Members.Select(m => m.UserId).Append(bauakte.CreatedByUserId).Append(bauakte.ResponsibleUserId ?? Guid.Empty).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var screedTypes = await db.Set<ScreedType>().AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Designation ?? t.Name, cancellationToken);
        var rooms = (await db.Set<Room>().AsNoTracking().Where(r => r.BauakteId == bauakteId).ToListAsync(cancellationToken))
            .OrderBy(r => r.Floor, StringComparer.CurrentCulture).ThenBy(r => r.Name, StringComparer.CurrentCulture)
            .Select(r => new RoomReportRow(r.Name, r.Floor, r.AreaSquareMeters, r.ScreedTypeId is { } t ? screedTypes.GetValueOrDefault(t) : null,
                r.ThicknessMillimeters, r.InsulationMillimeters, r.UnderfloorHeating, RoomStatusText.GetValueOrDefault(r.Status, r.Status.ToString())))
            .ToList();
        return new BauakteReportInfo(bauakte.Id, bauakte.Number, bauakte.Title, bauakte.Status, bauakte.Street, bauakte.PostalCode, bauakte.City,
            bauakte.Description, bauakte.PlannedStart, bauakte.PlannedEnd, bauakte.SiteContactName, bauakte.SiteContactPhone, bauakte.SiteNotes,
            customer, names.GetValueOrDefault(bauakte.CreatedByUserId) ?? "?", bauakte.CreatedAt,
            bauakte.ResponsibleUserId is { } r ? names.GetValueOrDefault(r) : null,
            bauakte.Members.Select(m => names.GetValueOrDefault(m.UserId)).OfType<string>().Order(StringComparer.CurrentCulture).ToList(), rooms);
    }
}
