using EstrichOne.Desktop.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace EstrichOne.Desktop.Views;

public sealed partial class BauaktenPage : Page, IRefreshable
{
    public BauaktenPage()
    {
        ViewModel = new BauaktenViewModel(App.Session);
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    public BauaktenViewModel ViewModel { get; }

    public Task RefreshAsync() => ViewModel.LoadCommand.ExecuteAsync(null);

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        if (ViewModel.Items.Count == 0)
        {
            await RefreshAsync();
        }
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.LoadCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnOpen(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is BauakteRow row)
        {
            App.MainWindow.OpenBauakte(row.Id);
        }
    }
}
