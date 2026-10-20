using EstrichOne.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace EstrichOne.Desktop.Views;

/// <summary>Erststart-Assistent und Anmeldung (06, Abschnitt 4).</summary>
public sealed partial class SignInView : UserControl
{
    public SignInView()
    {
        ViewModel = new SignInViewModel(App.Session);
        InitializeComponent();
        ViewModel.SignedIn += (_, _) => SignedIn?.Invoke(this, EventArgs.Empty);
    }

    public SignInViewModel ViewModel { get; }

    public event EventHandler? SignedIn;

    /// <summary>Anmeldung anzeigen; optional mit Hinweis (z. B. Sitzung abgelaufen).</summary>
    public void Start(string? notice = null)
    {
        Notice.Message = notice ?? string.Empty;
        Notice.IsOpen = notice is not null;
        if (!ViewModel.IsConnected && !string.IsNullOrWhiteSpace(ViewModel.ServerAddress))
        {
            ViewModel.ConnectCommand.Execute(null);
        }

        (ViewModel.IsConnected ? (Control)PasswordBox : ServerBox).Focus(FocusState.Programmatic);
    }

    private void OnServerKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.ConnectCommand.CanExecute(null))
        {
            ViewModel.ConnectCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnPasswordKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && ViewModel.SignInCommand.CanExecute(null))
        {
            ViewModel.SignInCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnOpenPasswordPage(object sender, RoutedEventArgs e) => MainWindow.OpenInBrowser("/konto/passwort");
}
