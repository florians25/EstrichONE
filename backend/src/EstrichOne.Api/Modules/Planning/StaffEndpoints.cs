using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Documentation.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

/// <param name="HourlyRate">Nur mit Recht „Personalstamm pflegen“, sonst leer.</param>
/// <param name="VacationRemaining">Resturlaub im laufenden Jahr (Anspruch + Übertrag − genommener/geplanter Urlaub).</param>
/// <param name="AbsentToday">Heutige Abwesenheit (z. B. Urlaub) oder leer.</param>
/// <param name="ExpiringQualifications">Qualifikationen, die abgelaufen sind oder in den nächsten 60 Tagen ablaufen.</param>
public sealed record EmployeeListItem(
    Guid Id, string? PersonnelNumber, string DisplayName, string FirstName, string LastName, string? JobTitle, EmploymentType EmploymentType,
    string? Phone, string? Mobile, string? Email, string? City, Guid? UserId, string? UserName, bool IsActive, DateOnly? EntryDate, DateOnly? ExitDate,
    IReadOnlyList<CrewRef> Crews, decimal? HourlyRate, decimal VacationRemaining, AbsenceKind? AbsentToday, int ExpiringQualifications,
    DateOnly? NextExpiry, long Version);

public sealed record QualificationView(
    Guid Id, QualificationKind Kind, string Name, string? Number, DateOnly? IssuedOn, DateOnly? ExpiresOn, string? Notes, bool Expired, bool ExpiresSoon);

public sealed record EquipmentView(
    Guid Id, Guid? ResourceId, string Name, string? SerialNumber, DateOnly? IssuedOn, DateOnly? ReturnedOn, string? Notes);

public sealed record EmployeeDocumentView(
    Guid Id, Guid FileId, EmployeeDocumentCategory Category, string Title, string? FileName, string? ContentType, long SizeBytes, DateOnly? ValidUntil,
    DateTimeOffset AddedAt);

/// <param name="Entitlement">Anspruch im Jahr (anteilig bei Ein-/Austritt im Jahr nicht gekürzt – das entscheidet das Büro).</param>
/// <param name="Taken">Urlaubstage (Arbeitstage nach Arbeitszeitmodell) bis heute.</param>
/// <param name="Planned">Urlaubstage ab morgen im selben Jahr.</param>
/// <param name="Requested">Arbeitstage in offenen Anträgen (noch nicht genehmigt, nicht abgezogen).</param>
public sealed record VacationSummary(
    int Year, decimal Entitlement, decimal CarryOver, decimal Taken, decimal Planned, decimal Remaining, int SickDays, decimal Requested = 0);

public sealed record EmployeeDetail(
    Guid Id, string? PersonnelNumber, string FirstName, string LastName, string DisplayName, string? JobTitle, EmploymentType EmploymentType,
    Guid? UserId, string? UserName, string? Email, string? Phone, string? Mobile, string? Street, string? PostalCode, string? City, DateOnly? BirthDate,
    string? EmergencyContactName, string? EmergencyContactPhone, string? EmergencyContactRelation, DateOnly? EntryDate, DateOnly? ExitDate,
    string? WorkTimeModel, decimal? WeeklyHours, IReadOnlyList<DayOfWeek> WorkDays, decimal? HourlyRate, decimal VacationDaysPerYear,
    decimal VacationCarryOver, string? Notes, bool IsActive, IReadOnlyList<CrewRef> Crews, IReadOnlyList<QualificationView> Qualifications,
    IReadOnlyList<EquipmentView> Equipment, IReadOnlyList<EmployeeDocumentView> Documents, IReadOnlyList<AbsenceView> Absences, VacationSummary Vacation,
    bool CanManage, DateOnly? ProbationEnd, long Version);

public sealed record SaveEmployeeRequest(
    [property: MaxLength(100)] string? FirstName, [property: Required, MaxLength(100)] string LastName, [property: MaxLength(20)] string? PersonnelNumber,
    [property: MaxLength(100)] string? JobTitle, EmploymentType EmploymentType = EmploymentType.FullTime, Guid? UserId = null,
    [property: MaxLength(320)] string? Email = null, [property: MaxLength(50)] string? Phone = null, [property: MaxLength(50)] string? Mobile = null,
    [property: MaxLength(200)] string? Street = null, [property: MaxLength(10)] string? PostalCode = null, [property: MaxLength(100)] string? City = null,
    DateOnly? BirthDate = null, [property: MaxLength(200)] string? EmergencyContactName = null, [property: MaxLength(50)] string? EmergencyContactPhone = null,
    [property: MaxLength(50)] string? EmergencyContactRelation = null, DateOnly? EntryDate = null, DateOnly? ExitDate = null,
    [property: MaxLength(100)] string? WorkTimeModel = null, decimal? WeeklyHours = null, IReadOnlyList<DayOfWeek>? WorkDays = null,
    decimal? HourlyRate = null, decimal VacationDaysPerYear = 30, decimal VacationCarryOver = 0, [property: MaxLength(4000)] string? Notes = null,
    DateOnly? ProbationEnd = null);

public sealed record SaveQualificationRequest(
    QualificationKind Kind, [property: Required, MaxLength(100)] string Name, [property: MaxLength(50)] string? Number, DateOnly? IssuedOn, DateOnly? ExpiresOn,
    [property: MaxLength(500)] string? Notes);

public sealed record SaveEquipmentRequest(
    Guid? ResourceId, [property: MaxLength(100)] string? Name, [property: MaxLength(100)] string? SerialNumber, DateOnly? IssuedOn, DateOnly? ReturnedOn,
    [property: MaxLength(500)] string? Notes);

public sealed record AddEmployeeDocumentRequest(Guid FileId, EmployeeDocumentCategory Category, [property: Required, MaxLength(200)] string Title, DateOnly? ValidUntil);

public sealed record SetEmployeeCrewsRequest(IReadOnlyList<Guid> CrewIds);

/// <summary>
/// Onboarding: ohne verknüpftes Konto entweder neu anlegen (<paramref name="UserName"/>, <paramref name="RoleIds"/>) oder ein
/// vorhandenes verknüpfen (<paramref name="LinkUserId"/>); mit Konto wird ein neues Einmalpasswort vergeben.
/// </summary>
/// <param name="DeviceCode">Optional ein vorher erzeugter Geräte-Einrichtungscode (POST /devices/enrollment-codes) für das Handy.</param>
/// <param name="DeviceQrPng">QR-Code dazu als PNG (Base64), höchstens 200 kB.</param>
/// <param name="SendEmail">Unterlagen zusätzlich per E-Mail an den Mitarbeiter (wenn eine Adresse und ein E-Mail-Konto vorhanden sind).</param>
public sealed record EmployeeOnboardingRequest(
    [property: MaxLength(64)] string? UserName = null,
    IReadOnlyList<Guid>? RoleIds = null,
    [property: MaxLength(320), EmailAddress] string? Email = null,
    Guid? LinkUserId = null,
    [property: MaxLength(40)] string? DeviceCode = null,
    [property: MaxLength(500)] string? DeviceLink = null,
    DateTimeOffset? DeviceCodeExpiresAt = null,
    [property: MaxLength(280_000)] string? DeviceQrPng = null,
    bool SendEmail = false);

/// <param name="OneTimePassword">Nur in dieser Antwort sichtbar; Wechsel bei der ersten Anmeldung.</param>
/// <param name="DocumentFileId">Onboarding-PDF in der Mitarbeiterakte (Dokumente).</param>
public sealed record EmployeeOnboardingResponse(Guid UserId, string UserName, string OneTimePassword, bool CreatedAccount, Guid DocumentFileId, bool Emailed);

public sealed record SaveEmployeeAbsenceRequest(DateOnly From, DateOnly To, AbsenceKind Kind, [property: MaxLength(500)] string? Note);

/// <summary>Ablaufende Qualifikationen und Führerscheine aller aktiven Mitarbeiter.</summary>
public sealed record ExpiringQualification(Guid EmployeeId, string EmployeeName, QualificationView Qualification);

public sealed record ImportEmployeesResult(int Created);

/// <summary>
/// Personalstamm (C5): Mitarbeiter mit Kontakt, Anschrift, Notfallkontakt, Ein-/Austritt, Qualifikationen und
/// Führerscheinklassen mit Ablaufdatum, Kolonnen, Stundensatz, Arbeitszeitmodell, Urlaub/Abwesenheiten, Geräte und
/// Personaldokumente; verknüpft mit dem Benutzerkonto.
/// </summary>
internal static class StaffEndpoints
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public static void Map(IEndpointRouteBuilder api)
    {
        var staff = api.MapGroup("/staff").WithTags("Personal");
        staff.MapGet("/employees", ListAsync).WithName("ListEmployees")
            .WithSummary("Mitarbeiter (status = active | former | all, search), mit Kolonnen, Resturlaub und ablaufenden Qualifikationen")
            .RequirePermission(Permissions.HrRead);
        staff.MapGet("/employees/{id:guid}", GetAsync).WithName("GetEmployee").ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.HrRead);
        staff.MapPut("/employees/{id:guid}", SaveAsync).WithName("SaveEmployee")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.HrManage);
        staff.MapDelete("/employees/{id:guid}", DeleteAsync).WithName("DeleteEmployee")
            .WithSummary("Mitarbeiter mit Qualifikationen, Geräten und Dokumentverweisen löschen (Benutzerkonto bleibt)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HrManage);
        staff.MapPost("/employees/import-users", ImportAsync).WithName("ImportEmployeesFromUsers")
            .WithSummary("Für alle aktiven Benutzerkonten ohne Personalstamm einen Mitarbeiter anlegen")
            .RequirePermission(Permissions.HrManage);

        staff.MapPut("/employees/{id:guid}/qualifications/{qualificationId:guid}", SaveQualificationAsync).WithName("SaveEmployeeQualification")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HrManage);
        staff.MapDelete("/employees/{id:guid}/qualifications/{qualificationId:guid}", DeleteQualificationAsync).WithName("DeleteEmployeeQualification")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HrManage);
        staff.MapPut("/employees/{id:guid}/equipment/{equipmentId:guid}", SaveEquipmentAsync).WithName("SaveEmployeeEquipment")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HrManage);
        staff.MapDelete("/employees/{id:guid}/equipment/{equipmentId:guid}", DeleteEquipmentAsync).WithName("DeleteEmployeeEquipment")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HrManage);
        staff.MapPut("/employees/{id:guid}/documents/{documentId:guid}", AddDocumentAsync).WithName("AddEmployeeDocument")
            .WithSummary("Hochgeladene Datei (POST /files ohne Bauakte) als Personaldokument ablegen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HrManage);
        staff.MapDelete("/employees/{id:guid}/documents/{documentId:guid}", DeleteDocumentAsync).WithName("DeleteEmployeeDocument")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HrManage);
        staff.MapPost("/employees/{id:guid}/onboarding", OnboardingAsync).WithName("OnboardEmployee")
            .WithSummary("Benutzerkonto anlegen/verknüpfen bzw. neues Einmalpasswort; Onboarding-PDF (DIN 5008) in die Mitarbeiterakte, optional per E-Mail")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.HrManage).RequirePermission(Permissions.UsersManage);
        staff.MapPut("/employees/{id:guid}/crews", SetCrewsAsync).WithName("SetEmployeeCrews")
            .WithSummary("Kolonnenzugehörigkeit setzen (nur mit verknüpftem Benutzerkonto)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HrManage);
        staff.MapPut("/employees/{id:guid}/absences/{absenceId:guid}", SaveAbsenceAsync).WithName("SaveEmployeeAbsence")
            .WithSummary("Urlaub, Krankheit, Schulung eintragen (nur mit verknüpftem Benutzerkonto; erscheint in der Planung)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HrManage);
        staff.MapDelete("/employees/{id:guid}/absences/{absenceId:guid}", DeleteAbsenceAsync).WithName("DeleteEmployeeAbsence")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HrManage);
        staff.MapGet("/qualifications/expiring", ExpiringAsync).WithName("ListExpiringQualifications")
            .WithSummary("Abgelaufene und bald ablaufende Qualifikationen und Führerscheine (days = Vorlauf, Standard 60)")
            .RequirePermission(Permissions.HrRead);
    }

    private static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Berlin).DateTime);

    private static QualificationView ToView(EmployeeQualification q, DateOnly today) =>
        new(q.Id, q.Kind, q.Name, q.Number, q.IssuedOn, q.ExpiresOn, q.Notes, q.ExpiresOn < today, q.ExpiresOn >= today && q.ExpiresOn <= today.AddDays(60));

    private static async Task<Ok<List<EmployeeListItem>>> ListAsync(
        EstrichOneDbContext db, IUserPermissions permissions, HrService hr, TimeProvider clock, CancellationToken cancellationToken,
        [FromQuery] string? status = null, [FromQuery] string? search = null)
    {
        var today = Today(clock);
        var holidays = await hr.HolidaysAsync(new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31), cancellationToken);
        var maySeeSickness = await hr.MaySeeSicknessAsync(cancellationToken);
        var query = db.Set<Employee>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(e => EF.Functions.ILike(e.LastName, pattern) || EF.Functions.ILike(e.FirstName, pattern)
                || (e.PersonnelNumber != null && EF.Functions.ILike(e.PersonnelNumber, pattern)) || (e.City != null && EF.Functions.ILike(e.City, pattern))
                || (e.JobTitle != null && EF.Functions.ILike(e.JobTitle, pattern)));
        }

        var employees = await query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ToListAsync(cancellationToken);
        employees = (status?.ToLowerInvariant()) switch
        {
            "former" => employees.Where(e => !e.IsActiveOn(today) && e.ExitDate < today).ToList(),
            "all" => employees,
            _ => employees.Where(e => e.ExitDate is null || e.ExitDate >= today).ToList(),
        };

        var ids = employees.Select(e => e.Id).ToList();
        var userIds = employees.Select(e => e.UserId).OfType<Guid>().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName, cancellationToken);
        var crews = await db.Set<Crew>().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);
        var yearStart = new DateOnly(today.Year, 1, 1);
        var absences = await db.Set<Absence>().AsNoTracking().Where(a => userIds.Contains(a.UserId) && a.To >= yearStart).ToListAsync(cancellationToken);
        var qualifications = await db.Set<EmployeeQualification>().AsNoTracking()
            .Where(q => ids.Contains(q.EmployeeId) && q.ExpiresOn != null && q.ExpiresOn <= today.AddDays(60)).ToListAsync(cancellationToken);
        var showRate = await permissions.HasAsync(Permissions.HrManage, cancellationToken);

        return TypedResults.Ok(employees.Select(e =>
        {
            var own = e.UserId is { } uid ? absences.Where(a => a.UserId == uid).ToList() : [];
            var expiring = qualifications.Where(q => q.EmployeeId == e.Id).ToList();
            return new EmployeeListItem(e.Id, e.PersonnelNumber, e.DisplayName, e.FirstName, e.LastName, e.JobTitle, e.EmploymentType, e.Phone, e.Mobile,
                e.Email, e.City, e.UserId, e.UserId is { } u ? users.GetValueOrDefault(u) : null, e.IsActiveOn(today), e.EntryDate, e.ExitDate,
                e.UserId is { } member ? crews.Where(c => c.MemberUserIds.Contains(member)).Select(c => new CrewRef(c.Id, c.Name, c.Color)).ToList() : [],
                showRate ? e.HourlyRate : null, HrService.Vacation(e, own, today, holidays, maySeeSickness).Remaining,
                own.FirstOrDefault(a => a.Covers(today)) is { } away ? HrService.Mask(away.Kind, maySeeSickness) : null, expiring.Count,
                expiring.Select(q => q.ExpiresOn).Min(), e.Version);
        }).ToList());
    }

    private static async Task<Results<Ok<EmployeeDetail>, ProblemHttpResult>> GetAsync(
        Guid id, EstrichOneDbContext db, IUserPermissions permissions, IStoredFiles files, HrService hr, TimeProvider clock, CancellationToken cancellationToken)
    {
        var employee = await db.Set<Employee>().AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        return employee is null
            ? NotFound("Mitarbeiter nicht gefunden")
            : TypedResults.Ok(await DetailAsync(employee, db, permissions, files, hr, clock, cancellationToken));
    }

    private static async Task<EmployeeDetail> DetailAsync(
        Employee e, EstrichOneDbContext db, IUserPermissions permissions, IStoredFiles files, HrService hr, TimeProvider clock, CancellationToken cancellationToken)
    {
        var today = Today(clock);
        var holidays = await hr.HolidaysAsync(new DateOnly(today.Year, 1, 1), new DateOnly(today.Year, 12, 31), cancellationToken);
        var manage = await permissions.HasAsync(Permissions.HrManage, cancellationToken);
        var user = e.UserId is { } uid ? await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == uid, cancellationToken) : null;
        var crews = e.UserId is { } member
            ? await db.Set<Crew>().AsNoTracking().Where(c => c.MemberUserIds.Contains(member)).OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(c => new CrewRef(c.Id, c.Name, c.Color)).ToListAsync(cancellationToken)
            : [];
        var qualifications = await db.Set<EmployeeQualification>().AsNoTracking().Where(q => q.EmployeeId == e.Id)
            .OrderBy(q => q.Kind).ThenBy(q => q.Name).ToListAsync(cancellationToken);
        var equipment = await db.Set<EmployeeEquipment>().AsNoTracking().Where(q => q.EmployeeId == e.Id)
            .OrderBy(q => q.ReturnedOn != null).ThenBy(q => q.Name).ToListAsync(cancellationToken);
        var documents = manage
            ? await db.Set<EmployeeDocument>().AsNoTracking().Where(d => d.EmployeeId == e.Id).OrderByDescending(d => d.AddedAt).ToListAsync(cancellationToken)
            : [];
        var info = documents.Count == 0 ? new Dictionary<Guid, StoredFileInfo>() : await files.FindAsync(documents.Select(d => d.FileId).ToList(), cancellationToken);
        var absences = e.UserId is { } owner
            ? await db.Set<Absence>().AsNoTracking().Where(a => a.UserId == owner && a.To >= new DateOnly(today.Year - 1, 1, 1))
                .OrderByDescending(a => a.From).ToListAsync(cancellationToken)
            : [];

        return new EmployeeDetail(e.Id, e.PersonnelNumber, e.FirstName, e.LastName, e.DisplayName, e.JobTitle, e.EmploymentType, e.UserId, user?.UserName,
            e.Email, e.Phone, e.Mobile, e.Street, e.PostalCode, e.City, e.BirthDate, e.EmergencyContactName, e.EmergencyContactPhone,
            e.EmergencyContactRelation, e.EntryDate, e.ExitDate, e.WorkTimeModel, e.WeeklyHours, e.WorkDayList, manage ? e.HourlyRate : null,
            e.VacationDaysPerYear, e.VacationCarryOver, e.Notes, e.IsActiveOn(today), crews, qualifications.Select(q => ToView(q, today)).ToList(),
            equipment.Select(q => new EquipmentView(q.Id, q.ResourceId, q.Name, q.SerialNumber, q.IssuedOn, q.ReturnedOn, q.Notes)).ToList(),
            documents.Select(d => new EmployeeDocumentView(d.Id, d.FileId, d.Category, d.Title, info.GetValueOrDefault(d.FileId)?.OriginalName,
                info.GetValueOrDefault(d.FileId)?.ContentType, info.GetValueOrDefault(d.FileId)?.SizeBytes ?? 0, d.ValidUntil, d.AddedAt)).ToList(),
            absences.Select(a => new AbsenceView(a.Id, a.UserId, e.DisplayName, a.From, a.To, HrService.Mask(a.Kind, manage),
                a.Kind == AbsenceKind.Sick && !manage ? null : a.Note)).ToList(),
            HrService.Vacation(e, absences, today, holidays, manage, await RequestedDaysAsync(e, db, holidays, cancellationToken)), manage, e.ProbationEnd, e.Version);
    }

    private static async Task<decimal> RequestedDaysAsync(Employee e, EstrichOneDbContext db, IReadOnlySet<DateOnly> holidays, CancellationToken cancellationToken)
    {
        if (e.UserId is not { } userId)
        {
            return 0;
        }

        var pending = await db.Set<AbsenceRequest>().AsNoTracking()
            .Where(r => r.UserId == userId && r.Status == AbsenceRequestStatus.Pending && r.Kind == AbsenceKind.Vacation).ToListAsync(cancellationToken);
        return pending.Sum(r => e.WorkingDays(r.From, r.To, holidays));
    }

    private static async Task<Results<Ok<EmployeeDetail>, ProblemHttpResult>> SaveAsync(
        Guid id, SaveEmployeeRequest request, EstrichOneDbContext db, IUserPermissions permissions, IStoredFiles files, HrService hr, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (request.UserId is { } userId)
        {
            if (!await db.Set<User>().AnyAsync(u => u.Id == userId, cancellationToken))
            {
                return Validation("Unbekanntes Benutzerkonto.");
            }

            if (await db.Set<Employee>().AnyAsync(e => e.UserId == userId && e.Id != id, cancellationToken))
            {
                return Conflict("Dieses Benutzerkonto ist bereits mit einem anderen Mitarbeiter verknüpft.", "USER_ALREADY_LINKED");
            }
        }

        var number = request.PersonnelNumber?.Trim();
        if (!string.IsNullOrEmpty(number) && await db.Set<Employee>().AnyAsync(e => e.PersonnelNumber == number && e.Id != id, cancellationToken))
        {
            return Conflict($"Die Personalnummer {number} ist schon vergeben.", "PERSONNEL_NUMBER_TAKEN");
        }

        try
        {
            var employee = await db.Set<Employee>().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (employee is null)
            {
                employee = Employee.Create(id);
                db.Add(employee);
            }

            employee.Update(request.FirstName ?? string.Empty, request.LastName, request.PersonnelNumber, request.JobTitle, request.EmploymentType);
            employee.ChangeContact(request.Email, request.Phone, request.Mobile, request.Street, request.PostalCode, request.City, request.BirthDate);
            employee.ChangeEmergencyContact(request.EmergencyContactName, request.EmergencyContactPhone, request.EmergencyContactRelation);
            employee.ChangeEmployment(request.EntryDate, request.ExitDate, request.WorkTimeModel, request.WeeklyHours, request.WorkDays,
                request.VacationDaysPerYear, request.VacationCarryOver);
            employee.ChangeProbation(request.ProbationEnd);
            employee.ChangeRate(request.HourlyRate);
            employee.ChangeNotes(request.Notes);
            employee.LinkUser(request.UserId);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(await DetailAsync(employee, db, permissions, files, hr, clock, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid id, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var deleted = await db.Set<Employee>().Where(e => e.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted == 0 ? NotFound("Mitarbeiter nicht gefunden") : TypedResults.NoContent();
    }

    private static async Task<Ok<ImportEmployeesResult>> ImportAsync(EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var linked = await db.Set<Employee>().Where(e => e.UserId != null).Select(e => e.UserId!.Value).ToListAsync(cancellationToken);
        var users = await db.Set<User>().AsNoTracking().Where(u => u.IsActive && !linked.Contains(u.Id)).ToListAsync(cancellationToken);
        foreach (var user in users)
        {
            var parts = user.DisplayName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var employee = Employee.Create(Guid.CreateVersion7());
            employee.Update(parts.Length > 1 ? parts[0] : string.Empty, parts.Length > 1 ? parts[1] : parts.ElementAtOrDefault(0) ?? user.UserName, null, null,
                EmploymentType.FullTime);
            employee.ChangeContact(user.Email, user.Phone, null, null, null, null, null);
            employee.LinkUser(user.Id);
            db.Add(employee);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new ImportEmployeesResult(users.Count));
    }

    private static async Task<Results<Ok<QualificationView>, ProblemHttpResult>> SaveQualificationAsync(
        Guid id, Guid qualificationId, SaveQualificationRequest request, EstrichOneDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!await db.Set<Employee>().AnyAsync(e => e.Id == id, cancellationToken))
        {
            return NotFound("Mitarbeiter nicht gefunden");
        }

        var item = await db.Set<EmployeeQualification>().SingleOrDefaultAsync(q => q.Id == qualificationId, cancellationToken);
        if (item is not null && item.EmployeeId != id)
        {
            return NotFound("Qualifikation nicht gefunden");
        }

        try
        {
            if (item is null)
            {
                item = EmployeeQualification.Create(qualificationId, id);
                db.Add(item);
            }

            item.Update(request.Kind, request.Name, request.Number, request.IssuedOn, request.ExpiresOn, request.Notes);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(ToView(item, Today(clock)));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteQualificationAsync(
        Guid id, Guid qualificationId, EstrichOneDbContext db, CancellationToken cancellationToken) =>
        await db.Set<EmployeeQualification>().Where(q => q.Id == qualificationId && q.EmployeeId == id).ExecuteDeleteAsync(cancellationToken) == 0
            ? NotFound("Qualifikation nicht gefunden")
            : TypedResults.NoContent();

    private static async Task<Results<Ok<EquipmentView>, ProblemHttpResult>> SaveEquipmentAsync(
        Guid id, Guid equipmentId, SaveEquipmentRequest request, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Set<Employee>().AnyAsync(e => e.Id == id, cancellationToken))
        {
            return NotFound("Mitarbeiter nicht gefunden");
        }

        var resource = request.ResourceId is { } rid ? await db.Set<Resource>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == rid, cancellationToken) : null;
        if (request.ResourceId is not null && resource is null)
        {
            return Validation("Unbekanntes Fahrzeug oder Gerät.");
        }

        var item = await db.Set<EmployeeEquipment>().SingleOrDefaultAsync(q => q.Id == equipmentId, cancellationToken);
        if (item is not null && item.EmployeeId != id)
        {
            return NotFound("Gerät nicht gefunden");
        }

        try
        {
            if (item is null)
            {
                item = EmployeeEquipment.Create(equipmentId, id);
                db.Add(item);
            }

            item.Update(request.ResourceId, string.IsNullOrWhiteSpace(request.Name) ? resource?.Name ?? string.Empty : request.Name,
                request.SerialNumber ?? resource?.Identifier, request.IssuedOn, request.ReturnedOn, request.Notes);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(new EquipmentView(item.Id, item.ResourceId, item.Name, item.SerialNumber, item.IssuedOn, item.ReturnedOn, item.Notes));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteEquipmentAsync(
        Guid id, Guid equipmentId, EstrichOneDbContext db, CancellationToken cancellationToken) =>
        await db.Set<EmployeeEquipment>().Where(q => q.Id == equipmentId && q.EmployeeId == id).ExecuteDeleteAsync(cancellationToken) == 0
            ? NotFound("Gerät nicht gefunden")
            : TypedResults.NoContent();

    private static async Task<Results<Ok<EmployeeDocumentView>, ProblemHttpResult>> AddDocumentAsync(
        Guid id, Guid documentId, AddEmployeeDocumentRequest request, EstrichOneDbContext db, IStoredFiles files, ICurrentUser currentUser,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!await db.Set<Employee>().AnyAsync(e => e.Id == id, cancellationToken))
        {
            return NotFound("Mitarbeiter nicht gefunden");
        }

        var existing = await db.Set<EmployeeDocument>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentId, cancellationToken);
        var file = (await files.FindAsync([request.FileId], cancellationToken)).GetValueOrDefault(request.FileId);
        if (existing is null)
        {
            // Nur eigene, vollständig hochgeladene Dateien ohne Bauakte (Personaldaten gehören nicht in eine Bauakte).
            if (file is null || !file.IsStored || file.BauakteId is not null || file.UploadedByUserId != currentUser.UserId)
            {
                return Validation("Die Datei wurde nicht (vollständig) hochgeladen.");
            }

            if (await db.Set<EmployeeDocument>().AnyAsync(d => d.FileId == request.FileId, cancellationToken))
            {
                return Validation("Die Datei ist bereits einem Mitarbeiter zugeordnet.");
            }

            try
            {
                existing = EmployeeDocument.Create(documentId, id, request.FileId, request.Category, request.Title, request.ValidUntil, currentUser.UserId,
                    clock.GetUtcNow());
            }
            catch (ArgumentException exception)
            {
                return Validation(exception.Message);
            }

            db.Add(existing);
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (existing.EmployeeId != id)
        {
            return NotFound("Dokument nicht gefunden");
        }

        return TypedResults.Ok(new EmployeeDocumentView(existing.Id, existing.FileId, existing.Category, existing.Title, file?.OriginalName, file?.ContentType,
            file?.SizeBytes ?? 0, existing.ValidUntil, existing.AddedAt));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteDocumentAsync(
        Guid id, Guid documentId, EstrichOneDbContext db, CancellationToken cancellationToken) =>
        await db.Set<EmployeeDocument>().Where(d => d.Id == documentId && d.EmployeeId == id).ExecuteDeleteAsync(cancellationToken) == 0
            ? NotFound("Dokument nicht gefunden")
            : TypedResults.NoContent();

    private static async Task<Results<Ok<EmployeeOnboardingResponse>, ProblemHttpResult>> OnboardingAsync(
        Guid id, EmployeeOnboardingRequest request, EstrichOneDbContext db, IUserOnboarding onboarding, IGeneratedDocuments generated,
        ICurrentUser currentUser, TimeProvider clock, HttpRequest http, CancellationToken cancellationToken)
    {
        var employee = await db.Set<Employee>().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return NotFound("Mitarbeiter nicht gefunden");
        }

        byte[]? qr = null;
        if (!string.IsNullOrWhiteSpace(request.DeviceQrPng))
        {
            try
            {
                qr = Convert.FromBase64String(request.DeviceQrPng.Contains(',', StringComparison.Ordinal) ? request.DeviceQrPng[(request.DeviceQrPng.IndexOf(',') + 1)..] : request.DeviceQrPng);
            }
            catch (FormatException)
            {
                return Validation("Der QR-Code ist kein gültiges Bild.");
            }

            if (qr.Length < 8 || qr[0] != 0x89 || qr[1] != (byte)'P')
            {
                return Validation("Der QR-Code muss ein PNG-Bild sein.");
            }
        }

        OnboardingAccount account;
        var created = false;
        if (employee.UserId is { } linked)
        {
            account = await onboarding.NewPasswordAsync(linked, cancellationToken);
        }
        else if (request.LinkUserId is { } link)
        {
            if (await db.Set<Employee>().AnyAsync(e => e.UserId == link && e.Id != id, cancellationToken))
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Benutzerkonto bereits verknüpft",
                    detail: "Dieses Benutzerkonto gehört schon zu einem anderen Mitarbeiter.", extensions: new Dictionary<string, object?> { ["code"] = "USER_LINKED" });
            }

            account = await onboarding.NewPasswordAsync(link, cancellationToken);
            if (account.UserId is not null)
            {
                employee.LinkUser(link);
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.UserName) || request.RoleIds is not { Count: > 0 })
            {
                return Validation("Bitte Benutzername und mindestens eine Rolle angeben.");
            }

            account = await onboarding.CreateAccountAsync(employee.DisplayName, request.UserName.Trim(), request.Email ?? employee.Email, request.RoleIds, cancellationToken);
            if (account.UserId is { } newId)
            {
                employee.LinkUser(newId);
                created = true;
            }
        }

        if (account.UserId is not { } userId || account.Password is not { } password)
        {
            return account.ErrorCode == "USER_EXISTS"
                ? TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Benutzer existiert bereits", detail: account.Error,
                    extensions: new Dictionary<string, object?> { ["code"] = "USER_EXISTS" })
                : account.ErrorCode == "USER_NOT_FOUND" ? NotFound("Benutzerkonto nicht gefunden") : Validation(account.Error ?? "Ungültige Angaben.");
        }

        await db.SaveChangesAsync(cancellationToken);

        var crews = await db.Set<Crew>().AsNoTracking().Where(c => c.IsActive && c.MemberUserIds.Contains(userId)).Select(c => c.Name).ToListAsync(cancellationToken);
        var contact = await db.Set<User>().AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => new { u.DisplayName, u.Phone, u.Email })
            .SingleOrDefaultAsync(cancellationToken);
        var serverUrl = onboarding.ServerUrl(http);
        var pdf = await onboarding.RenderAsync(userId, password, serverUrl, new OnboardingDetails(employee.JobTitle, crews, employee.EntryDate,
            contact?.DisplayName, contact?.Phone, contact?.Email, string.IsNullOrWhiteSpace(request.DeviceCode) ? null : request.DeviceCode.Trim(),
            request.DeviceLink, request.DeviceCodeExpiresAt, qr), cancellationToken);
        var now = clock.GetUtcNow();
        var fileId = await generated.StoreLooseAsync(currentUser.UserId, $"Onboarding {employee.DisplayName} {Today(clock):yyyy-MM-dd}.pdf", pdf, cancellationToken);
        db.Add(EmployeeDocument.Create(Guid.CreateVersion7(now), id, fileId, EmployeeDocumentCategory.Other,
            $"Onboarding-Unterlagen ({(created ? "neues Konto" : "neues Einmalpasswort")})", null, currentUser.UserId, now));
        await db.SaveChangesAsync(cancellationToken);
        var emailed = request.SendEmail && await onboarding.MailAsync(userId, pdf, serverUrl, cancellationToken);
        return TypedResults.Ok(new EmployeeOnboardingResponse(userId, account.UserName ?? "", password, created, fileId, emailed));
    }

    private static async Task<Results<Ok<List<CrewRef>>, ProblemHttpResult>> SetCrewsAsync(
        Guid id, SetEmployeeCrewsRequest request, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var employee = await db.Set<Employee>().AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return NotFound("Mitarbeiter nicht gefunden");
        }

        if (employee.UserId is not { } userId)
        {
            return Validation("Kolonnen brauchen ein Benutzerkonto – bitte zuerst eines verknüpfen.");
        }

        var wanted = request.CrewIds.Distinct().ToList();
        var crews = await db.Set<Crew>().ToListAsync(cancellationToken);
        if (wanted.Any(w => crews.All(c => c.Id != w)))
        {
            return Validation("Unbekannte Kolonne.");
        }

        foreach (var crew in crews)
        {
            var isMember = crew.MemberUserIds.Contains(userId);
            var shouldBe = wanted.Contains(crew.Id);
            if (isMember == shouldBe)
            {
                continue;
            }

            var members = shouldBe ? [.. crew.MemberUserIds, userId] : crew.MemberUserIds.Where(m => m != userId).ToList();
            crew.Update(crew.Name, crew.LeaderUserId == userId && !shouldBe ? null : crew.LeaderUserId, members, crew.Color, crew.IsActive, crew.SortOrder);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(crews.Where(c => wanted.Contains(c.Id)).OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CrewRef(c.Id, c.Name, c.Color)).ToList());
    }

    private static async Task<Results<Ok<AbsenceView>, ProblemHttpResult>> SaveAbsenceAsync(
        Guid id, Guid absenceId, SaveEmployeeAbsenceRequest request, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var employee = await db.Set<Employee>().AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (employee is null)
        {
            return NotFound("Mitarbeiter nicht gefunden");
        }

        if (employee.UserId is not { } userId)
        {
            return Validation("Abwesenheiten brauchen ein Benutzerkonto – bitte zuerst eines verknüpfen.");
        }

        try
        {
            var absence = Absence.Create(absenceId, userId, request.From, request.To, request.Kind, request.Note);
            var existing = await db.Set<Absence>().SingleOrDefaultAsync(a => a.Id == absenceId, cancellationToken);
            if (existing is not null)
            {
                if (existing.UserId != userId)
                {
                    return NotFound("Abwesenheit nicht gefunden");
                }

                db.Remove(existing);
            }

            db.Add(absence);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(new AbsenceView(absence.Id, userId, employee.DisplayName, absence.From, absence.To, absence.Kind, absence.Note));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAbsenceAsync(Guid id, Guid absenceId, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var userId = await db.Set<Employee>().Where(e => e.Id == id).Select(e => e.UserId).SingleOrDefaultAsync(cancellationToken);
        return userId is null || await db.Set<Absence>().Where(a => a.Id == absenceId && a.UserId == userId).ExecuteDeleteAsync(cancellationToken) == 0
            ? NotFound("Abwesenheit nicht gefunden")
            : TypedResults.NoContent();
    }

    private static async Task<Ok<List<ExpiringQualification>>> ExpiringAsync(
        EstrichOneDbContext db, TimeProvider clock, CancellationToken cancellationToken, [FromQuery] int days = 60)
    {
        var today = Today(clock);
        var limit = today.AddDays(Math.Clamp(days, 0, 730));
        var items = await db.Set<EmployeeQualification>().AsNoTracking().Where(q => q.ExpiresOn != null && q.ExpiresOn <= limit)
            .Join(db.Set<Employee>().AsNoTracking().Where(e => e.ExitDate == null || e.ExitDate >= today), q => q.EmployeeId, e => e.Id, (q, e) => new { q, e })
            .OrderBy(x => x.q.ExpiresOn).ToListAsync(cancellationToken);
        return TypedResults.Ok(items.Select(x => new ExpiringQualification(x.e.Id, x.e.DisplayName, ToView(x.q, today))).ToList());
    }

    private static ProblemHttpResult NotFound(string title) =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: title, extensions: new Dictionary<string, object?> { ["code"] = "NOT_FOUND" });

    private static ProblemHttpResult Conflict(string detail, string code) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Konflikt", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    private static ProblemHttpResult Validation(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Ungültige Angaben", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
}

/// <summary>Personaldokumente und AU-Bescheinigungen sind für „Personalstamm pflegen“ lesbar (auch wenn jemand anderes sie hochgeladen hat).</summary>
/// <summary>Bild und Dokumente von Fahrzeugen/Geräten lesen alle mit Planungsrecht.</summary>
internal sealed class ResourceFileGrant(EstrichOneDbContext db, IUserPermissions permissions) : IFileReadGrant
{
    public async Task<bool> CanReadAsync(Guid fileId, CancellationToken cancellationToken) =>
        await db.Set<Resource>().AnyAsync(r => r.ImageFileId == fileId || r.DocumentFileIds.Contains(fileId), cancellationToken)
        && await permissions.HasAsync(Permissions.PlanningRead, cancellationToken);
}

internal sealed class EmployeeDocumentGrant(EstrichOneDbContext db, IUserPermissions permissions) : IFileReadGrant
{
    public async Task<bool> CanReadAsync(Guid fileId, CancellationToken cancellationToken) =>
        (await db.Set<EmployeeDocument>().AnyAsync(d => d.FileId == fileId, cancellationToken)
            || await db.Set<Absence>().AnyAsync(a => a.CertificateFileId == fileId, cancellationToken))
        && await permissions.HasAsync(Permissions.HrManage, cancellationToken);
}
