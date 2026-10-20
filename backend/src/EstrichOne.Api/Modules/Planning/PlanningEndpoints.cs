using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

public sealed record ResourceResponse(Guid Id, ResourceKind Kind, string Name, string? Identifier, string? Notes, bool IsActive, long Version);

public sealed record SaveResourceRequest(
    ResourceKind Kind, [property: Required, MaxLength(100)] string Name, [property: MaxLength(50)] string? Identifier,
    [property: MaxLength(2000)] string? Notes, bool IsActive = true);

public sealed record CrewResponse(Guid Id, string Name, Guid? LeaderUserId, IReadOnlyList<Guid> MemberUserIds, string? Color, bool IsActive,
    int SortOrder, long Version);

public sealed record SaveCrewRequest(
    [property: Required, MaxLength(100)] string Name, Guid? LeaderUserId, IReadOnlyList<Guid>? MemberUserIds,
    [property: MaxLength(7)] string? Color, bool IsActive = true, int SortOrder = 0);

public sealed record SaveAbsenceRequest(Guid UserId, DateOnly From, DateOnly To, AbsenceKind Kind, [property: MaxLength(500)] string? Note);

public sealed record SaveAssignmentRequest(
    Guid BauakteId, DateOnly Day, TimeOnly? StartTime, [property: Required, MaxLength(200)] string Activity, Guid? CrewId,
    IReadOnlyList<Guid>? UserIds, IReadOnlyList<Guid>? ResourceIds, [property: MaxLength(2000)] string? Notes,
    AssignmentKind? Kind = null, TimeOnly? EndTime = null);

/// <summary>Einsatzplanung (AP-19). Planen nur online im Büro; mobil lesend („Mein Einsatz“, 15: „Planung nur online“).</summary>
/// <summary>Eintrag im Baustellen-Vorrat; <see cref="UpcomingAssignments"/> = 0 heißt „frei“ (noch nicht eingeplant).</summary>
public sealed record BacklogItem(
    Guid BauakteId, string? Number, string Title, BauakteStatus Status, string? Street, string? PostalCode, string? City,
    DateOnly? PlannedStart, DateOnly? PlannedEnd, string? CustomerName, decimal TotalAreaSquareMeters, IReadOnlyList<string> ScreedTypes,
    int UpcomingAssignments, DateOnly? NextAssignment);

internal static class PlanningEndpoints
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public static void Map(IEndpointRouteBuilder api)
    {
        var planning = api.MapGroup("/planning").WithTags("Planung");
        PlanningPrint.Map(planning);

        planning.MapGet("/resources", async (EstrichOneDbContext db, CancellationToken ct) => TypedResults.Ok(
                (await db.Set<Resource>().AsNoTracking().OrderBy(r => r.Kind).ThenBy(r => r.Name).ToListAsync(ct)).Select(ToResponse).ToList()))
            .WithName("ListResources").RequirePermission(Permissions.PlanningRead);
        planning.MapPut("/resources/{id:guid}", SaveResourceAsync).WithName("SaveResource")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.PlanningManage);

        planning.MapGet("/crews", async (EstrichOneDbContext db, CancellationToken ct) => TypedResults.Ok(
                (await db.Set<Crew>().AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct)).Select(ToResponse).ToList()))
            .WithName("ListCrews").RequirePermission(Permissions.PlanningRead);
        planning.MapPut("/crews/{id:guid}", SaveCrewAsync).WithName("SaveCrew")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.PlanningManage);

        planning.MapPut("/absences/{id:guid}", SaveAbsenceAsync).WithName("SaveAbsence")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.PlanningManage);
        planning.MapDelete("/absences/{id:guid}", DeleteAbsenceAsync).WithName("DeleteAbsence")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.PlanningManage);

        planning.MapGet("/range", RangeAsync).WithName("GetPlanningRange")
            .WithSummary("Einsätze und Abwesenheiten von–bis (höchstens 62 Tage), mit Konflikthinweisen")
            .ProducesProblem(StatusCodes.Status400BadRequest).RequirePermission(Permissions.PlanningRead);
        planning.MapGet("/mine", MineAsync).WithName("GetMyPlanning")
            .WithSummary("Eigene Einsätze („Mein Einsatz heute“) – über die Kolonne oder direkt eingeplant")
            .ProducesProblem(StatusCodes.Status400BadRequest).RequirePermission(Permissions.PlanningRead);
        planning.MapPut("/assignments/{id:guid}", SaveAssignmentAsync).WithName("SaveAssignment")
            .WithSummary("Einsatz anlegen oder ändern; Antwort enthält Konflikthinweise")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.PlanningManage);
        planning.MapGet("/backlog", BacklogAsync).WithName("GetPlanningBacklog")
            .WithSummary("Baustellen-Vorrat: geplante und laufende Bauakten mit Stand der Einplanung (frei, verplant)")
            .RequirePermission(Permissions.PlanningRead);
        planning.MapDelete("/assignments/{id:guid}", DeleteAssignmentAsync).WithName("DeleteAssignment")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.PlanningManage);
    }

    private static ResourceResponse ToResponse(Resource r) => new(r.Id, r.Kind, r.Name, r.Identifier, r.Notes, r.IsActive, r.Version);

    private static CrewResponse ToResponse(Crew c) => new(c.Id, c.Name, c.LeaderUserId, c.MemberUserIds, c.Color, c.IsActive, c.SortOrder, c.Version);

    private static async Task<Results<Ok<ResourceResponse>, ProblemHttpResult>> SaveResourceAsync(
        Guid id, SaveResourceRequest request, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            var resource = await db.Set<Resource>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (resource is null)
            {
                resource = Resource.Create(id, request.Kind, request.Name);
                db.Add(resource);
            }

            resource.Update(request.Kind, request.Name, request.Identifier, request.Notes, request.IsActive);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(ToResponse(resource));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }
    }

    private static async Task<Results<Ok<CrewResponse>, ProblemHttpResult>> SaveCrewAsync(
        Guid id, SaveCrewRequest request, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var members = (request.MemberUserIds ?? []).Concat(request.LeaderUserId is { } leader ? [leader] : []).Distinct().ToList();
        if (await db.Set<User>().CountAsync(u => members.Contains(u.Id), cancellationToken) != members.Count)
        {
            return Validation("Unbekannte Mitarbeiter in der Kolonne.");
        }

        try
        {
            var crew = await db.Set<Crew>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (crew is null)
            {
                crew = Crew.Create(id, request.Name);
                db.Add(crew);
            }

            crew.Update(request.Name, request.LeaderUserId, members, string.IsNullOrWhiteSpace(request.Color) ? null : request.Color, request.IsActive, request.SortOrder);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(ToResponse(crew));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }
    }

    private static async Task<Results<Ok<AbsenceView>, ProblemHttpResult>> SaveAbsenceAsync(
        Guid id, SaveAbsenceRequest request, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null)
        {
            return Validation("Unbekannter Mitarbeiter.");
        }

        try
        {
            var absence = Absence.Create(id, request.UserId, request.From, request.To, request.Kind, request.Note);
            var existing = await db.Set<Absence>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
            if (existing is not null)
            {
                db.Remove(existing);
            }

            db.Add(absence);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(new AbsenceView(absence.Id, absence.UserId, user.DisplayName, absence.From, absence.To, absence.Kind, absence.Note));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAbsenceAsync(Guid id, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var deleted = await db.Set<Absence>().Where(a => a.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? NotFound("Abwesenheit nicht gefunden") : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<PlanningRange>, ProblemHttpResult>> RangeAsync(
        string? from, string? to, PlanningService planning, CancellationToken cancellationToken)
    {
        if (!TryRange(from, to, out var start, out var end, out var problem))
        {
            return problem!;
        }

        return TypedResults.Ok(await planning.RangeAsync(start, end, null, cancellationToken));
    }

    private static async Task<Results<Ok<PlanningRange>, ProblemHttpResult>> MineAsync(
        string? from, string? to, PlanningService planning, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (!TryRange(from, to, out var start, out var end, out var problem))
        {
            return problem!;
        }

        return TypedResults.Ok(await planning.RangeAsync(start, end, currentUser.UserId, cancellationToken));
    }

    private static async Task<Results<Ok<AssignmentView>, ProblemHttpResult>> SaveAssignmentAsync(
        Guid id, SaveAssignmentRequest request, PlanningService planning, IBauakteAccess access, EstrichOneDbContext db,
        CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(request.BauakteId, cancellationToken))
        {
            return NotFound("Bauakte nicht gefunden", "BAUAKTE_NOT_FOUND");
        }

        var users = (request.UserIds ?? []).Distinct().ToList();
        var resources = (request.ResourceIds ?? []).Distinct().ToList();
        if (await db.Set<User>().CountAsync(u => users.Contains(u.Id), cancellationToken) != users.Count
            || await db.Set<Resource>().CountAsync(r => resources.Contains(r.Id), cancellationToken) != resources.Count
            || (request.CrewId is { } crewId && !await db.Set<Crew>().AnyAsync(c => c.Id == crewId, cancellationToken)))
        {
            return Validation("Unbekannte Kolonne, Mitarbeiter oder Geräte.");
        }

        try
        {
            var assignment = await db.Set<Assignment>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
            if (assignment is null)
            {
                assignment = Assignment.Create(id, request.BauakteId);
                db.Add(assignment);
            }

            assignment.MoveTo(request.BauakteId);
            assignment.Update(request.Day, request.StartTime, request.Activity, request.CrewId, users, resources, request.Notes,
                request.Kind ?? AssignmentKind.Screed, request.EndTime);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }

        var range = await planning.RangeAsync(request.Day, request.Day, null, cancellationToken);
        return TypedResults.Ok(range.Assignments.Single(a => a.Id == id));
    }

    private static async Task<Ok<List<BacklogItem>>> BacklogAsync(
        EstrichOneDbContext db, IBauakteAccess access, IBauakteSummaries summaries, TimeProvider clock, CancellationToken cancellationToken,
        [FromQuery] string? search = null)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Berlin).DateTime);
        var query = (await access.VisibleAsync(db.Set<Bauakte>().AsNoTracking(), cancellationToken))
            .Where(b => b.Status == BauakteStatus.Planned || b.Status == BauakteStatus.InProgress);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(b => EF.Functions.ILike(b.Title, pattern) || (b.Number != null && EF.Functions.ILike(b.Number, pattern))
                || (b.City != null && EF.Functions.ILike(b.City, pattern)) || (b.Street != null && EF.Functions.ILike(b.Street, pattern)));
        }

        var bauakten = await query.OrderBy(b => b.PlannedStart ?? DateOnly.MaxValue).ThenByDescending(b => b.UpdatedAt).Take(300).ToListAsync(cancellationToken);
        var ids = bauakten.Select(b => b.Id).ToList();
        var upcoming = await db.Set<Assignment>().AsNoTracking().Where(a => ids.Contains(a.BauakteId) && a.Day >= today)
            .GroupBy(a => a.BauakteId).Select(g => new { BauakteId = g.Key, Count = g.Count(), Next = g.Min(a => a.Day) })
            .ToDictionaryAsync(g => g.BauakteId, cancellationToken);
        var details = await summaries.GetAsync(ids, cancellationToken);

        return TypedResults.Ok(bauakten.Select(b =>
        {
            var plan = upcoming.GetValueOrDefault(b.Id);
            var summary = details.GetValueOrDefault(b.Id);
            return new BacklogItem(b.Id, b.Number, b.Title, b.Status, b.Street, b.PostalCode, b.City, b.PlannedStart, b.PlannedEnd,
                summary?.CustomerName, summary?.TotalAreaSquareMeters ?? 0, summary?.ScreedTypes ?? [], plan?.Count ?? 0, plan?.Next);
        }).ToList());
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAssignmentAsync(Guid id, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var deleted = await db.Set<Assignment>().Where(a => a.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? NotFound("Einsatz nicht gefunden") : TypedResults.NoContent();
    }

    private static bool TryRange(string? from, string? to, out DateOnly start, out DateOnly end, out ProblemHttpResult? problem)
    {
        problem = null;
        end = default;
        if (!PlanningService.TryParseDay(from, out start) || !PlanningService.TryParseDay(to, out end) || end < start || end.DayNumber - start.DayNumber > 62)
        {
            problem = TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Ungültiger Zeitraum",
                detail: "from und to im Format JJJJ-MM-TT, höchstens 62 Tage.", extensions: new Dictionary<string, object?> { ["code"] = "INVALID_RANGE" });
            return false;
        }

        return true;
    }

    private static ProblemHttpResult NotFound(string title, string code = "NOT_FOUND") =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });

    private static ProblemHttpResult Validation(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Ungültige Angaben", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
}
