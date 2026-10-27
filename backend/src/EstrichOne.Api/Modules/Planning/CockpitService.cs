using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>Stand der Freigabe eines Tages; <paramref name="ChangedCount"/> zählt neue, geänderte und entfernte Einsätze seit der Freigabe.</summary>
public sealed record PlanReleaseInfo(
    DateOnly Day, DateTimeOffset ReleasedAt, string ReleasedByName, string? Note, int ChangedCount, IReadOnlyList<PlanReleaseItem> Removed);

/// <summary>Tag im Cockpit: Arbeitstag (Mo–Fr ohne Feiertag), Feiertag, Freigabe.</summary>
public sealed record CockpitDay(DateOnly Day, bool IsWorkday, string? Holiday, PlanReleaseInfo? Release);

/// <param name="AbsenceNote">Abwesenheit oder Termin an dem Tag („Urlaub“, „Termin: …“); leer = verfügbar.</param>
public sealed record CockpitMember(Guid Id, string Name, string? AbsenceNote);

/// <summary>Kolonne an einem Tag: Tagesbesetzung, Fahrzeuge, Einsätze in Fahrtreihenfolge, Auslastung, Konflikte mit Erklärung.</summary>
/// <param name="RosterChanged">Tagesbesetzung weicht von der Stammbesetzung ab.</param>
/// <param name="PlannedMinutes">Geplante Zeit (ganztägig 8 h, ohne Ende bis 16 Uhr).</param>
/// <param name="CapacityMinutes">Kapazität: 8 h an Arbeitstagen, 0 wenn niemand verfügbar ist.</param>
public sealed record CockpitCell(
    DateOnly Day, IReadOnlyList<CockpitMember> Members, IReadOnlyList<ResourceRef> Vehicles, bool RosterChanged, string? RosterNote,
    IReadOnlyList<Guid> AssignmentIds, int PlannedMinutes, int CapacityMinutes, int AvailableMembers, IReadOnlyList<string> Warnings);

public sealed record CockpitCrew(
    Guid Id, string Name, string? Color, Guid? LeaderUserId, string? LeaderName, IReadOnlyList<PersonRef> Members, IReadOnlyList<ResourceRef> Vehicles,
    IReadOnlyList<CockpitCell> Cells);

/// <summary>Disponenten-Cockpit: Kolonnen × Tage mit Kapazität, Konflikten und Freigabestand; Einsätze wie in <c>/planning/range</c>.</summary>
public sealed record CockpitBoard(
    DateOnly From, DateOnly To, IReadOnlyList<CockpitDay> Days, IReadOnlyList<CockpitCrew> Crews, IReadOnlyList<AssignmentView> Assignments,
    IReadOnlyList<AbsenceView> Absences, IReadOnlyList<CalendarOccurrence> Appointments, bool CanPlan);

/// <summary>Station einer Kolonne am Tag („Wer fährt wohin“): Baustelle mit Reihenfolge, Uhrzeit, Adresse, Ansprechpartner.</summary>
/// <param name="Order">Position in der Fahrtreihenfolge (1 = erste Baustelle).</param>
/// <param name="Change">„new“ oder „changed“ seit der Freigabe, sonst leer.</param>
public sealed record DayPlanStop(
    Guid AssignmentId, int Order, int Sequence, TimeOnly? StartTime, TimeOnly? EndTime, AssignmentKind Kind, string Activity, Guid BauakteId,
    string? Number, string Title, string? Address, double? Latitude, double? Longitude, string? SiteContactName, string? SiteContactPhone,
    string? NavigationUrl, string? Notes, IReadOnlyList<PersonRef> People, IReadOnlyList<ResourceRef> Resources, IReadOnlyList<string> Warnings,
    string? Change, long Version);

public sealed record DayPlanCrew(
    Guid Id, string Name, string? Color, string? LeaderName, IReadOnlyList<CockpitMember> Members, IReadOnlyList<ResourceRef> Vehicles,
    bool RosterChanged, string? RosterNote, bool IsMine, IReadOnlyList<DayPlanStop> Stops, IReadOnlyList<string> Warnings);

/// <summary>„Wer fährt wohin“ je Kolonne für einen Tag (Web-Cockpit, iOS/Android): Besetzung, Fahrzeuge, Stationen, Freigabe.</summary>
/// <param name="WithoutCrew">Einsätze einzelner Mitarbeiter ohne Kolonne.</param>
/// <param name="Free">Aktive Mitarbeiter ohne Einsatz und ohne Abwesenheit.</param>
public sealed record DayPlan(
    DateOnly Day, string? Holiday, PlanReleaseInfo? Release, bool CanPlan, IReadOnlyList<Guid> MyCrewIds, IReadOnlyList<DayPlanCrew> Crews,
    IReadOnlyList<DayPlanStop> WithoutCrew, IReadOnlyList<CockpitMember> Absent, IReadOnlyList<PersonRef> Free);

public sealed class CockpitService(
    EstrichOneDbContext db, PlanningService planning, HrService hr, ICurrentUser currentUser, IUserPermissions permissions, TimeProvider clock)
{
    public const int DayMinutes = 8 * 60;

    private static readonly Dictionary<AbsenceKind, string> AbsenceText = new()
    {
        [AbsenceKind.Vacation] = "Urlaub", [AbsenceKind.Sick] = "abwesend", [AbsenceKind.Training] = "Schulung", [AbsenceKind.Other] = "abwesend",
        [AbsenceKind.SpecialLeave] = "Sonderurlaub", [AbsenceKind.School] = "Berufsschule",
    };

    /// <summary>Geplante Minuten: ganztägig 8 h, ohne Ende bis 16 Uhr (mindestens 1 h), sonst Dauer.</summary>
    public static int Minutes(TimeOnly? start, TimeOnly? end)
    {
        if (start is not { } s)
        {
            return DayMinutes;
        }

        if (end is not { } e)
        {
            return Math.Clamp((int)(new TimeOnly(16, 0) - s).TotalMinutes, 60, DayMinutes);
        }

        return Math.Max(30, (int)(e - s).TotalMinutes);
    }

    private sealed record Context(
        PlanningRange Range, List<Crew> Crews, Dictionary<(Guid, DateOnly), CrewDay> Rosters, Dictionary<Guid, Resource> Resources,
        Dictionary<Guid, string> Names, Dictionary<DateOnly, PlanRelease> Releases, IReadOnlyList<CalendarOccurrence> Busy);

    private async Task<Context> LoadAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var range = await planning.RangeAsync(from, to, null, cancellationToken);
        var crews = await db.Set<Crew>().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);
        var rosters = (await db.Set<CrewDay>().AsNoTracking().Where(d => d.Day >= from && d.Day <= to).ToListAsync(cancellationToken))
            .ToDictionary(d => (d.CrewId, d.Day));
        var resources = await db.Set<Resource>().AsNoTracking().ToDictionaryAsync(r => r.Id, cancellationToken);
        var names = await db.Set<User>().AsNoTracking().Where(u => u.IsActive).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var releases = await db.Set<PlanRelease>().AsNoTracking().Where(r => r.Day >= from && r.Day <= to).ToDictionaryAsync(r => r.Day, cancellationToken);
        return new Context(range, crews, rosters, resources, names, releases, range.Appointments ?? []);
    }

    private static PlanReleaseInfo? ReleaseOf(Context ctx, DateOnly day)
    {
        if (!ctx.Releases.TryGetValue(day, out var release))
        {
            return null;
        }

        var current = ctx.Range.Assignments.Where(a => a.Day == day).ToList();
        var ids = current.Select(a => a.Id).ToHashSet();
        var removed = release.Items().Where(i => !ids.Contains(i.AssignmentId)).ToList();
        return new PlanReleaseInfo(day, release.ReleasedAt, release.ReleasedByName, release.Note, current.Count(a => a.Change is not null) + removed.Count, removed);
    }

    private static List<Guid> MembersOf(Context ctx, Crew crew, DateOnly day) =>
        ctx.Rosters.TryGetValue((crew.Id, day), out var roster) ? roster.MemberUserIds : crew.MemberUserIds;

    private static List<Guid> VehiclesOf(Context ctx, Crew crew, DateOnly day) =>
        ctx.Rosters.TryGetValue((crew.Id, day), out var roster)
            ? roster.ResourceIds
            : ctx.Resources.Values.Where(r => r.CrewId == crew.Id && r.IsActive).OrderBy(r => r.Kind).ThenBy(r => r.Name).Select(r => r.Id).ToList();

    private static string? NoteOf(Context ctx, Guid userId, DateOnly day)
    {
        if (ctx.Range.Absences.FirstOrDefault(a => a.UserId == userId && a.From <= day && day <= a.To) is { } absence)
        {
            return AbsenceText[absence.Kind];
        }

        var start = day.ToDateTime(TimeOnly.MinValue);
        var end = day.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return ctx.Busy.FirstOrDefault(o => o.Start < end && start < o.End && o.People.Any(p => p.Id == userId)) is { } appointment
            ? $"Termin: {appointment.Title}"
            : null;
    }

    private static CockpitCell CellOf(Context ctx, Crew crew, DateOnly day, bool workday)
    {
        var members = MembersOf(ctx, crew, day).Select(id => new CockpitMember(id, ctx.Names.GetValueOrDefault(id) ?? "Unbekannt", NoteOf(ctx, id, day)))
            .OrderBy(m => m.Id == crew.LeaderUserId ? 0 : 1).ThenBy(m => m.Name, StringComparer.CurrentCulture).ToList();
        var vehicleIds = VehiclesOf(ctx, crew, day);
        var vehicles = vehicleIds.Where(ctx.Resources.ContainsKey).Select(id => new ResourceRef(id, ctx.Resources[id].Name, ctx.Resources[id].Kind)).ToList();
        var jobs = ctx.Range.Assignments.Where(a => a.Day == day && a.CrewId == crew.Id).ToList();
        var available = members.Count(m => m.AbsenceNote is null);
        var warnings = new List<string>();
        if (jobs.Count > 0)
        {
            foreach (var absent in members.Where(m => m.AbsenceNote is not null))
            {
                warnings.Add($"{absent.Name}: {absent.AbsenceNote}.");
            }

            if (available == 0)
            {
                warnings.Add("Niemand aus der Kolonne ist verfügbar.");
            }

            if (!workday)
            {
                warnings.Add("Einsatz an einem Wochenende oder Feiertag.");
            }

            for (var i = 0; i < jobs.Count; i++)
            {
                for (var k = i + 1; k < jobs.Count; k++)
                {
                    var (a, b) = (jobs[i], jobs[k]);
                    var aFrom = a.StartTime ?? TimeOnly.MinValue;
                    var aTo = a.EndTime ?? (a.StartTime is null ? TimeOnly.MaxValue : new TimeOnly(16, 0));
                    var bFrom = b.StartTime ?? TimeOnly.MinValue;
                    var bTo = b.EndTime ?? (b.StartTime is null ? TimeOnly.MaxValue : new TimeOnly(16, 0));
                    if (aFrom < bTo && bFrom < aTo)
                    {
                        warnings.Add($"Doppelbelegung: „{a.BauakteTitle}“ und „{b.BauakteTitle}“ überschneiden sich.");
                    }
                }
            }

            // Gerät/Fahrzeug der Kolonne ist an dem Tag auch bei einer anderen Kolonne eingeplant.
            foreach (var other in ctx.Crews.Where(c => c.Id != crew.Id))
            {
                var shared = VehiclesOf(ctx, other, day).Intersect(vehicleIds).ToList();
                if (shared.Count > 0 && ctx.Range.Assignments.Any(a => a.Day == day && a.CrewId == other.Id))
                {
                    warnings.AddRange(shared.Select(id => $"{ctx.Resources.GetValueOrDefault(id)?.Name ?? "Gerät"} ist auch bei {other.Name} eingeplant."));
                }
            }

            warnings.AddRange(jobs.SelectMany(a => a.Warnings));
        }

        var planned = jobs.Sum(a => Minutes(a.StartTime, a.EndTime));
        return new CockpitCell(day, members, vehicles, ctx.Rosters.ContainsKey((crew.Id, day)), ctx.Rosters.GetValueOrDefault((crew.Id, day))?.Note,
            Ordered(jobs).Select(a => a.Id).ToList(), planned, workday && available > 0 ? DayMinutes : 0, available, warnings.Distinct().ToList());
    }

    private static IEnumerable<AssignmentView> Ordered(IEnumerable<AssignmentView> jobs) =>
        jobs.OrderBy(a => a.Sequence == 0 ? int.MaxValue : a.Sequence).ThenBy(a => a.StartTime ?? TimeOnly.MinValue).ThenBy(a => a.Activity, StringComparer.CurrentCulture);

    private static bool Workday(DateOnly day, IReadOnlyList<Holiday>? holidays) =>
        day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && holidays?.Any(h => h.Date == day) != true;

    public async Task<CockpitBoard> BoardAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var ctx = await LoadAsync(from, to, cancellationToken);
        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays).ToList();
        var holidays = ctx.Range.Holidays ?? [];
        var crews = ctx.Crews.Select(c => new CockpitCrew(c.Id, c.Name, c.Color, c.LeaderUserId,
            c.LeaderUserId is { } l ? ctx.Names.GetValueOrDefault(l) : null,
            c.MemberUserIds.Where(ctx.Names.ContainsKey).Select(id => new PersonRef(id, ctx.Names[id])).OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList(),
            VehiclesOf(ctx, c, DateOnly.MinValue).Where(ctx.Resources.ContainsKey).Select(id => new ResourceRef(id, ctx.Resources[id].Name, ctx.Resources[id].Kind)).ToList(),
            days.Select(d => CellOf(ctx, c, d, Workday(d, holidays))).ToList())).ToList();
        return new CockpitBoard(from, to,
            days.Select(d => new CockpitDay(d, Workday(d, holidays), holidays.FirstOrDefault(h => h.Date == d)?.Name, ReleaseOf(ctx, d))).ToList(),
            crews, ctx.Range.Assignments, ctx.Range.Absences, ctx.Range.Appointments ?? [],
            await permissions.HasAsync(Permissions.PlanningManage, cancellationToken));
    }

    public async Task<DayPlan> DayAsync(DateOnly day, CancellationToken cancellationToken)
    {
        var ctx = await LoadAsync(day, day, cancellationToken);
        var holidays = ctx.Range.Holidays ?? [];
        var workday = Workday(day, holidays);
        var siteIds = ctx.Range.Assignments.Select(a => a.BauakteId).Distinct().ToList();
        var sites = await db.Set<Bauakte>().AsNoTracking().Where(b => siteIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, cancellationToken);

        DayPlanStop Stop(AssignmentView a, int order)
        {
            var b = sites.GetValueOrDefault(a.BauakteId);
            var navigation = b?.Latitude is { } lat && b.Longitude is { } lon
                ? FormattableString.Invariant($"https://www.openstreetmap.org/directions?to={lat:0.######},{lon:0.######}")
                : a.BauakteAddress is { Length: > 0 } address ? $"https://www.openstreetmap.org/search?query={Uri.EscapeDataString(address)}" : null;
            return new DayPlanStop(a.Id, order, a.Sequence, a.StartTime, a.EndTime, a.Kind, a.Activity, a.BauakteId, a.BauakteNumber, a.BauakteTitle,
                a.BauakteAddress, b?.Latitude, b?.Longitude, b?.SiteContactName, b?.SiteContactPhone, navigation, a.Notes, a.People, a.Resources, a.Warnings,
                a.Change, a.Version);
        }

        var me = currentUser.UserId;
        var crews = ctx.Crews.Select(c =>
        {
            var cell = CellOf(ctx, c, day, workday);
            var jobs = cell.AssignmentIds.Select(id => ctx.Range.Assignments.Single(a => a.Id == id)).Select((a, i) => Stop(a, i + 1)).ToList();
            var mine = c.LeaderUserId == me || cell.Members.Any(m => m.Id == me);
            return new DayPlanCrew(c.Id, c.Name, c.Color, c.LeaderUserId is { } l ? ctx.Names.GetValueOrDefault(l) : null, cell.Members, cell.Vehicles,
                cell.RosterChanged, cell.RosterNote, mine, jobs, cell.Warnings);
        }).ToList();
        var withoutCrew = Ordered(ctx.Range.Assignments.Where(a => a.CrewId is null)).Select((a, i) => Stop(a, i + 1)).ToList();
        var busy = ctx.Range.Assignments.SelectMany(a => a.People.Select(p => p.Id)).ToHashSet();
        var absent = ctx.Names.Keys.Select(id => new CockpitMember(id, ctx.Names[id], NoteOf(ctx, id, day))).Where(m => m.AbsenceNote is not null)
            .OrderBy(m => m.Name, StringComparer.CurrentCulture).ToList();
        var absentIds = absent.Select(a => a.Id).ToHashSet();
        var free = ctx.Names.Where(n => !busy.Contains(n.Key) && !absentIds.Contains(n.Key)).Select(n => new PersonRef(n.Key, n.Value))
            .OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList();
        return new DayPlan(day, holidays.FirstOrDefault(h => h.Date == day)?.Name, ReleaseOf(ctx, day),
            await permissions.HasAsync(Permissions.PlanningManage, cancellationToken),
            crews.Where(c => c.IsMine).Select(c => c.Id).ToList(), crews, withoutCrew, absent, free);
    }

    /// <summary>Tage freigeben: Stand der Einsätze festhalten; spätere Änderungen werden markiert.</summary>
    public async Task<IReadOnlyList<PlanReleaseInfo>> ReleaseAsync(DateOnly from, DateOnly to, string? note, CancellationToken cancellationToken)
    {
        var range = await planning.RangeAsync(from, to, null, cancellationToken);
        var name = await db.Set<User>().AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => u.DisplayName).SingleOrDefaultAsync(cancellationToken)
            ?? "Unbekannt";
        var existing = await db.Set<PlanRelease>().Where(r => r.Day >= from && r.Day <= to).ToDictionaryAsync(r => r.Day, cancellationToken);
        var now = clock.GetUtcNow();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (!existing.TryGetValue(day, out var release))
            {
                release = PlanRelease.Create(day);
                db.Add(release);
            }

            release.Release(now, currentUser.UserId, name, note,
                range.Assignments.Where(a => a.Day == day).Select(a => new PlanReleaseItem(a.Id, a.Version, a.BauakteTitle, a.CrewName, a.Activity)).ToList());
        }

        await db.SaveChangesAsync(cancellationToken);
        var ctx = await LoadAsync(from, to, cancellationToken);
        return Enumerable.Range(0, to.DayNumber - from.DayNumber + 1).Select(from.AddDays).Select(d => ReleaseOf(ctx, d)!).ToList();
    }

    /// <summary>Tagesbesetzung holen (zum Ändern); ohne Eintrag aus der Stammbesetzung angelegt.</summary>
    public async Task<CrewDay> RosterAsync(Crew crew, DateOnly day, CancellationToken cancellationToken)
    {
        var roster = await db.Set<CrewDay>().SingleOrDefaultAsync(d => d.CrewId == crew.Id && d.Day == day, cancellationToken);
        if (roster is not null)
        {
            return roster;
        }

        var vehicles = await db.Set<Resource>().AsNoTracking().Where(r => r.CrewId == crew.Id && r.IsActive).Select(r => r.Id).ToListAsync(cancellationToken);
        roster = CrewDay.Create(crew.Id, day, crew.MemberUserIds, vehicles);
        db.Add(roster);
        return roster;
    }

    /// <summary>Nächster freier Arbeitstag der Kolonne (kein Einsatz, jemand verfügbar), höchstens 90 Tage voraus.</summary>
    public async Task<DateOnly?> NextFreeDayAsync(Guid crewId, DateOnly after, CancellationToken cancellationToken)
    {
        var settings = await hr.SettingsAsync(cancellationToken);
        var until = after.AddDays(90);
        var holidays = Enumerable.Range(after.Year, until.Year - after.Year + 1).SelectMany(y => GermanHolidays.For(y, settings.State)).Select(h => h.Date).ToHashSet();
        var busyDays = (await db.Set<Assignment>().AsNoTracking().Where(a => a.CrewId == crewId && a.Day > after && a.Day <= until)
            .Select(a => a.Day).ToListAsync(cancellationToken)).ToHashSet();
        var crew = await db.Set<Crew>().AsNoTracking().SingleAsync(c => c.Id == crewId, cancellationToken);
        var rosters = await db.Set<CrewDay>().AsNoTracking().Where(d => d.CrewId == crewId && d.Day > after && d.Day <= until)
            .ToDictionaryAsync(d => d.Day, d => d.MemberUserIds, cancellationToken);
        var absences = await db.Set<Absence>().AsNoTracking().Where(a => a.To > after && a.From <= until).ToListAsync(cancellationToken);
        for (var day = after.AddDays(1); day <= until; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || holidays.Contains(day) || busyDays.Contains(day))
            {
                continue;
            }

            var members = rosters.GetValueOrDefault(day) ?? crew.MemberUserIds;
            if (members.Count == 0 || members.Any(m => !absences.Any(a => a.UserId == m && a.Covers(day))))
            {
                return day;
            }
        }

        return null;
    }
}
