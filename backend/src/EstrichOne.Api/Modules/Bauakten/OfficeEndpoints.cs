using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

public sealed record ScreedTypeResponse(Guid Id, string Name, string? Designation, ScreedBinder Binder, bool IsActive);

public sealed record SectionResponse(Guid Id, string Name, int SortOrder);

public sealed record SaveSectionRequest([property: Required] Guid Id, [property: Required, MaxLength(100)] string Name, int? SortOrder = null);

public sealed record RoomResponse(
    Guid Id, Guid? SectionId, string Name, string? Floor, decimal? AreaSquareMeters, Guid? ScreedTypeId, string? ScreedTypeName,
    int? ThicknessMillimeters, int? InsulationMillimeters, bool UnderfloorHeating, RoomStatus Status, string? Notes, long Version);

public sealed record SaveRoomRequest(
    [property: Required] Guid Id,
    Guid? SectionId,
    [property: Required, MaxLength(100)] string Name,
    [property: MaxLength(50)] string? Floor,
    [property: Range(0, 100_000)] decimal? AreaSquareMeters,
    Guid? ScreedTypeId,
    int? ThicknessMillimeters,
    int? InsulationMillimeters,
    bool UnderfloorHeating,
    RoomStatus Status,
    [property: MaxLength(2000)] string? Notes);

/// <summary>Räume und Flächen einer Bauakte mit Summen (Büro, Angebot und Planung).</summary>
public sealed record RoomOverview(IReadOnlyList<SectionResponse> Sections, IReadOnlyList<RoomResponse> Rooms, decimal TotalAreaSquareMeters,
    decimal HeatedAreaSquareMeters);

public sealed record CreateNoteRequest([property: Required] Guid Id, [property: Required, MaxLength(Note.MaxTextLength)] string Text);

/// <summary>Büroarbeit an der Bauakte im Web und am Desktop: Bereiche, Räume, Estrichaufbau, Notizen (AP-18).</summary>
internal static class OfficeEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/screed-types", async (EstrichOneDbContext db, CancellationToken ct) => TypedResults.Ok(
                await db.Set<ScreedType>().AsNoTracking().OrderBy(t => t.Name)
                    .Select(t => new ScreedTypeResponse(t.Id, t.Name, t.Designation, t.Binder, t.IsActive)).ToListAsync(ct)))
            .WithName("ListScreedTypes").WithTags("Bauakten").RequirePermission(Permissions.BauaktenRead);

        var group = api.MapGroup("/bauakten/{id:guid}").WithTags("Bauakten");
        group.MapGet("/rooms", ListRoomsAsync).WithName("ListRooms").WithSummary("Bereiche und Räume mit Flächensummen")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenRead);
        group.MapPut("/sections/{sectionId:guid}", SaveSectionAsync).WithName("SaveSection").WithSummary("Bereich anlegen oder umbenennen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.BauaktenEdit);
        group.MapDelete("/sections/{sectionId:guid}", DeleteSectionAsync).WithName("DeleteSection")
            .WithSummary("Bereich entfernen (Räume bleiben, ohne Bereich)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenEdit);
        group.MapPut("/rooms/{roomId:guid}", SaveRoomAsync).WithName("SaveRoom").WithSummary("Raum anlegen oder ändern (Fläche, Estrichaufbau, Status)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.BauaktenEdit);
        group.MapDelete("/rooms/{roomId:guid}", DeleteRoomAsync).WithName("DeleteRoom")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenEdit);
        group.MapGet("/notes", ListNotesAsync).WithName("ListNotes").WithSummary("Notizen (angeheftete zuerst, dann neueste)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenRead);
        group.MapPost("/notes", CreateNoteAsync).WithName("CreateNote").WithSummary("Notiz schreiben (idempotent über die ID)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.NotesWrite);
    }

    private static async Task<Results<Ok<RoomOverview>, ProblemHttpResult>> ListRoomsAsync(
        Guid id, IBauakteAccess access, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        return TypedResults.Ok(await OverviewAsync(id, db, cancellationToken));
    }

    private static async Task<RoomOverview> OverviewAsync(Guid id, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var sections = await db.Set<BauakteSection>().AsNoTracking().Where(s => s.BauakteId == id).OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .Select(s => new SectionResponse(s.Id, s.Name, s.SortOrder)).ToListAsync(cancellationToken);
        var rooms = await db.Set<Room>().AsNoTracking().Where(r => r.BauakteId == id).OrderBy(r => r.Floor).ThenBy(r => r.Name).ToListAsync(cancellationToken);
        var typeIds = rooms.Select(r => r.ScreedTypeId).OfType<Guid>().Distinct().ToList();
        var types = await db.Set<ScreedType>().AsNoTracking().Where(t => typeIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => t.Designation is null ? t.Name : $"{t.Name} ({t.Designation})", cancellationToken);
        var responses = rooms.Select(r => new RoomResponse(r.Id, r.SectionId, r.Name, r.Floor, r.AreaSquareMeters, r.ScreedTypeId,
            r.ScreedTypeId is { } typeId ? types.GetValueOrDefault(typeId) : null, r.ThicknessMillimeters, r.InsulationMillimeters,
            r.UnderfloorHeating, r.Status, r.Notes, r.Version)).ToList();
        return new RoomOverview(sections, responses, rooms.Sum(r => r.AreaSquareMeters ?? 0),
            rooms.Where(r => r.UnderfloorHeating).Sum(r => r.AreaSquareMeters ?? 0));
    }

    private static async Task<Results<Ok<RoomOverview>, ProblemHttpResult>> SaveSectionAsync(
        Guid id, Guid sectionId, SaveSectionRequest request, IBauakteAccess access, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (request.Id != sectionId)
        {
            return Validation("Die ID im Pfad und im Inhalt müssen übereinstimmen.");
        }

        try
        {
            var section = await db.Set<BauakteSection>().SingleOrDefaultAsync(s => s.Id == sectionId, cancellationToken);
            if (section is null)
            {
                var order = request.SortOrder ?? await db.Set<BauakteSection>().CountAsync(s => s.BauakteId == id, cancellationToken);
                db.Add(new BauakteSection(sectionId, id, request.Name, order));
            }
            else if (section.BauakteId != id)
            {
                return NotFound();
            }
            else
            {
                section.Rename(request.Name);
                if (request.SortOrder is { } sortOrder)
                {
                    section.Move(sortOrder);
                }
            }
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await OverviewAsync(id, db, cancellationToken));
    }

    private static async Task<Results<Ok<RoomOverview>, ProblemHttpResult>> DeleteSectionAsync(
        Guid id, Guid sectionId, IBauakteAccess access, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var section = await db.Set<BauakteSection>().SingleOrDefaultAsync(s => s.Id == sectionId && s.BauakteId == id, cancellationToken);
        if (section is null || !await access.CanReadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        db.Remove(section);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await OverviewAsync(id, db, cancellationToken));
    }

    private static async Task<Results<Ok<RoomOverview>, ProblemHttpResult>> SaveRoomAsync(
        Guid id, Guid roomId, SaveRoomRequest request, IBauakteAccess access, IBauakteHistory history, EstrichOneDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        if (request.Id != roomId)
        {
            return Validation("Die ID im Pfad und im Inhalt müssen übereinstimmen.");
        }

        if (request.SectionId is { } sectionId && !await db.Set<BauakteSection>().AnyAsync(s => s.Id == sectionId && s.BauakteId == id, cancellationToken))
        {
            return Validation("Der Bereich gehört nicht zu dieser Bauakte.");
        }

        if (request.ScreedTypeId is { } typeId && !await db.Set<ScreedType>().AnyAsync(t => t.Id == typeId, cancellationToken))
        {
            return Validation("Unbekannte Estrichart.");
        }

        try
        {
            var room = await db.Set<Room>().SingleOrDefaultAsync(r => r.Id == roomId, cancellationToken);
            if (room is not null && room.BauakteId != id)
            {
                return NotFound();
            }

            if (room is null)
            {
                room = Room.Create(roomId, id, request.Name);
                db.Add(room);
                history.Record(id, BauakteHistoryActions.RoomAdded);
            }

            room.Update(request.SectionId, request.Name, request.Floor, request.AreaSquareMeters, string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim());
            room.ChangeBuildup(request.ScreedTypeId, request.ThicknessMillimeters, request.InsulationMillimeters, request.UnderfloorHeating);
            room.ChangeStatus(request.Status);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await OverviewAsync(id, db, cancellationToken));
    }

    private static async Task<Results<Ok<RoomOverview>, ProblemHttpResult>> DeleteRoomAsync(
        Guid id, Guid roomId, IBauakteAccess access, EstrichOneDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var room = await db.Set<Room>().SingleOrDefaultAsync(r => r.Id == roomId && r.BauakteId == id, cancellationToken);
        if (room is null || !await access.CanReadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        room.Delete(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await OverviewAsync(id, db, cancellationToken));
    }

    private static async Task<Results<Ok<List<NoteDto>>, ProblemHttpResult>> ListNotesAsync(
        Guid id, IBauakteAccess access, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var notes = await db.Set<Note>().AsNoTracking().Where(n => n.BauakteId == id)
            .OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.CreatedAt).ToListAsync(cancellationToken);
        return TypedResults.Ok(notes.Select(NoteDto.From).ToList());
    }

    private static async Task<Results<Created<NoteDto>, Ok<NoteDto>, ProblemHttpResult>> CreateNoteAsync(
        Guid id, CreateNoteRequest request, IBauakteAccess access, IBauakteHistory history, ICurrentUser currentUser, EstrichOneDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var existing = await db.Set<Note>().IgnoreQueryFilters([QueryFilters.NotDeleted]).AsNoTracking().SingleOrDefaultAsync(n => n.Id == request.Id, cancellationToken);
        if (existing is not null)
        {
            return existing.AuthorUserId == currentUser.UserId && existing.BauakteId == id
                ? TypedResults.Ok(NoteDto.From(existing))
                : TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "ID bereits vergeben",
                    extensions: new Dictionary<string, object?> { ["code"] = "ID_CONFLICT" });
        }

        Note note;
        try
        {
            note = Note.Create(request.Id, id, currentUser.UserId, request.Text);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }

        db.Add(note);
        history.Record(id, BauakteHistoryActions.NoteAdded);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/bauakten/{id}/notes", NoteDto.From(note));
    }

    private static ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound,
        title: "Bauakte nicht gefunden", extensions: new Dictionary<string, object?> { ["code"] = "BAUAKTE_NOT_FOUND" });

    private static ProblemHttpResult Validation(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Ungültige Angaben", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
}
