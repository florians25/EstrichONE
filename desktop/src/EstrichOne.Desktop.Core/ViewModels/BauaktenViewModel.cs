using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EstrichOne.Client;

namespace EstrichOne.Desktop.ViewModels;

/// <summary>Zeile der Bauaktenliste – fertig formatiert für die Tabelle.</summary>
public sealed record BauakteRow(Guid Id, string Number, string Title, BauakteStatus Status, string StatusLabel, string Address, string Responsible,
    string Updated);

public sealed record StatusOption(BauakteStatus? Status, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Bauaktensuche mit Statusfilter und seitenweisem Nachladen (Cursor).</summary>
public sealed partial class BauaktenViewModel(AppSession session) : PageViewModel
{
    private string? _cursor;
    private Dictionary<Guid, string> _people = [];

    public static IReadOnlyList<StatusOption> StatusOptions { get; } =
    [
        new(null, "Alle"), .. Enum.GetValues<BauakteStatus>().Select(s => new StatusOption(s, Labels.Of(s))),
    ];

    public ObservableCollection<BauakteRow> Items { get; } = [];

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    [ObservableProperty]
    public partial StatusOption Status { get; set; } = StatusOptions[0];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    public partial bool HasMore { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    partial void OnStatusChanged(StatusOption value) => LoadCommand.Execute(null);

    [RelayCommand]
    private async Task LoadAsync()
    {
        var search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
        var status = Status.Status;
        await LoadLatestAsync(async () =>
        {
            if (_people.Count == 0)
            {
                _people = (await session.Client.GetDirectoryAsync()).ToDictionary(p => p.Id, p => p.DisplayName);
            }

            return await session.Client.ListBauaktenAsync(search, status);
        }, page =>
        {
            Items.Clear();
            Append(page);
        });
    }

    [RelayCommand(CanExecute = nameof(HasMore))]
    private async Task LoadMoreAsync()
    {
        await RunAsync(async () => Append(await session.Client.ListBauaktenAsync(string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            Status.Status, _cursor)));
    }

    private void Append(BauaktePage page)
    {
        foreach (var b in page.Items)
        {
            Items.Add(new BauakteRow(b.Id, b.Number ?? "neu", b.Title, b.Status, Labels.Of(b.Status), Labels.Address(b.Street, b.PostalCode, b.City),
                b.ResponsibleUserId is { } r ? _people.GetValueOrDefault(r, "–") : "–", Labels.DateTime(b.UpdatedAt)));
        }

        _cursor = page.NextCursor;
        HasMore = page.NextCursor is not null;
        Summary = HasMore ? $"{Items.Count} Bauakten geladen, weitere vorhanden" : $"{Items.Count} Bauakten";
    }
}
