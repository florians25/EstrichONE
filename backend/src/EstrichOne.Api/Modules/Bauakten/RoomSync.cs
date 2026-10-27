using System.Text.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Sync.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>Raum mit Fläche und Estrichaufbau im Sync (Entitätstyp <c>room</c>).</summary>
public sealed record RoomDto(
    Guid Id, Guid BauakteId, Guid? SectionId, string Name, string? Floor, decimal? AreaSquareMeters, Guid? ScreedTypeId,
    int? ThicknessMillimeters, int? InsulationMillimeters, bool UnderfloorHeating, RoomStatus Status, string? Notes, long Version,
    DateTimeOffset UpdatedAt)
{
    public static RoomDto From(Room room) => new(room.Id, room.BauakteId, room.SectionId, room.Name, room.Floor, room.AreaSquareMeters,
        room.ScreedTypeId, room.ThicknessMillimeters, room.InsulationMillimeters, room.UnderfloorHeating, room.Status, room.Notes, room.Version,
        room.UpdatedAt);
}

/// <summary>
/// Räume und Flächen offline erfassen (App, Baustelle): <c>room.create</c>, <c>room.update</c> (feldweise mit
/// <c>base</c>), <c>room.delete</c>. Recht wie im Büro: Bauakten bearbeiten; sichtbar wie die Bauakte.
/// </summary>
internal sealed class RoomSync(
    EstrichOneDbContext db, IBauakteAccess access, IUserPermissions permissions, IBauakteHistory history, TimeProvider clock) : ISyncOperationHandler
{
    private static readonly HashSet<string> EditableFields =
        ["name", "floor", "areaSquareMeters", "thicknessMillimeters", "insulationMillimeters", "underfloorHeating", "status", "notes",
            "screedTypeId", "sectionId"];

    public IReadOnlyCollection<string> OperationTypes { get; } = ["room.create", "room.update", "room.delete"];

    public async Task<SyncOperationResult> ApplyAsync(SyncOperation operation, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(Permissions.BauaktenEdit, cancellationToken))
        {
            return SyncOperationResult.Rejected(operation.OpId, "PERMISSION_DENIED", "Für diese Aktion fehlt die Berechtigung.");
        }

        try
        {
            return operation.Type switch
            {
                "room.create" => await CreateAsync(operation, cancellationToken),
                "room.update" => await UpdateAsync(operation, cancellationToken),
                _ => await DeleteAsync(operation, cancellationToken),
            };
        }
        catch (ArgumentException exception)
        {
            throw new SyncValidationException(exception.Message);
        }
        catch (JsonException)
        {
            throw new SyncValidationException("Ungültige Angaben zum Raum.");
        }
    }

    private async Task<SyncOperationResult> CreateAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        var bauakteId = SyncPayload.Read<Guid>(op.Payload, "bauakteId");
        if (!await access.CanReadAsync(bauakteId, cancellationToken))
        {
            return NotFound(op);
        }

        var existing = await db.Set<Room>().IgnoreQueryFilters([QueryFilters.NotDeleted]).SingleOrDefaultAsync(r => r.Id == op.EntityId, cancellationToken);
        if (existing is not null)
        {
            // Antwort verloren, App sendet erneut: dieselbe Anlage bestätigen.
            return existing.BauakteId == bauakteId && existing.DeletedAt is null
                ? Applied(op, existing)
                : SyncOperationResult.Rejected(op.OpId, "ID_CONFLICT", "Unter dieser ID existiert bereits ein anderer Raum.");
        }

        var room = Room.Create(op.EntityId, bauakteId, SyncPayload.Read<string>(op.Payload, "name") ?? string.Empty);
        await ApplyFieldsAsync(room, op.Payload, all: true, cancellationToken);
        db.Add(room);
        history.Record(bauakteId, BauakteHistoryActions.RoomAdded);
        await db.SaveChangesAsync(cancellationToken);
        return Applied(op, room);
    }

    private async Task<SyncOperationResult> UpdateAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        var room = await db.Set<Room>().SingleOrDefaultAsync(r => r.Id == op.EntityId, cancellationToken);
        if (room is null || !await access.CanReadAsync(room.BauakteId, cancellationToken))
        {
            return NotFound(op);
        }

        var current = (JsonObject)SyncPayload.ToNode(RoomDto.From(room))!;
        var (accepted, conflicts) = SyncPayload.Merge(op.Payload, current.ToDictionary(pair => pair.Key, pair => pair.Value), EditableFields);
        var changes = (JsonObject)op.Payload["changes"]!;
        var merged = new JsonObject();
        foreach (var field in accepted)
        {
            merged[field] = changes[field]?.DeepClone();
        }

        await ApplyFieldsAsync(room, merged, all: false, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new SyncOperationResult(op.OpId, conflicts.Count == 0 ? SyncOperationStatus.Applied : SyncOperationStatus.Conflict,
            Code: conflicts.Count == 0 ? null : "FIELD_CONFLICT",
            Message: conflicts.Count == 0 ? null : "Einige Angaben zum Raum wurden inzwischen von jemand anderem geändert.",
            Version: room.Version, Conflicts: conflicts.Count == 0 ? null : conflicts, Entity: SyncPayload.ToNode(RoomDto.From(room)));
    }

    private async Task<SyncOperationResult> DeleteAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        var room = await db.Set<Room>().SingleOrDefaultAsync(r => r.Id == op.EntityId, cancellationToken);
        if (room is null || !await access.CanReadAsync(room.BauakteId, cancellationToken))
        {
            return NotFound(op);
        }

        room.Delete(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: room.Version);
    }

    /// <summary>Übernimmt die gesendeten Felder (bei der Anlage alle, sonst nur die vorhandenen) mit den Regeln des Raums.</summary>
    private async Task ApplyFieldsAsync(Room room, JsonObject values, bool all, CancellationToken cancellationToken)
    {
        bool Has(string field) => all || values.ContainsKey(field);

        var sectionId = Has("sectionId") ? SyncPayload.Read<Guid?>(values, "sectionId") : room.SectionId;
        if (sectionId is { } section && section != room.SectionId
            && !await db.Set<BauakteSection>().AnyAsync(s => s.Id == section && s.BauakteId == room.BauakteId, cancellationToken))
        {
            throw new SyncValidationException("Der Bereich gehört nicht zu dieser Bauakte.");
        }

        var screedTypeId = Has("screedTypeId") ? SyncPayload.Read<Guid?>(values, "screedTypeId") : room.ScreedTypeId;
        if (screedTypeId is { } type && type != room.ScreedTypeId && !await db.Set<ScreedType>().AnyAsync(t => t.Id == type, cancellationToken))
        {
            throw new SyncValidationException("Unbekannte Estrichart.");
        }

        var notes = Has("notes") ? SyncPayload.Read<string>(values, "notes") : room.Notes;
        room.Update(sectionId,
            Has("name") ? SyncPayload.Read<string>(values, "name") ?? string.Empty : room.Name,
            Has("floor") ? SyncPayload.Read<string>(values, "floor") : room.Floor,
            Has("areaSquareMeters") ? SyncPayload.Read<decimal?>(values, "areaSquareMeters") : room.AreaSquareMeters,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim());
        room.ChangeBuildup(screedTypeId,
            Has("thicknessMillimeters") ? SyncPayload.Read<int?>(values, "thicknessMillimeters") : room.ThicknessMillimeters,
            Has("insulationMillimeters") ? SyncPayload.Read<int?>(values, "insulationMillimeters") : room.InsulationMillimeters,
            Has("underfloorHeating") ? SyncPayload.Read<bool>(values, "underfloorHeating") : room.UnderfloorHeating);
        if (Has("status"))
        {
            room.ChangeStatus(SyncPayload.Read<RoomStatus?>(values, "status") ?? RoomStatus.Planned);
        }
    }

    private static SyncOperationResult Applied(SyncOperation op, Room room) =>
        new(op.OpId, SyncOperationStatus.Applied, Version: room.Version, Entity: SyncPayload.ToNode(RoomDto.From(room)));

    private static SyncOperationResult NotFound(SyncOperation op) =>
        SyncOperationResult.Rejected(op.OpId, "NOT_FOUND", "Der Raum oder die Bauakte existiert nicht (mehr) oder ist nicht sichtbar.");
}

/// <summary>Stand der Räume für den Änderungsabruf; sichtbar wie die Bauakte.</summary>
internal sealed class RoomSource(EstrichOneDbContext db) : ISyncEntitySource
{
    public string EntityType => "room";

    public string? RequiredPermission => null;

    public async Task<IReadOnlyDictionary<Guid, JsonNode>> LoadAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Set<Room>().AsNoTracking().Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken))
            .ToDictionary(r => r.Id, r => SyncPayload.ToNode(RoomDto.From(r))!);
}
