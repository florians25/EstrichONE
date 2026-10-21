using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>Gemeinsame Rechenwege der Personalverwaltung: Feiertage, Resturlaub, Datenschutz bei Krankmeldungen.</summary>
public sealed class HrService(EstrichOneDbContext db, IUserPermissions permissions, TimeProvider clock)
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Berlin).DateTime);

    public async Task<HrSettings> SettingsAsync(CancellationToken cancellationToken) =>
        await db.Set<HrSettings>().AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new HrSettings();

    public async Task<HashSet<DateOnly>> HolidaysAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        GermanHolidays.Dates(from, to, (await SettingsAsync(cancellationToken)).State);

    /// <summary>Krankmeldungen sieht nur die Personalverwaltung; alle anderen sehen „abwesend“.</summary>
    public Task<bool> MaySeeSicknessAsync(CancellationToken cancellationToken) => permissions.HasAsync(Permissions.HrManage, cancellationToken);

    public static AbsenceKind Mask(AbsenceKind kind, bool maySeeSickness) => kind == AbsenceKind.Sick && !maySeeSickness ? AbsenceKind.Other : kind;

    /// <summary>
    /// Urlaub im Jahr von <paramref name="today"/>: Anspruch + Übertrag − genommen (bis heute) − geplant (ab morgen) = Rest.
    /// Gezählt werden Arbeitstage nach Arbeitszeitmodell ohne Feiertage; Sonderurlaub und Berufsschule zählen nicht.
    /// </summary>
    public static VacationSummary Vacation(Employee e, IEnumerable<Absence> absences, DateOnly today, IReadOnlySet<DateOnly> holidays, bool includeSick,
        decimal requested = 0)
    {
        var start = new DateOnly(today.Year, 1, 1);
        var end = new DateOnly(today.Year, 12, 31);
        decimal taken = 0, planned = 0;
        var sick = 0;
        foreach (var a in absences.Where(a => a.To >= start && a.From <= end))
        {
            var from = a.From < start ? start : a.From;
            var to = a.To > end ? end : a.To;
            if (a.Kind == AbsenceKind.Vacation)
            {
                taken += to <= today ? e.WorkingDays(from, to, holidays) : from > today ? 0 : e.WorkingDays(from, today, holidays);
                planned += from > today ? e.WorkingDays(from, to, holidays) : to > today ? e.WorkingDays(today.AddDays(1), to, holidays) : 0;
            }
            else if (a.Kind == AbsenceKind.Sick && from <= today)
            {
                sick += e.WorkingDays(from, to > today ? today : to, holidays);
            }
        }

        return new VacationSummary(today.Year, e.VacationDaysPerYear, e.VacationCarryOver, taken, planned,
            e.VacationDaysPerYear + e.VacationCarryOver - taken - planned, includeSick ? sick : 0, requested);
    }
}
