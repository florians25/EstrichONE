using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EstrichOne.Client;

namespace EstrichOne.Desktop.ViewModels;

public sealed record TicketRow(Guid Id, string Number, string Title, string Category, string Priority, string Status, string Reporter, string Assignee,
    string LastActivity, bool Blocked);

/// <summary>Helpdesk am Desktop: offene Meldungen sehen; „Problem melden“ für alle Benutzer.</summary>
public sealed partial class TicketsViewModel(AppSession session) : PageViewModel
{
    public ObservableCollection<TicketRow> Items { get; } = [];

    public bool IsAgent => session.Client.Has("helpdesk.agent");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReportCommand))]
    public partial string ProblemTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ProblemDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool Blocked { get; set; }

    [ObservableProperty]
    public partial string? Reported { get; set; }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var tickets = IsAgent ? await session.Client.GetTicketsAsync("open") : await session.Client.GetMyTicketsAsync();
            Items.Clear();
            foreach (var t in tickets)
            {
                Items.Add(new TicketRow(t.Id, t.Number, t.Title, Labels.Of(t.Category), Labels.Of(t.Priority), Labels.Of(t.Status),
                    t.ReporterDisplayName ?? "–", t.AssigneeName ?? "–", Labels.DateTime(t.LastActivityAt), t.ReporterBlocked));
            }
        });
    }

    private bool CanReport() => !string.IsNullOrWhiteSpace(ProblemTitle);

    [RelayCommand(CanExecute = nameof(CanReport))]
    private async Task ReportAsync()
    {
        var ok = await RunAsync(async () =>
        {
            var ticket = await session.Client.ReportProblemAsync(new CreateTicketRequest(Guid.CreateVersion7(), ProblemTitle.Trim(),
                string.IsNullOrWhiteSpace(ProblemDescription) ? null : ProblemDescription.Trim(), TicketCategory.Other, TicketSource.Desktop, Blocked,
                Diagnostics: new Dictionary<string, object?>
                {
                    ["appVersion"] = typeof(TicketsViewModel).Assembly.GetName().Version?.ToString(3), ["platform"] = "windows",
                    ["osVersion"] = Environment.OSVersion.Version.ToString(), ["deviceKind"] = "desktop",
                }));
            Reported = $"Meldung {ticket.Summary.Number} wurde an die IT übermittelt.";
        });
        if (ok)
        {
            ProblemTitle = string.Empty;
            ProblemDescription = string.Empty;
            Blocked = false;
            await LoadAsync();
        }
    }
}
