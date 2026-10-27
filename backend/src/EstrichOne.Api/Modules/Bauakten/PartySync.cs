using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Sync.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>
/// Beteiligter einer Bauakte im Sync (Entitätstyp <c>bauakteParty</c>): Bauherr, Bauleiter, Architekt, Lieferant … mit
/// Telefon, Mobil, E-Mail, Anschrift und Hauptkontakt. Bei Verknüpfung mit dem Kundenstamm ergänzt die App fehlende
/// Angaben aus dem Kontakt (Entitätstyp <c>contact</c>).
/// </summary>
public sealed record BauaktePartyDto(
    Guid Id, Guid BauakteId, BauaktePartyRole Role, Guid? ContactId, string Name, string? Company, string? Phone, string? Email, string? Note,
    long Version, DateTimeOffset UpdatedAt, string? Mobile = null, string? Street = null, string? PostalCode = null, string? City = null,
    bool IsPrimary = false)
{
    public static BauaktePartyDto From(BauakteParty party) => new(party.Id, party.BauakteId, party.Role, party.ContactId, party.Name,
        party.Company, party.Phone, party.Email, party.Note, party.Version, party.UpdatedAt, party.Mobile, party.Street, party.PostalCode,
        party.City, party.IsPrimary);
}

/// <summary>Stand der Beteiligten für den Änderungsabruf; sichtbar wie die Bauakte.</summary>
internal sealed class BauaktePartySource(EstrichOneDbContext db) : ISyncEntitySource
{
    public string EntityType => "bauakteParty";

    public string? RequiredPermission => null;

    public async Task<IReadOnlyDictionary<Guid, JsonNode>> LoadAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Set<BauakteParty>().AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken))
            .ToDictionary(p => p.Id, p => SyncPayload.ToNode(BauaktePartyDto.From(p))!);
}

/// <summary>
/// Beteiligte in der App pflegen (Recht <c>bauakten.edit</c>, Bauakte sichtbar): <c>bauakteParty.save</c> legt an oder
/// ersetzt den Eintrag vollständig (Nutzdaten wie <c>PUT /bauakten/{id}/parties/{partyId}</c> plus <c>bauakteId</c>;
/// <c>baseVersion</c> schützt vor dem Überschreiben fremder Änderungen), <c>bauakteParty.delete</c> entfernt ihn.
/// </summary>
internal sealed class BauaktePartySync(EstrichOneDbContext db, IBauakteAccess access, IUserPermissions permissions, IBauakteHistory history)
    : ISyncOperationHandler
{
    public IReadOnlyCollection<string> OperationTypes { get; } = ["bauakteParty.save", "bauakteParty.delete"];

    public async Task<SyncOperationResult> ApplyAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(Permissions.BauaktenEdit, cancellationToken))
        {
            return SyncOperationResult.Rejected(op.OpId, "PERMISSION_DENIED", "Für diese Aktion fehlt die Berechtigung.");
        }

        var existing = await db.Set<BauakteParty>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == op.EntityId, cancellationToken);
        if (existing is null && op.Type == "bauakteParty.delete")
        {
            // Bereits entfernt (z. B. im Büro): gilt als erledigt.
            return new SyncOperationResult(op.OpId, SyncOperationStatus.Applied);
        }

        var bauakteId = existing?.BauakteId ?? SyncPayload.Read<Guid?>(op.Payload, "bauakteId")
            ?? throw new SyncValidationException("Die Bauakte fehlt (bauakteId).");
        if (!await BauaktePartyEndpoints.VisibleAsync(bauakteId, access, db, cancellationToken))
        {
            return SyncOperationResult.Rejected(op.OpId, "NOT_FOUND", "Die Bauakte existiert nicht (mehr).");
        }

        if (op.Type == "bauakteParty.delete")
        {
            await BauaktePartyEndpoints.RemoveAsync(db, history, bauakteId, op.EntityId, cancellationToken);
            return new SyncOperationResult(op.OpId, SyncOperationStatus.Applied);
        }

        var p = op.Payload;
        string? Text(string name, int max)
        {
            var value = SyncPayload.Read<string>(p, name);
            return value is { Length: > 0 } && value.Length > max ? throw new SyncValidationException($"„{name}“ ist länger als {max} Zeichen.") : value;
        }

        var request = new SaveBauaktePartyRequest(
            p.ContainsKey("role") ? SyncPayload.Read<BauaktePartyRole>(p, "role") : BauaktePartyRole.Other,
            SyncPayload.Read<Guid?>(p, "contactId"), Text("name", 200), Text("company", 200), Text("phone", 50), Text("email", 320),
            Text("note", 1000), op.BaseVersion, Text("mobile", 50), Text("street", 200), Text("postalCode", 10), Text("city", 100),
            SyncPayload.Read<bool?>(p, "isPrimary"));
        var (party, error, conflict) = await BauaktePartyEndpoints.ApplyAsync(db, history, bauakteId, op.EntityId, request, cancellationToken);
        if (error is not null)
        {
            throw new SyncValidationException(error);
        }

        if (party is null)
        {
            return SyncOperationResult.Rejected(op.OpId, "ID_CONFLICT", "Unter dieser ID existiert bereits ein Eintrag einer anderen Bauakte.");
        }

        return conflict
            ? new SyncOperationResult(op.OpId, SyncOperationStatus.Rejected, "VERSION_CONFLICT",
                "Der Kontakt wurde inzwischen im Büro geändert. Bitte erneut bearbeiten.", party.Version, Entity: SyncPayload.ToNode(BauaktePartyDto.From(party)))
            : new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: party.Version, Entity: SyncPayload.ToNode(BauaktePartyDto.From(party)));
    }
}
