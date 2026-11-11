using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Documentation.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Mail.Exports;
using EstrichOne.Api.Modules.Notifications.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Planning;

public sealed record HrStateOption(string Key, string Name);

/// <param name="CarryOverExpiryMonth">Resturlaub des Vorjahres verfällt nach Tag/Monat (0 = kein Verfall).</param>
/// <param name="AccountingStartYear">Ab diesem Jahr wird der Rest automatisch ins Folgejahr übertragen.</param>
/// <param name="CrewLeadersApprove">Kolonnenführer genehmigen Anträge ihrer Kolonne.</param>
public sealed record HrSettingsResponse(
    string State, string StateName, IReadOnlyList<HrStateOption> States, int MaxCrewAbsencePercent, bool RemindBirthdays, bool RemindAnniversaries,
    int CarryOverExpiryMonth = 3, int CarryOverExpiryDay = 31, bool ProrateEntitlement = true, int AccountingStartYear = 2026, bool CrewLeadersApprove = false);

public sealed record SaveHrSettingsRequest([property: Required, StringLength(2, MinimumLength = 2)] string State, int MaxCrewAbsencePercent = 50,
    bool RemindBirthdays = true, bool RemindAnniversaries = true, int? CarryOverExpiryMonth = null, int? CarryOverExpiryDay = null,
    bool? ProrateEntitlement = null, int? AccountingStartYear = null, bool? CrewLeadersApprove = null);

/// <param name="Status">approved (Abwesenheit) oder pending (offener Antrag).</param>
/// <param name="TypeId">Abwesenheitsart (Krankheitsarten ohne Recht als „Sonstige Abwesenheit“).</param>
public sealed record VacationPlanEntry(DateOnly From, DateOnly To, AbsenceKind Kind, string Status, Guid? AbsenceId, Guid? RequestId, string? Note,
    Guid? TypeId = null, bool HalfDayStart = false, bool HalfDayEnd = false, Guid? ClosureId = null);

public sealed record VacationPlanRow(
    Guid? EmployeeId, Guid UserId, string Name, IReadOnlyList<CrewRef> Crews, IReadOnlyList<VacationPlanEntry> Entries, decimal? VacationRemaining);

/// <param name="Types">Abwesenheitsarten für Legende und Farben.</param>
/// <param name="Closures">Betriebsferien und Brückentage im Zeitraum.</param>
/// <param name="CrewMinimums">Mindestbesetzung je Kolonne (nur Kolonnen mit Angabe).</param>
public sealed record VacationPlan(DateOnly From, DateOnly To, string State, IReadOnlyList<Holiday> Holidays, IReadOnlyList<VacationPlanRow> Rows,
    IReadOnlyList<AbsenceTypeView>? Types = null, IReadOnlyList<ClosureView>? Closures = null, IReadOnlyList<CrewMinimum>? CrewMinimums = null);

public sealed record CrewMinimum(Guid CrewId, string Name, int MinimumStaff, int Members);

/// <param name="CanDecide">Angemeldeter Benutzer darf genehmigen oder ablehnen.</param>
/// <param name="CanCancel">Angemeldeter Benutzer darf zurückziehen bzw. stornieren.</param>
/// <param name="CanChange">Genehmigter, noch nicht begonnener Antrag: Änderungsantrag möglich.</param>
/// <param name="ReplacesRequestId">Änderungsantrag zu diesem genehmigten Antrag.</param>
public sealed record AbsenceRequestView(
    Guid Id, Guid UserId, string UserName, DateOnly From, DateOnly To, AbsenceKind Kind, string? Note, AbsenceRequestStatus Status, decimal WorkingDays,
    Guid RequestedByUserId, string? RequestedByName, DateTimeOffset RequestedAt, Guid? DecidedByUserId, string? DecidedByName, DateTimeOffset? DecidedAt,
    string? DecisionComment, IReadOnlyList<string> Warnings, decimal? RemainingAfter, bool CanDecide, bool CanCancel, long Version,
    Guid? TypeId = null, string? TypeName = null, string? TypeColor = null, bool HalfDayStart = false, bool HalfDayEnd = false,
    Guid? SubstituteUserId = null, string? SubstituteName = null, Guid? ReplacesRequestId = null, bool CanChange = false, bool CountsAsVacation = false);

/// <param name="UserId">Für wen (leer = für mich); für andere nur mit Genehmigungsrecht.</param>
/// <param name="TypeId">Abwesenheitsart (leer = Standardart zu Kind).</param>
/// <param name="HalfDayStart">Erster Tag nur halb.</param>
/// <param name="HalfDayEnd">Letzter Tag nur halb.</param>
/// <param name="SubstituteUserId">Vertretung (optional).</param>
/// <param name="ReplacesRequestId">Änderungsantrag: ersetzt nach Genehmigung diesen genehmigten Antrag.</param>
public sealed record SaveAbsenceRequestRequest(Guid? UserId, DateOnly From, DateOnly To, AbsenceKind Kind = AbsenceKind.Vacation, [property: MaxLength(500)] string? Note = null,
    Guid? TypeId = null, bool HalfDayStart = false, bool HalfDayEnd = false, Guid? SubstituteUserId = null, Guid? ReplacesRequestId = null);

/// <summary>Vorschau vor dem Absenden: Arbeitstage, Hinweise, Rest danach.</summary>
public sealed record AbsencePreview(decimal WorkingDays, IReadOnlyList<string> Warnings, decimal? RemainingAfter, bool RequiresApproval,
    IReadOnlyList<Holiday> Holidays);

public sealed record DecideAbsenceRequest(bool Approve, [property: MaxLength(1000)] string? Comment);

/// <param name="CaseId">Krankheitsfall (ID der Erstmeldung); Folgebescheinigungen haben dieselbe CaseId.</param>
public sealed record SickNoteView(Guid Id, Guid UserId, string UserName, DateOnly From, DateOnly To, int WorkingDays, string? Note, Guid? CertificateFileId,
    Guid? CaseId = null, bool IsFollowUp = false, DateTimeOffset? RecordedAt = null, Guid? TypeId = null);

/// <param name="CertificateFileId">AU-Bescheinigung: vorher ohne Bauakte hochgeladene eigene Datei (POST /files).</param>
/// <param name="FollowUpOf">Folgebescheinigung zu dieser Krankmeldung (eigener Eintrag im selben Fall).</param>
/// <param name="TypeId">Krankheitsart (z. B. Kind krank); leer = Krankheit. Keine Diagnose erfassen.</param>
public sealed record SaveSickNoteRequest(Guid UserId, DateOnly From, DateOnly To, [property: MaxLength(500)] string? Note, Guid? CertificateFileId,
    Guid? FollowUpOf = null, Guid? TypeId = null);

/// <param name="Kind">qualification, probation, birthday, anniversary, document</param>
/// <param name="Severity">error (abgelaufen), warning (bald), info</param>
public sealed record HrReminder(string Kind, Guid EmployeeId, string EmployeeName, DateOnly Date, string Text, string Severity);

/// <summary>Eigene Übersicht: Konto, Anträge, Abwesenheiten, Feiertage, Betriebsferien, beantragbare Arten.</summary>
public sealed record MyVacation(Guid? EmployeeId, VacationSummary? Vacation, IReadOnlyList<AbsenceRequestView> Requests, IReadOnlyList<Holiday> Holidays,
    int Year = 0, IReadOnlyList<AbsenceTypeView>? Types = null, IReadOnlyList<ClosureView>? Closures = null, IReadOnlyList<MyAbsence>? Absences = null,
    bool CanApprove = false, int PendingApprovals = 0);

/// <summary>Eigene genehmigte Abwesenheit (auch Krankmeldung, die der Mitarbeiter selbst sehen darf).</summary>
public sealed record MyAbsence(Guid Id, DateOnly From, DateOnly To, Guid TypeId, string TypeName, string Color, AbsenceKind Kind, bool HalfDayStart, bool HalfDayEnd,
    decimal WorkingDays, Guid? RequestId);

/// <summary>Kennzahlen für die Startseite: „Warten auf Genehmigung“, eigene offene Anträge, fällige Fristen.</summary>
public sealed record HrSummary(int PendingApprovals, int MyPending, int RemindersDue, decimal? MyRemaining);

/// <summary>
/// Personalverwaltung: Teamkalender mit Feiertagen je Bundesland, Abwesenheitsarten, Anträge mit Genehmigung, Vertretung,
/// Änderungsantrag und Konflikthinweisen, Urlaubskonten, Betriebsferien, Krankmeldungen (nur Personalverwaltung), Fristen, PDF/CSV.
/// Keine Lohnabrechnung, keine Zeiterfassung.
/// </summary>
internal static class HrEndpoints
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static void Map(IEndpointRouteBuilder api)
    {
        var root = api.MapGroup("/hr").WithTags("Personal");
        var hr = root.MapGroup(string.Empty).RequireModule(AppModules.Absence); // Urlaub und Abwesenheit (setzt Personal voraus)
        hr.MapGet("/settings", GetSettingsAsync).WithName("GetHrSettings").RequirePermission(Permissions.HrRead);
        hr.MapPut("/settings", SaveSettingsAsync).WithName("SaveHrSettings").ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.HrManage);
        root.MapGet("/holidays", HolidaysAsync).WithName("ListHolidays").WithSummary("Gesetzliche Feiertage eines Jahres im eingestellten Bundesland")
            .RequirePermission(Permissions.PlanningRead);
        hr.MapGet("/vacation-plan", PlanAsync).WithName("GetVacationPlan")
            .WithSummary("Teamkalender: Zeilen = Mitarbeiter, Abwesenheiten und offene Anträge von–bis (höchstens 370 Tage), Feiertage, Arten, Betriebsferien")
            .ProducesProblem(StatusCodes.Status400BadRequest).RequirePermission(Permissions.HrRead);
        hr.MapGet("/vacation-plan.pdf", HrPrint.CreateAsync).WithName("VacationPlanPdf")
            .WithSummary("Teamkalender als PDF (year, optional month; ohne Monat zwölf Seiten)")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").RequirePermission(Permissions.HrRead);

        hr.MapGet("/me", MineAsync).WithName("GetMyVacation").WithSummary("Eigenes Urlaubskonto, Anträge, Abwesenheiten, Feiertage und beantragbare Arten (year)")
            .RequirePermission(Permissions.AbsenceRequest);
        hr.MapGet("/summary", SummaryAsync).WithName("GetHrSummary").WithSummary("Startseite: wartende Anträge, eigene offene Anträge, fällige Fristen")
            .RequirePermission(Permissions.AbsenceRequest);
        hr.MapGet("/absence-requests", ListRequestsAsync).WithName("ListAbsenceRequests")
            .WithSummary("Anträge (scope = mine | pending | all); ohne Genehmigungsrecht nur die eigenen")
            .RequirePermission(Permissions.AbsenceRequest);
        hr.MapPost("/absence-requests/preview", PreviewAsync).WithName("PreviewAbsenceRequest")
            .WithSummary("Vorschau: Arbeitstage (halbe Tage, Wochenende, Feiertage), Konflikthinweise, Rest danach")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.AbsenceRequest);
        hr.MapPut("/absence-requests/{id:guid}", SaveRequestAsync).WithName("SaveAbsenceRequest")
            .WithSummary("Abwesenheit beantragen oder offenen Antrag ändern (idempotent über die ID); nicht genehmigungspflichtige Arten gelten sofort")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.AbsenceRequest);
        hr.MapPost("/absence-requests/{id:guid}/decision", DecideAsync).WithName("DecideAbsenceRequest")
            .WithSummary("Genehmigen (wird Abwesenheit in Planung und Teamkalender) oder ablehnen, mit Kommentar; Genehmiger: Recht oder Kolonnenführer")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.AbsenceRequest);
        hr.MapPost("/absence-requests/{id:guid}/cancel", CancelAsync).WithName("CancelAbsenceRequest")
            .WithSummary("Antrag zurückziehen bzw. stornieren (genehmigte Abwesenheit wird wieder freigegeben)")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.AbsenceRequest);

        hr.MapGet("/sick-notes", SickNotesAsync).WithName("ListSickNotes").WithSummary("Krankmeldungen eines Jahres (nur Personalverwaltung)")
            .RequirePermission(Permissions.HrManage);
        hr.MapPut("/sick-notes/{id:guid}", SaveSickNoteAsync).WithName("SaveSickNote").WithSummary("Krankmeldung mit optionaler AU-Bescheinigung erfassen")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HrManage);
        hr.MapDelete("/sick-notes/{id:guid}", DeleteSickNoteAsync).WithName("DeleteSickNote").ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.HrManage);
        root.MapGet("/reminders", RemindersAsync).WithName("ListHrReminders")
            .WithSummary("Fristen: ablaufende Qualifikationen/Führerscheine/Unterweisungen, Probezeitende, Geburtstage, Jubiläen (days = Vorlauf)")
            .RequirePermission(Permissions.HrRead).RequireModule(AppModules.Staff);

        AbsenceAdminEndpoints.Map(hr);
    }

    private static HrSettingsResponse ToResponse(HrSettings s) => new(s.State, GermanHolidays.States[s.State],
        GermanHolidays.States.Select(x => new HrStateOption(x.Key, x.Value)).OrderBy(x => x.Name, StringComparer.CurrentCulture).ToList(),
        s.MaxCrewAbsencePercent, s.RemindBirthdays, s.RemindAnniversaries, s.CarryOverExpiryMonth, s.CarryOverExpiryDay, s.ProrateEntitlement,
        s.AccountingStartYear, s.CrewLeadersApprove);

    private static async Task<Ok<HrSettingsResponse>> GetSettingsAsync(HrService hr, CancellationToken cancellationToken) =>
        TypedResults.Ok(ToResponse(await hr.SettingsAsync(cancellationToken)));

    private static async Task<Results<Ok<HrSettingsResponse>, ProblemHttpResult>> SaveSettingsAsync(
        SaveHrSettingsRequest request, EstrichOneDbContext db, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
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
            settings.UpdateAccounts(request.CarryOverExpiryMonth ?? settings.CarryOverExpiryMonth, request.CarryOverExpiryDay ?? settings.CarryOverExpiryDay,
                request.ProrateEntitlement ?? settings.ProrateEntitlement, request.AccountingStartYear ?? settings.AccountingStartYear,
                request.CrewLeadersApprove ?? settings.CrewLeadersApprove);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }

        audit.Record("hr.settings.saved", currentUser.UserId, "hr_settings", settings.Id.ToString(), new Dictionary<string, string?>
        {
            ["state"] = settings.State, ["carryOverExpiry"] = settings.CarryOverExpiryMonth == 0 ? "kein Verfall" : $"{settings.CarryOverExpiryDay:00}.{settings.CarryOverExpiryMonth:00}.",
            ["crewLeadersApprove"] = settings.CrewLeadersApprove ? "ja" : "nein",
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(settings));
    }

    private static async Task<Ok<List<Holiday>>> HolidaysAsync(HrService hr, CancellationToken cancellationToken, [FromQuery] int? year = null) =>
        TypedResults.Ok(GermanHolidays.For(year is >= 2000 and <= 2100 ? year.Value : hr.Today.Year, (await hr.SettingsAsync(cancellationToken)).State));

    internal static string Range(DateOnly from, DateOnly to) =>
        from == to ? from.ToString("dd.MM.yyyy", German) : $"{from.ToString(from.Year == to.Year ? "dd.MM." : "dd.MM.yyyy", German)} – {to.ToString("dd.MM.yyyy", German)}";

    internal static string Days(decimal days) => $"{days.ToString("0.#", German)} {(days == 1 ? "Tag" : "Tage")}";

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
        var accounts = await hr.AccountsAsync(employees, today.Year, false, cancellationToken);
        var crews = await db.Set<Crew>().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);
        var types = await hr.TypesAsync(cancellationToken);

        var rows = users.Select(u =>
        {
            var e = byUser.GetValueOrDefault(u.Id);
            var entries = absences.Where(a => a.UserId == u.Id)
                .Select(a => new VacationPlanEntry(a.From, a.To, HrService.Mask(a.Kind, sick), "approved", a.Id, a.RequestId, a.Kind == AbsenceKind.Sick && !sick ? null : a.Note,
                    HrService.MaskType(HrService.TypeOf(a), a.Kind, sick), a.HalfDayStart, a.HalfDayEnd, a.ClosureId))
                .Concat(pending.Where(r => r.UserId == u.Id).Select(r => new VacationPlanEntry(r.From, r.To, r.Kind, "pending", null, r.Id, r.Note,
                    HrService.TypeOf(r), r.HalfDayStart, r.HalfDayEnd)))
                .OrderBy(x => x.From).ToList();
            return new VacationPlanRow(e?.Id, u.Id, e?.DisplayName is { Length: > 0 } n ? n : u.DisplayName,
                crews.Where(c => c.MemberUserIds.Contains(u.Id)).Select(c => new CrewRef(c.Id, c.Name, c.Color)).ToList(), entries,
                e is null ? null : accounts.GetValueOrDefault(e.Id)?.Remaining);
        }).OrderBy(r => r.Crews.Count > 0 ? r.Crews[0].Name : "￿", StringComparer.CurrentCulture).ThenBy(r => r.Name, StringComparer.CurrentCulture).ToList();
        var closures = await AbsenceAdminEndpoints.ClosuresAsync(db, hr, from, to, cancellationToken);
        return new VacationPlan(from, to, settings.State, holidays, rows, types.Values.OrderBy(t => t.SortOrder).Select(AbsenceAdminEndpoints.ToView).ToList(), closures,
            crews.Where(c => c.MinimumStaff is > 0).Select(c => new CrewMinimum(c.Id, c.Name, c.MinimumStaff!.Value, c.MemberUserIds.Count)).ToList());
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

    /// <summary>Zu prüfender Zeitraum (Antrag oder Vorschau).</summary>
    private sealed record Candidate(Guid Id, Guid UserId, DateOnly From, DateOnly To, Guid? SubstituteUserId, Guid? ReplacesRequestId);

    private static async Task<List<AbsenceRequestView>> ViewsAsync(
        List<AbsenceRequest> requests, EstrichOneDbContext db, HrService hr, ICurrentUser currentUser, bool withWarnings, CancellationToken cancellationToken)
    {
        if (requests.Count == 0)
        {
            return [];
        }

        var settings = await hr.SettingsAsync(cancellationToken);
        var types = await hr.TypesAsync(cancellationToken);
        var userIds = requests.SelectMany(r => new[] { r.UserId, r.RequestedByUserId, r.DecidedByUserId ?? Guid.Empty, r.SubstituteUserId ?? Guid.Empty }).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var employees = await db.Set<Employee>().AsNoTracking().Where(e => e.UserId != null && userIds.Contains(e.UserId.Value))
            .ToDictionaryAsync(e => e.UserId!.Value, cancellationToken);
        var minFrom = requests.Min(r => r.From);
        var maxTo = requests.Max(r => r.To);
        var holidays = GermanHolidays.Dates(new DateOnly(minFrom.Year, 1, 1), new DateOnly(maxTo.Year, 12, 31), settings.State);
        var mayDecide = new Dictionary<Guid, bool>();
        foreach (var id in requests.Select(r => r.UserId).Distinct())
        {
            mayDecide[id] = await hr.MayDecideAsync(id, cancellationToken);
        }

        // Konten je Jahr der Anträge (Rest danach).
        var accounts = new Dictionary<(Guid, int), VacationSummary>();
        foreach (var year in requests.Select(r => r.From.Year).Distinct())
        {
            var people = requests.Where(r => r.From.Year == year).Select(r => employees.GetValueOrDefault(r.UserId)).OfType<Employee>().DistinctBy(e => e.Id).ToList();
            foreach (var (id, summary) in await hr.AccountsAsync(people, year, false, cancellationToken))
            {
                accounts[(id, year)] = summary;
            }
        }

        var result = new List<AbsenceRequestView>();
        foreach (var r in requests)
        {
            var employee = employees.GetValueOrDefault(r.UserId);
            var type = types.GetValueOrDefault(HrService.TypeOf(r));
            var days = VacationCalculator.WorkingDays(employee?.WorkDays ?? VacationCalculator.DefaultWorkDays, r.From, r.To, r.HalfDayStart, r.HalfDayEnd, holidays);
            var warnings = withWarnings && r.Status == AbsenceRequestStatus.Pending
                ? await WarningsAsync(new Candidate(r.Id, r.UserId, r.From, r.To, r.SubstituteUserId, r.ReplacesRequestId), db, hr, cancellationToken)
                : [];
            decimal? remaining = null;
            if (employee is not null && type?.CountsAsVacation == true && accounts.TryGetValue((employee.Id, r.From.Year), out var summary))
            {
                // Das Konto zählt offene Anträge nicht ab; beim Änderungsantrag wird der ersetzte Antrag frei.
                var replaced = r.ReplacesRequestId is { } old && r.Status == AbsenceRequestStatus.Pending
                    ? await ReplacedDaysAsync(old, employee, db, hr, cancellationToken)
                    : 0;
                remaining = r.Status == AbsenceRequestStatus.Pending ? summary.Remaining - days + replaced : summary.Remaining;
                if (remaining < 0 && r.Status == AbsenceRequestStatus.Pending)
                {
                    warnings = [.. warnings, $"Der Resturlaub reicht nicht ({remaining.Value.ToString("0.#", German)} Tage nach Genehmigung)."];
                }
            }

            var own = r.UserId == currentUser.UserId || r.RequestedByUserId == currentUser.UserId;
            var decide = mayDecide[r.UserId];
            var started = r.From <= hr.Today;
            result.Add(new AbsenceRequestView(r.Id, r.UserId, employee?.DisplayName is { Length: > 0 } n ? n : names.GetValueOrDefault(r.UserId) ?? "Unbekannt",
                r.From, r.To, r.Kind, r.Note, r.Status, days, r.RequestedByUserId, names.GetValueOrDefault(r.RequestedByUserId), r.RequestedAt, r.DecidedByUserId,
                r.DecidedByUserId is { } d ? names.GetValueOrDefault(d) : null, r.DecidedAt, r.DecisionComment, warnings, remaining,
                decide && r.Status == AbsenceRequestStatus.Pending,
                (r.Status == AbsenceRequestStatus.Pending && (own || decide)) || (r.Status == AbsenceRequestStatus.Approved && ((own && !started) || decide)),
                r.Version, type?.Id, type?.Name, type?.Color, r.HalfDayStart, r.HalfDayEnd, r.SubstituteUserId,
                r.SubstituteUserId is { } s ? names.GetValueOrDefault(s) : null, r.ReplacesRequestId,
                r.Status == AbsenceRequestStatus.Approved && (own || decide) && (!started || decide) && r.AbsenceId is not null, type?.CountsAsVacation == true));
        }

        return result;
    }

    /// <summary>Urlaubstage des ersetzten (genehmigten) Antrags, die bei Genehmigung des Änderungsantrags frei werden.</summary>
    private static async Task<decimal> ReplacedDaysAsync(Guid requestId, Employee employee, EstrichOneDbContext db, HrService hr, CancellationToken cancellationToken)
    {
        var old = await db.Set<AbsenceRequest>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == requestId && r.Status == AbsenceRequestStatus.Approved, cancellationToken);
        if (old?.AbsenceId is not { } absenceId || (await hr.TypesAsync(cancellationToken)).GetValueOrDefault(HrService.TypeOf(old))?.CountsAsVacation != true)
        {
            return 0;
        }

        var absence = await db.Set<Absence>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == absenceId, cancellationToken);
        if (absence is null)
        {
            return 0;
        }

        var holidays = await hr.HolidaysAsync(absence.From, absence.To, cancellationToken);
        return VacationCalculator.WorkingDays(employee.WorkDays, absence.From, absence.To, absence.HalfDayStart, absence.HalfDayEnd, holidays);
    }

    /// <summary>
    /// Hinweise (nicht blockierend): eigene Abwesenheiten, Betriebsferien, zu viele aus derselben Kolonne gleichzeitig, Mindestbesetzung,
    /// eingeplante Einsätze, Vertretung selbst abwesend.
    /// </summary>
    private static async Task<List<string>> WarningsAsync(Candidate r, EstrichOneDbContext db, HrService hr, CancellationToken cancellationToken)
    {
        var settings = await hr.SettingsAsync(cancellationToken);
        var warnings = new List<string>();
        var replacedAbsence = r.ReplacesRequestId is { } old
            ? await db.Set<AbsenceRequest>().AsNoTracking().Where(x => x.Id == old).Select(x => x.AbsenceId).SingleOrDefaultAsync(cancellationToken)
            : null;
        var ownAbsences = await db.Set<Absence>().AsNoTracking()
            .Where(a => a.UserId == r.UserId && a.From <= r.To && a.To >= r.From && a.Id != replacedAbsence).ToListAsync(cancellationToken);
        if (ownAbsences.Any(a => a.ClosureId is not null))
        {
            warnings.Add("Im Zeitraum liegen Betriebsferien; diese Tage sind bereits eingetragen.");
        }

        if (ownAbsences.Any(a => a.ClosureId is null))
        {
            warnings.Add("Im Zeitraum ist bereits eine Abwesenheit eingetragen.");
        }

        var holidays = await hr.HolidaysAsync(r.From, r.To, cancellationToken);
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
                if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || holidays.Contains(d))
                {
                    continue;
                }

                var count = 1 + others.Count(m => absent.Any(a => a.UserId == m && a.Covers(d)) || requested.Any(x => x.UserId == m && x.From <= d && d <= x.To));
                if (count > worst)
                {
                    worst = count;
                    worstDay = d;
                }
            }

            if (members.Count > 0 && worst > 1 && worst * 100 > members.Count * settings.MaxCrewAbsencePercent)
            {
                warnings.Add(string.Create(German, $"{crew.Name}: {worst} von {members.Count} wären gleichzeitig abwesend (z. B. am {worstDay:dd.MM.})."));
            }

            if (crew.MinimumStaff is { } minimum && worst > 0 && members.Count - worst < minimum)
            {
                warnings.Add(string.Create(German,
                    $"{crew.Name}: am {worstDay:dd.MM.} nur {members.Count - worst} von {members.Count} anwesend (Mindestbesetzung {minimum})."));
            }
        }

        var assignments = await db.Set<Assignment>().AsNoTracking().Where(a => a.Day >= r.From && a.Day <= r.To).ToListAsync(cancellationToken);
        var crewMembers = await db.Set<Crew>().AsNoTracking().ToDictionaryAsync(c => c.Id, c => (IReadOnlyCollection<Guid>)c.MemberUserIds, cancellationToken);
        var planned = assignments.Where(a => a.PeopleWith(a.CrewId is { } id && crewMembers.TryGetValue(id, out var m) ? m : []).Contains(r.UserId))
            .Select(a => a.Day).Distinct().Order().ToList();
        if (planned.Count > 0)
        {
            var count = assignments.Count(a => a.PeopleWith(a.CrewId is { } id && crewMembers.TryGetValue(id, out var m) ? m : []).Contains(r.UserId));
            warnings.Add($"Im Zeitraum {(count == 1 ? "ist ein Einsatz" : $"sind {count} Einsätze")} eingeplant ("
                + string.Join(", ", planned.Take(5).Select(d => d.ToString("dd.MM.", German))) + (planned.Count > 5 ? " …" : string.Empty) + ").");
        }

        if (r.SubstituteUserId is { } substitute
            && await db.Set<Absence>().AnyAsync(a => a.UserId == substitute && a.From <= r.To && a.To >= r.From, cancellationToken))
        {
            warnings.Add("Die Vertretung ist im Zeitraum selbst abwesend.");
        }

        return warnings;
    }

    private static async Task<Ok<List<AbsenceRequestView>>> ListRequestsAsync(
        EstrichOneDbContext db, HrService hr, IUserPermissions permissions, ICurrentUser currentUser, CancellationToken cancellationToken,
        [FromQuery] string? scope = null, [FromQuery] int? year = null)
    {
        var mayApprove = await permissions.HasAsync(Permissions.AbsenceApprove, cancellationToken);
        var led = mayApprove ? [] : await hr.LedUserIdsAsync(cancellationToken);
        var query = db.Set<AbsenceRequest>().AsNoTracking();
        var userId = currentUser.UserId;
        var mode = scope?.ToLowerInvariant();
        if (mode == "mine")
        {
            query = query.Where(r => r.UserId == userId || r.RequestedByUserId == userId);
        }
        else if (!mayApprove)
        {
            // Kolonnenführer (falls eingestellt) sehen zusätzlich Anträge ihrer Kolonne.
            query = query.Where(r => r.UserId == userId || r.RequestedByUserId == userId || led.Contains(r.UserId));
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
        return TypedResults.Ok(await ViewsAsync(requests, db, hr, currentUser, mode == "pending", cancellationToken));
    }

    /// <summary>Gemeinsame Prüfung für Antrag und Vorschau: Benutzer, Art, Recht, Änderungsantrag.</summary>
    private static async Task<(Guid UserId, AbsenceType Type, AbsenceRequest? Replaces, ProblemHttpResult? Problem)> ValidateAsync(
        SaveAbsenceRequestRequest request, EstrichOneDbContext db, HrService hr, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var userId = request.UserId ?? currentUser.UserId;
        var forOther = userId != currentUser.UserId;
        if (forOther && !await hr.MayDecideAsync(userId, cancellationToken))
        {
            return (userId, null!, null, Problem(StatusCodes.Status403Forbidden, "Keine Berechtigung", "Für andere beantragt das Büro (Recht „Urlaubsanträge genehmigen“).", "PERMISSION_DENIED"));
        }

        if (!await db.Set<User>().AnyAsync(u => u.Id == userId, cancellationToken))
        {
            return (userId, null!, null, Validation("Unbekannter Mitarbeiter."));
        }

        var types = await hr.TypesAsync(cancellationToken);
        var type = types.GetValueOrDefault(request.TypeId ?? StandardAbsenceTypes.ForKind(request.Kind));
        if (type is null || !type.IsActive)
        {
            return (userId, null!, null, Validation("Unbekannte oder nicht mehr verwendete Abwesenheitsart."));
        }

        if (type.IsSickness)
        {
            return (userId, type, null, Validation("Krankmeldungen werden nicht beantragt, sondern von der Personalverwaltung erfasst."));
        }

        if (!type.Requestable && !forOther && !await hr.MayDecideAsync(userId, cancellationToken))
        {
            return (userId, type, null, Validation($"{type.Name} trägt das Büro ein."));
        }

        if (request.SubstituteUserId is { } substitute && !await db.Set<User>().AnyAsync(u => u.Id == substitute && u.IsActive, cancellationToken))
        {
            return (userId, type, null, Validation("Unbekannte Vertretung."));
        }

        AbsenceRequest? replaces = null;
        if (request.ReplacesRequestId is { } old)
        {
            replaces = await db.Set<AbsenceRequest>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == old && r.UserId == userId, cancellationToken);
            if (replaces is null || replaces.Status != AbsenceRequestStatus.Approved)
            {
                return (userId, type, null, Validation("Ein Änderungsantrag bezieht sich auf einen genehmigten Antrag."));
            }

            if (replaces.From <= hr.Today && !await hr.MayDecideAsync(userId, cancellationToken))
            {
                return (userId, type, null, Problem(StatusCodes.Status409Conflict, "Abwesenheit hat begonnen", "Begonnene Abwesenheiten ändert das Büro.", "ALREADY_STARTED"));
            }
        }

        if (request.To < request.From)
        {
            return (userId, type, null, Validation("Das Ende liegt vor dem Beginn."));
        }

        if (request.To.DayNumber - request.From.DayNumber > 366)
        {
            return (userId, type, null, Validation("Ein Antrag umfasst höchstens ein Jahr."));
        }

        return (userId, type, replaces, null);
    }

    private static async Task<Results<Ok<AbsencePreview>, ProblemHttpResult>> PreviewAsync(
        SaveAbsenceRequestRequest request, EstrichOneDbContext db, HrService hr, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var (userId, type, replaces, problem) = await ValidateAsync(request, db, hr, currentUser, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var employee = await db.Set<Employee>().AsNoTracking().SingleOrDefaultAsync(e => e.UserId == userId, cancellationToken);
        var settings = await hr.SettingsAsync(cancellationToken);
        var holidays = GermanHolidays.Dates(new DateOnly(request.From.Year, 1, 1), new DateOnly(request.To.Year, 12, 31), settings.State);
        var halfEnd = request.HalfDayEnd && (request.From != request.To || !request.HalfDayStart);
        var days = VacationCalculator.WorkingDays(employee?.WorkDays ?? VacationCalculator.DefaultWorkDays, request.From, request.To, request.HalfDayStart, halfEnd, holidays);
        var warnings = await WarningsAsync(new Candidate(Guid.Empty, userId, request.From, request.To, request.SubstituteUserId, replaces?.Id), db, hr, cancellationToken);
        decimal? remaining = null;
        if (employee is not null && type.CountsAsVacation)
        {
            var summary = await hr.AccountAsync(employee, request.From.Year, false, cancellationToken);
            var freed = replaces is null ? 0 : await ReplacedDaysAsync(replaces.Id, employee, db, hr, cancellationToken);
            remaining = summary.Remaining - summary.Requested - days + freed;
            if (remaining < 0)
            {
                warnings.Add($"Der Resturlaub reicht nicht ({remaining.Value.ToString("0.#", German)} Tage nach Genehmigung, offene Anträge eingerechnet).");
            }
        }

        if (days == 0)
        {
            warnings.Add("Im Zeitraum liegen keine Arbeitstage.");
        }

        return TypedResults.Ok(new AbsencePreview(days, warnings, remaining, type.RequiresApproval,
            GermanHolidays.For(request.From.Year, settings.State).Concat(request.To.Year != request.From.Year ? GermanHolidays.For(request.To.Year, settings.State) : [])
                .Where(h => h.Date >= request.From && h.Date <= request.To).ToList()));
    }

    private static async Task<Results<Ok<AbsenceRequestView>, ProblemHttpResult>> SaveRequestAsync(
        Guid id, SaveAbsenceRequestRequest request, EstrichOneDbContext db, HrService hr, ICurrentUser currentUser, IAuditLog audit, INotifier notifier,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        var (userId, type, replaces, problem) = await ValidateAsync(request, db, hr, currentUser, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var existing = await db.Set<AbsenceRequest>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (existing is not null)
        {
            if (existing.UserId != userId)
            {
                return Problem(StatusCodes.Status409Conflict, "Konflikt", "Unter dieser ID gibt es bereits einen anderen Antrag.", "ID_CONFLICT");
            }

            var same = existing.From == request.From && existing.To == request.To && HrService.TypeOf(existing) == type.Id
                && existing.HalfDayStart == request.HalfDayStart && existing.SubstituteUserId == request.SubstituteUserId && existing.Note == request.Note?.Trim();
            if (!same)
            {
                // Offenen Antrag ändern (Antragsteller oder Genehmiger).
                if (existing.Status != AbsenceRequestStatus.Pending)
                {
                    return Problem(StatusCodes.Status409Conflict, "Bereits entschieden", "Über diesen Antrag wurde bereits entschieden. Bitte stellen Sie einen Änderungsantrag.",
                        "ALREADY_DECIDED");
                }

                if (await OverlapsAsync(db, userId, request.From, request.To, existing.Id, existing.ReplacesRequestId, cancellationToken))
                {
                    return Problem(StatusCodes.Status409Conflict, "Überschneidung", "Für diesen Zeitraum gibt es bereits einen offenen Antrag.", "REQUEST_OVERLAP");
                }

                try
                {
                    existing.Change(request.From, request.To, type.Kind, type.Id, request.HalfDayStart, request.HalfDayEnd, request.Note, request.SubstituteUserId);
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                    return Validation(exception.Message);
                }

                audit.Record("hr.absence_request.changed", currentUser.UserId, "absence_request", existing.Id.ToString(), Details(existing, type));
                await db.SaveChangesAsync(cancellationToken);
            }

            return TypedResults.Ok((await ViewsAsync([existing], db, hr, currentUser, true, cancellationToken))[0]);
        }

        if (await OverlapsAsync(db, userId, request.From, request.To, null, replaces?.Id, cancellationToken))
        {
            return Problem(StatusCodes.Status409Conflict, "Überschneidung", "Für diesen Zeitraum gibt es bereits einen offenen Antrag.", "REQUEST_OVERLAP");
        }

        if (replaces is not null && await db.Set<AbsenceRequest>().AnyAsync(r => r.ReplacesRequestId == replaces.Id && r.Status == AbsenceRequestStatus.Pending, cancellationToken))
        {
            return Problem(StatusCodes.Status409Conflict, "Änderung offen", "Zu diesem Antrag gibt es bereits einen offenen Änderungsantrag.", "CHANGE_PENDING");
        }

        AbsenceRequest item;
        try
        {
            item = AbsenceRequest.Create(id, userId, request.From, request.To, type.Kind, request.Note, currentUser.UserId, clock.GetUtcNow(), type.Id,
                request.HalfDayStart, request.HalfDayEnd, request.SubstituteUserId, replaces?.Id);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }

        db.Add(item);
        if (!type.RequiresApproval)
        {
            // Nicht genehmigungspflichtig (z. B. Berufsschule): gilt sofort.
            var absence = Absence.Create(Guid.CreateVersion7(), item.UserId, item.From, item.To, type.Kind, item.Note, requestId: item.Id)
                .WithDetails(type.Id, item.HalfDayStart, item.HalfDayEnd);
            db.Add(absence);
            item.AutoApprove(clock.GetUtcNow(), absence.Id);
            await SupersedeAsync(item, db, currentUser.UserId, clock, cancellationToken);
        }

        audit.Record(replaces is null ? "hr.absence_request.created" : "hr.absence_request.change_requested", currentUser.UserId, "absence_request", item.Id.ToString(),
            Details(item, type));
        await db.SaveChangesAsync(cancellationToken);

        var view = (await ViewsAsync([item], db, hr, currentUser, true, cancellationToken))[0];
        if (item.Status == AbsenceRequestStatus.Pending)
        {
            await notifier.NotifyAsync(await hr.ApproversAsync(item.UserId, cancellationToken), new NotificationRequest(NotificationCategory.Absence,
                replaces is null ? "Abwesenheitsantrag eingegangen" : "Änderungsantrag eingegangen",
                $"{view.UserName} · {type.Name} · {Range(item.From, item.To)} · {Days(view.WorkingDays)}", "/personal/urlaub?reiter=antraege", "absenceRequest", item.Id,
                Event: NotificationEvents.AbsenceRequested), cancellationToken);
        }
        else if (item.UserId != currentUser.UserId)
        {
            await notifier.NotifyAsync([item.UserId], new NotificationRequest(NotificationCategory.Absence, "Abwesenheit eingetragen",
                $"{type.Name} · {Range(item.From, item.To)}", "/personal/urlaub", "absenceRequest", item.Id, Event: NotificationEvents.AbsenceDecided), cancellationToken);
        }

        return TypedResults.Ok(view);
    }

    private static Task<bool> OverlapsAsync(EstrichOneDbContext db, Guid userId, DateOnly from, DateOnly to, Guid? except, Guid? replaced, CancellationToken cancellationToken) =>
        db.Set<AbsenceRequest>().AnyAsync(r => r.UserId == userId && r.Status == AbsenceRequestStatus.Pending && r.From <= to && r.To >= from
            && r.Id != except && r.Id != replaced, cancellationToken);

    private static Dictionary<string, string?> Details(AbsenceRequest r, AbsenceType? type) => new()
    {
        ["userId"] = r.UserId.ToString(), ["type"] = type?.Key ?? r.Kind.ToString(), ["from"] = r.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["to"] = r.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["halfDayStart"] = r.HalfDayStart ? "ja" : null, ["halfDayEnd"] = r.HalfDayEnd ? "ja" : null,
        ["substitute"] = r.SubstituteUserId?.ToString(), ["replaces"] = r.ReplacesRequestId?.ToString(), ["status"] = r.Status.ToString(),
    };

    /// <summary>Genehmigter Änderungsantrag: alter Antrag und seine Abwesenheit entfallen.</summary>
    private static async Task SupersedeAsync(AbsenceRequest item, EstrichOneDbContext db, Guid by, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (item.ReplacesRequestId is not { } oldId || await db.Set<AbsenceRequest>().SingleOrDefaultAsync(r => r.Id == oldId, cancellationToken) is not { } old)
        {
            return;
        }

        if (old.AbsenceId is { } absenceId && await db.Set<Absence>().SingleOrDefaultAsync(a => a.Id == absenceId, cancellationToken) is { } absence)
        {
            db.Remove(absence);
        }

        old.Supersede(by, clock.GetUtcNow());
    }

    private static async Task<Results<Ok<AbsenceRequestView>, ProblemHttpResult>> DecideAsync(
        Guid id, DecideAbsenceRequest request, EstrichOneDbContext db, HrService hr, ICurrentUser currentUser, IAuditLog audit,
        IMailService mail, IOptions<EstrichOneOptions> options, TimeProvider clock, INotifier notifier, CancellationToken cancellationToken)
    {
        var item = await db.Set<AbsenceRequest>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (item is null)
        {
            return Problem(StatusCodes.Status404NotFound, "Antrag nicht gefunden", null, "NOT_FOUND");
        }

        if (!await hr.MayDecideAsync(item.UserId, cancellationToken))
        {
            return item.UserId == currentUser.UserId
                ? Problem(StatusCodes.Status403Forbidden, "Keine Berechtigung", "Über eigene Anträge entscheidet jemand anderes.", "PERMISSION_DENIED")
                : Problem(StatusCodes.Status403Forbidden, "Keine Berechtigung", "Über diesen Antrag entscheiden Genehmiger mit dem Recht „Urlaubsanträge genehmigen“.",
                    "PERMISSION_DENIED");
        }

        if (item.Status != AbsenceRequestStatus.Pending)
        {
            return Problem(StatusCodes.Status409Conflict, "Bereits entschieden", "Über diesen Antrag wurde bereits entschieden.", "ALREADY_DECIDED");
        }

        if (!request.Approve && string.IsNullOrWhiteSpace(request.Comment))
        {
            return Validation("Bitte begründen Sie die Ablehnung.");
        }

        var types = await hr.TypesAsync(cancellationToken);
        var type = types.GetValueOrDefault(HrService.TypeOf(item));
        Guid? absenceId = null;
        if (request.Approve)
        {
            var absence = Absence.Create(Guid.CreateVersion7(), item.UserId, item.From, item.To, item.Kind, item.Note, requestId: item.Id)
                .WithDetails(HrService.TypeOf(item), item.HalfDayStart, item.HalfDayEnd);
            db.Add(absence);
            absenceId = absence.Id;
            await SupersedeAsync(item, db, currentUser.UserId, clock, cancellationToken);
        }

        item.Decide(request.Approve, request.Comment, currentUser.UserId, clock.GetUtcNow(), absenceId);
        audit.Record(request.Approve ? "hr.absence_request.approved" : "hr.absence_request.rejected", currentUser.UserId, "absence_request", item.Id.ToString(),
            Details(item, type));
        await db.SaveChangesAsync(cancellationToken);

        // Postfach/Push: Ereignis, Art und Zeitraum, nie der Kommentar.
        var noun = type?.Kind == AbsenceKind.Vacation ? "Urlaubsantrag" : "Abwesenheitsantrag";
        await notifier.NotifyAsync([item.UserId], new NotificationRequest(NotificationCategory.Absence,
            item.ReplacesRequestId is null ? $"{noun} {(request.Approve ? "genehmigt" : "abgelehnt")}" : $"Änderungsantrag {(request.Approve ? "genehmigt" : "abgelehnt")}",
            $"{type?.Name ?? "Abwesenheit"} · {Range(item.From, item.To)}", "/personal/urlaub", "absenceRequest", item.Id,
            Event: NotificationEvents.AbsenceDecided), cancellationToken);
        if (item.SubstituteUserId is { } substitute && request.Approve)
        {
            var name = await db.Set<User>().AsNoTracking().Where(u => u.Id == item.UserId).Select(u => u.DisplayName).SingleAsync(cancellationToken);
            await notifier.NotifyAsync([substitute], new NotificationRequest(NotificationCategory.Absence, "Vertretung eingetragen",
                $"{name} · {Range(item.From, item.To)}", "/personal/urlaub", "absenceRequest", item.Id, Event: NotificationEvents.AbsenceDecided), cancellationToken);
        }

        // E-Mail an den Mitarbeiter (nur wenn ein E-Mail-Konto eingerichtet ist).
        var user = await db.Set<User>().AsNoTracking().SingleAsync(u => u.Id == item.UserId, cancellationToken);
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            var link = options.Value.PublicUrl is { } url ? new Uri(url, "/personal/urlaub").ToString() : string.Empty;
            var range = $"{item.From.ToString("dd.MM.yyyy", German)} – {item.To.ToString("dd.MM.yyyy", German)}";
            await mail.QueueAsync(new MailRequest(user.Email,
                $"{noun} {(request.Approve ? "genehmigt" : "abgelehnt")}: {range}",
                $"Guten Tag {user.DisplayName},\n\nIhr {noun} ({type?.Name ?? "Abwesenheit"}) für den Zeitraum {range} wurde {(request.Approve ? "genehmigt" : "abgelehnt")}."
                + (string.IsNullOrWhiteSpace(request.Comment) ? string.Empty : $"\n\nKommentar: {request.Comment}")
                + (link.Length > 0 ? $"\n\nÜbersicht: {link}" : string.Empty) + "\n\nMit freundlichen Grüßen\nEstrichONE\n",
                "hr.absence_decided", $"absence_request:{item.Id}"), cancellationToken);
        }

        return TypedResults.Ok((await ViewsAsync([item], db, hr, currentUser, false, cancellationToken))[0]);
    }

    private static async Task<Results<Ok<AbsenceRequestView>, ProblemHttpResult>> CancelAsync(
        Guid id, EstrichOneDbContext db, HrService hr, ICurrentUser currentUser, IAuditLog audit, INotifier notifier, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var item = await db.Set<AbsenceRequest>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        var own = item is not null && (item.UserId == currentUser.UserId || item.RequestedByUserId == currentUser.UserId);
        var mayDecide = item is not null && await hr.MayDecideAsync(item.UserId, cancellationToken);
        if (item is null || (!own && !mayDecide))
        {
            return Problem(StatusCodes.Status404NotFound, "Antrag nicht gefunden", null, "NOT_FOUND");
        }

        if (item.Status == AbsenceRequestStatus.Approved && item.From <= hr.Today && !mayDecide)
        {
            return Problem(StatusCodes.Status409Conflict, "Abwesenheit hat begonnen", "Begonnene Abwesenheiten ändert das Büro.", "ALREADY_STARTED");
        }

        var wasApproved = item.Status == AbsenceRequestStatus.Approved;
        try
        {
            item.Cancel(currentUser.UserId, clock.GetUtcNow());
            if (wasApproved && await db.Set<Absence>().Where(a => a.RequestId == item.Id).ToListAsync(cancellationToken) is { Count: > 0 } absences)
            {
                db.RemoveRange(absences);
            }
        }
        catch (InvalidOperationException exception)
        {
            return Problem(StatusCodes.Status409Conflict, "Nicht möglich", exception.Message, "INVALID_STATE");
        }

        var type = (await hr.TypesAsync(cancellationToken)).GetValueOrDefault(HrService.TypeOf(item));
        audit.Record("hr.absence_request.cancelled", currentUser.UserId, "absence_request", item.Id.ToString(), Details(item, type));
        await db.SaveChangesAsync(cancellationToken);

        var view = (await ViewsAsync([item], db, hr, currentUser, false, cancellationToken))[0];
        var text = $"{view.UserName} · {type?.Name ?? "Abwesenheit"} · {Range(item.From, item.To)}";
        if (item.UserId == currentUser.UserId)
        {
            // Genehmiger erfahren von zurückgezogenen offenen und stornierten genehmigten Anträgen.
            await notifier.NotifyAsync(await hr.ApproversAsync(item.UserId, cancellationToken), new NotificationRequest(NotificationCategory.Absence,
                wasApproved ? "Genehmigte Abwesenheit storniert" : "Antrag zurückgezogen", text, "/personal/urlaub?reiter=antraege", "absenceRequest", item.Id,
                Event: NotificationEvents.AbsenceCancelled), cancellationToken);
        }
        else
        {
            await notifier.NotifyAsync([item.UserId], new NotificationRequest(NotificationCategory.Absence, "Abwesenheit storniert",
                $"{type?.Name ?? "Abwesenheit"} · {Range(item.From, item.To)}", "/personal/urlaub", "absenceRequest", item.Id, Event: NotificationEvents.AbsenceCancelled),
                cancellationToken);
        }

        return TypedResults.Ok(view);
    }

    private static async Task<Ok<MyVacation>> MineAsync(
        EstrichOneDbContext db, HrService hr, IUserPermissions permissions, ICurrentUser currentUser, CancellationToken cancellationToken,
        [FromQuery] int? year = null)
    {
        var y = year is >= 2000 and <= 2100 ? year.Value : hr.Today.Year;
        var settings = await hr.SettingsAsync(cancellationToken);
        var holidays = GermanHolidays.For(y, settings.State);
        var dates = holidays.Select(h => h.Date).ToHashSet();
        var employee = await db.Set<Employee>().AsNoTracking().SingleOrDefaultAsync(e => e.UserId == currentUser.UserId, cancellationToken);
        var requests = await db.Set<AbsenceRequest>().AsNoTracking()
            .Where(r => r.UserId == currentUser.UserId && r.To >= new DateOnly(y - 1, 1, 1) && r.From <= new DateOnly(y + 1, 12, 31))
            .OrderByDescending(r => r.From).Take(300).ToListAsync(cancellationToken);
        var summary = employee is null ? null : await hr.AccountAsync(employee, y, true, cancellationToken);
        var types = await hr.TypesAsync(cancellationToken);
        var absences = await db.Set<Absence>().AsNoTracking()
            .Where(a => a.UserId == currentUser.UserId && a.To >= new DateOnly(y, 1, 1) && a.From <= new DateOnly(y, 12, 31)).OrderBy(a => a.From).ToListAsync(cancellationToken);
        var mine = absences.Select(a =>
        {
            var t = types.GetValueOrDefault(HrService.TypeOf(a));
            return new MyAbsence(a.Id, a.From, a.To, HrService.TypeOf(a), t?.Name ?? "Abwesenheit", t?.Color ?? "#9E9E9E", a.Kind, a.HalfDayStart, a.HalfDayEnd,
                VacationCalculator.WorkingDays(employee?.WorkDays ?? VacationCalculator.DefaultWorkDays, a.From, a.To, a.HalfDayStart, a.HalfDayEnd, dates), a.RequestId);
        }).ToList();
        var canApprove = await permissions.HasAsync(Permissions.AbsenceApprove, cancellationToken) || (await hr.LedUserIdsAsync(cancellationToken)).Count > 0;
        var pending = 0;
        if (canApprove)
        {
            var led = await permissions.HasAsync(Permissions.AbsenceApprove, cancellationToken) ? null : await hr.LedUserIdsAsync(cancellationToken);
            pending = await db.Set<AbsenceRequest>().CountAsync(r => r.Status == AbsenceRequestStatus.Pending && r.UserId != currentUser.UserId
                && (led == null || led.Contains(r.UserId)), cancellationToken);
        }

        return TypedResults.Ok(new MyVacation(employee?.Id, summary, await ViewsAsync(requests, db, hr, currentUser, false, cancellationToken), holidays, y,
            types.Values.Where(t => t.IsActive && t.Requestable && !t.IsSickness).OrderBy(t => t.SortOrder).Select(AbsenceAdminEndpoints.ToView).ToList(),
            await AbsenceAdminEndpoints.ClosuresAsync(db, hr, new DateOnly(y, 1, 1), new DateOnly(y, 12, 31), cancellationToken), mine, canApprove, pending));
    }

    private static async Task<Ok<HrSummary>> SummaryAsync(
        EstrichOneDbContext db, HrService hr, IUserPermissions permissions, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var approve = await permissions.HasAsync(Permissions.AbsenceApprove, cancellationToken);
        var led = approve ? [] : await hr.LedUserIdsAsync(cancellationToken);
        var pending = approve
            ? await db.Set<AbsenceRequest>().CountAsync(r => r.Status == AbsenceRequestStatus.Pending && r.UserId != userId, cancellationToken)
            : led.Count > 0 ? await db.Set<AbsenceRequest>().CountAsync(r => r.Status == AbsenceRequestStatus.Pending && led.Contains(r.UserId), cancellationToken) : 0;
        var mine = await db.Set<AbsenceRequest>().CountAsync(r => r.Status == AbsenceRequestStatus.Pending && r.UserId == userId, cancellationToken);
        var reminders = await permissions.HasAsync(Permissions.HrRead, cancellationToken)
            ? (await RemindersCoreAsync(db, hr, permissions, 30, cancellationToken)).Count(r => r.Severity != "info")
            : 0;
        decimal? remaining = null;
        if (await db.Set<Employee>().AsNoTracking().SingleOrDefaultAsync(e => e.UserId == userId, cancellationToken) is { } employee)
        {
            remaining = (await hr.AccountAsync(employee, hr.Today.Year, false, cancellationToken)).Remaining;
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
            a.CaseId ?? a.Id, a.CaseId is { } c && c != a.Id, a.RecordedAt, HrService.TypeOf(a))).ToList());
    }

    private static int Weekdays(DateOnly from, DateOnly to, HashSet<DateOnly> holidays) =>
        (int)VacationCalculator.WorkingDays(VacationCalculator.DefaultWorkDays, from, to, false, false, holidays);

    private static async Task<Results<Ok<SickNoteView>, ProblemHttpResult>> SaveSickNoteAsync(
        Guid id, SaveSickNoteRequest request, EstrichOneDbContext db, IStoredFiles files, ICurrentUser currentUser, HrService hr, IAuditLog audit, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == request.UserId, cancellationToken);
        if (user is null)
        {
            return Validation("Unbekannter Mitarbeiter.");
        }

        var typeId = request.TypeId ?? StandardAbsenceTypes.Sick;
        if ((await hr.TypesAsync(cancellationToken)).GetValueOrDefault(typeId) is not { IsSickness: true })
        {
            return Validation("Bitte eine Krankheitsart wählen.");
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
                caseId: caseId, recordedAt: existing?.RecordedAt ?? clock.GetUtcNow()).WithDetails(typeId, false, false);
            if (existing is not null)
            {
                db.Remove(existing);
            }

            db.Add(absence);
            // Protokoll ohne Notiz und ohne Diagnose: nur wer, wann, Zeitraum.
            audit.Record(existing is null ? "hr.sick_note.created" : "hr.sick_note.changed", currentUser.UserId, "absence", absence.Id.ToString(),
                new Dictionary<string, string?>
                {
                    ["userId"] = absence.UserId.ToString(), ["from"] = absence.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["to"] = absence.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["certificate"] = absence.CertificateFileId is null ? "nein" : "ja",
                });
            await db.SaveChangesAsync(cancellationToken);
            var holidays = await hr.HolidaysAsync(absence.From, absence.To, cancellationToken);
            var employee = await db.Set<Employee>().AsNoTracking().SingleOrDefaultAsync(e => e.UserId == request.UserId, cancellationToken);
            return TypedResults.Ok(new SickNoteView(absence.Id, absence.UserId, user.DisplayName, absence.From, absence.To,
                employee?.WorkingDays(absence.From, absence.To, holidays) ?? Weekdays(absence.From, absence.To, holidays), absence.Note, absence.CertificateFileId,
                absence.CaseId, absence.CaseId is { } c && c != absence.Id, absence.RecordedAt, typeId));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteSickNoteAsync(Guid id, EstrichOneDbContext db, IAuditLog audit, ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var item = await db.Set<Absence>().SingleOrDefaultAsync(a => a.Id == id && a.Kind == AbsenceKind.Sick, cancellationToken);
        if (item is null)
        {
            return Problem(StatusCodes.Status404NotFound, "Krankmeldung nicht gefunden", null, "NOT_FOUND");
        }

        db.Remove(item);
        audit.Record("hr.sick_note.deleted", currentUser.UserId, "absence", id.ToString(), new Dictionary<string, string?> { ["userId"] = item.UserId.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

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
                $"{kindText[q.Kind]} {q.Name} {(expired ? "abgelaufen seit" : "läuft ab am")} {q.ExpiresOn.Value.ToString("dd.MM.yyyy", German)}", expired ? "error" : "warning"));
        }

        if (await permissions.HasAsync(Permissions.HrManage, cancellationToken))
        {
            foreach (var d in await db.Set<EmployeeDocument>().AsNoTracking().Where(d => ids.Contains(d.EmployeeId) && d.ValidUntil != null && d.ValidUntil <= limit)
                .ToListAsync(cancellationToken))
            {
                var expired = d.ValidUntil < today;
                list.Add(new HrReminder("document", d.EmployeeId, byId[d.EmployeeId].DisplayName, d.ValidUntil!.Value,
                    $"Dokument „{d.Title}“ {(expired ? "abgelaufen seit" : "gültig bis")} {d.ValidUntil.Value.ToString("dd.MM.yyyy", German)}", expired ? "error" : "warning"));
            }
        }

        foreach (var e in employees)
        {
            if (e.ProbationEnd is { } probation && probation >= today.AddDays(-7) && probation <= limit)
            {
                list.Add(new HrReminder("probation", e.Id, e.DisplayName, probation, $"Probezeit endet am {probation.ToString("dd.MM.yyyy", German)}", "warning"));
            }

            if (settings.RemindBirthdays && e.BirthDate is { } birth && Next(birth, today) is var birthday && birthday <= limit)
            {
                list.Add(new HrReminder("birthday", e.Id, e.DisplayName, birthday,
                    $"Geburtstag am {birthday.ToString("dd.MM.", German)} ({birthday.Year - birth.Year} Jahre)", "info"));
            }

            if (settings.RemindAnniversaries && e.EntryDate is { } entry && Next(entry, today) is var anniversary && anniversary <= limit
                && (anniversary.Year - entry.Year) is var years && years > 0 && (years == 1 || years % 5 == 0))
            {
                list.Add(new HrReminder("anniversary", e.Id, e.DisplayName, anniversary,
                    $"{years} {(years == 1 ? "Jahr" : "Jahre")} im Betrieb am {anniversary.ToString("dd.MM.yyyy", German)}", "info"));
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

    internal static ProblemHttpResult Problem(int status, string title, string? detail, string code) =>
        TypedResults.Problem(statusCode: status, title: title, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    internal static ProblemHttpResult Validation(string detail) => Problem(StatusCodes.Status422UnprocessableEntity, "Ungültige Angaben", detail, "VALIDATION_FAILED");
}
