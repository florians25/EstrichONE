using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Contacts.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Calls;

/// <summary>Bezüge eines Kunden zu Gesprächsnotizen und Anfragen.</summary>
internal sealed class CallCustomerReferences(EstrichOneDbContext db) : ICustomerReferenceSource
{
    public async Task<IReadOnlyList<CustomerReference>> FindAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var calls = db.Set<CallRecord>().IgnoreQueryFilters([QueryFilters.NotDeleted]).AsNoTracking().Where(c => c.CustomerId == contactId);
        var count = await calls.CountAsync(cancellationToken);
        if (count == 0)
        {
            return [];
        }

        var items = await calls.OrderByDescending(c => c.CalledAt).Take(5)
            .Select(c => new { c.Id, c.Kind, c.Number, c.Subject, c.CalledAt }).ToListAsync(cancellationToken);
        return
        [
            new CustomerReference("calls", count == 1 ? "Anruf oder Anfrage" : "Anrufe und Anfragen", count, $"/kunden/{contactId}?abschnitt=verlauf",
                items.Select(c => new CustomerReferenceItem(c.Id,
                    $"{(c.Kind == CallKind.Inquiry ? "Anfrage" : "Anruf")} {c.Number ?? c.CalledAt.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture)}{(string.IsNullOrWhiteSpace(c.Subject) ? string.Empty : $": {c.Subject}")}",
                    c.Kind == CallKind.Inquiry ? "/anfragen" : "/anrufe")).ToList()),
        ];
    }

    public async Task AnonymizeAsync(Guid contactId, string replacementName, CancellationToken cancellationToken)
    {
        foreach (var call in await db.Set<CallRecord>().IgnoreQueryFilters([QueryFilters.NotDeleted]).Where(c => c.CustomerId == contactId).ToListAsync(cancellationToken))
        {
            call.AnonymizeCaller(replacementName);
        }
    }
}
