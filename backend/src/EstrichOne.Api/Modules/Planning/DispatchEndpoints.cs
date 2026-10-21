using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>Ein Einsatz auf der Baustelle (Tätigkeit mit Uhrzeit und Kolonne).</summary>
public sealed record DispatchJob(
    Guid AssignmentId, AssignmentKind Kind, string Activity, TimeOnly? StartTime, TimeOnly? EndTime, Guid? CrewId, string? CrewName, string? CrewColor,
    string? Notes);

/// <param name="SiteIds">Baustellen (Bauakte-IDs), auf denen die Person an dem Tag eingeteilt ist.</param>
/// <param name="Note">Abwesenheit oder Termin, z. B. „Urlaub“.</param>
public sealed record DispatchPerson(Guid Id, string Name, Guid? CrewId, string? CrewName, string? CrewColor, IReadOnlyList<Guid> SiteIds, string? Note);

public sealed record DispatchVehicle(Guid Id, string Name, ResourceKind Kind, string? Identifier, IReadOnlyList<Guid> SiteIds);

/// <param name="StartTime">Frühester Beginn vor Ort.</param>
/// <param name="NavigationUrl">Routenplanung (OpenStreetMap) zur Baustelle.</param>
public sealed record DispatchSite(
    Guid BauakteId, string? Number, string Title, string? Address, double? Latitude, double? Longitude, string? SiteContactName,
    string? SiteContactPhone, TimeOnly? StartTime, IReadOnlyList<DispatchJob> Jobs, IReadOnlyList<PersonRef> People, IReadOnlyList<ResourceRef> Vehicles,
    IReadOnlyList<string> Warnings, string? NavigationUrl);

/// <summary>Termin ohne Bauvorhaben, der Mitarbeiter an dem Tag bindet (z. B. Schulung, Kundentermin).</summary>
public sealed record DispatchAppointment(Guid EventId, string Title, string? Location, DateTime Start, DateTime End, bool AllDay, IReadOnlyList<PersonRef> People);

/// <summary>
/// „Wer fährt wohin“ für einen Tag: Baustellen mit Team und Fahrzeugen, wer noch frei ist, wer fehlt.
/// <paramref name="MySiteIds"/> sind die Baustellen des angemeldeten Benutzers (für „Mein Einsatz“ in der App).
/// </summary>
public sealed record DispatchBoard(
    DateOnly Day, IReadOnlyList<DispatchSite> Sites, IReadOnlyList<DispatchPerson> People, IReadOnlyList<DispatchVehicle> Vehicles,
    IReadOnlyList<DispatchAppointment> Appointments, IReadOnlyList<Guid> MySiteIds, bool CanPlan);

public enum DispatchTarget
{
    Person,
    Vehicle,
}

/// <param name="ToBauakteId">Ziel-Baustelle; leer = an diesem Tag nirgends einteilen.</param>
/// <param name="FromBauakteId">Nur von dieser Baustelle abziehen; leer = von allen anderen Baustellen des Tages (eine Person, ein Ort).</param>
/// <param name="Keep">Zusätzlich einteilen, ohne von anderen Baustellen abzuziehen.</param>
/// <param name="Activity">Tätigkeit, falls auf der Ziel-Baustelle an dem Tag noch kein Einsatz geplant ist (Standard: „Estrich einbringen“).</param>
public sealed record DispatchMoveRequest(
    DispatchTarget Target, Guid Id, Guid? ToBauakteId, Guid? FromBauakteId = null, bool Keep = false,
    [property: MaxLength(200)] string? Activity = null, TimeOnly? StartTime = null);

/// <summary>
/// „Wer fährt wohin“ (Schnelleinsatzplanung): Mitarbeiter und Fahrzeuge für heute/morgen per Ziehen oder Auswahl auf
/// Baustellen verteilen. Grundlage sind die Einsätze der Planung – die Einteilung ändert sie direkt (keine Doppelpflege).
/// Für Web und iOS gleich: GET liefert die Tafel, POST …/moves verschiebt eine Person oder ein Fahrzeug.
/// </summary>
internal static class DispatchEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/planning/dispatch").WithTags("Planung");
        group.MapGet("/{day}", GetAsync).WithName("GetDispatchBoard")
            .WithSummary("Wer fährt wohin: Baustellen des Tages mit Team und Fahrzeugen, freie und abwesende Mitarbeiter (day = JJJJ-MM-TT, heute, morgen)")
            .ProducesProblem(StatusCodes.Status400BadRequest).RequirePermission(Permissions.PlanningRead);
        group.MapPost("/{day}/moves", MoveAsync).WithName("MoveDispatch")
            .WithSummary("Person oder Fahrzeug auf eine Baustelle einteilen bzw. abziehen; Antwort ist die neue Tafel")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.PlanningManage);
        group.MapGet("/{day}/print.pdf", DispatchPrint.CreateAsync).WithName("DispatchPdf")
            .WithSummary("Wer fährt wohin als PDF (Aushang, Fahrerliste)")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status400BadRequest).RequirePermission(Permissions.PlanningRead);
    }

    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    internal static bool TryDay(string day, TimeProvider clock, out DateOnly result)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Berlin).DateTime);
        switch (day.ToLowerInvariant())
        {
            case "heute" or "today":
                result = today;
                return true;
            case "morgen" or "tomorrow":
                result = today.AddDays(1);
                return true;
            default:
                return PlanningService.TryParseDay(day, out result);
        }
    }

    private static ProblemHttpResult InvalidDay() => TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Ungültiger Tag",
        detail: "Tag im Format JJJJ-MM-TT oder „heute“/„morgen“.", extensions: new Dictionary<string, object?> { ["code"] = "INVALID_DAY" });

    private static async Task<Results<Ok<DispatchBoard>, ProblemHttpResult>> GetAsync(
        string day, DispatchService service, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!TryDay(day, clock, out var date))
        {
            return InvalidDay();
        }

        return TypedResults.Ok(await service.BoardAsync(date, cancellationToken));
    }

    private static async Task<Results<Ok<DispatchBoard>, ProblemHttpResult>> MoveAsync(
        string day, DispatchMoveRequest request, DispatchService service, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!TryDay(day, clock, out var date))
        {
            return InvalidDay();
        }

        var problem = await service.MoveAsync(date, request, cancellationToken);
        return problem ?? (Results<Ok<DispatchBoard>, ProblemHttpResult>)TypedResults.Ok(await service.BoardAsync(date, cancellationToken));
    }
}

public sealed class DispatchService(
    EstrichOneDbContext db, PlanningService planning, IBauakteAccess access, ICurrentUser currentUser, IUserPermissions permissions)
{
    public async Task<DispatchBoard> BoardAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var range = await planning.RangeAsync(day, day, null, cancellationToken);
        // Wie die Planung: Wer Planung sehen darf, sieht alle Baustellen des Tages (Name, Adresse, Ansprechpartner).
        var siteIds = range.Assignments.Select(a => a.BauakteId).Distinct().ToList();
        var visible = await db.Set<Bauakte>().AsNoTracking().Where(b => siteIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, cancellationToken);
        var crews = await db.Set<Crew>().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);
        var users = await db.Set<User>().AsNoTracking().Where(u => u.IsActive).OrderBy(u => u.DisplayName).ToListAsync(cancellationToken);
        var vehicles = await db.Set<Resource>().AsNoTracking().Where(r => r.IsActive).OrderBy(r => r.Kind).ThenBy(r => r.Name).ToListAsync(cancellationToken);

        var sites = range.Assignments.Where(a => visible.ContainsKey(a.BauakteId)).GroupBy(a => a.BauakteId).Select(g =>
        {
            var b = visible[g.Key];
            var jobs = g.OrderBy(a => a.StartTime ?? TimeOnly.MaxValue).ToList();
            var place = string.Join(" ", new[] { b.PostalCode, b.City }.Where(s => !string.IsNullOrEmpty(s)));
            var address = string.Join(", ", new[] { b.Street, place }.Where(s => !string.IsNullOrEmpty(s)));
            var people = jobs.SelectMany(a => a.People).DistinctBy(p => p.Id).OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList();
            var warnings = jobs.SelectMany(a => a.Warnings).Distinct().ToList();
            if (people.Count == 0)
            {
                warnings.Insert(0, "Noch niemand eingeteilt.");
            }

            var navigation = b.Latitude is { } lat && b.Longitude is { } lon
                ? FormattableString.Invariant($"https://www.openstreetmap.org/directions?to={lat:0.######},{lon:0.######}")
                : address.Length > 0 ? $"https://www.openstreetmap.org/search?query={Uri.EscapeDataString(address)}" : null;
            return new DispatchSite(b.Id, b.Number, b.Title, address.Length > 0 ? address : null, b.Latitude, b.Longitude, b.SiteContactName,
                b.SiteContactPhone, jobs.Select(a => a.StartTime).Min(),
                jobs.Select(a => new DispatchJob(a.Id, a.Kind, a.Activity, a.StartTime, a.EndTime, a.CrewId, a.CrewName, a.CrewColor, a.Notes)).ToList(),
                people, jobs.SelectMany(a => a.Resources).DistinctBy(r => r.Id).ToList(), warnings, navigation);
        }).OrderBy(s => s.StartTime ?? TimeOnly.MaxValue).ThenBy(s => s.Title, StringComparer.CurrentCulture).ToList();

        var appointments = (range.Appointments ?? []).Select(o => new DispatchAppointment(o.EventId, o.Title, o.Location, o.Start, o.End, o.AllDay, o.People)).ToList();
        var absenceText = new Dictionary<AbsenceKind, string>
        {
            [AbsenceKind.Vacation] = "Urlaub", [AbsenceKind.Sick] = "krank", [AbsenceKind.Training] = "Schulung", [AbsenceKind.Other] = "abwesend",
        };
        var people = users.Select(u =>
        {
            var crew = crews.FirstOrDefault(c => c.MemberUserIds.Contains(u.Id));
            var absence = range.Absences.FirstOrDefault(a => a.UserId == u.Id);
            var appointment = appointments.FirstOrDefault(a => a.People.Any(p => p.Id == u.Id));
            var note = absence is not null ? absenceText[absence.Kind] : appointment is not null ? $"Termin: {appointment.Title}" : null;
            return new DispatchPerson(u.Id, u.DisplayName, crew?.Id, crew?.Name, crew?.Color,
                sites.Where(s => s.People.Any(p => p.Id == u.Id)).Select(s => s.BauakteId).ToList(), note);
        }).ToList();
        var vehicleList = vehicles.Select(r => new DispatchVehicle(r.Id, r.Name, r.Kind, r.Identifier,
            sites.Where(s => s.Vehicles.Any(v => v.Id == r.Id)).Select(s => s.BauakteId).ToList())).ToList();
        var mine = sites.Where(s => s.People.Any(p => p.Id == currentUser.UserId)).Select(s => s.BauakteId).ToList();
        return new DispatchBoard(day, sites, people, vehicleList, appointments, mine, await permissions.HasAsync(Permissions.PlanningManage, cancellationToken));
    }

    public async Task<ProblemHttpResult?> MoveAsync(DateOnly day, DispatchMoveRequest request, CancellationToken cancellationToken)
    {
        var person = request.Target == DispatchTarget.Person;
        if (person ? !await db.Set<User>().AnyAsync(u => u.Id == request.Id, cancellationToken) : !await db.Set<Resource>().AnyAsync(r => r.Id == request.Id, cancellationToken))
        {
            return Problem(StatusCodes.Status404NotFound, person ? "Mitarbeiter nicht gefunden" : "Fahrzeug nicht gefunden", null, "NOT_FOUND");
        }

        if (request.ToBauakteId is { } target)
        {
            var status = await (await access.VisibleAsync(db.Set<Bauakte>().AsNoTracking(), cancellationToken))
                .Where(b => b.Id == target).Select(b => (BauakteStatus?)b.Status).SingleOrDefaultAsync(cancellationToken);
            if (status is null)
            {
                return Problem(StatusCodes.Status404NotFound, "Bauakte nicht gefunden", null, "BAUAKTE_NOT_FOUND");
            }

            if (status == BauakteStatus.Cancelled)
            {
                return Problem(StatusCodes.Status422UnprocessableEntity, "Ungültige Angaben", "Die Bauakte ist storniert.", "VALIDATION_FAILED");
            }
        }

        var assignments = await db.Set<Assignment>().Where(a => a.Day == day).OrderBy(a => a.StartTime).ThenBy(a => a.CreatedAt).ToListAsync(cancellationToken);
        var crews = await db.Set<Crew>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => (IReadOnlyCollection<Guid>)c.MemberUserIds, cancellationToken);
        IReadOnlyCollection<Guid> Members(Assignment a) => a.CrewId is { } id && crews.TryGetValue(id, out var m) ? m : [];

        if (!request.Keep)
        {
            foreach (var a in assignments.Where(a => a.BauakteId != request.ToBauakteId && (request.FromBauakteId is null || a.BauakteId == request.FromBauakteId)))
            {
                if (person)
                {
                    a.SetPerson(request.Id, false, Members(a));
                }
                else
                {
                    a.SetResource(request.Id, false);
                }
            }
        }

        if (request.ToBauakteId is { } to)
        {
            var job = assignments.FirstOrDefault(a => a.BauakteId == to);
            if (job is null)
            {
                if (!person)
                {
                    return Problem(StatusCodes.Status422UnprocessableEntity, "Noch kein Einsatz",
                        "Auf dieser Baustelle ist an dem Tag noch nichts geplant – bitte zuerst einen Mitarbeiter einteilen.", "NO_ASSIGNMENT");
                }

                job = Assignment.Create(Guid.CreateVersion7(), to);
                try
                {
                    job.Update(day, request.StartTime ?? new TimeOnly(7, 0), string.IsNullOrWhiteSpace(request.Activity) ? "Estrich einbringen" : request.Activity,
                        null, [request.Id], [], null);
                }
                catch (ArgumentException exception)
                {
                    return Problem(StatusCodes.Status422UnprocessableEntity, "Ungültige Angaben", exception.Message, "VALIDATION_FAILED");
                }

                db.Add(job);
            }
            else if (person)
            {
                job.SetPerson(request.Id, true, Members(job));
            }
            else
            {
                job.SetResource(request.Id, true);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    private static ProblemHttpResult Problem(int status, string title, string? detail, string code) =>
        TypedResults.Problem(statusCode: status, title: title, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });
}
