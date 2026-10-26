using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Documentation.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Mail.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Planning;

public sealed record HrStateOption(string Key, string Name);

public sealed record HrSettingsResponse(
    string State, string StateName, IReadOnlyList<HrStateOption> States, int MaxCrewAbsencePercent, bool RemindBirthdays, bool RemindAnniversaries);

public sealed record SaveHrSettingsRequest([property: Required, StringLength(2, MinimumLength = 2)] string State, int MaxCrewAbsencePercent = 50,
    bool RemindBirthdays = true, bool RemindAnniversaries = true);

/// <param name="Status">approved (Abwesenheit) oder pending (offener Antrag).</param>
public sealed record VacationPlanEntry(DateOnly From, DateOnly To, AbsenceKind Kind, string Status, Guid? AbsenceId, Guid? RequestId, string? Note);

public sealed record VacationPlanRow(
    Guid? EmployeeId, Guid UserId, string Name, IReadOnlyList<CrewRef> Crews, IReadOnlyList<VacationPlanEntry> Entries, decimal? VacationRemaining);

public sealed record VacationPlan(DateOnly From, DateOnly To, string State, IReadOnlyList<Holiday> Holidays, IReadOnlyList<VacationPlanRow> Rows);

public sealed record AbsenceRequestView(
    Guid Id, Guid UserId, string UserName, DateOnly From, DateOnly To, AbsenceKind Kind, string? Note, AbsenceRequestStatus Status, decimal WorkingDays,
    Guid RequestedByUserId, string? RequestedByName, DateTimeOffset RequestedAt, Guid? DecidedByUserId, string? DecidedByName, DateTimeOffset? DecidedAt,
    string? DecisionComment, IReadOnlyList<string> Warnings, decimal? RemainingAfter, bool CanDecide, bool CanCancel, long Version);

/// <param name="UserId">Für wen (leer = für mich); für andere nur mit „Urlaubsanträge genehmigen“.</param>
public sealed record SaveAbsenceRequestRequest(Guid? UserId, DateOnly From, DateOnly To, AbsenceKind Kind = AbsenceKind.Vacation, [property: MaxLength(500)] string? Note = null);

public sealed record DecideAbsenceRequest(bool Approve, [property: MaxLength(1000)] string? Comment);

/// <param name="CaseId">Krankheitsfall (ID der Erstmeldung); Folgebescheinigungen haben dieselbe CaseId.</param>
public sealed record SickNoteView(Guid Id, Guid UserId, string UserName, DateOnly From, DateOnly To, int WorkingDays, string? Note, Guid? CertificateFileId,
    Guid? CaseId = null, bool IsFollowUp = false, DateTimeOffset? RecordedAt = null);

/// <param name="CertificateFileId">AU-Bescheinigung: vorher ohne Bauakte hochgeladene eigene Datei (POST /files).</param>
/// <param name="FollowUpOf">Folgebescheinigung zu dieser Krankmeldung (eigener Eintrag im selben Fall).</param>
public sealed record SaveSickNoteRequest(Guid UserId, DateOnly From, DateOnly To, [property: MaxLength(500)] string? Note, Guid? CertificateFileId,
    Guid? FollowUpOf = null);

/// <param name="Kind">qualification, probation, birthday, anniversary, document</param>
/// <param name="Severity">error (abgelaufen), warning (bald), info</param>
public sealed record HrReminder(string Kind, Guid EmployeeId, string EmployeeName, DateOnly Date, string Text, string Severity);

public sealed record MyVacation(Guid? EmployeeId, VacationSummary? Vacation, IReadOnlyList<AbsenceRequestView> Requests, IReadOnlyList<Holiday> Holidays);

/// <summary>Kennzahlen für die Startseite: „Warten auf Genehmigung“, eigene offene Anträge, fällige Fristen.</summary>
public sealed record HrSummary(int PendingApprovals, int MyPending, int RemindersDue, decimal? MyRemaining);

/// <summary>
/// Personalverwaltung (C7, nur Basis): Urlaubsplan mit Feiertagen je Bundesland, Urlaubsanträge mit Genehmigung und
/// Konflikthinweisen, Krankmeldungen mit AU-Bescheinigung (nur Personalverwaltung), Fristen-Erinnerungen, PDF.
/// Keine Lohnabrechnung, keine Zeiterfassung.
/// </summary>
internal static class HrEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var hr = api.MapGroup("/hr").WithTags("Personal");
        hr.MapGet("/settings", GetSettingsAsync).WithName("GetHrSettings").RequirePermission(Permissions.HrRead);
        hr.MapPut("/settings", SaveSettingsAsync).WithName("SaveHrSettings").ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.HrManage);
        hr.MapGet("/holidays", HolidaysAsync).WithName("ListHolidays").WithSummary("Gesetzliche Feiertage eines Jahres im eingestellten Bundesland")
            .RequirePermission(Permissions.PlanningRead);
        hr.MapGet("/vacation-plan", PlanAsync).WithName("GetVacationPlan")
            .WithSummary("Urlaubsplan: Zeilen = Mitarbeiter, Abwesenheiten und offene Anträge von–bis (höchstens 370 Tage), Feiertage")
            .ProducesProblem(StatusCodes.Status400BadRequest).RequirePermission(Permissions.HrRead);
        hr.MapGet("/vacation-plan.pdf", HrPrint.CreateAsync).WithName("VacationPlanPdf")
            .WithSummary("Urlaubsplan als PDF (year, optional month; ohne Monat zwölf Seiten)")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").RequirePermission(Permissions.HrRead);

        hr.MapGet("/me", MineAsync).WithName("GetMyVacation").WithSummary("Eigener Resturlaub, eigene Anträge, Feiertage des Jahres")
            .RequirePermission(Permissions.AbsenceRequest);
        hr.MapGet("/summary", SummaryAsync).WithName("GetHrSummary").WithSummary("Startseite: wartende Anträge, eigene offene Anträge, fällige Fristen")
            .RequirePermission(Permissions.AbsenceRequest);
        hr.MapGet("/absence-requests", ListRequestsAsync).WithName("ListAbsenceRequests")
            .WithSummary("Anträge (scope = mine | pending | all); ohne Genehmigungsrecht nur die eigenen")
            .RequirePermission(Permissions.AbsenceRequest);
        hr.MapPut("/absence-requests/{id:guid}", SaveRequestAsync).WithName("SaveAbsenceRequest")
            .WithSummary("Urlaub beantragen (idempotent über die ID); Antwort mit Konflikthinweisen und Resturlaub danach")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.AbsenceRequest);
        hr.MapPost("/absence-requests/{id:guid}/decision", DecideAsync).WithName("DecideAbsenceRequest")
            .WithSummary("Genehmigen (wird Abwesenheit in Planung und Urlaubsplan) oder ablehnen, mit Kommentar")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.AbsenceApprove);
        hr.MapPost("/absence-requests/{id:guid}/cancel", CancelAsync).WithName("CancelAbsenceRequest")
            .WithSummary("Antrag zurückziehen (genehmigter Urlaub wird wieder freigegeben)")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.AbsenceRequest);

        hr.MapGet("/sick-notes", SickNotesAsync).WithName("ListSickNotes").WithSummary("Krankmeldungen eines Jahres (nur Personalverwaltung)")
            .RequirePermission(Permissions.HrManage);
        hr.MapPut("/sick-notes/{id:guid}", SaveSickNoteAsync).WithName("SaveSickNote").WithSummary("Krankmeldung mit optionaler AU-Bescheinigung erfassen")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HrManage);
        hr.MapDelete("/sick-notes/{id:guid}", DeleteSickNoteAsync).WithName("DeleteSickNote").ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.HrManage);
        hr.MapGet("/reminders", RemindersAsync).WithName("ListHrReminders")
            .WithSummary("Fristen: ablaufende Qualifikationen/Führerscheine/Unterweisungen, Probezeitende, Geburtstage, Jubiläen (days = Vorlauf)")
            .RequirePermission(Permissions.HrRead);
    }

    private static HrSettingsResponse ToResponse(HrSettings s) => new(s.State, GermanHolidays.States[s.State],
        GermanHolidays.States.Select(x => new HrStateOption(x.Key, x.Value)).OrderBy(x => x.Name, StringComparer.CurrentCulture).ToList(),
        s.MaxCrewAbsencePercent, s.RemindBirthdays, s.RemindAnniversaries);

    private static async Task<Ok<HrSettingsResponse>> GetSettingsAsync(HrService hr, CancellationToken cancellationToken) =>
        TypedResults.Ok(ToResponse(await hr.SettingsAsync(cancellationToken)));

    private static async Task<Results<Ok<HrSettingsResponse>, ProblemHttpResult>> SaveSettingsAsync(
        SaveHrSettingsRequest request, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var settings = await db.Set<HrSettings>().SingleOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new HrSettings();
            db.Add(settings);
        }

        try
        {
            settings.Update(request.State.ToUpperInvariant(), request.MaxCrewAbsencePercent, request.RemindBirthdays, request.RemindAnniversaries);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(settings));
    }

    private static async Task<Ok<List<Holiday>>> HolidaysAsync(HrService hr, CancellationToken cancellationToken, [FromQuery] int? year = null) =>
        TypedResults.Ok(GermanHolidays.For(year is >= 2000 and <= 2100 ? year.Value : hr.Today.Year, (await hr.SettingsAsync(cancellationToken)).State));

    internal static async Task<VacationPlan> BuildPlanAsync(DateOnly from, DateOnly to, EstrichOneDbContext db, HrService hr, CancellationToken cancellationToken)
    {
        var settings = await hr.SettingsAsync(cancellationToken);
        var sick = await hr.MaySeeSicknessAsync(cancellationToken);
        var holidays = Enumerable.Range(from.Year, to.Year - from.Year + 1).SelectMany(y => GermanHolidays.For(y, settings.State))
            .Where(h => h.Date >= from && h.Date <= to).ToList();
        var today = hr.Today;
        var employees = await db.Set<Employee>().AsNoTracking().Where(e => e.UserId != null && (e.ExitDate == null || e.ExitDate >= from)
            && (e.EntryDate == null || e.EntryDate <= to)).ToListAsync(cancellationToken);
        var byUser = employees.ToDictionary(e => e.UserId!.Value);
        var users = await db.Set<User>().AsNoTracking().Where(u => u.IsActive || byUser.Keys.Contains(u.Id)).OrderBy(u => u.DisplayName).ToListAsync(cancellationToken);
        var ids = users.Select(u => u.Id).ToList();
        var absences = await db.Set<Absence>().AsNoTracking().Where(a => ids.Contains(a.UserId) && a.From <= to && a.To >= from).ToListAsync(cancellationToken);
        var pending = await db.Set<AbsenceRequest>().AsNoTracking()
            .Where(r => ids.Contains(r.UserId) && r.Status == AbsenceRequestStatus.Pending && r.From <= to && r.To >= from).ToListAsync(cancellationToken);
        var yearAbsences = await db.Set<Absence>().AsNoTracking()
            .Where(a => byUser.Keys.Contains(a.UserId) && a.To >= new DateOnly(today.Year, 1, 1) && a.From <= new DateOnly(today.Year, 12, 31)).ToListAsync(cancellationToken);
        var yearHolidays = GermanHolidays.Dates(new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31), settings.State);
        var crews = await db.Set<Crew>().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);

        var rows = users.Select(u =>
        {
            var e = byUser.GetValueOrDefault(u.Id);
            var entries = absences.Where(a => a.UserId == u.Id)
                .Select(a => new VacationPlanEntry(a.From, a.To, HrService.Mask(a.Kind, sick), "approved", a.Id, a.RequestId, a.Kind == AbsenceKind.Sick && !sick ? null : a.Note))
                .Concat(pending.Where(r => r.UserId == u.Id).Select(r => new VacationPlanEntry(r.From, r.To, r.Kind, "pending", null, r.Id, r.Note)))
                .OrderBy(x => x.From).ToList();
            return new VacationPlanRow(e?.Id, u.Id, e?.DisplayName is { Length: > 0 } n ? n : u.DisplayName,
                crews.Where(c => c.MemberUserIds.Contains(u.Id)).Select(c => new CrewRef(c.Id, c.Name, c.Color)).ToList(), entries,
                e is null ? null : HrService.Vacation(e, yearAbsences.Where(a => a.UserId == u.Id), today, yearHolidays, false).Remaining);
        }).OrderBy(r => r.Crews.Count > 0 ? r.Crews[0].Name : "\uffff", StringComparer.CurrentCulture).ThenBy(r => r.Name, StringComparer.CurrentCulture).ToList();
        return new VacationPlan(from, to, settings.State, holidays, rows);
    }

    private static async Task<Results<Ok<VacationPlan>, ProblemHttpResult>> PlanAsync(
        EstrichOneDbContext db, HrService hr, CancellationToken cancellationToken, [FromQuery] string? from = null, [FromQuery] string? to = null)
    {
        if (!PlanningService.TryParseDay(from, out var start) || !PlanningService.TryParseDay(to, out var end) || end < start || end.DayNumber - start.DayNumber > 370)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Ungültiger Zeitraum",
                detail: "from und to im Format JJJJ-MM-TT, höchstens 370 Tage.", extensions: new Dictionary<string, object?> { ["code"] = "INVALID_RANGE" });
        }

        return TypedResults.Ok(await BuildPlanAsync(start, end, db, hr, cancellationToken));
    }

    // ---- Anträge ---------------------------------------------------------------------------------------------------

    private static async Task<List<AbsenceRequestView>> ViewsAsync(
        List<AbsenceRequest> requests, EstrichOneDbContext db, HrService hr, IUserPermissions permissions, ICurrentUser currentUser,
        bool withWarnings, CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
        {
            return [];
        }

        var mayApprove = await permissions.HasAsync(Permissions.AbsenceApprove, cancellationToken);
        var hrManage = await permissions.HasAsync(Permissions.HrManage, cancellationToken);
        var settings = await hr.SettingsAsync(cancellationToken);
        var userIds = requests.SelectMany(r => new[] { r.UserId, r.RequestedByUserId, r.DecidedByUserId ?? Guid.Empty }).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var employees = await db.Set<Employee>().AsNoTracking().Where(e => e.UserId != null && userIds.Contains(e.UserId.Value))
            .ToDictionaryAsync(e => e.UserId!.Value, cancellationToken);
        var minFrom = requests.Min(r => r.From);
        var maxTo = requests.Max(r => r.To);
        var holidays = GermanHolidays.Dates(new DateOnly(minFrom.Year, 1, 1), new DateOnly(maxTo.Year, 12, 31), settings.State);
        var result = new List<AbsenceRequestView>();
        foreach (var r in requests)
        {
            var employee = employees.GetValueOrDefault(r.UserId);
            var days = employee?.WorkingDays(r.From, r.To, holidays) ?? Weekdays(r.From, r.To, holidays);
            var warnings = withWarnings && r.Status == AbsenceRequestStatus.Pending ? await WarningsAsync(r, db, settings, cancellationToken) : [];
            decimal? remaining = null;
            if (employee is not null && r.Kind == AbsenceKind.Vacation)
            {
                var year = await db.Set<Absence>().AsNoTracking().Where(a => a.UserId == r.UserId && a.To >= new DateOnly(hr.Today.Year, 1, 1)
                    && a.From <= new DateOnly(hr.Today.Year, 12, 31)).ToListAsync(cancellationToken);
                var summary = HrService.Vacation(employee, year, hr.Today, holidays, false);
                remaining = r.Status == AbsenceRequestStatus.Pending && r.From.Year == hr.Today.Year ? summary.Remaining - days : summary.Remaining;
                if (remaining < 0 && r.Status == AbsenceRequestStatus.Pending)
                {
                    warnings = [.. warnings, $"Der Resturlaub reicht nicht ({remaining:0.#} Tage nach Genehmigung)."];
                }
            }

            var own = r.UserId == currentUser.UserId;
            result.Add(new AbsenceRequestView(r.Id, r.UserId, employee?.DisplayName is { Length: > 0 } n ? n : names.GetValueOrDefault(r.UserId) ?? "Unbekannt",
                r.From, r.To, r.Kind, r.Note, r.Status, days, r.RequestedByUserId, names.GetValueOrDefault(r.RequestedByUserId), r.RequestedAt, r.DecidedByUserId,
                r.DecidedByUserId is { } d ? names.GetValueOrDefault(d) : null, r.DecidedAt, r.DecisionComment, warnings, remaining,
                mayApprove && r.Status == AbsenceRequestStatus.Pending && (!own || hrManage),
                (own || mayApprove) && r.Status is AbsenceRequestStatus.Pending or AbsenceRequestStatus.Approved, r.Version));
        }

        return result;
    }

    private static int Weekdays(DateOnly from, DateOnly to, HashSet<DateOnly> holidays)
    {
        var n = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && !holidays.Contains(d))
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>Überschneidungen: eigene Abwesenheiten, zu viele aus derselben Kolonne gleichzeitig, eingeplante Einsätze.</summary>
    private static async Task<List<string>> WarningsAsync(AbsenceRequest r, EstrichOneDbContext db, HrSettings settings, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        if (await db.Set<Absence>().AnyAsync(a => a.UserId == r.UserId && a.From <= r.To && a.To >= r.From, cancellationToken))
        {
            warnings.Add("Im Zeitraum ist bereits eine Abwesenheit eingetragen.");
        }

        var crews = await db.Set<Crew>().AsNoTracking().Where(c => c.IsActive && c.MemberUserIds.Contains(r.UserId)).ToListAsync(cancellationToken);
        foreach (var crew in crews)
        {
            var members = crew.MemberUserIds;
            var others = members.Where(m => m != r.UserId).ToList();
            var absent = await db.Set<Absence>().AsNoTracking().Where(a => others.Contains(a.UserId) && a.From <= r.To && a.To >= r.From).ToListAsync(cancellationToken);
            var requested = await db.Set<AbsenceRequest>().AsNoTracking().Where(x => x.Id != r.Id && others.Contains(x.UserId)
                && x.Status == AbsenceRequestStatus.Pending && x.From <= r.To && x.To >= r.From).ToListAsync(cancellationToken);
            var worst = 0;
            var worstDay = r.From;
            for (var d = r.From; d <= r.To; d = d.AddDays(1))
            {
                var count = 1 + others.Count(m => absent.Any(a => a.UserId == m && a.Covers(d)) || requested.Any(x => x.UserId == m && x.From <= d && d <= x.To));
                if (count > worst)
                {
                    worst = count;
                    worstDay = d;
                }
            }

            if (members.Count > 0 && worst > 1 && worst * 100 > members.Count * settings.MaxCrewAbsencePercent)
            {
                warnings.Add(string.Create(CultureInfo.GetCultureInfo("de-DE"),
                    $"{crew.Name}: {worst} von {members.Count} wären gleichzeitig abwesend (z. B. am {worstDay:dd.MM.})."));
            }
        }

        var assignments = await db.Set<Assignment>().AsNoTracking().Where(a => a.Day >= r.From && a.Day <= r.To).ToListAsync(cancellationToken);
        var crewMembers = await db.Set<Crew>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => (IReadOnlyCollection<Guid>)c.MemberUserIds, cancellationToken);
        var planned = assignments.Count(a => a.PeopleWith(a.CrewId is { } id && crewMembers.TryGetValue(id, out var m) ? m : []).Contains(r.UserId));
        if (planned > 0)
        {
            warnings.Add($"Im Zeitraum {(planned == 1 ? "ist ein Einsatz" : $"sind {planned} Einsätze")} eingeplant.");
        }

        return warnings;
    }

    private static async Task<Ok<List<AbsenceRequestView>>> ListRequestsAsync(
        EstrichOneDbContext db, HrService hr, IUserPermissions permissions, ICurrentUser currentUser, CancellationToken cancellationToken,
        [FromQuery] string? scope = null, [FromQuery] int? year = null)
    {
        var mayApprove = await permissions.HasAsync(Permissions.AbsenceApprove, cancellationToken);
        var query = db.Set<AbsenceRequest>().AsNoTracking();
        var userId = currentUser.UserId;
        var mode = scope?.ToLowerInvariant();
        if (!mayApprove || mode == "mine")
        {
            query = query.Where(r => r.UserId == userId || r.RequestedByUserId == userId);
        }

        if (mode == "pending")
        {
            query = query.Where(r => r.Status == AbsenceRequestStatus.Pending);
        }

        if (year is >= 2000 and <= 2100)
        {
            query = query.Where(r => r.From <= new DateOnly(year.Value, 12, 31) && r.To >= new DateOnly(year.Value, 1, 1));
        }

        var requests = await query.OrderBy(r => r.Status != AbsenceRequestStatus.Pending).ThenBy(r => r.From).Take(500).ToListAsync(cancellationToken);
        return TypedResults.Ok(await ViewsAsync(requests, db, hr, permissions, currentUser, mode == "pending", cancellationToken));
    }

    private static async Task<Results<Ok<AbsenceRequestView>, ProblemHttpResult>> SaveRequestAsync(
        Guid id, SaveAbsenceRequestRequest request, EstrichOneDbContext db, HrService hr, IUserPermissions permissions, ICurrentUser currentUser,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var userId = request.UserId ?? currentUser.UserId;
        if (userId != currentUser.UserId && !await permissions.HasAsync(Permissions.AbsenceApprove, cancellationToken))
        {
            return Problem(StatusCodes.Status403Forbidden, "Keine Berechtigung", "Für andere beantragt das Büro (Recht „Urlaubsanträge genehmigen“).", "PERMISSION_DENIED");
        }

        if (!await db.Set<User>().AnyAsync(u => u.Id == userId, cancellationToken))
        {
            return Validation("Unbekannter Mitarbeiter.");
        }

        var existing = await db.Set<AbsenceRequest>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (existing is not null)
        {
            // Wiederholung (z. B. nach Verbindungsabbruch): gleicher Antrag, keine Änderung.
            if (existing.UserId != userId || existing.From != request.From || existing.To != request.To)
            {
                return Problem(StatusCodes.Status409Conflict, "Konflikt", "Unter dieser ID gibt es bereits einen anderen Antrag.", "ID_CONFLICT");
            }
        }
        else
        {
            if (await db.Set<AbsenceRequest>().AnyAsync(r => r.UserId == userId && r.Status == AbsenceRequestStatus.Pending && r.From <= request.To && r.To >= request.From,
                cancellationToken))
            {
                return Problem(StatusCodes.Status409Conflict, "Überschneidung", "Für diesen Zeitraum gibt es bereits einen offenen Antrag.", "REQUEST_OVERLAP");
            }

            try
            {
                existing = AbsenceRequest.Create(id, userId, request.From, request.To, request.Kind, request.Note, currentUser.UserId, clock.GetUtcNow());
            }
            catch (ArgumentException exception)
            {
                return Validation(exception.Message);
            }

            db.Add(existing);
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok((await ViewsAsync([existing], db, hr, permissions, currentUser, true, cancellationToken))[0]);
    }

    private static async Task<Results<Ok<AbsenceRequestView>, ProblemHttpResult>> DecideAsync(
        Guid id, DecideAbsenceRequest request, EstrichOneDbContext db, HrService hr, IUserPermissions permissions, ICurrentUser currentUser,
        IMailService mail, IOptions<EstrichOneOptions> options, TimeProvider clock, CancellationToken cancellationToken)
    {
        var item = await db.Set<AbsenceRequest>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (item is null)
        {
            return Problem(StatusCodes.Status404NotFound, "Antrag nicht gefunden", null, "NOT_FOUND");
        }

        if (item.UserId == currentUser.UserId && !await permissions.HasAsync(Permissions.HrManage, cancellationToken))
        {
            return Problem(StatusCodes.Status403Forbidden, "Keine Berechtigung", "Über eigene Anträge entscheidet jemand anderes.", "PERMISSION_DENIED");
        }

        if (item.Status != AbsenceRequestStatus.Pending)
        {
            return Problem(StatusCodes.Status409Conflict, "Bereits entschieden", "Über diesen Antrag wurde bereits entschieden.", "ALREADY_DECIDED");
        }

        Guid? absenceId = null;
        if (request.Approve)
        {
            var absence = Absence.Create(Guid.CreateVersion7(), item.UserId, item.From, item.To, item.Kind, item.Note, requestId: item.Id);
            db.Add(absence);
            absenceId = absence.Id;
        }

        item.Decide(request.Approve, request.Comment, currentUser.UserId, clock.GetUtcNow(), absenceId);
        await db.SaveChangesAsync(cancellationToken);

        // Benachrichtigung an den Mitarbeiter (nur wenn ein E-Mail-Konto eingerichtet ist).
        var user = await db.Set<User>().AsNoTracking().SingleAsync(u => u.Id == item.UserId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            var link = options.Value.PublicUrl is { } url ? new Uri(url, "/personal/urlaub").ToString() : string.Empty;
            var range = $"{item.From.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE"))} – {item.To.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-DE"))}";
            await mail.QueueAsync(new MailRequest(user.Email,
                $"Urlaubsantrag {(request.Approve ? "genehmigt" : "abgelehnt")}: {range}",
                $"Hallo {user.DisplayName},\n\nIhr Antrag für {range} wurde {(request.Approve ? "genehmigt" : "abgelehnt")}."
                + (string.IsNullOrWhiteSpace(request.Comment) ? string.Empty : $"\n\nKommentar: {request.Comment}")
                + (link.Length > 0 ? $"\n\nÜbersicht: {link}" : string.Empty) + "\n",
                "hr.absence_decided", $"absence_request:{item.Id}"), cancellationToken);
        }

        return TypedResults.Ok((await ViewsAsync([item], db, hr, permissions, currentUser, false, cancellationToken))[0]);
    }

    private static async Task<Results<Ok<AbsenceRequestView>, ProblemHttpResult>> CancelAsync(
        Guid id, EstrichOneDbContext db, HrService hr, IUserPermissions permissions, ICurrentUser currentUser, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var item = await db.Set<AbsenceRequest>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        var mayApprove = await permissions.HasAsync(Permissions.AbsenceApprove, cancellationToken);
        if (item is null || (item.UserId != currentUser.UserId && item.RequestedByUserId != currentUser.UserId && !mayApprove))
        {
            return Problem(StatusCodes.Status404NotFound, "Antrag nicht gefunden", null, "NOT_FOUND");
        }

        if (item.Status == AbsenceRequestStatus.Approved && item.From <= hr.Today && !mayApprove)
        {
            return Problem(StatusCodes.Status409Conflict, "Urlaub hat begonnen", "Begonnener Urlaub wird über das Büro geändert.", "ALREADY_STARTED");
        }

        try
        {
            if (item.AbsenceId is { } absenceId)
            {
                await db.Set<Absence>().Where(a => a.Id == absenceId).ExecuteDeleteAsync(cancellationToken);
            }

            item.Cancel(currentUser.UserId, clock.GetUtcNow());
        }
        catch (InvalidOperationException exception)
        {
            return Problem(StatusCodes.Status409Conflict, "Nicht möglich", exception.Message, "INVALID_STATE");
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await ViewsAsync([item], db, hr, permissions, currentUser, false, cancellationToken))[0]);
    }

    private static async Task<Ok<MyVacation>> MineAsync(
        EstrichOneDbContext db, HrService hr, IUserPermissions permissions, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var today = hr.Today;
        var settings = await hr.SettingsAsync(cancellationToken);
        var holidays = GermanHolidays.For(today.Year, settings.State);
        var employee = await db.Set<Employee>().AsNoTracking().SingleOrDefaultAsync(e => e.UserId == currentUser.UserId, cancellationToken);
        var requests = await db.Set<AbsenceRequest>().AsNoTracking().Where(r => r.UserId == currentUser.UserId && r.To >= new DateOnly(today.Year - 1, 1, 1))
            .OrderByDescending(r => r.From).ToListAsync(cancellationToken);
        VacationSummary? summary = null;
        if (employee is not null)
        {
            var dates = holidays.Select(h => h.Date).ToHashSet();
            var absences = await db.Set<Absence>().AsNoTracking().Where(a => a.UserId == currentUser.UserId && a.To >= new DateOnly(today.Year, 1, 1)).ToListAsync(cancellationToken);
            var requested = requests.Where(r => r.Status == AbsenceRequestStatus.Pending && r.Kind == AbsenceKind.Vacation).Sum(r => employee.WorkingDays(r.From, r.To, dates));
            summary = HrService.Vacation(employee, absences, today, dates, true, requested);
        }

        return TypedResults.Ok(new MyVacation(employee?.Id, summary, await ViewsAsync(requests, db, hr, permissions, currentUser, false, cancellationToken), holidays));
    }

    private static async Task<Ok<HrSummary>> SummaryAsync(
        EstrichOneDbContext db, HrService hr, IUserPermissions permissions, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var pending = await permissions.HasAsync(Permissions.AbsenceApprove, cancellationToken)
            ? await db.Set<AbsenceRequest>().CountAsync(r => r.Status == AbsenceRequestStatus.Pending && r.UserId != userId, cancellationToken)
            : 0;
        var mine = await db.Set<AbsenceRequest>().CountAsync(r => r.Status == AbsenceRequestStatus.Pending && r.UserId == userId, cancellationToken);
        var reminders = await permissions.HasAsync(Permissions.HrRead, cancellationToken)
            ? (await RemindersCoreAsync(db, hr, permissions, 30, cancellationToken)).Count(r => r.Severity != "info")
            : 0;
        decimal? remaining = null;
        if (await db.Set<Employee>().AsNoTracking().SingleOrDefaultAsync(e => e.UserId == userId, cancellationToken) is { } employee)
        {
            var today = hr.Today;
            var holidays = await hr.HolidaysAsync(new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31), cancellationToken);
            var absences = await db.Set<Absence>().AsNoTracking().Where(a => a.UserId == userId && a.To >= new DateOnly(today.Year, 1, 1)).ToListAsync(cancellationToken);
            remaining = HrService.Vacation(employee, absences, today, holidays, false).Remaining;
        }

        return TypedResults.Ok(new HrSummary(pending, mine, reminders, remaining));
    }

    // ---- Krankmeldungen ------------------------------------------------------------------------------------------

    private static async Task<Ok<List<SickNoteView>>> SickNotesAsync(EstrichOneDbContext db, HrService hr, CancellationToken cancellationToken, [FromQuery] int? year = null)
    {
        var y = year is >= 2000 and <= 2100 ? year.Value : hr.Today.Year;
        var from = new DateOnly(y, 1, 1);
        var to = new DateOnly(y, 12, 31);
        var holidays = await hr.HolidaysAsync(from, to.AddDays(60), cancellationToken);
        var items = await db.Set<Absence>().AsNoTracking().Where(a => a.Kind == AbsenceKind.Sick && a.From <= to && a.To >= from)
            .OrderByDescending(a => a.From).ToListAsync(cancellationToken);
        var ids = items.Select(a => a.UserId).Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var employees = await db.Set<Employee>().AsNoTracking().Where(e => e.UserId != null && ids.Contains(e.UserId.Value)).ToDictionaryAsync(e => e.UserId!.Value, cancellationToken);
        return TypedResults.Ok(items.Select(a => new SickNoteView(a.Id, a.UserId, users.GetValueOrDefault(a.UserId) ?? "Unbekannt", a.From, a.To,
            employees.GetValueOrDefault(a.UserId)?.WorkingDays(a.From, a.To, holidays) ?? Weekdays(a.From, a.To, holidays), a.Note, a.CertificateFileId,
            a.CaseId ?? a.Id, a.CaseId is { } c && c != a.Id, a.RecordedAt)).ToList());
    }

    private static async Task<Results<Ok<SickNoteView>, ProblemHttpResult>> SaveSickNoteAsync(
        Guid id, SaveSickNoteRequest request, EstrichOneDbContext db, IStoredFiles files, ICurrentUser currentUser, HrService hr, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null)
        {
            return Validation("Unbekannter Mitarbeiter.");
        }

        var existing = await db.Set<Absence>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (existing is not null && existing.Kind != AbsenceKind.Sick)
        {
            return Validation("Diese Abwesenheit ist keine Krankmeldung.");
        }

        if (request.CertificateFileId is { } fileId && fileId != existing?.CertificateFileId)
        {
            var file = (await files.FindAsync([fileId], cancellationToken)).GetValueOrDefault(fileId);
            if (file is null || !file.IsStored || file.BauakteId is not null || file.UploadedByUserId != currentUser.UserId)
            {
                return Validation("Die AU-Bescheinigung wurde nicht (vollständig) hochgeladen.");
            }
        }

        Guid? caseId = existing?.CaseId;
        if (request.FollowUpOf is { } parentId && existing is null)
        {
            var parent = await db.Set<Absence>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == parentId && a.Kind == AbsenceKind.Sick, cancellationToken);
            if (parent is null || parent.UserId != request.UserId)
            {
                return Validation("Die Erstmeldung zu dieser Folgebescheinigung wurde nicht gefunden.");
            }

            if (request.From < parent.From)
            {
                return Validation("Die Folgebescheinigung beginnt vor der Erstmeldung.");
            }

            caseId = parent.CaseId ?? parent.Id;
        }

        try
        {
            var absence = Absence.Create(id, request.UserId, request.From, request.To, AbsenceKind.Sick, request.Note, request.CertificateFileId,
                caseId: caseId, recordedAt: existing?.RecordedAt ?? clock.GetUtcNow());
            if (existing is not null)
            {
                db.Remove(existing);
            }

            db.Add(absence);
            await db.SaveChangesAsync(cancellationToken);
            var holidays = await hr.HolidaysAsync(absence.From, absence.To, cancellationToken);
            var employee = await db.Set<Employee>().AsNoTracking().SingleOrDefaultAsync(e => e.UserId == request.UserId, cancellationToken);
            return TypedResults.Ok(new SickNoteView(absence.Id, absence.UserId, user.DisplayName, absence.From, absence.To,
                employee?.WorkingDays(absence.From, absence.To, holidays) ?? Weekdays(absence.From, absence.To, holidays), absence.Note, absence.CertificateFileId,
                absence.CaseId, absence.CaseId is { } c && c != absence.Id, absence.RecordedAt));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteSickNoteAsync(Guid id, EstrichOneDbContext db, CancellationToken cancellationToken) =>
        await db.Set<Absence>().Where(a => a.Id == id && a.Kind == AbsenceKind.Sick).ExecuteDeleteAsync(cancellationToken) == 0
            ? Problem(StatusCodes.Status404NotFound, "Krankmeldung nicht gefunden", null, "NOT_FOUND")
            : TypedResults.NoContent();

    // ---- Fristen -------------------------------------------------------------------------------------------------

    private static async Task<Ok<List<HrReminder>>> RemindersAsync(
        EstrichOneDbContext db, HrService hr, IUserPermissions permissions, CancellationToken cancellationToken, [FromQuery] int days = 60) =>
        TypedResults.Ok(await RemindersCoreAsync(db, hr, permissions, Math.Clamp(days, 1, 365), cancellationToken));

    internal static async Task<List<HrReminder>> RemindersCoreAsync(EstrichOneDbContext db, HrService hr, IUserPermissions permissions, int days,
        CancellationToken cancellationToken)
    {
        var today = hr.Today;
        var limit = today.AddDays(days);
        var settings = await hr.SettingsAsync(cancellationToken);
        var german = CultureInfo.GetCultureInfo("de-DE");
        var employees = await db.Set<Employee>().AsNoTracking().Where(e => e.ExitDate == null || e.ExitDate >= today).ToListAsync(cancellationToken);
        var ids = employees.Select(e => e.Id).ToList();
        var byId = employees.ToDictionary(e => e.Id);
        var list = new List<HrReminder>();
        var kindText = new Dictionary<QualificationKind, string>
        {
            [QualificationKind.DriverLicense] = "Führerschein", [QualificationKind.Certificate] = "Nachweis", [QualificationKind.Training] = "Unterweisung",
            [QualificationKind.Medical] = "Vorsorge", [QualificationKind.Other] = "Nachweis",
        };
        foreach (var q in await db.Set<EmployeeQualification>().AsNoTracking().Where(q => ids.Contains(q.EmployeeId) && q.ExpiresOn != null && q.ExpiresOn <= limit)
            .ToListAsync(cancellationToken))
        {
            var expired = q.ExpiresOn < today;
            list.Add(new HrReminder("qualification", q.EmployeeId, byId[q.EmployeeId].DisplayName, q.ExpiresOn!.Value,
                $"{kindText[q.Kind]} {q.Name} {(expired ? "abgelaufen seit" : "läuft ab am")} {q.ExpiresOn.Value.ToString("dd.MM.yyyy", german)}", expired ? "error" : "warning"));
        }

        if (await permissions.HasAsync(Permissions.HrManage, cancellationToken))
        {
            foreach (var d in await db.Set<EmployeeDocument>().AsNoTracking().Where(d => ids.Contains(d.EmployeeId) && d.ValidUntil != null && d.ValidUntil <= limit)
                .ToListAsync(cancellationToken))
            {
                var expired = d.ValidUntil < today;
                list.Add(new HrReminder("document", d.EmployeeId, byId[d.EmployeeId].DisplayName, d.ValidUntil!.Value,
                    $"Dokument „{d.Title}“ {(expired ? "abgelaufen seit" : "gültig bis")} {d.ValidUntil.Value.ToString("dd.MM.yyyy", german)}", expired ? "error" : "warning"));
            }
        }

        foreach (var e in employees)
        {
            if (e.ProbationEnd is { } probation && probation >= today.AddDays(-7) && probation <= limit)
            {
                list.Add(new HrReminder("probation", e.Id, e.DisplayName, probation, $"Probezeit endet am {probation.ToString("dd.MM.yyyy", german)}", "warning"));
            }

            if (settings.RemindBirthdays && e.BirthDate is { } birth && Next(birth, today) is var birthday && birthday <= limit)
            {
                list.Add(new HrReminder("birthday", e.Id, e.DisplayName, birthday,
                    $"Geburtstag am {birthday.ToString("dd.MM.", german)} ({birthday.Year - birth.Year} Jahre)", "info"));
            }

            if (settings.RemindAnniversaries && e.EntryDate is { } entry && Next(entry, today) is var anniversary && anniversary <= limit
                && (anniversary.Year - entry.Year) is var years && years > 0 && (years == 1 || years % 5 == 0))
            {
                list.Add(new HrReminder("anniversary", e.Id, e.DisplayName, anniversary,
                    $"{years} {(years == 1 ? "Jahr" : "Jahre")} im Betrieb am {anniversary.ToString("dd.MM.yyyy", german)}", "info"));
            }
        }

        return list.OrderBy(r => r.Date).ThenBy(r => r.EmployeeName, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>Nächster Jahrestag ab heute (29. Februar → 28. Februar in Nicht-Schaltjahren).</summary>
    private static DateOnly Next(DateOnly date, DateOnly today)
    {
        DateOnly In(int year) => new(year, date.Month, Math.Min(date.Day, DateTime.DaysInMonth(year, date.Month)));
        var candidate = In(today.Year);
        return candidate < today ? In(today.Year + 1) : candidate;
    }

    private static ProblemHttpResult Problem(int status, string title, string? detail, string code) =>
        TypedResults.Problem(statusCode: status, title: title, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    private static ProblemHttpResult Validation(string detail) => Problem(StatusCodes.Status422UnprocessableEntity, "Ungültige Angaben", detail, "VALIDATION_FAILED");
}
