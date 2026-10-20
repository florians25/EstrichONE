using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EstrichOne.Client;

/// <summary>Fehler der API mit verständlicher Meldung und stabilem <see cref="Code"/> (RFC 9457).</summary>
public sealed class ApiException(HttpStatusCode status, string? code, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;

    public string? Code { get; } = code;
}

/// <summary>Speichert die Anmeldung dauerhaft (Windows: Anmeldeinformationsverwaltung bzw. DPAPI).</summary>
public interface ITokenStore
{
    string? LoadRefreshToken();

    void SaveRefreshToken(string? refreshToken);
}

/// <summary>Nur im Speicher, z. B. für Tests oder eine Sitzung ohne „Angemeldet bleiben“.</summary>
public sealed class MemoryTokenStore : ITokenStore
{
    private string? _token;

    public string? LoadRefreshToken() => _token;

    public void SaveRefreshToken(string? refreshToken) => _token = refreshToken;
}

/// <summary>
/// Client der EstrichONE-API v1 für den Windows-Desktop: Anmeldung als <c>desktop</c> (ohne Gerätebindung),
/// Zugriffstoken im Speicher, Refresh-Token im <see cref="ITokenStore"/>. Ein abgelaufenes Zugriffstoken wird
/// einmal erneuert und die Anfrage wiederholt – auch bei parallelen Anfragen nur ein Refresh.
/// </summary>
public sealed partial class EstrichOneClient : IDisposable
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    private readonly HttpClient _http;
    private readonly ITokenStore _tokens;
    private readonly string _clientName;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private string? _accessToken;

    public EstrichOneClient(Uri serverUrl, ITokenStore tokens, HttpMessageHandler? handler = null, string clientName = "Windows")
    {
        ArgumentNullException.ThrowIfNull(serverUrl);
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.BaseAddress = new Uri(serverUrl.ToString().TrimEnd('/') + "/api/v1/");
        _http.Timeout = TimeSpan.FromSeconds(60);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _tokens = tokens;
        _clientName = clientName;
    }

    /// <summary>Angemeldeter Benutzer, sobald <see cref="SignInAsync"/> oder <see cref="RestoreAsync"/> erfolgreich war.</summary>
    public UserSummary? User { get; private set; }

    public bool MustChangePassword { get; private set; }

    public IReadOnlySet<string> Permissions { get; private set; } = new HashSet<string>();

    /// <summary>Die Sitzung ist endgültig vorbei (Refresh abgelehnt, Konto gesperrt); die Oberfläche zeigt die Anmeldung.</summary>
    public event EventHandler<string?>? SessionEnded;

    public bool Has(string permission) => Permissions.Contains(permission);

    public Task<ServerInfo> GetServerInfoAsync(CancellationToken cancellationToken = default) =>
        SendAsync<ServerInfo>(HttpMethod.Get, "server-info", null, authenticated: false, cancellationToken: cancellationToken);

    public async Task SignInAsync(string login, string password, CancellationToken cancellationToken = default)
    {
        var tokens = await SendAsync<TokenResponse>(HttpMethod.Post, "auth/login",
            new { login, password, client = "desktop", clientName = _clientName }, authenticated: false, cancellationToken: cancellationToken);
        _accessToken = tokens.AccessToken;
        _tokens.SaveRefreshToken(tokens.RefreshToken);
        User = tokens.User;
        MustChangePassword = tokens.MustChangePassword;
        await LoadPermissionsAsync(cancellationToken);
    }

    /// <summary>Gespeicherte Anmeldung wiederherstellen. <c>false</c>, wenn keine oder keine gültige vorhanden ist.</summary>
    public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (_tokens.LoadRefreshToken() is null || !await RefreshAsync(null, cancellationToken))
        {
            return false;
        }

        User = await GetAsync<UserSummary>("auth/me", cancellationToken: cancellationToken);
        await LoadPermissionsAsync(cancellationToken);
        return true;
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_accessToken is not null)
            {
                await SendAsync<object>(HttpMethod.Post, "auth/logout", null, cancellationToken: cancellationToken);
            }
        }
        catch (ApiException)
        {
            // Abmelden gelingt lokal immer; der Server beendet die Sitzung spätestens mit Ablauf des Tokens.
        }
        finally
        {
            Clear();
        }
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        await SendAsync<object>(HttpMethod.Post, "auth/change-password", new { currentPassword, newPassword }, cancellationToken: cancellationToken);
        MustChangePassword = false;
    }

    private async Task LoadPermissionsAsync(CancellationToken cancellationToken) =>
        Permissions = (await GetAsync<List<string>>("access/me/permissions", cancellationToken: cancellationToken)).ToHashSet();

    private void Clear()
    {
        _accessToken = null;
        _tokens.SaveRefreshToken(null);
        User = null;
        Permissions = new HashSet<string>();
        MustChangePassword = false;
    }

    /// <summary>Einmal erneuern; wartende Anfragen nutzen das Ergebnis, statt selbst zu erneuern.</summary>
    private async Task<bool> RefreshAsync(string? failedToken, CancellationToken cancellationToken)
    {
        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (failedToken is not null && _accessToken != failedToken)
            {
                return _accessToken is not null;
            }

            var refreshToken = _tokens.LoadRefreshToken();
            if (refreshToken is null)
            {
                return false;
            }

            using var response = await _http.PostAsJsonAsync("auth/refresh", new { refreshToken }, Json, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                Clear();
                return false;
            }

            var tokens = (await response.Content.ReadFromJsonAsync<RefreshResponse>(Json, cancellationToken))!;
            _accessToken = tokens.AccessToken;
            _tokens.SaveRefreshToken(tokens.RefreshToken);
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    internal Task<T> GetAsync<T>(string path, IReadOnlyDictionary<string, string?>? query = null, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Get, WithQuery(path, query), null, cancellationToken: cancellationToken);

    internal async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, bool authenticated = true,
        IReadOnlyDictionary<string, string>? headers = null, CancellationToken cancellationToken = default)
    {
        var token = _accessToken;
        using var response = await SendOnceAsync(method, path, body, authenticated ? token : null, headers, cancellationToken);
        if (authenticated && response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var error = await ToExceptionAsync(response, cancellationToken);
            if (error.Code == "ACCESS_TOKEN_EXPIRED" && await RefreshAsync(token, cancellationToken))
            {
                using var retry = await SendOnceAsync(method, path, body, _accessToken, headers, cancellationToken);
                return await ReadAsync<T>(retry, cancellationToken);
            }

            Clear();
            SessionEnded?.Invoke(this, error.Code);
            throw error;
        }

        return await ReadAsync<T>(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, object? body, string? token,
        IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);
        }

        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        foreach (var (name, value) in headers ?? new Dictionary<string, string>())
        {
            request.Headers.TryAddWithoutValidation(name, value);
        }

        try
        {
            return await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(0, "SERVER_UNREACHABLE", $"Der Server ist nicht erreichbar. Bitte die Verbindung prüfen. ({ex.Message})");
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw await ToExceptionAsync(response, cancellationToken);
        }

        if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0 || typeof(T) == typeof(object))
        {
            return default!;
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken))!;
    }

    private static async Task<ApiException> ToExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? code = null;
        string? message = null;
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            code = root.TryGetProperty("code", out var c) ? c.GetString() : null;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                message = errors.EnumerateObject().SelectMany(e => e.Value.EnumerateArray()).Select(v => v.GetString()).FirstOrDefault();
            }

            message ??= root.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()
                : root.TryGetProperty("title", out var t) ? t.GetString() : null;
        }
        catch (JsonException)
        {
            // Kein Problem-JSON (z. B. Proxy-Fehlerseite): Statusmeldung genügt.
        }

        return new ApiException(response.StatusCode, code,
            message ?? string.Create(CultureInfo.InvariantCulture, $"Die Anfrage ist fehlgeschlagen (Status {(int)response.StatusCode})."));
    }

    private static string WithQuery(string path, IReadOnlyDictionary<string, string?>? query)
    {
        var parts = (query ?? new Dictionary<string, string?>())
            .Where(p => !string.IsNullOrEmpty(p.Value))
            .Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value!)}")
            .ToList();
        return parts.Count == 0 ? path : $"{path}?{string.Join('&', parts)}";
    }

    public void Dispose()
    {
        _http.Dispose();
        _refreshLock.Dispose();
    }
}
