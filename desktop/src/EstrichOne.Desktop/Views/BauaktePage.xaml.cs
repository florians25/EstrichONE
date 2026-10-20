using System.ComponentModel;
using EstrichOne.Client;
using EstrichOne.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace EstrichOne.Desktop.Views;

/// <summary>Bauakte im Desktop: Kopf, Hinweiszeilen, Reiter (06: „Bauakten … Akte mit allen Reitern“).</summary>
public sealed partial class BauaktePage : Page, IRefreshable, INotifyPropertyChanged
{
    public BauaktePage()
    {
        InitializeComponent();
    }

    public BauakteViewModel ViewModel { get; private set; } = null!;

    public string CustomerName => ViewModel?.Customer?.Name ?? "–";

    public string CustomerPhone => ViewModel?.Customer?.Phone ?? "–";

    public string CustomerEmail => ViewModel?.Customer?.Email ?? "–";

    public event PropertyChangedEventHandler? PropertyChanged;

    public static InfoBarSeverity SeverityOf(BauakteHintSeverity severity) => severity switch
    {
        BauakteHintSeverity.Success => InfoBarSeverity.Success,
        BauakteHintSeverity.Warning => InfoBarSeverity.Warning,
        BauakteHintSeverity.Danger => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };

    public Task RefreshAsync() => ViewModel.LoadCommand.ExecuteAsync(null);

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel = new BauakteViewModel(App.Session, (Guid)e.Parameter);
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(BauakteViewModel.Customer))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CustomerName)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CustomerPhone)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CustomerEmail)));
            }
        };
        MaterialTab.Visibility = ViewModel.CanSeeMaterial ? Visibility.Visible : Visibility.Collapsed;
        Bindings.Update();
        await RefreshAsync();
    }

    /// <summary>Reiter öffnen (auch aus dem Kontextregister „Bauakte“).</summary>
    public void ShowTab(string key)
    {
        Tabs.SelectedItem = Tabs.Items.FirstOrDefault(i => (string)i.Tag == key) ?? Tabs.Items[0];
        if (key == "notizen")
        {
            NoteBox.Focus(FocusState.Programmatic);
        }
        else if (key == "aufgaben")
        {
            WorkBox.Focus(FocusState.Programmatic);
        }
    }

    public void SetStatus(BauakteStatus status) => ViewModel.SetStatusCommand.Execute(status);

    public void OpenRoute()
    {
        if (ViewModel.Bauakte is { } b)
        {
            var target = b.Latitude is { } lat && b.Longitude is { } lon
                ? FormattableString.Invariant($"{lat},{lon}")
                : Uri.EscapeDataString(Labels.Address(b.Street, b.PostalCode, b.City));
            _ = Launcher.LaunchUriAsync(new Uri($"https://www.google.com/maps/dir/?api=1&destination={target}"));
        }
    }

    public void OpenInBrowser() => MainWindow.OpenInBrowser($"/bauakten/{ViewModel.Id}");

    private void OnTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var key = sender.SelectedItem?.Tag as string;
        if (PanelMaterial is null)
        {
            return; // Während InitializeComponent: Übersicht ist bereits sichtbar.
        }

        PanelOverview.Visibility = Show.If(key == "uebersicht");
        PanelNotes.Visibility = Show.If(key == "notizen");
        PanelRooms.Visibility = Show.If(key == "raeume");
        PanelWork.Visibility = Show.If(key == "aufgaben");
        PanelFiles.Visibility = Show.If(key == "dateien");
        PanelMaterial.Visibility = Show.If(key == "material");
    }

    private void OnComplete(object sender, RoutedEventArgs e) => SetStatus(BauakteStatus.Completed);

    private void OnRoute(object sender, RoutedEventArgs e) => OpenRoute();

    private void OnOpenWeb(object sender, RoutedEventArgs e) => OpenInBrowser();

    private void OnNoteKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (e.Key == VirtualKey.Enter && ctrl && ViewModel.AddNoteCommand.CanExecute(null))
        {
            ViewModel.AddNoteCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnWorkKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.AddWorkItemCommand.CanExecute(null))
        {
            ViewModel.AddWorkItemCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnToggleWork(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkItemRow row })
        {
            ViewModel.ToggleWorkItemCommand.Execute(row);
        }
    }
}
