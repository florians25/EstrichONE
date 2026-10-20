using System.Globalization;
using EstrichOne.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace EstrichOne.Desktop.Views;

/// <summary>Plantafel der Woche (Lesen und Öffnen); Planen per Ziehen bleibt vorerst im Browser.</summary>
public sealed partial class PlanningPage : Page, IRefreshable
{
    public PlanningPage()
    {
        ViewModel = new PlanningViewModel(App.Session);
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Required;
    }

    public PlanningViewModel ViewModel { get; }

    /// <summary>Kolonnenfarbe „#RRGGBB“ als Pinsel; ohne Farbe neutral grau.</summary>
    public static Brush Brush(string? color)
    {
        if (color is { Length: 7 } && color[0] == '#' && uint.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    public Task RefreshAsync() => ViewModel.LoadCommand.ExecuteAsync(null);

    protected override async void OnNavigatedTo(NavigationEventArgs e) => await RefreshAsync();

    private void OnOpenAssignment(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Guid id })
        {
            App.MainWindow.OpenBauakte(id);
        }
    }

    private void OnEditInBrowser(object sender, RoutedEventArgs e) => MainWindow.OpenInBrowser("/planung");
}
