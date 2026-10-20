using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EstrichOne.Client;

namespace EstrichOne.Desktop.ViewModels;

/// <summary>Einsatz als Karte in der Plantafel.</summary>
public sealed record AssignmentCard(Guid Id, Guid BauakteId, string Time, string Kind, string Title, string Activity, string Address, string People,
    string Resources, string? Color, bool HasWarnings, string Warnings);

public sealed record PlanningCell(DateOnly Day, IReadOnlyList<AssignmentCard> Items, IReadOnlyList<string> Absent);

/// <summary>Zeile der Plantafel: eine Kolonne bzw. „Ohne Kolonne“ mit einer Zelle je Tag.</summary>
public sealed record PlanningRow(string Name, string? Color, IReadOnlyList<PlanningCell> Cells);

public sealed record DayHeader(DateOnly Day, string Weekday, string Date, bool IsToday);

/// <summary>Kolonnenplanung als Wochentafel: Kolonnen × Tage, Abwesenheiten, Hinweise (wie Web „Kolonnenplanung“).</summary>
public sealed partial class PlanningViewModel(AppSession session, TimeProvider? clock = null) : PageViewModel
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public ObservableCollection<DayHeader> Days { get; } = [];

    public ObservableCollection<PlanningRow> Rows { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial DateOnly WeekStart { get; set; }

    /// <summary>Samstag und Sonntag anzeigen (Standardwoche) statt nur Arbeitswoche.</summary>
    [ObservableProperty]
    public partial bool ShowWeekend { get; set; }

    [ObservableProperty]
    public partial int WarningCount { get; set; }

    public string Title => $"KW {Labels.Week(WeekStart)} · {Labels.Date(WeekStart)} – {Labels.Date(WeekStart.AddDays(ShowWeekend ? 6 : 4))}";

    private DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

    public static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    partial void OnShowWeekendChanged(bool value) => LoadCommand.Execute(null);

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (WeekStart == default)
        {
            WeekStart = MondayOf(Today);
        }

        var from = WeekStart;
        var weekend = ShowWeekend;
        await LoadLatestAsync(async () =>
        {
            var range = session.Client.GetPlanningAsync(from, from.AddDays(weekend ? 6 : 4));
            var crews = session.Client.GetCrewsAsync();
            await Task.WhenAll(range, crews);
            return (Range: range.Result, Crews: crews.Result);
        }, data => Build(data.Range, data.Crews));
        OnPropertyChanged(nameof(Title));
    }

    [RelayCommand]
    private Task PreviousAsync()
    {
        WeekStart = WeekStart.AddDays(-7);
        return LoadAsync();
    }

    [RelayCommand]
    private Task NextAsync()
    {
        WeekStart = WeekStart.AddDays(7);
        return LoadAsync();
    }

    [RelayCommand]
    private Task ThisWeekAsync()
    {
        WeekStart = MondayOf(Today);
        return LoadAsync();
    }

    private void Build(PlanningRange range, IReadOnlyList<Crew> crews)
    {
        var days = Enumerable.Range(0, range.To.DayNumber - range.From.DayNumber + 1).Select(range.From.AddDays).ToList();
        Days.Clear();
        foreach (var day in days)
        {
            Days.Add(new DayHeader(day, Labels.Weekday(day).Split(',')[0], day.ToString("dd.MM.", System.Globalization.CultureInfo.InvariantCulture), day == Today));
        }

        string[] AbsentOn(DateOnly day, IEnumerable<Guid> members) => range.Absences
            .Where(a => members.Contains(a.UserId) && a.From <= day && a.To >= day)
            .Select(a => $"{a.UserName} ({Labels.Of(a.Kind)})").ToArray();

        Rows.Clear();
        foreach (var crew in crews.Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
        {
            Rows.Add(new PlanningRow(crew.Name, crew.Color, days.Select(d => new PlanningCell(d,
                range.Assignments.Where(a => a.CrewId == crew.Id && a.Day == d).OrderBy(a => a.StartTime).Select(ToCard).ToList(),
                AbsentOn(d, crew.MemberUserIds))).ToList()));
        }

        var loose = range.Assignments.Where(a => a.CrewId is null || crews.All(c => c.Id != a.CrewId || !c.IsActive)).ToList();
        if (loose.Count > 0 || Rows.Count == 0)
        {
            Rows.Add(new PlanningRow("Ohne Kolonne", null, days.Select(d => new PlanningCell(d,
                loose.Where(a => a.Day == d).OrderBy(a => a.StartTime).Select(ToCard).ToList(), [])).ToList()));
        }

        WarningCount = range.Assignments.Count(a => a.Warnings.Count > 0);
    }

    public static AssignmentCard ToCard(Assignment a) => new(
        a.Id, a.BauakteId,
        a.StartTime is null ? "ganztags" : a.EndTime is null ? Labels.Time(a.StartTime) : $"{Labels.Time(a.StartTime)}–{Labels.Time(a.EndTime)}",
        Labels.Of(a.Kind), $"{a.BauakteNumber ?? "neu"} · {a.BauakteTitle}", a.Activity, a.BauakteAddress ?? string.Empty,
        string.Join(", ", a.People.Select(p => p.Name)), string.Join(", ", a.Resources.Select(r => r.Name)), a.CrewColor,
        a.Warnings.Count > 0, string.Join(Environment.NewLine, a.Warnings));
}
