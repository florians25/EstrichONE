using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Sync.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>
/// Beteiligter einer Bauakte im Sync (Entitätstyp <c>bauakteParty</c>, nur lesend): Bauherr, Bauleiter, Architekt,
/// Lieferant … mit Telefon und E-Mail. Bei Verknüpfung mit dem Kundenstamm ergänzt die App fehlende Angaben aus dem Kontakt.
/// </summary>
public sealed record BauaktePartyDto(
    Guid Id, Guid BauakteId, BauaktePartyRole Role, Guid? ContactId, string Name, string? Company, string? Phone, string? Email, string? Note,
    long Version, DateTimeOffset UpdatedAt)
{
    public static BauaktePartyDto From(BauakteParty party) => new(party.Id, party.BauakteId, party.Role, party.ContactId, party.Name,
        party.Company, party.Phone, party.Email, party.Note, party.Version, party.UpdatedAt);
}

/// <summary>Stand der Beteiligten für den Änderungsabruf; sichtbar wie die Bauakte. Gepflegt werden sie im Büro.</summary>
internal sealed class BauaktePartySource(EstrichOneDbContext db) : ISyncEntitySource
{
    public string EntityType => "bauakteParty";

    public string? RequiredPermission => null;

    public async Task<IReadOnlyDictionary<Guid, JsonNode>> LoadAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Set<BauakteParty>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id, p => SyncPayload.ToNode(BauaktePartyDto.From(p))!);
}
