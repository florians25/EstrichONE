using EstrichOne.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EstrichOne.Desktop.Views;

public sealed partial class TodayPage : Page, IRefreshable
{
    public TodayPage()
    {
        ViewModel = new TodayViewModel(App.Session);
        InitializeComponent();
    }

    public TodayViewModel ViewModel { get; }

    public Task RefreshAsync() => ViewModel.LoadCommand.ExecuteAsync(null);

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await RefreshAsync();

    private void OnOpenAssignment(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Guid id })
        {
            App.MainWindow.OpenBauakte(id);
        }
    }
}
