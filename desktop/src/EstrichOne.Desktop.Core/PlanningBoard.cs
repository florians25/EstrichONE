using EstrichOne.Client;

namespace EstrichOne.Desktop;

public enum PlanningView { Day, Week, Month }

/// <summary>Zeile der Plantafel: Kolonne oder „Termine ohne Kolonne“ (<see cref="CrewId"/> = null).</summary>
public sealed record PlanningLane(Guid? CrewId, string Name, string? Color, IReadOnlyList<Guid> Members);

/// <summary>Regeln der Disposition (wie Web): sichtbare Tage, Zeilen, Verschieben per Ziehen, Vorschläge je Terminart.</summary>
public static class PlanningBoard
{
    public static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>Tag = ein Tag, Woche = Mo–Fr bzw. Mo–So, Monat = volle Wochen um den Monat.</summary>
    public static IReadOnlyList<DateOnly> VisibleDays(PlanningView view, DateOnly anchor, bool workWeek)
    {
        switch (view)
        {
            case PlanningView.Day:
                return [anchor];
            case PlanningView.Week:
                var monday = MondayOf(anchor);
                return Enumerable.Range(0, workWeek ? 5 : 7).Select(monday.AddDays).ToList();
            default:
                var first = new DateOnly(anchor.Year, anchor.Month, 1);
                var start = MondayOf(first);
                var end = MondayOf(first.AddMonths(1).AddDays(-1)).AddDays(6);
                return Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(start.AddDays).ToList();
        }
    }

    /// <summary>Nächster Zeitraum (Tag ±1, Woche ±7, Monat ±1 Monat).</summary>
    public static DateOnly Shift(PlanningView view, DateOnly anchor, int direction) => view switch
    {
        PlanningView.Day => anchor.AddDays(direction),
        PlanningView.Week => anchor.AddDays(7 * direction),
        _ => new DateOnly(anchor.Year, anchor.Month, 1).AddMonths(direction),
    };

    public static string Title(PlanningView view, DateOnly anchor, IReadOnlyList<DateOnly> days)
    {
        ArgumentNullException.ThrowIfNull(days);
        return view switch
        {
            PlanningView.Day => Labels.LongDay(anchor),
            PlanningView.Week => $"KW {Labels.Week(days[0])} · {Labels.Date(days[0])} – {Labels.Date(days[^1])}",
            _ => Labels.Month(anchor),
        };
    }

    public static IReadOnlyList<PlanningLane> Lanes(IEnumerable<Crew> crews) =>
    [
        .. crews.Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.CurrentCulture)
            .Select(c => new PlanningLane(c.Id, c.Name, c.Color, c.MemberUserIds)),
        new PlanningLane(null, "Termine ohne Kolonne", null, []),
    ];

    /// <summary>Einsätze einer Zelle; Einsätze inaktiver Kolonnen erscheinen bei „ohne Kolonne“.</summary>
    public static IEnumerable<Assignment> InCell(IEnumerable<Assignment> assignments, IReadOnlyList<PlanningLane> lanes, Guid? crewId, DateOnly day) =>
        assignments.Where(a => a.Day == day && (crewId is null
                ? a.CrewId is null || lanes.All(l => l.CrewId != a.CrewId)
                : a.CrewId == crewId))
            .OrderBy(a => a.StartTime ?? TimeOnly.MinValue).ThenBy(a => a.BauakteTitle, StringComparer.CurrentCulture);

    /// <summary>
    /// Einsatz auf anderen Tag oder andere Kolonne legen; zusätzliche Mitarbeiter (nicht aus der alten Kolonne) und Geräte bleiben.
    /// <c>null</c>, wenn sich nichts ändert. Ohne Kolonne braucht der Einsatz mindestens einen Mitarbeiter.
    /// </summary>
    public static SaveAssignmentRequest? Move(Assignment assignment, IEnumerable<Crew> crews, Guid? crewId, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        if (assignment.Day == day && assignment.CrewId == crewId)
        {
            return null;
        }

        var extras = ExtraPeople(assignment, crews);
        if (crewId is null && extras.Count == 0)
        {
            throw new InvalidOperationException("Ohne Kolonne braucht der Einsatz mindestens einen Mitarbeiter. Bitte im Einsatz Mitarbeiter wählen.");
        }

        return ToRequest(assignment, extras) with { Day = day, CrewId = crewId };
    }

    /// <summary>Mitarbeiter, die zusätzlich zur Kolonne eingeplant sind.</summary>
    public static IReadOnlyList<Guid> ExtraPeople(Assignment assignment, IEnumerable<Crew> crews)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        var members = crews.FirstOrDefault(c => c.Id == assignment.CrewId)?.MemberUserIds ?? [];
        return assignment.People.Select(p => p.Id).Where(id => !members.Contains(id)).ToList();
    }

    public static SaveAssignmentRequest ToRequest(Assignment a, IReadOnlyList<Guid> extraPeople)
    {
        ArgumentNullException.ThrowIfNull(a);
        return new SaveAssignmentRequest(a.BauakteId, a.Day, a.StartTime, a.Activity, a.CrewId, extraPeople, a.Resources.Select(r => r.Id).ToList(),
            a.Notes, a.Kind, a.EndTime);
    }

    /// <summary>Vorschlag für die Tätigkeit je Terminart.</summary>
    public static string SuggestedActivity(AssignmentKind kind) => kind switch
    {
        AssignmentKind.Screed => "Estrich einbringen",
        AssignmentKind.Other => string.Empty,
        _ => Labels.Of(kind),
    };

    public static string TimeRange(Assignment a)
    {
        ArgumentNullException.ThrowIfNull(a);
        return a.StartTime is null ? "ganztags" : a.EndTime is null ? Labels.Time(a.StartTime) : $"{Labels.Time(a.StartTime)}–{Labels.Time(a.EndTime)}";
    }
}
