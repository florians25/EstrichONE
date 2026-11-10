using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>Gemeinsame Rechenwege der Personalverwaltung: Feiertage, Abwesenheitsarten, Urlaubskonten, Genehmiger, Datenschutz bei Krankmeldungen.</summary>
public sealed class HrService(EstrichOneDbContext db, IUserPermissions permissions, IRoleAssignments roles, ICurrentUser currentUser, TimeProvider clock)
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    private HrSettings? _settings;
    private Dictionary<Guid, AbsenceType>? _types;

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Berlin).DateTime);

    public async Task<HrSettings> SettingsAsync(CancellationToken cancellationToken) =>
        _settings ??= await db.Set<HrSettings>().AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new HrSettings();

    public async Task<HashSet<DateOnly>> HolidaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        GermanHolidays.Dates(from, to, (await SettingsAsync(cancellationToken)).State);

    /// <summary>Alle Abwesenheitsarten (auch inaktive, damit ältere Einträge ihre Art behalten).</summary>
    public async Task<Dictionary<Guid, AbsenceType>> TypesAsync(CancellationToken cancellationToken) =>
        _types ??= await db.Set<AbsenceType>().AsNoTracking().ToDictionaryAsync(t => t.Id, cancellationToken);

    public static Guid TypeOf(Absence a) => a.TypeId ?? StandardAbsenceTypes.ForKind(a.Kind);

    public static Guid TypeOf(AbsenceRequest r) => r.TypeId ?? StandardAbsenceTypes.ForKind(r.Kind);

    /// <summary>Krankmeldungen sieht nur die Personalverwaltung; alle anderen sehen „abwesend“.</summary>
    public Task<bool> MaySeeSicknessAsync(CancellationToken cancellationToken) => permissions.HasAsync(Permissions.HrManage, cancellationToken);

    public static AbsenceKind Mask(AbsenceKind kind, bool maySeeSickness) => kind == AbsenceKind.Sick && !maySeeSickness ? AbsenceKind.Other : kind;

    /// <summary>Art für die Anzeige: Krankheitsarten ohne Recht als „Sonstige Abwesenheit“.</summary>
    public static Guid MaskType(Guid typeId, AbsenceKind kind, bool maySeeSickness) => kind == AbsenceKind.Sick && !maySeeSickness ? StandardAbsenceTypes.Other : typeId;

    /// <summary>Urlaubskonten mehrerer Mitarbeiter für ein Jahr (eine Abfrage je Tabelle).</summary>
    public async Task<Dictionary<Guid, VacationSummary>> AccountsAsync(IReadOnlyCollection<Employee> employees, int year, bool includeSick,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, VacationSummary>();
        if (employees.Count == 0)
        {
            return result;
        }

        var settings = await SettingsAsync(cancellationToken);
        var types = (await TypesAsync(cancellationToken)).ToDictionary(x => x.Key, x => AbsenceTypeRule.From(x.Value));
        var firstYear = Math.Min(year, Math.Max(settings.AccountingStartYear, year - 5));
        var from = new DateOnly(firstYear, 1, 1);
        var to = new DateOnly(year, 12, 31);
        var employeeIds = employees.Select(e => e.Id).ToList();
        var userIds = employees.Select(e => e.UserId).OfType<Guid>().ToList();
        var accounts = await db.Set<VacationAccount>().AsNoTracking().Where(a => employeeIds.Contains(a.EmployeeId) && a.Year >= firstYear && a.Year <= year)
            .ToListAsync(cancellationToken);
        var absences = await db.Set<Absence>().AsNoTracking().Where(a => userIds.Contains(a.UserId) && a.To >= from && a.From <= to).ToListAsync(cancellationToken);
        var pending = await db.Set<AbsenceRequest>().AsNoTracking()
            .Where(r => userIds.Contains(r.UserId) && r.Status == AbsenceRequestStatus.Pending && r.To >= new DateOnly(year, 1, 1) && r.From <= to)
            .ToListAsync(cancellationToken);
        var holidays = GermanHolidays.Dates(from, to, settings.State);
        var today = Today;
        foreach (var e in employees)
        {
            var own = e.UserId is { } uid
                ? absences.Where(a => a.UserId == uid).Select(a => new AbsenceSpan(a.From, a.To, TypeOf(a), a.HalfDayStart, a.HalfDayEnd)).ToList()
                : [];
            var open = e.UserId is { } u
                ? pending.Where(r => r.UserId == u).Select(r => new AbsenceSpan(r.From, r.To, TypeOf(r), r.HalfDayStart, r.HalfDayEnd)).ToList()
                : [];
            // Für den Übertrag zählt der Rest am Jahresende (verfallener Übertrag des Vorjahres eingerechnet).
            VacationSummary For(int y, IReadOnlyList<AbsenceSpan> requested, DateOnly asOf)
            {
                var account = accounts.FirstOrDefault(a => a.EmployeeId == e.Id && a.Year == y);
                decimal? auto = null;
                if (account?.CarryOver is null && y - 1 >= settings.AccountingStartYear && y - 1 >= firstYear
                    && (e.EntryDate is null || e.EntryDate.Value.Year <= y - 1))
                {
                    var end = new DateOnly(y - 1, 12, 31);
                    auto = Math.Max(0, For(y - 1, [], asOf > end ? asOf : end).Remaining);
                }

                return VacationCalculator.Calculate(new VacationInput(y, asOf, e.VacationDaysPerYear, e.WorkDays, e.EntryDate, e.ExitDate, account, auto, own,
                    requested, holidays, settings.CarryOverExpiry(y), settings.ProrateEntitlement, types, includeSick));
            }

            result[e.Id] = For(year, open, today);
        }

        return result;
    }

    public async Task<VacationSummary> AccountAsync(Employee employee, int year, bool includeSick, CancellationToken cancellationToken) =>
        (await AccountsAsync([employee], year, includeSick, cancellationToken))[employee.Id];

    /// <summary>Arbeitstage eines Zeitraums für einen Mitarbeiter (ohne Personalstamm: Montag bis Freitag).</summary>
    public async Task<decimal> WorkingDaysAsync(Employee? employee, DateOnly from, DateOnly to, bool halfDayStart, bool halfDayEnd, CancellationToken cancellationToken)
    {
        var holidays = await HolidaysAsync(from, to, cancellationToken);
        return VacationCalculator.WorkingDays(employee?.WorkDays ?? VacationCalculator.DefaultWorkDays, from, to, halfDayStart, halfDayEnd, holidays);
    }

    /// <summary>Wer über Anträge dieses Mitarbeiters entscheiden darf: Recht „Urlaubsanträge genehmigen“, ggf. Kolonnenführer; nie man selbst (außer Personalverwaltung).</summary>
    public async Task<IReadOnlyList<Guid>> ApproversAsync(Guid userId, CancellationToken cancellationToken)
    {
        var ids = (await roles.GetUserIdsWithPermissionAsync(Permissions.AbsenceApprove, cancellationToken)).ToHashSet();
        if ((await SettingsAsync(cancellationToken)).CrewLeadersApprove)
        {
            foreach (var leader in await db.Set<Crew>().AsNoTracking().Where(c => c.IsActive && c.LeaderUserId != null && c.MemberUserIds.Contains(userId))
                .Select(c => c.LeaderUserId!.Value).ToListAsync(cancellationToken))
            {
                ids.Add(leader);
            }
        }

        ids.Remove(userId);
        var active = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id) && u.IsActive).Select(u => u.Id).ToListAsync(cancellationToken);
        return active;
    }

    /// <summary>Darf der angemeldete Benutzer über Anträge dieses Mitarbeiters entscheiden?</summary>
    public async Task<bool> MayDecideAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == currentUser.UserId)
        {
            return await permissions.HasAsync(Permissions.HrManage, cancellationToken);
        }

        if (await permissions.HasAsync(Permissions.AbsenceApprove, cancellationToken))
        {
            return true;
        }

        return (await SettingsAsync(cancellationToken)).CrewLeadersApprove
            && await db.Set<Crew>().AnyAsync(c => c.IsActive && c.LeaderUserId == currentUser.UserId && c.MemberUserIds.Contains(userId), cancellationToken);
    }

    /// <summary>Mitarbeiter, deren Anträge der angemeldete Benutzer als Kolonnenführer genehmigen darf (leer ohne Einstellung).</summary>
    public async Task<List<Guid>> LedUserIdsAsync(CancellationToken cancellationToken)
    {
        if (!(await SettingsAsync(cancellationToken)).CrewLeadersApprove)
        {
            return [];
        }

        var crews = await db.Set<Crew>().AsNoTracking().Where(c => c.IsActive && c.LeaderUserId == currentUser.UserId).ToListAsync(cancellationToken);
        return crews.SelectMany(c => c.MemberUserIds).Where(id => id != currentUser.UserId).Distinct().ToList();
    }
}
