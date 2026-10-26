using System.Globalization;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

public sealed record PersonRef(Guid Id, string Name);

public sealed record ResourceRef(Guid Id, string Name, ResourceKind Kind);

/// <param name="Warnings">Hinweise wie Doppelbelegung oder Abwesenheit – sie blockieren nicht, die Planung entscheidet.</param>
public sealed record AssignmentView(
    Guid Id, DateOnly Day, TimeOnly? StartTime, string Activity, Guid BauakteId, string? BauakteNumber, string BauakteTitle,
    string? BauakteAddress, Guid? CrewId, string? CrewName, string? CrewColor, IReadOnlyList<PersonRef> People,
    IReadOnlyList<ResourceRef> Resources, string? Notes, IReadOnlyList<string> Warnings, long Version,
    AssignmentKind Kind = AssignmentKind.Screed, TimeOnly? EndTime = null, BauakteStatus? BauakteStatus = null,
    IReadOnlyList<Guid>? ExcludedUserIds = null);

public sealed record AbsenceView(Guid Id, Guid UserId, string UserName, DateOnly From, DateOnly To, AbsenceKind Kind, string? Note);

/// <param name="Appointments">Termine aus den Kalendern mit Teilnehmern oder Kolonnen (Termine ohne Bauvorhaben im Plan).</param>
public sealed record PlanningRange(
    DateOnly From, DateOnly To, IReadOnlyList<AssignmentView> Assignments, IReadOnlyList<AbsenceView> Absences,
    IReadOnlyList<CalendarOccurrence>? Appointments = null, IReadOnlyList<Holiday>? Holidays = null);

/// <summary>Einsätze lesen und auf Konflikte prüfen (Doppelbelegung von Personen und Geräten, Abwesenheiten).</summary>
public sealed class PlanningService(EstrichOneDbContext db, CalendarService calendar, HrService hr)
{
    private static readonly Dictionary<AbsenceKind, string> AbsenceNames = new()
    {
        [AbsenceKind.Vacation] = "im Urlaub", [AbsenceKind.Sick] = "krankgemeldet", [AbsenceKind.Training] = "auf Schulung",
        [AbsenceKind.Other] = "abwesend", [AbsenceKind.SpecialLeave] = "im Sonderurlaub", [AbsenceKind.School] = "in der Berufsschule",
    };

    public async Task<PlanningRange> RangeAsync(DateOnly from, DateOnly to, Guid? onlyForUser, CancellationToken cancellationToken)
    {
        var assignments = await db.Set<Assignment>().AsNoTracking().Where(a => a.Day >= from && a.Day <= to).ToListAsync(cancellationToken);
        var crews = await db.Set<Crew>().AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);
        var absences = await db.Set<Absence>().AsNoTracking().Where(a => a.From <= to && a.To >= from).ToListAsync(cancellationToken);

        IReadOnlyList<Guid> PeopleOf(Assignment a) => a.PeopleWith(a.CrewId is { } crewId && crews.TryGetValue(crewId, out var crew) ? crew.MemberUserIds : []);

        if (onlyForUser is { } userId)
        {
            assignments = assignments.Where(a => PeopleOf(a).Contains(userId)).ToList();
            absences = absences.Where(a => a.UserId == userId).ToList();
        }

        // Termine der Beteiligten (für Konflikte alle, angezeigt nur die sichtbaren mit Teilnehmern/Kolonnen).
        var busy = await calendar.OccurrencesAsync(from.ToDateTime(TimeOnly.MinValue), to.AddDays(1).ToDateTime(TimeOnly.MinValue), null, true,
            cancellationToken, all: true);
        var shown = await calendar.OccurrencesAsync(from.ToDateTime(TimeOnly.MinValue), to.AddDays(1).ToDateTime(TimeOnly.MinValue), null, true,
            cancellationToken);
        if (onlyForUser is { } me)
        {
            shown = shown.Where(o => o.People.Any(p => p.Id == me)).ToList();
        }

        // Datenschutz: In der Planung heißt jede Krankheit „abwesend“ – Einzelheiten nur in der HR-Ansicht (Personal).
        const bool maySeeSickness = false;
        var settings = await hr.SettingsAsync(cancellationToken);
        var holidays = Enumerable.Range(from.Year, to.Year - from.Year + 1).SelectMany(y => GermanHolidays.For(y, settings.State))
            .Where(h => h.Date >= from && h.Date <= to).ToList();
        var userIds = assignments.SelectMany(PeopleOf).Concat(absences.Select(a => a.UserId)).Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var resourceIds = assignments.SelectMany(a => a.ResourceIds).Distinct().ToList();
        var resources = await db.Set<Resource>().AsNoTracking().Where(r => resourceIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
        var bauakteIds = assignments.Select(a => a.BauakteId).Distinct().ToList();
        var bauakten = await db.Set<Bauakte>().AsNoTracking().Where(b => bauakteIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, cancellationToken);

        // Konflikte über alle Einsätze des Tages, nicht nur die angezeigten (sonst sähe ein Mitarbeiter sie nie).
        var allOfDays = onlyForUser is null
            ? assignments
            : await db.Set<Assignment>().AsNoTracking().Where(a => a.Day >= from && a.Day <= to).ToListAsync(cancellationToken);
        var allAbsences = onlyForUser is null
            ? absences
            : await db.Set<Absence>().AsNoTracking().Where(a => a.From <= to && a.To >= from).ToListAsync(cancellationToken);

        string Name(Guid id) => users.GetValueOrDefault(id) ?? "Unbekannt";

        var views = assignments.OrderBy(a => a.Day).ThenBy(a => a.StartTime).ThenBy(a => a.Activity).Select(a =>
        {
            var warnings = new List<string>();
            var people = PeopleOf(a);
            var (spanFrom, spanTo) = a.Span();
            bool Overlaps(Assignment other)
            {
                var (otherFrom, otherTo) = other.Span();
                return other.Id != a.Id && other.Day == a.Day && otherFrom < spanTo && spanFrom < otherTo;
            }

            var dayStart = a.Day.ToDateTime(spanFrom);
            var dayEnd = spanTo == TimeOnly.MaxValue ? a.Day.AddDays(1).ToDateTime(TimeOnly.MinValue) : a.Day.ToDateTime(spanTo);
            foreach (var person in people)
            {
                if (allOfDays.Any(other => Overlaps(other) && PeopleOf(other).Contains(person)))
                {
                    warnings.Add($"{Name(person)} ist an diesem Tag mehrfach eingeplant.");
                }

                if (allAbsences.FirstOrDefault(abs => abs.UserId == person && abs.Covers(a.Day)) is { } absence)
                {
                    warnings.Add($"{Name(person)} ist {AbsenceNames[HrService.Mask(absence.Kind, maySeeSickness)]}.");
                }

                if (busy.FirstOrDefault(o => o.Start < dayEnd && dayStart < o.End && o.People.Any(p => p.Id == person)) is { } appointment)
                {
                    warnings.Add($"{Name(person)} hat zur selben Zeit den Termin „{appointment.Title}“.");
                }
            }

            foreach (var resourceId in a.ResourceIds)
            {
                if (allOfDays.Any(other => Overlaps(other) && other.ResourceIds.Contains(resourceId)))
                {
                    warnings.Add($"{(resources.TryGetValue(resourceId, out var r) ? r.Name : "Ein Gerät")} ist an diesem Tag mehrfach eingeplant.");
                }
            }

            var bauakte = bauakten.GetValueOrDefault(a.BauakteId);
            var crew = a.CrewId is { } crewId ? crews.GetValueOrDefault(crewId) : null;
            var place = bauakte is null ? null : string.Join(" ", new[] { bauakte.PostalCode, bauakte.City }.Where(s => !string.IsNullOrEmpty(s)));
            var address = bauakte is null ? null : string.Join(", ", new[] { bauakte.Street, place }.Where(s => !string.IsNullOrEmpty(s)));
            return new AssignmentView(a.Id, a.Day, a.StartTime, a.Activity, a.BauakteId, bauakte?.Number, bauakte?.Title ?? "Bauakte gelöscht",
                string.IsNullOrEmpty(address) ? null : address, a.CrewId, crew?.Name, crew?.Color,
                people.Select(id => new PersonRef(id, Name(id))).OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList(),
                a.ResourceIds.Where(resources.ContainsKey).Select(id => new ResourceRef(id, resources[id].Name, resources[id].Kind)).ToList(),
                a.Notes, warnings.Distinct().ToList(), a.Version, a.Kind, a.EndTime, bauakte?.Status, a.ExcludedUserIds);
        }).ToList();

        return new PlanningRange(from, to, views,
            absences.OrderBy(a => a.From).Select(a => new AbsenceView(a.Id, a.UserId, Name(a.UserId), a.From, a.To, HrService.Mask(a.Kind, maySeeSickness),
                a.Kind == AbsenceKind.Sick && !maySeeSickness ? null : a.Note)).ToList(), shown, holidays);
    }

    public static bool TryParseDay(string? value, out DateOnly day) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
}
