using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

public sealed record BauakteMemberResponse(Guid UserId, string DisplayName, string? Email, BauakteMemberRole Role, bool IsActive);

/// <summary>Zugewiesene Mitarbeiter (zusätzlich zur zuständigen Person); sie sehen die Bauakte auch ohne „alle Bauakten“.</summary>
public sealed record SetMembersRequest([property: Required] IReadOnlyList<Guid> UserIds);

/// <param name="Color">Eine der Notizfarben (yellow, red, green, blue) oder <c>null</c>.</param>
public sealed record UpdateNoteRequest(bool? IsPinned, [property: MaxLength(20)] string? Color);

/// <summary>Team, Notiz-Markierungen und Hinweiszeilen einer Bauakte.</summary>
internal static class BauakteTeamEndpoints
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly HashSet<string> NoteColors = ["yellow", "red", "green", "blue"];

    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/bauakten/{id:guid}").WithTags("Bauakten");
        group.MapGet("/members", ListMembersAsync).WithName("ListBauakteMembers").WithSummary("Zuständige Person und zugewiesene Mitarbeiter")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenRead);
        group.MapPut("/members", SetMembersAsync).WithName("SetBauakteMembers").WithSummary("Zugewiesene Mitarbeiter festlegen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.BauaktenEdit);
        group.MapPost("/members/me", AssignMeAsync).WithName("AssignBauakteToMe")
            .WithSummary("„Mir zuweisen“: sich selbst als Mitarbeiter eintragen (sichtbar, eingeplant oder mit Recht bauakten.assign)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenRead);
        group.MapPatch("/notes/{noteId:guid}", UpdateNoteAsync).WithName("UpdateNote").WithSummary("Notiz anheften, lösen oder einfärben")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.NotesWrite);
        group.MapGet("/hints", HintsAsync).WithName("GetBauakteHints").WithSummary("Hinweiszeilen (Einsatz heute, Erstbegehung offen, Mängel …)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenRead);
    }

    private static async Task<Results<Ok<List<BauakteMemberResponse>>, ProblemHttpResult>> ListMembersAsync(
        Guid id, IBauakteAccess access, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var bauakte = await (await access.VisibleAsync(db.Set<Bauakte>().AsNoTracking(), cancellationToken))
            .Include(b => b.Members).SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        return bauakte is null ? NotFound() : TypedResults.Ok(await MembersAsync(db, bauakte, cancellationToken));
    }

    private static async Task<Results<Ok<List<BauakteMemberResponse>>, ProblemHttpResult>> SetMembersAsync(
        Guid id, SetMembersRequest request, IBauakteAccess access, IBauakteHistory history, EstrichOneDbContext db,
        Notifications.Exports.INotifier notifier, CancellationToken cancellationToken)
    {
        var bauakte = await (await access.VisibleAsync(db.Set<Bauakte>(), cancellationToken))
            .Include(b => b.Members).SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bauakte is null)
        {
            return NotFound();
        }

        var wanted = request.UserIds.Distinct().ToHashSet();
        var known = await db.Set<User>().Where(u => wanted.Contains(u.Id)).Select(u => u.Id).ToListAsync(cancellationToken);
        if (known.Count != wanted.Count)
        {
            return Validation("Mindestens ein Benutzer existiert nicht.");
        }

        // Die zuständige Person behält immer Zugriff; frühere Zuständige sind normale Mitglieder.
        var responsible = bauakte.ResponsibleUserId;
        var current = bauakte.Members.Where(m => m.UserId != responsible).Select(m => m.UserId).ToHashSet();
        bauakte.Members.RemoveAll(m => m.UserId != responsible && !wanted.Contains(m.UserId));
        foreach (var userId in wanted.Where(u => !current.Contains(u) && bauakte.Members.All(m => m.UserId != u)))
        {
            bauakte.Members.Add(new BauakteMember(bauakte.Id, userId, BauakteMemberRole.Member));
        }

        if (!current.SetEquals(wanted))
        {
            history.Record(bauakte.Id, BauakteHistoryActions.MembersChanged,
                new Dictionary<string, string?> { ["count"] = wanted.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        }

        await db.SaveChangesAsync(cancellationToken);
        var added = wanted.Where(u => !current.Contains(u) && u != responsible).ToList();
        await notifier.NotifyAsync(added, new Notifications.Exports.NotificationRequest(Notifications.Exports.NotificationCategory.Bauakte,
            "Neue Bauakte zugewiesen", $"{bauakte.Number} {bauakte.Title}".Trim(), $"/bauakten/{bauakte.Id}", "bauakte", bauakte.Id), cancellationToken);
        return TypedResults.Ok(await MembersAsync(db, bauakte, cancellationToken));
    }

    private static async Task<Results<Ok<List<BauakteMemberResponse>>, ProblemHttpResult>> AssignMeAsync(
        Guid id, IBauakteAccess access, IBauakteHistory history, IUserPermissions permissions, ICurrentUser currentUser,
        IEnumerable<IBauakteVisibilitySource> sources, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var bauakte = await db.Set<Bauakte>().Include(b => b.Members).SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bauakte is null)
        {
            return NotFound();
        }

        var granted = await permissions.GetAsync(cancellationToken);
        var allowed = await access.CanReadAsync(id, cancellationToken) || granted.Contains(Permissions.BauaktenAssign);
        if (!allowed && granted.Contains(Permissions.PlanningRead))
        {
            foreach (var source in sources)
            {
                if (await source.IsPlannedAsync(id, cancellationToken))
                {
                    allowed = true;
                    break;
                }
            }
        }

        if (!allowed)
        {
            // Wie „nicht vorhanden“: keine Auskunft über fremde Bauakten.
            return NotFound();
        }

        var userId = currentUser.UserId;
        if (bauakte.ResponsibleUserId != userId && bauakte.Members.All(m => m.UserId != userId))
        {
            bauakte.Members.Add(new BauakteMember(bauakte.Id, userId, BauakteMemberRole.Member));
            history.Record(bauakte.Id, BauakteHistoryActions.MembersChanged,
                new Dictionary<string, string?> { ["count"] = bauakte.Members.Count(m => m.UserId != bauakte.ResponsibleUserId).ToString(System.Globalization.CultureInfo.InvariantCulture) });
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(await MembersAsync(db, bauakte, cancellationToken));
    }

    private static async Task<List<BauakteMemberResponse>> MembersAsync(EstrichOneDbContext db, Bauakte bauakte, CancellationToken cancellationToken)
    {
        var ids = bauakte.Members.Select(m => m.UserId).Append(bauakte.ResponsibleUserId ?? Guid.Empty).Where(u => u != Guid.Empty).Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(cancellationToken);
        return users
            .Select(u => new BauakteMemberResponse(u.Id, u.DisplayName, u.Email,
                u.Id == bauakte.ResponsibleUserId ? BauakteMemberRole.Responsible : BauakteMemberRole.Member, u.IsActive))
            .OrderBy(m => m.Role).ThenBy(m => m.DisplayName, StringComparer.CurrentCulture)
            .ToList();
    }

    private static async Task<Results<Ok<NoteDto>, ProblemHttpResult>> UpdateNoteAsync(
        Guid id, Guid noteId, UpdateNoteRequest request, IBauakteAccess access, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var note = await db.Set<Note>().SingleOrDefaultAsync(n => n.Id == noteId && n.BauakteId == id, cancellationToken);
        if (note is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Notiz nicht gefunden",
                extensions: new Dictionary<string, object?> { ["code"] = "NOTE_NOT_FOUND" });
        }

        if (request.Color is { } color && !NoteColors.Contains(color))
        {
            return Validation("Unbekannte Notizfarbe.");
        }

        if (request.IsPinned == false)
        {
            note.Unpin();
        }
        else if (request.IsPinned == true || request.Color is not null)
        {
            note.Pin(request.Color ?? note.Color);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(NoteDto.From(note));
    }

    private static async Task<Results<Ok<List<BauakteHint>>, ProblemHttpResult>> HintsAsync(
        Guid id, IBauakteAccess access, IEnumerable<IBauakteHintSource> sources, EstrichOneDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var bauakte = await (await access.VisibleAsync(db.Set<Bauakte>().AsNoTracking(), cancellationToken))
            .SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bauakte is null)
        {
            return NotFound();
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Berlin).DateTime);
        var hints = new List<BauakteHint>();
        foreach (var source in sources)
        {
            hints.AddRange(await source.GetHintsAsync(bauakte, today, cancellationToken));
        }

        // Wichtigstes zuerst.
        return TypedResults.Ok(hints.OrderByDescending(h => h.Severity).ToList());
    }

    private static ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound,
        title: "Bauakte nicht gefunden", extensions: new Dictionary<string, object?> { ["code"] = "BAUAKTE_NOT_FOUND" });

    private static ProblemHttpResult Validation(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Ungültige Angaben", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
}

/// <summary>Hinweise aus der Bauakte selbst: Hinweise zur Baustelle, Storno, fehlende Angaben.</summary>
internal sealed class BauakteOwnHints : IBauakteHintSource
{
    public Task<IReadOnlyList<BauakteHint>> GetHintsAsync(Bauakte bauakte, DateOnly today, CancellationToken cancellationToken)
    {
        var hints = new List<BauakteHint>();
        if (bauakte.Status == BauakteStatus.Cancelled)
        {
            hints.Add(new("cancelled", BauakteHintSeverity.Danger,
                bauakte.CancellationReason is null ? "Storniert." : $"Storniert: {bauakte.CancellationReason}"));
        }

        if (bauakte.SiteNotes is { } notes)
        {
            hints.Add(new("site_notes", BauakteHintSeverity.Warning, notes));
        }

        if (bauakte.Status is BauakteStatus.Planned or BauakteStatus.InProgress && bauakte.Street is null && bauakte.City is null && bauakte.Latitude is null)
        {
            hints.Add(new("address_missing", BauakteHintSeverity.Info, "Keine Baustellenadresse hinterlegt."));
        }

        if (bauakte.PlannedEnd is { } end && end < today && bauakte.Status is BauakteStatus.Planned or BauakteStatus.InProgress)
        {
            hints.Add(new("period_exceeded", BauakteHintSeverity.Warning, $"Geplantes Ende ({end:dd.MM.yyyy}) ist überschritten."));
        }

        return Task.FromResult<IReadOnlyList<BauakteHint>>(hints);
    }
}
