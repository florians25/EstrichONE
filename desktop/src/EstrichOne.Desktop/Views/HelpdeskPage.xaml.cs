using EstrichOne.Desktop.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace EstrichOne.Desktop.Views;

/// <summary>Problem melden (alle) und offene Meldungen (Helpdesk-Bearbeitung); Bearbeitung der Tickets im Browser.</summary>
public sealed partial class HelpdeskPage : Page, IRefreshable
{
    public HelpdeskPage()
    {
        ViewModel = new TicketsViewModel(App.Session);
        InitializeComponent();
    }

    public TicketsViewModel ViewModel { get; }

    public string ListTitle => ViewModel.IsAgent ? "Offene Meldungen" : "Meine Meldungen";

    public Task RefreshAsync() => ViewModel.LoadCommand.ExecuteAsync(null);

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await RefreshAsync();
}
