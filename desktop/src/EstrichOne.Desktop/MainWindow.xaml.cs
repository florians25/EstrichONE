using EstrichOne.Client;
using EstrichOne.Desktop.Controls;
using EstrichOne.Desktop.Services;
using EstrichOne.Desktop.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace EstrichOne.Desktop;

/// <summary>Seite, die „Aktualisieren“ (F5, Ribbon) unterstützt.</summary>
public interface IRefreshable
{
    Task RefreshAsync();
}

/// <summary>Treffer der Suche in der Titelleiste: Befehl oder Bauakte.</summary>
public sealed record SearchHit(string Text, Action Open)
{
    public override string ToString() => Text;
}

/// <summary>
/// Hauptfenster (docs/reengineering/06, Abschnitt 3): Titelleiste mit Suche, Ribbon, Navigation links, Inhalt, Statusleiste.
/// </summary>
public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> Pages = new()
    {
        ["today"] = typeof(TodayPage),
        ["bauakten"] = typeof(BauaktenPage),
        ["bauakte"] = typeof(BauaktePage),
        ["planning"] = typeof(PlanningPage),
        ["calls"] = typeof(CallsPage),
        ["orders"] = typeof(OrdersPage),
        ["helpdesk"] = typeof(HelpdeskPage),
    };

    private CancellationTokenSource? _search;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1440, 900));
        AppWindow.Title = "EstrichONE";
        StatusVersion.Text = $"EstrichONE für Windows {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";

        SignIn.SignedIn += (_, _) => ShowShell();
        App.Session.SignedOut += (_, reason) => DispatcherQueue.TryEnqueue(() => ShowSignIn(reason));
        AddAccelerator(VirtualKey.K, VirtualKeyModifiers.Control, () => SearchBox.Focus(FocusState.Keyboard));
        AddAccelerator(VirtualKey.F1, VirtualKeyModifiers.Control, Ribbon.ToggleMinimized);
        AddAccelerator(VirtualKey.F5, VirtualKeyModifiers.None, () => _ = RefreshCurrentAsync());
        Root.Loaded += async (_, _) =>
        {
            if (await App.Session.RestoreAsync())
            {
                ShowShell();
            }
            else
            {
                SignIn.Start();
            }
        };
    }

    public void Navigate(string key, object? parameter = null)
    {
        if (Pages.TryGetValue(key, out var page))
        {
            ContentFrame.Navigate(page, parameter);
        }
    }

    public void OpenBauakte(Guid id) => Navigate("bauakte", id);

    /// <summary>Verwaltung und Pflege, die bewusst nur im Web liegt (06, Abschnitt 1), im Standardbrowser öffnen.</summary>
    public static void OpenInBrowser(string path)
    {
        if (App.Session.ServerUrl is { } server)
        {
            _ = Launcher.LaunchUriAsync(new Uri(server, path));
        }
    }

    private void ShowShell()
    {
        var client = App.Session.Client;
        SignIn.Visibility = Visibility.Collapsed;
        Shell.Visibility = Visibility.Visible;
        SearchBox.Visibility = Visibility.Visible;
        UserButton.Visibility = Visibility.Visible;
        UserButton.Content = client.User?.DisplayName;
        StatusServer.Text = $"Verbunden mit {App.Session.Server?.ServerName} ({App.Session.ServerUrl?.Host})";
        StatusUser.Text = $"Angemeldet: {client.User?.DisplayName}";
        NavPlanning.Visibility = client.Has("planning.read") ? Visibility.Visible : Visibility.Collapsed;
        NavCalls.Visibility = client.Has("contacts.read") ? Visibility.Visible : Visibility.Collapsed;
        NavOrders.Visibility = client.Has("materials.read") ? Visibility.Visible : Visibility.Collapsed;
        Ribbon.SetTabs(RibbonDefinition.Build(this, client));
        Diagnostics.Log("Angemeldet");
        Navigate("today");
    }

    private void ShowSignIn(string? reason)
    {
        Shell.Visibility = Visibility.Collapsed;
        SearchBox.Visibility = Visibility.Collapsed;
        UserButton.Visibility = Visibility.Collapsed;
        ContentFrame.BackStack.Clear();
        SignIn.Visibility = Visibility.Visible;
        SignIn.Start(reason is null ? null : "Ihre Sitzung ist abgelaufen. Bitte erneut anmelden.");
    }

    internal async Task RefreshCurrentAsync()
    {
        if (ContentFrame.Content is IRefreshable page)
        {
            await page.RefreshAsync();
        }
    }

    internal BauaktePage? CurrentBauakte => ContentFrame.Content as BauaktePage;

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string key } && ContentFrame.Content?.GetType() != Pages.GetValueOrDefault(key))
        {
            Navigate(key);
        }
    }

    private void OnBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    private void OnNavigated(object sender, NavigationEventArgs e)
    {
        var key = Pages.FirstOrDefault(p => p.Value == e.SourcePageType).Key;
        Ribbon.ShowContext(key == "bauakte" ? "bauakte" : null);
        var item = Nav.MenuItems.Concat(Nav.FooterMenuItems).OfType<NavigationViewItem>()
            .FirstOrDefault(i => (string)i.Tag == (key == "bauakte" ? "bauakten" : key));
        if (item is not null && !ReferenceEquals(Nav.SelectedItem, item))
        {
            Nav.SelectedItem = item;
        }
    }

    private async void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        var text = sender.Text.Trim();
        _search?.Cancel();
        _search = new CancellationTokenSource();
        var token = _search.Token;
        var hits = Ribbon.AllCommands.Where(c => text.Length > 0 && c.Label.Contains(text, StringComparison.CurrentCultureIgnoreCase))
            .DistinctBy(c => c.Label).Take(5).Select(c => new SearchHit($"Befehl: {c.Label}", c.Execute)).ToList();
        sender.ItemsSource = hits;
        if (text.Length < 2)
        {
            return;
        }

        try
        {
            await Task.Delay(250, token);
            var page = await App.Session.Client.ListBauaktenAsync(text, limit: 8, cancellationToken: token);
            hits.AddRange(page.Items.Select(b => new SearchHit($"{b.Number ?? "neu"} · {b.Title} ({Labels.Of(b.Status)})", () => OpenBauakte(b.Id))));
            if (!token.IsCancellationRequested)
            {
                sender.ItemsSource = hits.ToList();
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or ApiException)
        {
            // Neue Eingabe oder Server nicht erreichbar: Befehlstreffer bleiben stehen.
        }
    }

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var hit = args.ChosenSuggestion as SearchHit ?? (sender.ItemsSource as IEnumerable<SearchHit>)?.FirstOrDefault();
        if (hit is not null)
        {
            sender.Text = string.Empty;
            sender.ItemsSource = null;
            hit.Open();
        }
    }

    private void OnChangePassword(object sender, RoutedEventArgs e) => OpenInBrowser("/konto/passwort");

    private void OnOpenWeb(object sender, RoutedEventArgs e) => OpenInBrowser("/start");

    private async void OnSignOut(object sender, RoutedEventArgs e) => await App.Session.SignOutAsync();

    private void AddAccelerator(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, args) =>
        {
            if (Shell.Visibility == Visibility.Visible)
            {
                action();
                args.Handled = true;
            }
        };
        Root.KeyboardAccelerators.Add(accelerator);
    }
}
