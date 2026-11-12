using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Contacts.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Mail;

/// <summary>
/// Versandprotokoll von Serien-E-Mails an Kunden (<c>contact:{id}</c>): kein Hinderungsgrund fürs Löschen, aber
/// Empfängeradresse und Betreff werden beim Löschen bzw. Anonymisieren entfernt.
/// </summary>
internal sealed class MailCustomerReferences(EstrichOneDbContext db) : ICustomerReferenceSource
{
    public Task<IReadOnlyList<CustomerReference>> FindAsync(Guid contactId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CustomerReference>>([]);

    public async Task AnonymizeAsync(Guid contactId, string replacementName, CancellationToken cancellationToken)
    {
        var related = $"contact:{contactId}";
        foreach (var entry in await db.Set<MailLogEntry>().Where(m => m.RelatedEntity == related).ToListAsync(cancellationToken))
        {
            entry.Anonymize();
        }
    }
}
