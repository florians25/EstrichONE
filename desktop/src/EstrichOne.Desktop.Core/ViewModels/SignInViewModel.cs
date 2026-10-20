using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace EstrichOne.Desktop.ViewModels;

/// <summary>Erststart und Anmeldung: Server verbinden, dann Anmeldename und Passwort.</summary>
public sealed partial class SignInViewModel(AppSession session) : PageViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    public partial string ServerAddress { get; set; } = session.SavedServer ?? string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string Login { get; set; } = session.LastLogin ?? string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    public partial string Password { get; set; } = string.Empty;

    /// <summary>Server ist bestätigt; jetzt Anmeldedaten.</summary>
    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    public partial string? ServerName { get; set; }

    /// <summary>Nach Einmalpasswort: eigenes Passwort im Browser setzen.</summary>
    [ObservableProperty]
    public partial bool MustChangePassword { get; set; }

    public event EventHandler? SignedIn;

    private bool CanConnect() => !string.IsNullOrWhiteSpace(ServerAddress);

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        await RunAsync(async () =>
        {
            var info = await session.ConnectAsync(ServerAddress);
            ServerName = info.ServerName;
            IsConnected = true;
        });
    }

    [RelayCommand]
    private void ChangeServer()
    {
        IsConnected = false;
        Error = null;
    }

    private bool CanSignIn() => !string.IsNullOrWhiteSpace(Login) && !string.IsNullOrEmpty(Password);

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        var ok = await RunAsync(() => session.SignInAsync(Login.Trim(), Password));
        Password = string.Empty;
        if (ok)
        {
            MustChangePassword = session.Client.MustChangePassword;
            if (!MustChangePassword)
            {
                SignedIn?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
