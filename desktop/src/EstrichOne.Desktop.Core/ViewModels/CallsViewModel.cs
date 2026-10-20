using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EstrichOne.Client;

namespace EstrichOne.Desktop.ViewModels;

/// <summary>Anrufe mit Wiedervorlage und Telefon-Schnellerfassung (wie Web „Telefon“).</summary>
public sealed partial class CallsViewModel(AppSession session, TimeProvider? clock = null) : PageViewModel
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public ObservableCollection<CallRow> Items { get; } = [];

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool OnlyDue { get; set; }

    // Schnellerfassung

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveQuickCallCommand))]
    public partial string Caller { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveQuickCallCommand))]
    public partial string Phone { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool Outbound { get; set; }

    [ObservableProperty]
    public partial bool FollowUp { get; set; }

    [ObservableProperty]
    public partial string? Saved { get; set; }

    public bool CanWrite => session.Client.Has("contacts.edit");

    private DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);

    partial void OnOnlyDueChanged(bool value) => LoadCommand.Execute(null);

    [RelayCommand]
    private async Task LoadAsync()
    {
        await LoadLatestAsync(() => session.Client.GetCallsAsync(CallKind.Call, due: OnlyDue, search: string.IsNullOrWhiteSpace(Search) ? null : Search.Trim()),
            calls =>
            {
                Items.Clear();
                foreach (var c in calls)
                {
                    Items.Add(ToRow(c, Today));
                }
            });
    }

    private bool CanSaveQuickCall() => !string.IsNullOrWhiteSpace(Caller) || !string.IsNullOrWhiteSpace(Phone);

    [RelayCommand(CanExecute = nameof(CanSaveQuickCall))]
    private async Task SaveQuickCallAsync()
    {
        var ok = await RunAsync(async () =>
        {
            var saved = await session.Client.SaveCallAsync(new SaveCallRequest(Guid.CreateVersion7(), CallKind.Call,
                Outbound ? CallDirection.Outbound : CallDirection.Inbound, CallerName: Blank(Caller), CallerPhone: Blank(Phone), Notes: Blank(Notes),
                FollowUp: FollowUp));
            Items.Insert(0, ToRow(saved, Today));
            Saved = $"Gesprächsnotiz „{saved.CallerName ?? saved.CallerPhone}“ gespeichert.";
        });
        if (ok)
        {
            Caller = string.Empty;
            Phone = string.Empty;
            Notes = string.Empty;
            FollowUp = false;
            Outbound = false;
        }
    }

    [RelayCommand]
    private async Task CompleteFollowUpAsync(CallRow? row)
    {
        if (row is null)
        {
            return;
        }

        await RunAsync(async () =>
        {
            var updated = await session.Client.CompleteFollowUpAsync(row.Id);
            var index = Items.IndexOf(row);
            if (OnlyDue)
            {
                Items.Remove(row);
            }
            else if (index >= 0)
            {
                Items[index] = ToRow(updated, Today);
            }
        });
    }

    public static CallRow ToRow(CallRecord c, DateOnly today) => new(
        c.Id,
        string.Join(" · ", new[] { c.CallerName, c.Company ?? c.CustomerName }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } who ? who : "–",
        c.CallerPhone ?? string.Empty, c.Subject ?? c.Notes ?? string.Empty, Labels.DateTime(c.CalledAt),
        c.FollowUp ? (c.DueDate is { } d ? $"Wiedervorlage {Labels.Date(d)}" : "Wiedervorlage") : string.Empty,
        c.FollowUp && c.DueDate is { } due && due < today, c.Kind == CallKind.Inquiry ? Labels.Of(c.InquiryStatus) : string.Empty,
        c.BauakteLabel, c.BauakteId ?? c.ConvertedBauakteId, c.HandledByName ?? "–");

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
