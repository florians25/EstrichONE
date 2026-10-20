using System.Text.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Sync.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>
/// Sync-Operationen für Bauakten und Notizen (docs/sync-protokoll.md, Abschnitt 4).
/// Bauakte ändern: feldweises Zusammenführen. Notizen: anfügen; bearbeiten nur durch den Verfasser.
/// </summary>
internal sealed class BauakteSync(
    EstrichOneDbContext db,
    BauakteCommands commands,
    IBauakteAccess access,
    IUserPermissions permissions,
    ICurrentUser currentUser,
    IBauakteHistory history,
    TimeProvider clock) : ISyncOperationHandler
{
    private static readonly HashSet<string> EditableFields =
        ["title", "status", "street", "postalCode", "city", "latitude", "longitude", "description", "customerId", "responsibleUserId",
            "siteContactName", "siteContactPhone", "siteContactEmail", "siteNotes"];

    public IReadOnlyCollection<string> OperationTypes { get; } =
        ["bauakte.create", "bauakte.update", "bauakte.delete", "note.create", "note.update", "note.delete"];

    public Task<SyncOperationResult> ApplyAsync(SyncOperation operation, CancellationToken cancellationToken) => operation.Type switch
    {
        "bauakte.create" => CreateBauakteAsync(operation, cancellationToken),
        "bauakte.update" => UpdateBauakteAsync(operation, cancellationToken),
        "bauakte.delete" => DeleteBauakteAsync(operation, cancellationToken),
        "note.create" => CreateNoteAsync(operation, cancellationToken),
        "note.update" => UpdateNoteAsync(operation, cancellationToken),
        _ => DeleteNoteAsync(operation, cancellationToken),
    };

    private async Task<SyncOperationResult> CreateBauakteAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(Permissions.BauaktenCreate, cancellationToken))
        {
            return Denied(op);
        }

        var p = op.Payload;
        var title = SyncPayload.Read<string>(p, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new SyncValidationException("Das Bauvorhaben braucht eine Bezeichnung.");
        }

        var (bauakte, outcome) = await commands.CreateAsync(new NewBauakte(op.EntityId, title,
            SyncPayload.Read<string>(p, "street"), SyncPayload.Read<string>(p, "postalCode"), SyncPayload.Read<string>(p, "city"),
            SyncPayload.Read<double?>(p, "latitude"), SyncPayload.Read<double?>(p, "longitude"), SyncPayload.Read<string>(p, "description"),
            SyncPayload.Read<Guid?>(p, "customerId"), SyncPayload.Read<string>(p, "templateKey")), cancellationToken);

        return outcome switch
        {
            CreateBauakteOutcome.IdConflict => SyncOperationResult.Rejected(op.OpId, "ID_CONFLICT", "Unter dieser ID existiert bereits eine andere Bauakte."),
            CreateBauakteOutcome.UnknownCustomer => SyncOperationResult.Rejected(op.OpId, "UNKNOWN_CUSTOMER", "Der Kunde existiert nicht."),
            _ => new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: bauakte!.Version,
                Entity: SyncPayload.ToNode(BauakteEndpoints.ToDetail(bauakte))),
        };
    }

    private async Task<SyncOperationResult> UpdateBauakteAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(Permissions.BauaktenEdit, cancellationToken))
        {
            return Denied(op);
        }

        var bauakte = await (await access.VisibleAsync(db.Set<Bauakte>(), cancellationToken)).SingleOrDefaultAsync(b => b.Id == op.EntityId, cancellationToken);
        if (bauakte is null)
        {
            return NotFound(op);
        }

        var current = (JsonObject)SyncPayload.ToNode(BauakteEndpoints.ToDetail(bauakte))!;
        var (accepted, conflicts) = SyncPayload.Merge(op.Payload, current.ToDictionary(pair => pair.Key, pair => pair.Value), EditableFields);
        var changes = (JsonObject)op.Payload["changes"]!;
        var before = BauakteEndpoints.ToDetail(bauakte);

        foreach (var field in accepted)
        {
            var value = changes[field];
            switch (field)
            {
                case "title":
                    var title = value?.GetValue<string>();
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        throw new SyncValidationException("Das Bauvorhaben braucht eine Bezeichnung.");
                    }

                    bauakte.Rename(title);
                    break;
                case "status":
                    var status = value.Deserialize<BauakteStatus>(SyncPayload.Json);
                    if (status == BauakteStatus.Cancelled && bauakte.Status != BauakteStatus.Cancelled)
                    {
                        // Stornieren braucht eine Begründung und geschieht im Büro.
                        throw new SyncValidationException("Stornieren ist nur im Büro mit Begründung möglich.");
                    }

                    bauakte.ChangeStatus(status);
                    break;
                case "street":
                    bauakte.ChangeAddress(value?.GetValue<string>(), bauakte.PostalCode, bauakte.City, bauakte.Latitude, bauakte.Longitude);
                    break;
                case "postalCode":
                    bauakte.ChangeAddress(bauakte.Street, value?.GetValue<string>(), bauakte.City, bauakte.Latitude, bauakte.Longitude);
                    break;
                case "city":
                    bauakte.ChangeAddress(bauakte.Street, bauakte.PostalCode, value?.GetValue<string>(), bauakte.Latitude, bauakte.Longitude);
                    break;
                case "latitude":
                    bauakte.ChangeAddress(bauakte.Street, bauakte.PostalCode, bauakte.City, value?.GetValue<double>(), bauakte.Longitude);
                    break;
                case "longitude":
                    bauakte.ChangeAddress(bauakte.Street, bauakte.PostalCode, bauakte.City, bauakte.Latitude, value?.GetValue<double>());
                    break;
                case "description":
                    bauakte.Describe(value?.GetValue<string>());
                    break;
                case "siteContactName":
                    bauakte.ChangeSiteContact(value?.GetValue<string>(), bauakte.SiteContactPhone, bauakte.SiteContactEmail);
                    break;
                case "siteContactPhone":
                    bauakte.ChangeSiteContact(bauakte.SiteContactName, value?.GetValue<string>(), bauakte.SiteContactEmail);
                    break;
                case "siteContactEmail":
                    bauakte.ChangeSiteContact(bauakte.SiteContactName, bauakte.SiteContactPhone, value?.GetValue<string>());
                    break;
                case "siteNotes":
                    bauakte.ChangeSiteNotes(value?.GetValue<string>());
                    break;
                case "customerId":
                    var customerId = value.Deserialize<Guid?>(SyncPayload.Json);
                    if (customerId is { } id && !await db.Set<Contact>().AnyAsync(c => c.Id == id, cancellationToken))
                    {
                        throw new SyncValidationException("Der Kunde existiert nicht.");
                    }

                    bauakte.AssignCustomer(customerId);
                    break;
                case "responsibleUserId":
                    var userId = value.Deserialize<Guid?>(SyncPayload.Json);
                    if (userId is { } uid && !await db.Set<User>().AnyAsync(u => u.Id == uid, cancellationToken))
                    {
                        throw new SyncValidationException("Der Benutzer existiert nicht.");
                    }

                    bauakte.AssignResponsible(userId);
                    break;
            }
        }

        BauakteEndpoints.RecordChanges(history, before, BauakteEndpoints.ToDetail(bauakte));
        await db.SaveChangesAsync(cancellationToken);
        return new SyncOperationResult(op.OpId, conflicts.Count == 0 ? SyncOperationStatus.Applied : SyncOperationStatus.Conflict,
            Code: conflicts.Count == 0 ? null : "FIELD_CONFLICT",
            Message: conflicts.Count == 0 ? null : "Einige Felder wurden inzwischen von jemand anderem geändert.",
            Version: bauakte.Version, Conflicts: conflicts.Count == 0 ? null : conflicts,
            Entity: SyncPayload.ToNode(BauakteEndpoints.ToDetail(bauakte)));
    }

    private async Task<SyncOperationResult> DeleteBauakteAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(Permissions.BauaktenDelete, cancellationToken))
        {
            return Denied(op);
        }

        var bauakte = await (await access.VisibleAsync(db.Set<Bauakte>(), cancellationToken)).SingleOrDefaultAsync(b => b.Id == op.EntityId, cancellationToken);
        if (bauakte is null)
        {
            return NotFound(op);
        }

        bauakte.Delete(clock.GetUtcNow());
        history.Record(bauakte.Id, BauakteHistoryActions.Deleted);
        await db.SaveChangesAsync(cancellationToken);
        return new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: bauakte.Version);
    }

    private async Task<SyncOperationResult> CreateNoteAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(Permissions.NotesWrite, cancellationToken))
        {
            return Denied(op);
        }

        var bauakteId = SyncPayload.Read<Guid>(op.Payload, "bauakteId");
        if (!await access.CanReadAsync(bauakteId, cancellationToken))
        {
            return NotFound(op);
        }

        var existing = await db.Set<Note>().IgnoreQueryFilters([QueryFilters.NotDeleted]).SingleOrDefaultAsync(n => n.Id == op.EntityId, cancellationToken);
        if (existing is not null)
        {
            return existing.AuthorUserId == currentUser.UserId
                ? new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: existing.Version, Entity: SyncPayload.ToNode(NoteDto.From(existing)))
                : SyncOperationResult.Rejected(op.OpId, "ID_CONFLICT", "Unter dieser ID existiert bereits eine andere Notiz.");
        }

        Note note;
        try
        {
            note = Note.Create(op.EntityId, bauakteId, currentUser.UserId, SyncPayload.Read<string>(op.Payload, "text") ?? string.Empty);
        }
        catch (ArgumentException exception)
        {
            throw new SyncValidationException(exception.Message);
        }

        if (SyncPayload.Read<bool>(op.Payload, "pinned"))
        {
            note.Pin(SyncPayload.Read<string>(op.Payload, "color"));
        }

        db.Add(note);
        history.Record(bauakteId, BauakteHistoryActions.NoteAdded);
        await db.SaveChangesAsync(cancellationToken);
        return new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: note.Version, Entity: SyncPayload.ToNode(NoteDto.From(note)));
    }

    private async Task<SyncOperationResult> UpdateNoteAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        var note = await OwnNoteAsync(op.EntityId, cancellationToken);
        if (note is null)
        {
            return NotFound(op);
        }

        if (SyncPayload.Read<string>(op.Payload, "text") is { } text)
        {
            try
            {
                note.Edit(text, clock.GetUtcNow());
            }
            catch (ArgumentException exception)
            {
                throw new SyncValidationException(exception.Message);
            }
        }

        if (op.Payload.ContainsKey("pinned"))
        {
            if (SyncPayload.Read<bool>(op.Payload, "pinned"))
            {
                note.Pin(SyncPayload.Read<string>(op.Payload, "color"));
            }
            else
            {
                note.Unpin();
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: note.Version, Entity: SyncPayload.ToNode(NoteDto.From(note)));
    }

    private async Task<SyncOperationResult> DeleteNoteAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        var note = await OwnNoteAsync(op.EntityId, cancellationToken);
        if (note is null)
        {
            return NotFound(op);
        }

        note.Delete(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: note.Version);
    }

    /// <summary>Notizen ändert nur der Verfasser – auch das macht sie offline konfliktfrei.</summary>
    private async Task<Note?> OwnNoteAsync(Guid id, CancellationToken cancellationToken)
    {
        var note = await db.Set<Note>().SingleOrDefaultAsync(n => n.Id == id && n.AuthorUserId == currentUser.UserId, cancellationToken);
        return note is not null && await access.CanReadAsync(note.BauakteId, cancellationToken) ? note : null;
    }

    private static SyncOperationResult Denied(SyncOperation op) =>
        SyncOperationResult.Rejected(op.OpId, "PERMISSION_DENIED", "Für diese Aktion fehlt die Berechtigung.");

    private static SyncOperationResult NotFound(SyncOperation op) =>
        SyncOperationResult.Rejected(op.OpId, "NOT_FOUND", "Der Datensatz existiert nicht (mehr) oder ist nicht sichtbar.");
}

internal sealed class BauakteSource(EstrichOneDbContext db) : ISyncEntitySource
{
    public string EntityType => "bauakte";

    public string? RequiredPermission => null;

    public async Task<IReadOnlyDictionary<Guid, JsonNode>> LoadAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Set<Bauakte>().AsNoTracking().Where(b => ids.Contains(b.Id)).ToListAsync(cancellationToken))
            .ToDictionary(b => b.Id, b => SyncPayload.ToNode(BauakteEndpoints.ToDetail(b))!);
}

internal sealed class NoteSource(EstrichOneDbContext db) : ISyncEntitySource
{
    public string EntityType => "note";

    public string? RequiredPermission => null;

    public async Task<IReadOnlyDictionary<Guid, JsonNode>> LoadAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Set<Note>().AsNoTracking().Where(n => ids.Contains(n.Id)).ToListAsync(cancellationToken))
            .ToDictionary(n => n.Id, n => SyncPayload.ToNode(NoteDto.From(n))!);
}

internal sealed class ScreedTypeSource(EstrichOneDbContext db) : ISyncEntitySource
{
    public string EntityType => "screedType";

    public string? RequiredPermission => Permissions.BauaktenRead;

    public async Task<IReadOnlyDictionary<Guid, JsonNode>> LoadAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Set<ScreedType>().AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(cancellationToken))
            .ToDictionary(t => t.Id, t => SyncPayload.ToNode(ScreedTypeDto.From(t))!);
}
