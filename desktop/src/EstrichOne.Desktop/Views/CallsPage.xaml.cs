using EstrichOne.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace EstrichOne.Desktop.Views;

/// <summary>Anrufe mit Schnellerfassung während des Gesprächs (wie „Telefon“ im Web).</summary>
public sealed partial class CallsPage : Page, IRefreshable
{
    public CallsPage()
    {
        ViewModel = new CallsViewModel(App.Session);
        InitializeComponent();
    }

    public CallsViewModel ViewModel { get; }

    public static bool HasText(string? value) => !string.IsNullOrEmpty(value);

    public Task RefreshAsync() => ViewModel.LoadCommand.ExecuteAsync(null);

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        await RefreshAsync();
        CallerBox.Focus(FocusState.Programmatic);
    }

    private void OnNotesKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (e.Key == VirtualKey.Enter && ctrl && ViewModel.SaveQuickCallCommand.CanExecute(null))
        {
            ViewModel.SaveQuickCallCommand.Execute(null);
            e.Handled = true;
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

    private void OnDone(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CallRow row })
        {
            ViewModel.CompleteFollowUpCommand.Execute(row);
        }
    }
}
