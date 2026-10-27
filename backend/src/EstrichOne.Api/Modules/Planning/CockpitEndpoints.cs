using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

public enum PlanOperationKind
{
    /// <summary>Einsatz anlegen oder vollständig ersetzen (<see cref="PlanOperation.Assignment"/>).</summary>
    Upsert,

    /// <summary>Tag, Kolonne, Uhrzeit oder Reihenfolge ändern.</summary>
    Move,
    Delete,
}

/// <param name="Id">Einsatz-ID (bei Upsert neu vom Client erzeugt).</param>
/// <param name="Day">Move: neuer Tag (leer = unverändert).</param>
/// <param name="ChangeCrew">Move: Kolonne auf <paramref name="CrewId"/> setzen (leer = ohne Kolonne).</param>
/// <param name="SetTime">Move: Uhrzeit auf <paramref name="StartTime"/>/<paramref name="EndTime"/> setzen (sonst unverändert).</param>
/// <param name="Sequence">Move: Position in der Fahrtreihenfolge (0 = nach Uhrzeit).</param>
public sealed record PlanOperation(
    PlanOperationKind Op, Guid Id, SaveAssignmentRequest? Assignment = null, DateOnly? Day = null, bool ChangeCrew = false, Guid? CrewId = null,
    bool SetTime = false, TimeOnly? StartTime = null, TimeOnly? EndTime = null, int? Sequence = null);

/// <param name="DryRun">Nur prüfen (Konfliktvorschau beim Ziehen): nichts wird gespeichert.</param>
public sealed record PlanBatchRequest([property: Required, MaxLength(200)] IReadOnlyList<PlanOperation> Operations, bool DryRun = false);

/// <param name="Assignments">Betroffene Einsätze nach der Änderung, mit Konflikthinweisen.</param>
/// <param name="Warnings">Alle Hinweise der betroffenen Einsätze (für eine Meldung).</param>
public sealed record PlanBatchResult(IReadOnlyList<AssignmentView> Assignments, IReadOnlyList<Guid> DeletedIds, IReadOnlyList<string> Warnings, bool DryRun);

public sealed record PlanCheckResult(IReadOnlyList<string> Warnings);

public sealed record SaveRosterRequest(
    [property: MaxLength(50)] IReadOnlyList<Guid>? MemberUserIds, [property: MaxLength(20)] IReadOnlyList<Guid>? ResourceIds,
    [property: MaxLength(500)] string? Note);

/// <summary>Tagesbesetzung tauschen: Mitarbeiter bzw. Fahrzeug raus/rein. Ist der neue bei einer anderen Kolonne eingeteilt, wechselt er
/// von dort; mit <paramref name="Exchange"/> geht der bisherige im Gegenzug dorthin.</summary>
public sealed record SwapRosterRequest(Guid? FromUserId = null, Guid? ToUserId = null, Guid? FromResourceId = null, Guid? ToResourceId = null, bool Exchange = true);

/// <param name="StartTimes">Optional je Einsatz eine neue Beginnzeit (gleiche Länge wie <paramref name="AssignmentIds"/>, leer = unverändert).</param>
public sealed record OrderStopsRequest([property: Required, MaxLength(50)] IReadOnlyList<Guid> AssignmentIds, IReadOnlyList<TimeOnly?>? StartTimes = null);

public sealed record SwapCrewsRequest(Guid CrewA, Guid CrewB);

public sealed record ReleasePlanRequest(DateOnly From, DateOnly To, [property: MaxLength(500)] string? Note);

/// <summary>
/// Disponenten-Cockpit (W6) und Schnelldisposition vom Handy (I3): dieselben Endpunkte für Web und Apps.
/// Lesen mit planning.read, Ändern mit planning.manage. Beschreibung: docs/planung-api.md.
/// </summary>
internal static class CockpitEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var planning = api.MapGroup("/planning").WithTags("Planung");
        planning.MapGet("/cockpit", BoardAsync).WithName("GetPlanningCockpit")
            .WithSummary("Disponenten-Cockpit: Kolonnen × Tage mit Tagesbesetzung, Fahrzeugen, Auslastung, Konflikten und Freigabestand (höchstens 62 Tage)")
            .ProducesProblem(StatusCodes.Status400BadRequest).RequirePermission(Permissions.PlanningRead);
        planning.MapGet("/day/{day}", DayAsync).WithName("GetDayPlan")
            .WithSummary("Wer fährt wohin je Kolonne: Besetzung, Fahrzeuge, Baustellen in Reihenfolge mit Uhrzeit, Ansprechpartner, Freigabe (day = JJJJ-MM-TT, heute, morgen)")
            .ProducesProblem(StatusCodes.Status400BadRequest).RequirePermission(Permissions.PlanningRead);
        planning.MapPost("/batch", BatchAsync).WithName("ApplyPlanBatch")
            .WithSummary("Mehrere Einsätze auf einmal anlegen, verschieben, umsortieren oder löschen (alles oder nichts); dryRun = nur Konflikte prüfen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.PlanningManage);
        planning.MapPost("/check", CheckAsync).WithName("CheckAssignment")
            .WithSummary("Konfliktprüfung ohne Speichern (Doppelbelegung, Abwesenheit, Gerät belegt, Termin); id = vorhandener Einsatz beim Ändern")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.PlanningManage);
        planning.MapPost("/assignments/{id:guid}/next-free-day", NextFreeDayAsync).WithName("MoveAssignmentToNextFreeDay")
            .WithSummary("Einsatz auf den nächsten freien Arbeitstag seiner Kolonne verschieben")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.PlanningManage);
        planning.MapPut("/crews/{crewId:guid}/days/{day}/roster", SaveRosterAsync).WithName("SaveCrewRoster")
            .WithSummary("Tagesbesetzung einer Kolonne setzen (Mitglieder, Fahrzeuge/Geräte, Notiz)")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.PlanningManage);
        planning.MapDelete("/crews/{crewId:guid}/days/{day}/roster", ResetRosterAsync).WithName("ResetCrewRoster")
            .WithSummary("Tagesbesetzung zurücksetzen (wieder Stammbesetzung)")
            .ProducesProblem(StatusCodes.Status400BadRequest).RequirePermission(Permissions.PlanningManage);
        planning.MapPost("/crews/{crewId:guid}/days/{day}/swap", SwapRosterAsync).WithName("SwapCrewRoster")
            .WithSummary("Mitarbeiter oder Fahrzeug der Kolonne für den Tag tauschen; Antwort ist der Tagesplan")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.PlanningManage);
        planning.MapPut("/crews/{crewId:guid}/days/{day}/order", OrderAsync).WithName("OrderCrewStops")
            .WithSummary("Fahrtreihenfolge (und optional Uhrzeiten) der Baustellen einer Kolonne am Tag; Antwort ist der Tagesplan")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.PlanningManage);
        planning.MapPost("/days/{day}/swap-crews", SwapCrewsAsync).WithName("SwapCrews")
            .WithSummary("Alle Einsätze zweier Kolonnen an einem Tag tauschen; Antwort ist der Tagesplan")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.PlanningManage);
        planning.MapPost("/releases", ReleaseAsync).WithName("ReleasePlan")
            .WithSummary("Plan für Tage freigeben (Mitarbeiter sehen ihn als verbindlich, spätere Änderungen werden markiert)")
            .ProducesProblem(StatusCodes.Status400BadRequest).RequirePermission(Permissions.PlanningManage);
        planning.MapDelete("/releases/{day}", WithdrawAsync).WithName("WithdrawPlanRelease")
            .WithSummary("Freigabe eines Tages zurücknehmen")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.PlanningManage);
    }

    private static ProblemHttpResult Problem(int status, string title, string? detail = null, string code = "VALIDATION_FAILED") =>
        TypedResults.Problem(statusCode: status, title: title, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    private static ProblemHttpResult Validation(string detail) => Problem(StatusCodes.Status422UnprocessableEntity, "Ungültige Angaben", detail);

    private static ProblemHttpResult InvalidDay() => Problem(StatusCodes.Status400BadRequest, "Ungültiger Tag", "Tag im Format JJJJ-MM-TT oder „heute“/„morgen“.", "INVALID_DAY");

    private static async Task<Results<Ok<CockpitBoard>, ProblemHttpResult>> BoardAsync(
        string? from, string? to, CockpitService cockpit, CancellationToken cancellationToken)
    {
        if (!PlanningService.TryParseDay(from, out var start) || !PlanningService.TryParseDay(to, out var end) || end < start || end.DayNumber - start.DayNumber > 62)
        {
            return Problem(StatusCodes.Status400BadRequest, "Ungültiger Zeitraum", "from und to im Format JJJJ-MM-TT, höchstens 62 Tage.", "INVALID_RANGE");
        }

        return TypedResults.Ok(await cockpit.BoardAsync(start, end, cancellationToken));
    }

    private static async Task<Results<Ok<DayPlan>, ProblemHttpResult>> DayAsync(string day, CockpitService cockpit, TimeProvider clock, CancellationToken cancellationToken) =>
        DispatchEndpoints.TryDay(day, clock, out var date) ? TypedResults.Ok(await cockpit.DayAsync(date, cancellationToken)) : InvalidDay();

    /// <summary>Prüft Bauakte, Kolonne, Mitarbeiter und Geräte eines Einsatzes; null = in Ordnung.</summary>
    private static async Task<ProblemHttpResult?> ValidateAsync(SaveAssignmentRequest request, EstrichOneDbContext db, IBauakteAccess access, CancellationToken ct)
    {
        if (!await access.CanReadAsync(request.BauakteId, ct))
        {
            return Problem(StatusCodes.Status404NotFound, "Bauakte nicht gefunden", null, "BAUAKTE_NOT_FOUND");
        }

        var status = await db.Set<Bauakte>().Where(b => b.Id == request.BauakteId).Select(b => (BauakteStatus?)b.Status).SingleOrDefaultAsync(ct);
        if (status is null)
        {
            return Problem(StatusCodes.Status404NotFound, "Bauakte nicht gefunden", null, "BAUAKTE_NOT_FOUND");
        }

        if (status == BauakteStatus.Cancelled)
        {
            return Validation("Die Bauakte ist storniert und kann nicht eingeplant werden.");
        }

        var users = (request.UserIds ?? []).Distinct().ToList();
        var resources = (request.ResourceIds ?? []).Distinct().ToList();
        if (await db.Set<User>().CountAsync(u => users.Contains(u.Id), ct) != users.Count
            || await db.Set<Resource>().CountAsync(r => resources.Contains(r.Id), ct) != resources.Count
            || (request.CrewId is { } crewId && !await db.Set<Crew>().AnyAsync(c => c.Id == crewId, ct)))
        {
            return Validation("Unbekannte Kolonne, Mitarbeiter oder Geräte.");
        }

        return null;
    }

    private static async Task<ProblemHttpResult?> ApplyAsync(PlanOperation op, EstrichOneDbContext db, IBauakteAccess access, HashSet<DateOnly> days,
        List<Guid> deleted, CancellationToken ct)
    {
        var assignment = await db.Set<Assignment>().SingleOrDefaultAsync(a => a.Id == op.Id, ct);
        if (assignment is not null)
        {
            if (!await access.CanReadAsync(assignment.BauakteId, ct))
            {
                return Problem(StatusCodes.Status404NotFound, "Einsatz nicht gefunden", null, "NOT_FOUND");
            }

            days.Add(assignment.Day);
        }

        switch (op.Op)
        {
            case PlanOperationKind.Delete:
                if (assignment is not null)
                {
                    db.Remove(assignment);
                    deleted.Add(op.Id);
                }

                return null;
            case PlanOperationKind.Upsert:
                if (op.Assignment is not { } request)
                {
                    return Validation("Upsert braucht die Angaben des Einsatzes (assignment).");
                }

                if (await ValidateAsync(request, db, access, ct) is { } problem)
                {
                    return problem;
                }

                if (assignment is null)
                {
                    assignment = Assignment.Create(op.Id, request.BauakteId);
                    db.Add(assignment);
                }

                assignment.MoveTo(request.BauakteId);
                assignment.Update(request.Day, request.StartTime, request.Activity, request.CrewId, (request.UserIds ?? []).Distinct().ToList(),
                    (request.ResourceIds ?? []).Distinct().ToList(), request.Notes, request.Kind ?? AssignmentKind.Screed, request.EndTime, request.ExcludedUserIds);
                if (op.Sequence is { } sequence)
                {
                    assignment.SetSequence(sequence);
                }

                days.Add(assignment.Day);
                return null;
            default:
                if (assignment is null)
                {
                    return Problem(StatusCodes.Status404NotFound, "Einsatz nicht gefunden", null, "NOT_FOUND");
                }

                if (op.ChangeCrew && op.CrewId != assignment.CrewId)
                {
                    if (op.CrewId is { } target && !await db.Set<Crew>().AnyAsync(c => c.Id == target, ct))
                    {
                        return Validation("Unbekannte Kolonne.");
                    }

                    // Ausnahmen gelten nur für die bisherige Kolonne.
                    assignment.Update(assignment.Day, assignment.StartTime, assignment.Activity, op.CrewId, assignment.UserIds, assignment.ResourceIds,
                        assignment.Notes, assignment.Kind, assignment.EndTime);
                }

                assignment.Reschedule(op.Day ?? assignment.Day, op.SetTime ? op.StartTime : assignment.StartTime, op.SetTime ? op.EndTime : assignment.EndTime);
                if (op.Sequence is { } order)
                {
                    assignment.SetSequence(order);
                }

                days.Add(assignment.Day);
                return null;
        }
    }

    private static async Task<Results<Ok<PlanBatchResult>, ProblemHttpResult>> BatchAsync(
        PlanBatchRequest request, EstrichOneDbContext db, IBauakteAccess access, PlanningService planning, CancellationToken cancellationToken)
    {
        if (request.Operations is not { Count: > 0 and <= 200 })
        {
            return Validation("Zwischen 1 und 200 Änderungen auf einmal.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var days = new HashSet<DateOnly>();
        var deleted = new List<Guid>();
        var touched = new List<Guid>();
        try
        {
            foreach (var op in request.Operations)
            {
                if (await ApplyAsync(op, db, access, days, deleted, cancellationToken) is { } problem)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return problem;
                }

                touched.Add(op.Id);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch (ArgumentException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Validation(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Validation(exception.Message);
        }

        var views = new List<AssignmentView>();
        foreach (var day in days.Order())
        {
            views.AddRange((await planning.RangeAsync(day, day, null, cancellationToken)).Assignments.Where(a => touched.Contains(a.Id)));
        }

        if (request.DryRun)
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        else
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return TypedResults.Ok(new PlanBatchResult(views, deleted.Distinct().ToList(), views.SelectMany(v => v.Warnings).Distinct().ToList(), request.DryRun));
    }

    private static async Task<Results<Ok<PlanCheckResult>, ProblemHttpResult>> CheckAsync(
        SaveAssignmentRequest request, EstrichOneDbContext db, IBauakteAccess access, PlanningService planning, CancellationToken cancellationToken,
        [FromQuery] Guid? id = null)
    {
        var result = await BatchAsync(new PlanBatchRequest([new PlanOperation(PlanOperationKind.Upsert, id ?? Guid.CreateVersion7(), request)], true),
            db, access, planning, cancellationToken);
        return result.Result is Ok<PlanBatchResult> ok ? TypedResults.Ok(new PlanCheckResult(ok.Value!.Warnings)) : (ProblemHttpResult)result.Result;
    }

    private static async Task<Results<Ok<AssignmentView>, ProblemHttpResult>> NextFreeDayAsync(
        Guid id, EstrichOneDbContext db, IBauakteAccess access, CockpitService cockpit, PlanningService planning, CancellationToken cancellationToken)
    {
        var assignment = await db.Set<Assignment>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (assignment is null || !await access.CanReadAsync(assignment.BauakteId, cancellationToken))
        {
            return Problem(StatusCodes.Status404NotFound, "Einsatz nicht gefunden", null, "NOT_FOUND");
        }

        if (assignment.CrewId is not { } crewId)
        {
            return Validation("Der nächste freie Tag wird für Kolonnen ermittelt – dieser Einsatz hat keine Kolonne.");
        }

        if (await cockpit.NextFreeDayAsync(crewId, assignment.Day, cancellationToken) is not { } day)
        {
            return Validation("In den nächsten 90 Tagen ist kein Tag der Kolonne frei.");
        }

        assignment.Reschedule(day, assignment.StartTime, assignment.EndTime);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await planning.RangeAsync(day, day, null, cancellationToken)).Assignments.Single(a => a.Id == id));
    }

    private static async Task<ProblemHttpResult?> CheckPeopleAsync(EstrichOneDbContext db, List<Guid> users, List<Guid> resources,
        CancellationToken ct) =>
        await db.Set<User>().CountAsync(u => users.Contains(u.Id), ct) != users.Count
            || await db.Set<Resource>().CountAsync(r => resources.Contains(r.Id), ct) != resources.Count
            ? Validation("Unbekannte Mitarbeiter oder Geräte.")
            : null;

    private static async Task<Results<Ok<CockpitCell>, ProblemHttpResult>> SaveRosterAsync(
        Guid crewId, string day, SaveRosterRequest request, EstrichOneDbContext db, CockpitService cockpit, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!DispatchEndpoints.TryDay(day, clock, out var date))
        {
            return InvalidDay();
        }

        var crew = await db.Set<Crew>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == crewId, cancellationToken);
        if (crew is null)
        {
            return Problem(StatusCodes.Status404NotFound, "Kolonne nicht gefunden", null, "NOT_FOUND");
        }

        var users = (request.MemberUserIds ?? []).Distinct().ToList();
        var resources = (request.ResourceIds ?? []).Distinct().ToList();
        if (await CheckPeopleAsync(db, users, resources, cancellationToken) is { } problem)
        {
            return problem;
        }

        try
        {
            var roster = await cockpit.RosterAsync(crew, date, cancellationToken);
            roster.Update(users, resources, request.Note);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }

        var board = await cockpit.BoardAsync(date, date, cancellationToken);
        return TypedResults.Ok(board.Crews.Single(c => c.Id == crewId).Cells.Single());
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ResetRosterAsync(
        Guid crewId, string day, EstrichOneDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!DispatchEndpoints.TryDay(day, clock, out var date))
        {
            return InvalidDay();
        }

        await db.Set<CrewDay>().Where(d => d.CrewId == crewId && d.Day == date).ExecuteDeleteAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<DayPlan>, ProblemHttpResult>> SwapRosterAsync(
        Guid crewId, string day, SwapRosterRequest request, EstrichOneDbContext db, CockpitService cockpit, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!DispatchEndpoints.TryDay(day, clock, out var date))
        {
            return InvalidDay();
        }

        var crews = await db.Set<Crew>().AsNoTracking().Where(c => c.IsActive).ToListAsync(cancellationToken);
        if (crews.FirstOrDefault(c => c.Id == crewId) is not { } crew)
        {
            return Problem(StatusCodes.Status404NotFound, "Kolonne nicht gefunden", null, "NOT_FOUND");
        }

        if (request.FromUserId is null && request.ToUserId is null && request.FromResourceId is null && request.ToResourceId is null)
        {
            return Validation("Bitte angeben, wer oder was getauscht wird.");
        }

        var users = new[] { request.FromUserId, request.ToUserId }.OfType<Guid>().Distinct().ToList();
        var resources = new[] { request.FromResourceId, request.ToResourceId }.OfType<Guid>().Distinct().ToList();
        if (await CheckPeopleAsync(db, users, resources, cancellationToken) is { } problem)
        {
            return problem;
        }

        var roster = await cockpit.RosterAsync(crew, date, cancellationToken);
        var members = roster.MemberUserIds.ToList();
        var vehicles = roster.ResourceIds.ToList();

        // Woher kommt der Neue? Bei einer anderen Kolonne (Tagesbesetzung oder Stamm) wird er dort herausgenommen.
        async Task SwapAcrossAsync(Guid? incoming, Guid? outgoing, bool person)
        {
            if (incoming is not { } add)
            {
                return;
            }

            foreach (var other in crews.Where(c => c.Id != crewId))
            {
                var otherRoster = await db.Set<CrewDay>().SingleOrDefaultAsync(d => d.CrewId == other.Id && d.Day == date, cancellationToken);
                var holds = otherRoster is not null
                    ? (person ? otherRoster.MemberUserIds : otherRoster.ResourceIds).Contains(add)
                    : person ? other.MemberUserIds.Contains(add)
                        : await db.Set<Resource>().AnyAsync(r => r.Id == add && r.CrewId == other.Id && r.IsActive, cancellationToken);
                if (!holds)
                {
                    continue;
                }

                otherRoster ??= await cockpit.RosterAsync(other, date, cancellationToken);
                var list = (person ? otherRoster.MemberUserIds : otherRoster.ResourceIds).Where(x => x != add).ToList();
                if (request.Exchange && outgoing is { } back)
                {
                    list.Add(back);
                }

                otherRoster.Update(person ? list : otherRoster.MemberUserIds, person ? otherRoster.ResourceIds : list, otherRoster.Note);
            }
        }

        await SwapAcrossAsync(request.ToUserId, request.FromUserId, true);
        await SwapAcrossAsync(request.ToResourceId, request.FromResourceId, false);
        members = members.Where(m => m != request.FromUserId).Concat(request.ToUserId is { } u ? [u] : []).Distinct().ToList();
        vehicles = vehicles.Where(v => v != request.FromResourceId).Concat(request.ToResourceId is { } r ? [r] : []).Distinct().ToList();
        roster.Update(members, vehicles, roster.Note);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await cockpit.DayAsync(date, cancellationToken));
    }

    private static async Task<Results<Ok<DayPlan>, ProblemHttpResult>> OrderAsync(
        Guid crewId, string day, OrderStopsRequest request, EstrichOneDbContext db, CockpitService cockpit, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!DispatchEndpoints.TryDay(day, clock, out var date))
        {
            return InvalidDay();
        }

        if (request.StartTimes is { } times && times.Count != request.AssignmentIds.Count)
        {
            return Validation("Für jede Baustelle genau eine Uhrzeit (oder keine) angeben.");
        }

        var jobs = await db.Set<Assignment>().Where(a => a.Day == date && a.CrewId == crewId).ToListAsync(cancellationToken);
        if (request.AssignmentIds.Any(id => jobs.All(j => j.Id != id)))
        {
            return Problem(StatusCodes.Status404NotFound, "Einsatz nicht gefunden", "Nicht alle Einsätze gehören an dem Tag zur Kolonne.", "NOT_FOUND");
        }

        try
        {
            for (var i = 0; i < request.AssignmentIds.Count; i++)
            {
                var job = jobs.Single(j => j.Id == request.AssignmentIds[i]);
                job.SetSequence(i + 1);
                if (request.StartTimes?[i] is { } start)
                {
                    var end = job.EndTime is { } e && job.StartTime is { } s && e > s ? start.Add(e - s) : job.EndTime;
                    job.Reschedule(date, start, end is { } x && x <= start ? null : end);
                }
            }
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await cockpit.DayAsync(date, cancellationToken));
    }

    private static async Task<Results<Ok<DayPlan>, ProblemHttpResult>> SwapCrewsAsync(
        string day, SwapCrewsRequest request, EstrichOneDbContext db, CockpitService cockpit, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!DispatchEndpoints.TryDay(day, clock, out var date))
        {
            return InvalidDay();
        }

        if (request.CrewA == request.CrewB || await db.Set<Crew>().CountAsync(c => c.Id == request.CrewA || c.Id == request.CrewB, cancellationToken) != 2)
        {
            return Problem(StatusCodes.Status404NotFound, "Kolonne nicht gefunden", "Bitte zwei verschiedene Kolonnen wählen.", "NOT_FOUND");
        }

        var jobs = await db.Set<Assignment>().Where(a => a.Day == date && (a.CrewId == request.CrewA || a.CrewId == request.CrewB)).ToListAsync(cancellationToken);
        foreach (var job in jobs)
        {
            job.Update(job.Day, job.StartTime, job.Activity, job.CrewId == request.CrewA ? request.CrewB : request.CrewA, job.UserIds, job.ResourceIds, job.Notes,
                job.Kind, job.EndTime);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await cockpit.DayAsync(date, cancellationToken));
    }

    private static async Task<Results<Ok<IReadOnlyList<PlanReleaseInfo>>, ProblemHttpResult>> ReleaseAsync(
        ReleasePlanRequest request, CockpitService cockpit, PlanningService planning, EstrichOneDbContext db,
        Notifications.Exports.INotifier notifier, CancellationToken cancellationToken)
    {
        if (request.To < request.From || request.To.DayNumber - request.From.DayNumber > 62)
        {
            return Problem(StatusCodes.Status400BadRequest, "Ungültiger Zeitraum", "Höchstens 62 Tage auf einmal freigeben.", "INVALID_RANGE");
        }

        var update = await db.Set<PlanRelease>().AnyAsync(r => r.Day >= request.From && r.Day <= request.To, cancellationToken);
        var released = await cockpit.ReleaseAsync(request.From, request.To, request.Note, cancellationToken);
        await PlanningNotifications.ReleasedAsync(planning, notifier, request.From, request.To, update, cancellationToken);
        return TypedResults.Ok(released);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> WithdrawAsync(string day, EstrichOneDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!DispatchEndpoints.TryDay(day, clock, out var date))
        {
            return InvalidDay();
        }

        var deleted = await db.Set<PlanRelease>().Where(r => r.Day == date).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? Problem(StatusCodes.Status404NotFound, "Keine Freigabe", null, "NOT_FOUND") : TypedResults.NoContent();
    }
}
