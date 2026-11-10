using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

/// <param name="Key">Stabiler Schlüssel (Standardarten: vacation, sick, child_sick, school, training, special_leave, overtime, unpaid, closure, other).</param>
/// <param name="IsSickness">Krankheitsart: nur für die Personalverwaltung sichtbar, sonst „abwesend“.</param>
public sealed record AbsenceTypeView(Guid Id, string Key, string Name, string Code, string Color, AbsenceKind Kind, bool RequiresApproval, bool CountsAsVacation,
    bool Requestable, bool IsPaid, bool IsActive, int SortOrder, bool IsSystem, bool IsSickness);

public sealed record SaveAbsenceTypeRequest([property: Required, MaxLength(60)] string Name, [property: Required, MaxLength(3)] string Code,
    [property: Required, MaxLength(7)] string Color, AbsenceKind Kind = AbsenceKind.Other, bool RequiresApproval = true, bool CountsAsVacation = false,
    bool Requestable = true, bool IsPaid = true, bool IsActive = true, int SortOrder = 100);

/// <param name="WorkingDays">Arbeitstage (Montag bis Freitag ohne Feiertage).</param>
/// <param name="Employees">Anzahl eingetragener Mitarbeiter.</param>
public sealed record ClosureView(Guid Id, string Name, DateOnly From, DateOnly To, Guid TypeId, decimal WorkingDays, int Employees, long Version);

/// <param name="TypeId">Art (Standard: Betriebsferien, zählt vom Urlaub; „Sonstige Abwesenheit“ zählt nicht).</param>
public sealed record SaveClosureRequest([property: Required, MaxLength(100)] string Name, DateOnly From, DateOnly To, Guid? TypeId = null);

/// <summary>Urlaubskonto eines Mitarbeiters im Jahr (Jahresübersicht).</summary>
/// <param name="Account">Erfasste Abweichungen (leer: alles berechnet).</param>
public sealed record VacationAccountRow(Guid EmployeeId, Guid? UserId, string Name, string? PersonnelNumber, IReadOnlyList<CrewRef> Crews, DateOnly? EntryDate,
    DateOnly? ExitDate, VacationSummary Vacation, VacationAccountValues? Account);

public sealed record VacationAccountValues(decimal? Entitlement, decimal? CarryOver, DateOnly? CarryOverExpiresOn, decimal Adjustment, string? Note, long Version);

/// <param name="Year">Kontojahr.</param>
/// <param name="Entitlement">Abweichender Jahresanspruch (leer: laut Personalstamm, ggf. anteilig).</param>
/// <param name="CarryOver">Übertrag aus dem Vorjahr (leer: automatisch).</param>
/// <param name="CarryOverExpiresOn">Abweichendes Verfallsdatum des Übertrags.</param>
/// <param name="Adjustment">Korrektur in Tagen (+/−).</param>
public sealed record SaveVacationAccountRequest(int Year, decimal? Entitlement, decimal? CarryOver, DateOnly? CarryOverExpiresOn, decimal Adjustment = 0,
    [property: MaxLength(500)] string? Note = null);

/// <summary>Verwaltung: Abwesenheitsarten, Betriebsferien/Brückentage, Urlaubskonten mit Jahresübersicht (CSV, PDF).</summary>
internal static class AbsenceAdminEndpoints
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private static readonly string[] CsvHeader =
    [
        "Personalnummer", "Mitarbeiter", "Kolonne", "Eintritt", "Austritt", "Jahresanspruch", "Anspruch", "Übertrag", "Verfällt am", "Verfallen", "Korrektur",
        "Genommen", "Geplant", "Beantragt", "Rest", "Rest nach Anträgen",
    ];

    public static void Map(RouteGroupBuilder hr)
    {
        hr.MapGet("/absence-types", TypesAsync).WithName("ListAbsenceTypes").WithSummary("Abwesenheitsarten mit Farbe und Regeln (all = auch inaktive)")
            .RequirePermission(Permissions.AbsenceRequest);
        hr.MapPut("/absence-types/{id:guid}", SaveTypeAsync).WithName("SaveAbsenceType")
            .WithSummary("Abwesenheitsart anlegen oder ändern (Standardarten: Grundart fest; löschen = deaktivieren)")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HrManage);

        hr.MapGet("/closures", ListClosuresAsync).WithName("ListCompanyClosures").WithSummary("Betriebsferien und Brückentage eines Jahres")
            .RequirePermission(Permissions.AbsenceRequest);
        hr.MapPut("/closures/{id:guid}", SaveClosureAsync).WithName("SaveCompanyClosure")
            .WithSummary("Betriebsferien oder Brückentag speichern; wird für alle aktiven Mitarbeiter als Abwesenheit eingetragen")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HrManage);
        hr.MapDelete("/closures/{id:guid}", DeleteClosureAsync).WithName("DeleteCompanyClosure").ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.HrManage);

        hr.MapGet("/vacation-accounts", AccountsAsync).WithName("ListVacationAccounts")
            .WithSummary("Urlaubskonten aller Mitarbeiter eines Jahres: Anspruch, Übertrag, Verfall, genommen, geplant, beantragt, Rest, Tage je Art")
            .RequirePermission(Permissions.HrRead);
        hr.MapGet("/vacation-accounts.csv", CsvAsync).WithName("VacationAccountsCsv").WithSummary("Jahresübersicht der Urlaubskonten als CSV (Semikolon, UTF-8)")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv").RequirePermission(Permissions.HrRead);
        hr.MapGet("/vacation-accounts.pdf", HrPrint.AccountsAsync).WithName("VacationAccountsPdf").WithSummary("Jahresübersicht der Urlaubskonten als PDF")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").RequirePermission(Permissions.HrRead);
        hr.MapPut("/vacation-accounts/{employeeId:guid}", SaveAccountAsync).WithName("SaveVacationAccount")
            .WithSummary("Urlaubskonto korrigieren: abweichender Anspruch, Übertrag, Verfallsdatum, Korrektur")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HrManage);
    }

    internal static AbsenceTypeView ToView(AbsenceType t) => new(t.Id, t.Key, t.Name, t.Code, t.Color, t.Kind, t.RequiresApproval, t.CountsAsVacation,
        t.Requestable, t.IsPaid, t.IsActive, t.SortOrder, t.IsSystem, t.IsSickness);

    // ---- Arten ---------------------------------------------------------------------------------------------------

    private static async Task<Ok<List<AbsenceTypeView>>> TypesAsync(EstrichOneDbContext db, CancellationToken cancellationToken, [FromQuery] bool all = false) =>
        TypedResults.Ok((await db.Set<AbsenceType>().AsNoTracking().Where(t => all || t.IsActive).OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync(cancellationToken))
            .Select(ToView).ToList());

    private static async Task<Results<Ok<AbsenceTypeView>, ProblemHttpResult>> SaveTypeAsync(
        Guid id, SaveAbsenceTypeRequest request, EstrichOneDbContext db, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var type = await db.Set<AbsenceType>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        try
        {
            if (type is null)
            {
                type = AbsenceType.Create(id, $"custom_{id.ToString("N")[..12]}", request.Kind, false);
                db.Add(type);
            }

            type.Update(request.Name, request.Code.Trim(), request.Color, request.Kind, request.RequiresApproval, request.CountsAsVacation,
                request.Requestable, request.IsPaid, request.IsActive, request.SortOrder);
        }
        catch (ArgumentException exception)
        {
            return HrEndpoints.Validation(exception.Message);
        }

        if (!type.IsActive && type.Id == StandardAbsenceTypes.Vacation)
        {
            return HrEndpoints.Validation("Die Art Urlaub lässt sich nicht deaktivieren.");
        }

        audit.Record("hr.absence_type.saved", currentUser.UserId, "absence_type", type.Id.ToString(), new Dictionary<string, string?>
        {
            ["key"] = type.Key, ["name"] = type.Name, ["kind"] = type.Kind.ToString(), ["requiresApproval"] = type.RequiresApproval ? "ja" : "nein",
            ["countsAsVacation"] = type.CountsAsVacation ? "ja" : "nein", ["active"] = type.IsActive ? "ja" : "nein",
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToView(type));
    }

    // ---- Betriebsferien ------------------------------------------------------------------------------------------

    internal static async Task<List<ClosureView>> ClosuresAsync(EstrichOneDbContext db, HrService hr, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var closures = await db.Set<CompanyClosure>().AsNoTracking().Where(c => c.From <= to && c.To >= from).OrderBy(c => c.From).ToListAsync(cancellationToken);
        if (closures.Count == 0)
        {
            return [];
        }

        var ids = closures.Select(c => c.Id).ToList();
        var counts = await db.Set<Absence>().AsNoTracking().Where(a => a.ClosureId != null && ids.Contains(a.ClosureId.Value))
            .GroupBy(a => a.ClosureId!.Value).Select(g => new { g.Key, Count = g.Select(a => a.UserId).Distinct().Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var holidays = await hr.HolidaysAsync(closures.Min(c => c.From), closures.Max(c => c.To), cancellationToken);
        return closures.Select(c => new ClosureView(c.Id, c.Name, c.From, c.To, c.TypeId,
            VacationCalculator.WorkingDays(VacationCalculator.DefaultWorkDays, c.From, c.To, false, false, holidays), counts.GetValueOrDefault(c.Id), c.Version)).ToList();
    }

    private static async Task<Ok<List<ClosureView>>> ListClosuresAsync(EstrichOneDbContext db, HrService hr, CancellationToken cancellationToken,
        [FromQuery] int? year = null)
    {
        var y = year is >= 2000 and <= 2100 ? year.Value : hr.Today.Year;
        return TypedResults.Ok(await ClosuresAsync(db, hr, new DateOnly(y, 1, 1), new DateOnly(y, 12, 31), cancellationToken));
    }

    /// <summary>Für alle aktiven Benutzer (mit Personalstamm: nur im Beschäftigungszeitraum) als Abwesenheit eintragen.</summary>
    internal static async Task<int> ApplyClosureAsync(CompanyClosure closure, AbsenceType type, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var old = await db.Set<Absence>().Where(a => a.ClosureId == closure.Id).ToListAsync(cancellationToken);
        db.RemoveRange(old);
        var employees = await db.Set<Employee>().AsNoTracking().Where(e => e.UserId != null).ToListAsync(cancellationToken);
        var byUser = employees.GroupBy(e => e.UserId!.Value).ToDictionary(g => g.Key, g => g.First());
        var users = await db.Set<User>().AsNoTracking().Where(u => u.IsActive).Select(u => u.Id).ToListAsync(cancellationToken);
        var count = 0;
        foreach (var userId in users)
        {
            var from = closure.From;
            var to = closure.To;
            if (byUser.GetValueOrDefault(userId) is { } e)
            {
                from = e.EntryDate is { } entry && entry > from ? entry : from;
                to = e.ExitDate is { } exit && exit < to ? exit : to;
            }

            if (to < from)
            {
                continue;
            }

            db.Add(Absence.Create(Guid.CreateVersion7(), userId, from, to, type.Kind, closure.Name).WithDetails(type.Id, false, false, closure.Id));
            count++;
        }

        return count;
    }

    /// <summary>Neu verknüpfter Mitarbeiter: laufende und künftige Betriebsferien eintragen.</summary>
    internal static async Task ApplyClosuresForUserAsync(Guid userId, DateOnly? entry, DateOnly? exit, DateOnly today, EstrichOneDbContext db,
        CancellationToken cancellationToken)
    {
        var closures = await db.Set<CompanyClosure>().AsNoTracking().Where(c => c.To >= today).ToListAsync(cancellationToken);
        if (closures.Count == 0)
        {
            return;
        }

        var ids = closures.Select(c => c.Id).ToList();
        var existing = await db.Set<Absence>().AsNoTracking().Where(a => a.UserId == userId && a.ClosureId != null && ids.Contains(a.ClosureId.Value))
            .Select(a => a.ClosureId!.Value).ToListAsync(cancellationToken);
        var types = await db.Set<AbsenceType>().AsNoTracking().ToDictionaryAsync(t => t.Id, cancellationToken);
        foreach (var closure in closures.Where(c => !existing.Contains(c.Id)))
        {
            var from = entry is { } e && e > closure.From ? e : closure.From;
            var to = exit is { } x && x < closure.To ? x : closure.To;
            if (to >= from && types.GetValueOrDefault(closure.TypeId) is { } type)
            {
                db.Add(Absence.Create(Guid.CreateVersion7(), userId, from, to, type.Kind, closure.Name).WithDetails(type.Id, false, false, closure.Id));
            }
        }
    }

    private static async Task<Results<Ok<ClosureView>, ProblemHttpResult>> SaveClosureAsync(
        Guid id, SaveClosureRequest request, EstrichOneDbContext db, HrService hr, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var type = await db.Set<AbsenceType>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == (request.TypeId ?? StandardAbsenceTypes.Closure), cancellationToken);
        if (type is null || type.IsSickness)
        {
            return HrEndpoints.Validation("Bitte eine Art für die Betriebsferien wählen (keine Krankheitsart).");
        }

        var closure = await db.Set<CompanyClosure>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (closure is null)
        {
            closure = CompanyClosure.Create(id);
            db.Add(closure);
        }

        try
        {
            closure.Update(request.Name, request.From, request.To, type.Id);
        }
        catch (ArgumentException exception)
        {
            return HrEndpoints.Validation(exception.Message);
        }

        var count = await ApplyClosureAsync(closure, type, db, cancellationToken);
        audit.Record("hr.closure.saved", currentUser.UserId, "company_closure", closure.Id.ToString(), new Dictionary<string, string?>
        {
            ["name"] = closure.Name, ["from"] = closure.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["to"] = closure.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["type"] = type.Key, ["employees"] = count.ToString(CultureInfo.InvariantCulture),
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await ClosuresAsync(db, hr, closure.From, closure.To, cancellationToken)).Single(c => c.Id == closure.Id));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteClosureAsync(Guid id, EstrichOneDbContext db, IAuditLog audit, ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var closure = await db.Set<CompanyClosure>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (closure is null)
        {
            return HrEndpoints.Problem(StatusCodes.Status404NotFound, "Betriebsferien nicht gefunden", null, "NOT_FOUND");
        }

        db.RemoveRange(await db.Set<Absence>().Where(a => a.ClosureId == id).ToListAsync(cancellationToken));
        db.Remove(closure);
        audit.Record("hr.closure.deleted", currentUser.UserId, "company_closure", id.ToString(), new Dictionary<string, string?> { ["name"] = closure.Name });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    // ---- Urlaubskonten -------------------------------------------------------------------------------------------

    internal static async Task<List<VacationAccountRow>> RowsAsync(EstrichOneDbContext db, HrService hr, IUserPermissions permissions, int year,
        CancellationToken cancellationToken)
    {
        var start = new DateOnly(year, 1, 1);
        var end = new DateOnly(year, 12, 31);
        var employees = await db.Set<Employee>().AsNoTracking().Where(e => (e.ExitDate == null || e.ExitDate >= start) && (e.EntryDate == null || e.EntryDate <= end))
            .ToListAsync(cancellationToken);
        var sick = await permissions.HasAsync(Permissions.HrManage, cancellationToken);
        var summaries = await hr.AccountsAsync(employees, year, sick, cancellationToken);
        var ids = employees.Select(e => e.Id).ToList();
        var accounts = await db.Set<VacationAccount>().AsNoTracking().Where(a => ids.Contains(a.EmployeeId) && a.Year == year).ToDictionaryAsync(a => a.EmployeeId, cancellationToken);
        var crews = await db.Set<Crew>().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);
        return employees.Select(e => new VacationAccountRow(e.Id, e.UserId, e.DisplayName, e.PersonnelNumber,
                e.UserId is { } u ? crews.Where(c => c.MemberUserIds.Contains(u)).Select(c => new CrewRef(c.Id, c.Name, c.Color)).ToList() : [],
                e.EntryDate, e.ExitDate, summaries[e.Id],
                accounts.GetValueOrDefault(e.Id) is { } a ? new VacationAccountValues(a.Entitlement, a.CarryOver, a.CarryOverExpiresOn, a.Adjustment, a.Note, a.Version) : null))
            .OrderBy(r => r.Name, StringComparer.CurrentCulture).ToList();
    }

    private static async Task<Ok<List<VacationAccountRow>>> AccountsAsync(EstrichOneDbContext db, HrService hr, IUserPermissions permissions,
        CancellationToken cancellationToken, [FromQuery] int? year = null) =>
        TypedResults.Ok(await RowsAsync(db, hr, permissions, year is >= 2000 and <= 2100 ? year.Value : hr.Today.Year, cancellationToken));

    private static async Task<FileContentHttpResult> CsvAsync(EstrichOneDbContext db, HrService hr, IUserPermissions permissions, CancellationToken cancellationToken,
        [FromQuery] int? year = null)
    {
        var y = year is >= 2000 and <= 2100 ? year.Value : hr.Today.Year;
        var rows = await RowsAsync(db, hr, permissions, y, cancellationToken);
        var types = rows.SelectMany(r => r.Vacation.ByType ?? []).Select(t => t.Name).Distinct().Order(StringComparer.CurrentCulture).ToList();
        string N(decimal v) => v.ToString("0.#", German);
        static string Q(string? v) => v is null ? string.Empty : v.Contains(';') || v.Contains('"') || v.Contains('\n') ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(';', CsvHeader.Concat(types.Select(t => $"Tage {t}"))));
        foreach (var r in rows)
        {
            var v = r.Vacation;
            csv.AppendLine(string.Join(';', new[]
            {
                Q(r.PersonnelNumber), Q(r.Name), Q(string.Join(", ", r.Crews.Select(c => c.Name))), r.EntryDate?.ToString("dd.MM.yyyy", German) ?? string.Empty,
                r.ExitDate?.ToString("dd.MM.yyyy", German) ?? string.Empty, N(v.AnnualEntitlement), N(v.Entitlement), N(v.CarryOver),
                v.CarryOverExpiresOn?.ToString("dd.MM.yyyy", German) ?? string.Empty, N(v.CarryOverExpired), N(v.Adjustment), N(v.Taken), N(v.Planned), N(v.Requested),
                N(v.Remaining), N(v.RemainingAfterRequests),
            }.Concat(types.Select(t => N((v.ByType ?? []).Where(x => x.Name == t).Sum(x => x.Days))))));
        }

        // BOM, damit Excel Umlaute richtig erkennt.
        return TypedResults.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8",
            $"Urlaubskonten {y}.csv");
    }

    private static async Task<Results<Ok<VacationAccountRow>, ProblemHttpResult>> SaveAccountAsync(
        Guid employeeId, SaveVacationAccountRequest request, EstrichOneDbContext db, HrService hr, IUserPermissions permissions, IAuditLog audit,
        ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var employee = await db.Set<Employee>().SingleOrDefaultAsync(e => e.Id == employeeId, cancellationToken);
        if (employee is null)
        {
            return HrEndpoints.Problem(StatusCodes.Status404NotFound, "Mitarbeiter nicht gefunden", null, "NOT_FOUND");
        }

        var year = request.Year;
        if (year is < 2000 or > 2100)
        {
            return HrEndpoints.Validation("Bitte ein Jahr zwischen 2000 und 2100 angeben.");
        }

        var account = await db.Set<VacationAccount>().SingleOrDefaultAsync(a => a.EmployeeId == employeeId && a.Year == year, cancellationToken);
        if (account is null)
        {
            account = VacationAccount.Create(Guid.CreateVersion7(), employeeId, year);
            db.Add(account);
        }

        try
        {
            account.Update(request.Entitlement, request.CarryOver, request.CarryOverExpiresOn, request.Adjustment, request.Note);
        }
        catch (ArgumentException exception)
        {
            return HrEndpoints.Validation(exception.Message);
        }

        if (year == hr.Today.Year)
        {
            // Personalstamm zeigt den Übertrag des laufenden Jahres.
            employee.SyncCarryOver(account.CarryOver ?? 0);
        }

        audit.Record("hr.vacation_account.saved", currentUser.UserId, "employee", employeeId.ToString(), new Dictionary<string, string?>
        {
            ["year"] = year.ToString(CultureInfo.InvariantCulture), ["entitlement"] = account.Entitlement?.ToString(CultureInfo.InvariantCulture),
            ["carryOver"] = account.CarryOver?.ToString(CultureInfo.InvariantCulture), ["expires"] = account.CarryOverExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["adjustment"] = account.Adjustment.ToString(CultureInfo.InvariantCulture),
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await RowsAsync(db, hr, permissions, year, cancellationToken)).Single(r => r.EmployeeId == employeeId));
    }
}
