using EstrichOne.Client;

namespace EstrichOne.Desktop;

/// <summary>Dauerhafte Einstellungen der App (Serveradresse, zuletzt verwendeter Anmeldename).</summary>
public interface ISettingsStore
{
    string? Read(string key);

    void Write(string key, string? value);
}

public sealed class MemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, string?> _values = [];

    public string? Read(string key) => _values.GetValueOrDefault(key);

    public void Write(string key, string? value) => _values[key] = value;
}

/// <summary>
/// Verbindung der App zum EstrichONE-Server: Serveradresse merken, anmelden, Anmeldung beim Start wiederherstellen.
/// Es gibt genau eine Sitzung je App; alle Seiten nutzen <see cref="Client"/>.
/// </summary>
public sealed class AppSession(ISettingsStore settings, ITokenStore tokens, Func<Uri, ITokenStore, EstrichOneClient>? clientFactory = null) : IDisposable
{
    public const string ServerKey = "server";
    public const string LoginKey = "login";

    private readonly Func<Uri, ITokenStore, EstrichOneClient> _factory = clientFactory ?? ((url, store) => new EstrichOneClient(url, store));
    private EstrichOneClient? _client;

    public EstrichOneClient Client => _client ?? throw new InvalidOperationException("Keine Verbindung zum Server.");

    public bool IsConnected => _client is not null;

    public bool IsSignedIn => _client?.User is not null;

    public ServerInfo? Server { get; private set; }

    public Uri? ServerUrl { get; private set; }

    public string? LastLogin => settings.Read(LoginKey);

    public string? SavedServer => settings.Read(ServerKey);

    /// <summary>Die Sitzung ist abgelaufen oder wurde abgemeldet; die Oberfläche zeigt wieder die Anmeldung.</summary>
    public event EventHandler<string?>? SignedOut;

    /// <summary>Server prüfen: erreichbar, EstrichONE und eingerichtet.</summary>
    public async Task<ServerInfo> ConnectAsync(string address, CancellationToken cancellationToken = default)
    {
        var url = NormalizeAddress(address);
        var client = _factory(url, tokens);
        try
        {
            var info = await client.GetServerInfoAsync(cancellationToken);
            if (info.SetupRequired)
            {
                throw new InvalidOperationException("Der Server ist noch nicht eingerichtet. Bitte zuerst die Ersteinrichtung im Browser abschließen.");
            }

            Replace(client);
            Server = info;
            ServerUrl = url;
            settings.Write(ServerKey, url.ToString());
            return info;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task SignInAsync(string login, string password, CancellationToken cancellationToken = default)
    {
        await Client.SignInAsync(login, password, cancellationToken);
        settings.Write(LoginKey, login.Trim());
    }

    /// <summary>Beim Start: gespeicherten Server verbinden und mit dem Refresh-Token anmelden. Ohne Erfolg bleibt die Anmeldung sichtbar.</summary>
    public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (SavedServer is not { } server)
        {
            return false;
        }

        try
        {
            await ConnectAsync(server, cancellationToken);
            return await Client.RestoreAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or ApiException or InvalidOperationException or TaskCanceledException)
        {
            return false;
        }
    }

    public async Task SignOutAsync()
    {
        if (_client is not null)
        {
            await _client.SignOutAsync();
        }

        SignedOut?.Invoke(this, null);
    }

    /// <summary>„estrich.firma.de“ → https://estrich.firma.de; Pfade und Schrägstriche am Ende werden entfernt.</summary>
    public static Uri NormalizeAddress(string address)
    {
        var text = (address ?? string.Empty).Trim().TrimEnd('/');
        if (text.Length == 0)
        {
            throw new ArgumentException("Bitte die Adresse des EstrichONE-Servers angeben.", nameof(address));
        }

        if (!text.Contains("://", StringComparison.Ordinal))
        {
            text = "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Die Serveradresse ist ungültig.", nameof(address));
        }

        return new Uri(uri.GetLeftPart(UriPartial.Authority));
    }

    private void Replace(EstrichOneClient client)
    {
        if (_client is not null)
        {
            _client.SessionEnded -= OnSessionEnded;
            _client.Dispose();
        }

        _client = client;
        _client.SessionEnded += OnSessionEnded;
    }

    private void OnSessionEnded(object? sender, string? reason) => SignedOut?.Invoke(this, reason);

    public void Dispose() => _client?.Dispose();
}
