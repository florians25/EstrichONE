using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Sync.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Contacts;

/// <summary>Kunden für die Offline-Kundenauswahl in der App (nur mit <c>contacts.read</c>).</summary>
internal sealed class ContactSource(EstrichOneDbContext db) : ISyncEntitySource
{
    public string EntityType => "contact";

    public string? RequiredPermission => Permissions.ContactsRead;

    public async Task<IReadOnlyDictionary<Guid, JsonNode>> LoadAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Set<Contact>().AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken))
            .ToDictionary(c => c.Id, c => SyncPayload.ToNode(ContactEndpoints.ToResponse(c))!);
}
