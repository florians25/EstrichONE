using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EstrichOne.Client;

namespace EstrichOne.Desktop.ViewModels;

public sealed record OrderFilter(string Key, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class OrderItem(MaterialOrder order) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Number), nameof(Delivery), nameof(Supplier), nameof(Site), nameof(Lines), nameof(Total), nameof(Status),
        nameof(CanOrder), nameof(CanConfirm), nameof(CanDeliver))]
    public partial MaterialOrder Order { get; set; } = order;

    public string Number => Order.Number;

    public string Delivery => Order.DeliveryDate is { } d ? Labels.Date(d) + (Order.DeliveryTime is { } t ? $", {t}" : string.Empty) : "nach Absprache";

    public string Supplier => Order.SupplierName;

    public string Site => Order.BauakteId is null ? "Lager / ohne Bauakte" : $"{Order.BauakteNumber} · {Order.BauakteTitle}";

    public string Lines => string.Join(" · ", Order.Lines.Select(l => $"{Labels.Quantity(l.Quantity)} {l.Unit} {l.Description}"));

    public string Total => Labels.Money(Order.Total);

    public string Status => Labels.Of(Order.Status);

    public bool CanOrder => Order.Status == OrderStatus.Draft && Order.Lines.Count > 0;

    public bool CanConfirm => Order.Status == OrderStatus.Ordered;

    public bool CanDeliver => Order.Status is OrderStatus.Ordered or OrderStatus.Confirmed;
}

/// <summary>Materialbestellungen: Filter, Status führen (Bestellt, Bestätigt, Geliefert).</summary>
public sealed partial class OrdersViewModel(AppSession session) : PageViewModel
{
    public static IReadOnlyList<OrderFilter> Filters { get; } =
    [
        new("open", "Offen"), new("draft", "Entwürfe"), new("delivered", "Geliefert"), new("cancelled", "Storniert"), new("all", "Alle"),
    ];

    public ObservableCollection<OrderItem> Items { get; } = [];

    [ObservableProperty]
    public partial OrderFilter Filter { get; set; } = Filters[0];

    [ObservableProperty]
    public partial string Search { get; set; } = string.Empty;

    public bool CanManage => session.Client.Has("materials.manage");

    partial void OnFilterChanged(OrderFilter value) => LoadCommand.Execute(null);

    [RelayCommand]
    private async Task LoadAsync()
    {
        OrderStatus? status = Filter.Key switch
        {
            "draft" => OrderStatus.Draft,
            "delivered" => OrderStatus.Delivered,
            "cancelled" => OrderStatus.Cancelled,
            _ => null,
        };
        await LoadLatestAsync(() => session.Client.GetOrdersAsync(open: Filter.Key == "open", status: status,
                search: string.IsNullOrWhiteSpace(Search) ? null : Search.Trim()),
            orders =>
            {
                Items.Clear();
                foreach (var o in orders)
                {
                    Items.Add(new OrderItem(o));
                }
            });
    }

    [RelayCommand]
    private Task MarkOrderedAsync(OrderItem? item) => SetAsync(item, OrderStatus.Ordered);

    [RelayCommand]
    private Task MarkConfirmedAsync(OrderItem? item) => SetAsync(item, OrderStatus.Confirmed);

    [RelayCommand]
    private Task MarkDeliveredAsync(OrderItem? item) => SetAsync(item, OrderStatus.Delivered);

    private async Task SetAsync(OrderItem? item, OrderStatus status)
    {
        if (item is null)
        {
            return;
        }

        await RunAsync(async () => item.Order = await session.Client.SetOrderStatusAsync(item.Order.Id, status));
    }
}
