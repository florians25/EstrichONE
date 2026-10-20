using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EstrichOne.Client;

namespace EstrichOne.Desktop.ViewModels;

public sealed record CallRow(Guid Id, string Caller, string Phone, string Subject, string When, string Due, bool IsOverdue, string Status,
    string? BauakteLabel, Guid? BauakteId, string Handler);

public sealed record DeliveryRow(Guid Id, string Time, string Supplier, string Bauakte, string Lines, string Status);

/// <summary>Tagesübersicht: Einsätze heute, Abwesenheiten, Wiedervorlagen, Anfragen, Lieferungen, eigene Aufgaben.</summary>
public sealed partial class TodayViewModel(AppSession session, TimeProvider? clock = null) : PageViewModel
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public ObservableCollection<AssignmentCard> Assignments { get; } = [];

    public ObservableCollection<string> Absences { get; } = [];

    public ObservableCollection<CallRow> DueCalls { get; } = [];

    public ObservableCollection<CallRow> Inquiries { get; } = [];

    public ObservableCollection<DeliveryRow> Deliveries { get; } = [];

    public ObservableCollection<WorkItemRow> MyWork { get; } = [];

    [ObservableProperty]
    public partial string Heading { get; set; } = string.Empty;

    public bool ShowPlanning => session.Client.Has("planning.read");

    public bool ShowOffice => session.Client.Has("contacts.read");

    public bool ShowMaterial => session.Client.Has("materials.read");

    [RelayCommand]
    private async Task LoadAsync()
    {
        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        Heading = Labels.Weekday(today) + today.Year.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await RunAsync(async () =>
        {
            var client = session.Client;
            var planning = ShowPlanning ? client.GetPlanningAsync(today, today) : Task.FromResult<PlanningRange?>(null)!;
            var due = ShowOffice ? client.GetCallsAsync(due: true) : Task.FromResult(new List<CallRecord>());
            var inquiries = ShowOffice ? client.GetCallsAsync(CallKind.Inquiry, open: true, limit: 20) : Task.FromResult(new List<CallRecord>());
            var deliveries = ShowMaterial ? client.GetOrdersAsync(from: today, to: today) : Task.FromResult(new List<MaterialOrder>());
            var work = client.Has("work.read") ? client.GetMyWorkItemsAsync() : Task.FromResult(new List<WorkItem>());
            await Task.WhenAll(planning, due, inquiries, deliveries, work);

            Fill(Assignments, (planning.Result?.Assignments ?? []).OrderBy(a => a.CrewName).ThenBy(a => a.StartTime).Select(PlanningViewModel.ToCard));
            Fill(Absences, (planning.Result?.Absences ?? []).Select(a => $"{a.UserName} · {Labels.Of(a.Kind)}"));
            Fill(DueCalls, due.Result.Select(c => CallsViewModel.ToRow(c, today)));
            Fill(Inquiries, inquiries.Result.Select(c => CallsViewModel.ToRow(c, today)));
            Fill(Deliveries, deliveries.Result.Where(o => o.Status != OrderStatus.Cancelled).Select(o => new DeliveryRow(o.Id, o.DeliveryTime ?? "–",
                o.SupplierName, o.BauakteId is null ? "Lager" : $"{o.BauakteNumber} · {o.BauakteTitle}",
                string.Join(" · ", o.Lines.Select(l => $"{Labels.Quantity(l.Quantity)} {l.Unit} {l.Description}")), Labels.Of(o.Status))));
            Fill(MyWork, work.Result.Where(w => w.Status != WorkItemStatus.Done).OrderBy(w => w.DueDate ?? DateOnly.MaxValue).Take(15).Select(w => new WorkItemRow(w)));
        });
    }

    private static void Fill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
