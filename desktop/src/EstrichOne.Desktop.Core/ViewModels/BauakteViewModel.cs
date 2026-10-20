using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EstrichOne.Client;

namespace EstrichOne.Desktop.ViewModels;

public sealed record NoteRow(Guid Id, string Author, string Text, string When, bool IsPinned);

public sealed record RoomRow(string Name, string Floor, string Area, string Screed, string Thickness, string Heating, string Status);

public sealed partial class WorkItemRow(WorkItem item) : ObservableObject
{
    public WorkItem Item { get; private set; } = item;

    public string Title => Item.Title;

    public string Kind => Labels.Of(Item.Kind);

    public string Due => Item.DueDate is { } d ? Labels.Date(d) : string.Empty;

    [ObservableProperty]
    public partial bool IsDone { get; set; } = item.Status == WorkItemStatus.Done;

    internal void Update(WorkItem item)
    {
        Item = item;
        IsDone = item.Status == WorkItemStatus.Done;
    }
}

public sealed record OrderRow(Guid Id, string Number, string Delivery, string Supplier, string Lines, string Total, string Status);

public sealed record FileRow(Guid Id, string Name, string Kind, string Size, string Created);

/// <summary>Bauakte am Desktop: Kopf, Hinweise, Notizen, Räume, Aufgaben/Mängel, Dateien, Material.</summary>
public sealed partial class BauakteViewModel(AppSession session, Guid id) : PageViewModel
{
    private Dictionary<Guid, string> _people = [];

    public Guid Id { get; } = id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading), nameof(StatusLabel), nameof(Address), nameof(Period), nameof(SiteContact), nameof(Description),
        nameof(Responsible), nameof(CanComplete))]
    public partial BauakteDetail? Bauakte { get; set; }

    [ObservableProperty]
    public partial Contact? Customer { get; set; }

    public string Heading => Bauakte is null ? string.Empty : $"{Bauakte.Number ?? "neu"} · {Bauakte.Title}";

    public string StatusLabel => Bauakte is null ? string.Empty : Labels.Of(Bauakte.Status);

    public string Address => Bauakte is null ? string.Empty : Labels.Address(Bauakte.Street, Bauakte.PostalCode, Bauakte.City);

    public string Period => Bauakte?.PlannedStart is null ? "–" : $"{Labels.Date(Bauakte.PlannedStart)} – {Labels.Date(Bauakte.PlannedEnd)}";

    public string SiteContact => Bauakte is null ? "–"
        : string.Join(", ", new[] { Bauakte.SiteContactName, Bauakte.SiteContactPhone }.Where(s => !string.IsNullOrWhiteSpace(s))) is { Length: > 0 } c ? c : "–";

    public string Description => Bauakte?.Description ?? string.Empty;

    public string Responsible => Bauakte?.ResponsibleUserId is { } r ? _people.GetValueOrDefault(r, "–") : "–";

    public bool CanComplete => Bauakte is { Status: not (BauakteStatus.Completed or BauakteStatus.Cancelled) } && session.Client.Has("bauakten.edit");

    public ObservableCollection<BauakteHint> Hints { get; } = [];

    public ObservableCollection<NoteRow> Notes { get; } = [];

    public ObservableCollection<RoomRow> Rooms { get; } = [];

    public ObservableCollection<WorkItemRow> WorkItems { get; } = [];

    public ObservableCollection<OrderRow> Orders { get; } = [];

    public ObservableCollection<FileRow> Files { get; } = [];

    [ObservableProperty]
    public partial string RoomSummary { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddNoteCommand))]
    public partial string NewNote { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddWorkItemCommand))]
    public partial string NewWorkItem { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool NewWorkItemIsDefect { get; set; }

    public bool CanWriteNotes => session.Client.Has("notes.write");

    public bool CanEditWork => session.Client.Has("work.edit");

    public bool CanSeeMaterial => session.Client.Has("materials.read");

    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunAsync(async () =>
        {
            var client = session.Client;
            var detail = client.GetBauakteAsync(Id);
            var people = client.GetDirectoryAsync();
            var hints = client.GetHintsAsync(Id);
            var notes = client.GetNotesAsync(Id);
            var rooms = client.GetRoomsAsync(Id);
            var work = client.Has("work.read") ? client.GetWorkItemsAsync(Id) : Task.FromResult(new List<WorkItem>());
            var files = client.GetFilesAsync(Id);
            var orders = CanSeeMaterial ? client.GetOrdersAsync(bauakteId: Id) : Task.FromResult(new List<MaterialOrder>());
            await Task.WhenAll(detail, people, hints, notes, rooms, work, files, orders);

            _people = people.Result.ToDictionary(p => p.Id, p => p.DisplayName);
            Bauakte = detail.Result;
            Customer = Bauakte.CustomerId is { } customerId && client.Has("contacts.read") ? await client.GetContactAsync(customerId) : null;
            Replace(Hints, hints.Result);
            Replace(Notes, notes.Result.OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.CreatedAt).Select(ToRow));
            Replace(Rooms, rooms.Result.Rooms.Select(r => new RoomRow(r.Name, r.Floor ?? "–", r.AreaSquareMeters is { } a ? Labels.Area(a) : "–",
                r.ScreedTypeName ?? "–", r.ThicknessMillimeters is { } t ? $"{t} mm" : "–", r.UnderfloorHeating ? "ja" : "nein", Labels.Of(r.Status))));
            RoomSummary = $"{rooms.Result.Rooms.Count} Räume · {Labels.Area(rooms.Result.TotalAreaSquareMeters)} gesamt · {Labels.Area(rooms.Result.HeatedAreaSquareMeters)} beheizt";
            Replace(WorkItems, work.Result.OrderBy(w => w.Status == WorkItemStatus.Done).ThenBy(w => w.DueDate).Select(w => new WorkItemRow(w)));
            Replace(Files, files.Result.Where(f => f.State == "stored")
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new FileRow(f.Id, f.Caption ?? f.OriginalName, KindLabel(f.Kind), Size(f.SizeBytes), Labels.DateTime(f.CreatedAt))));
            Replace(Orders, orders.Result.Select(o => new OrderRow(o.Id, o.Number,
                o.DeliveryDate is { } d ? Labels.Date(d) + (o.DeliveryTime is { } t ? $", {t}" : string.Empty) : "nach Absprache", o.SupplierName,
                string.Join(" · ", o.Lines.Select(l => $"{Labels.Quantity(l.Quantity)} {l.Unit} {l.Description}")), Labels.Money(o.Total), Labels.Of(o.Status))));
            OnPropertyChanged(nameof(Responsible));
        });
    }

    private bool CanAddNote() => !string.IsNullOrWhiteSpace(NewNote);

    [RelayCommand(CanExecute = nameof(CanAddNote))]
    private async Task AddNoteAsync()
    {
        await RunAsync(async () =>
        {
            var note = await session.Client.AddNoteAsync(Id, NewNote.Trim());
            Notes.Insert(Notes.TakeWhile(n => n.IsPinned).Count(), ToRow(note));
            NewNote = string.Empty;
        });
    }

    private bool CanAddWorkItem() => !string.IsNullOrWhiteSpace(NewWorkItem);

    [RelayCommand(CanExecute = nameof(CanAddWorkItem))]
    private async Task AddWorkItemAsync()
    {
        await RunAsync(async () =>
        {
            var item = await session.Client.CreateWorkItemAsync(new CreateWorkItemRequest(Guid.CreateVersion7(), Id,
                NewWorkItemIsDefect ? WorkItemKind.Defect : WorkItemKind.Task, NewWorkItem.Trim()));
            WorkItems.Insert(0, new WorkItemRow(item));
            NewWorkItem = string.Empty;
        });
    }

    [RelayCommand]
    private async Task ToggleWorkItemAsync(WorkItemRow? row)
    {
        if (row is null)
        {
            return;
        }

        var status = row.Item.Status == WorkItemStatus.Done ? WorkItemStatus.Open : WorkItemStatus.Done;
        await RunAsync(async () => row.Update(await session.Client.SetWorkItemStatusAsync(row.Item.Id, status)));
    }

    /// <summary>Status setzen (Abschließen, Als Auftrag übernehmen …). Speichert den vollständigen Stand mit Versionsprüfung.</summary>
    [RelayCommand]
    private async Task SetStatusAsync(BauakteStatus status)
    {
        if (Bauakte is not { } current)
        {
            return;
        }

        await RunAsync(async () =>
        {
            Bauakte = await session.Client.UpdateBauakteAsync(current.Id, current.Version, UpdateBauakteRequest.From(current) with { Status = status });
            Replace(Hints, await session.Client.GetHintsAsync(Id));
        });
    }

    private NoteRow ToRow(Note n) => new(n.Id, _people.GetValueOrDefault(n.AuthorUserId, "–"), n.Text, Labels.DateTime(n.CreatedAt), n.IsPinned);

    private static string KindLabel(string kind) => kind switch
    {
        "photo" => "Foto",
        "video" => "Video",
        _ => "Dokument",
    };

    private static string Size(long bytes) => bytes switch
    {
        < 1024 * 1024 => $"{Math.Max(1, bytes / 1024)} KB",
        _ => $"{bytes / (1024.0 * 1024):0.0} MB",
    };

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
