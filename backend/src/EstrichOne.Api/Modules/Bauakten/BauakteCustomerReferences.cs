using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>Bezüge eines Kunden zu Bauakten (als Kunde, auch im Papierkorb) und als Projektbeteiligter.</summary>
internal sealed class BauakteCustomerReferences(EstrichOneDbContext db) : ICustomerReferenceSource
{
    public async Task<IReadOnlyList<CustomerReference>> FindAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var result = new List<CustomerReference>();
        var bauakten = db.Set<Bauakte>().IgnoreQueryFilters([QueryFilters.NotDeleted]).AsNoTracking().Where(b => b.CustomerId == contactId);
        var count = await bauakten.CountAsync(cancellationToken);
        if (count > 0)
        {
            var items = await bauakten.OrderByDescending(b => b.UpdatedAt).Take(5)
                .Select(b => new { b.Id, b.Number, b.Title, b.DeletedAt }).ToListAsync(cancellationToken);
            result.Add(new CustomerReference("bauakten", count == 1 ? "Bauakte" : "Bauakten", count, $"/bauakten?kunde={contactId}",
                items.Select(b => new CustomerReferenceItem(b.Id, Text(b.Number, b.Title) + (b.DeletedAt is null ? string.Empty : " (Papierkorb)"),
                    b.DeletedAt is null ? $"/bauakten/{b.Id}" : null)).ToList()));
        }

        var parties = await (from p in db.Set<BauakteParty>().AsNoTracking()
                             join b in db.Set<Bauakte>().IgnoreQueryFilters([QueryFilters.NotDeleted]) on p.BauakteId equals b.Id
                             where p.ContactId == contactId
                             orderby b.UpdatedAt descending
                             select new { b.Id, b.Number, b.Title }).ToListAsync(cancellationToken);
        if (parties.Count > 0)
        {
            result.Add(new CustomerReference("parties", parties.Count == 1 ? "Projektbeteiligung" : "Projektbeteiligungen", parties.Count, null,
                parties.Take(5).Select(b => new CustomerReferenceItem(b.Id, Text(b.Number, b.Title), $"/bauakten/{b.Id}?abschnitt=kontakte")).ToList()));
        }

        return result;
    }

    public async Task AnonymizeAsync(Guid contactId, string replacementName, CancellationToken cancellationToken)
    {
        foreach (var party in await db.Set<BauakteParty>().Where(p => p.ContactId == contactId).ToListAsync(cancellationToken))
        {
            party.Anonymize(replacementName);
        }
    }

    private static string Text(string? number, string title) => number is null ? title : $"{number} · {title}";
}
