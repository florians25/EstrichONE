using EstrichOne.Desktop.Services;
using Microsoft.UI.Xaml;

namespace EstrichOne.Desktop;

/// <summary>Einstieg der Windows-App: eine Sitzung, ein Hauptfenster.</summary>
public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => Diagnostics.Log("Unbehandelter Fehler", e.Exception);
    }

    public static AppSession Session { get; } = new(new JsonSettingsStore(), new PasswordVaultTokenStore());

    public static MainWindow MainWindow => ((App)Current)._window!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
