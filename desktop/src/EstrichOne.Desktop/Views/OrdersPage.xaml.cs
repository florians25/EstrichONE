using EstrichOne.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace EstrichOne.Desktop.Views;

/// <summary>Materialbestellungen verfolgen: bestellt, bestätigt, geliefert. Anlegen mit Bestellschein im Browser.</summary>
public sealed partial class OrdersPage : Page, IRefreshable
{
    public OrdersPage()
    {
        ViewModel = new OrdersViewModel(App.Session);
        InitializeComponent();
    }

    public OrdersViewModel ViewModel { get; }

    public Task RefreshAsync() => ViewModel.LoadCommand.ExecuteAsync(null);

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await RefreshAsync();

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.LoadCommand.Execute(null);
            e.Handled = true;
        }
    }

    private static OrderItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as OrderItem;

    private void OnOrdered(object sender, RoutedEventArgs e) => ViewModel.MarkOrderedCommand.Execute(ItemOf(sender));

    private void OnConfirmed(object sender, RoutedEventArgs e) => ViewModel.MarkConfirmedCommand.Execute(ItemOf(sender));

    private void OnDelivered(object sender, RoutedEventArgs e) => ViewModel.MarkDeliveredCommand.Execute(ItemOf(sender));

    private void OnNewInBrowser(object sender, RoutedEventArgs e) => MainWindow.OpenInBrowser("/bestellungen");
}
