using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EstrichOne.Hosting.Docker;
using EstrichOne.Hosting.Instances;

namespace EstrichOne.Hosting.Status;

/// <summary>Antwort einer Instanz auf <c>/health/ready</c> und <c>/api/v1/server-info</c>.</summary>
public sealed record ProbeResult(
    bool Reachable,
    int? HealthStatusCode,
    string? HealthText,
    string? ServerVersion,
    string? ServerName,
    bool? SetupRequired,
    string? Error);

public interface IInstanceProbe
{
    Task<ProbeResult> ProbeAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Setzt die Systemhinweise der Instanz (<c>PUT /api/v1/system/notices</c> mit Instanz-Schlüssel).</summary>
    Task<NoticePushResult> PushNoticesAsync(string slug, string token, string json, CancellationToken cancellationToken);

    /// <summary>
    /// Stand der App einer Plattform (<c>GET /api/v1/app-updates/check</c>, ohne Anmeldung). <c>null</c>, wenn die
    /// Suite-Version den Endpunkt noch nicht kennt (vor 1.0.13).
    /// </summary>
    Task<AppPlatformInfo?> CheckAppAsync(string slug, string platform, CancellationToken cancellationToken);

    /// <summary>Ersteinrichtung der Suite (<c>POST /api/v1/setup</c>): Firma und erste Admin-Person mit dem Einrichtungscode.</summary>
    Task<NoticePushResult> CompleteSetupAsync(string slug, SuiteSetupRequest request, CancellationToken cancellationToken);
}

public sealed record NoticePushResult(bool Success, int? StatusCode, string? Error);

public sealed record SuiteSetupAdministrator(string DisplayName, string UserName, string? Email, string Password);

/// <summary>Anfrage an <c>POST /api/v1/setup</c> der Suite (Felder wie <c>CompleteSetupRequest</c>).</summary>
public sealed record SuiteSetupRequest(string SetupCode, string CompanyName, SuiteSetupAdministrator Administrator);

/// <summary>Fragt die Instanz über ihr Edge-Netz direkt beim Caddy der Instanz ab (ohne den öffentlichen Proxy).</summary>
public sealed class HttpInstanceProbe(HttpClient http) : IInstanceProbe
{
    private sealed record ServerInfo(
        [property: JsonPropertyName("serverName")] string? ServerName,
        [property: JsonPropertyName("serverVersion")] string? ServerVersion,
        [property: JsonPropertyName("setupRequired")] bool? SetupRequired);

    public async Task<ProbeResult> ProbeAsync(string slug, CancellationToken cancellationToken)
    {
        var baseUri = new Uri($"http://{InstanceLayout.WebContainer(slug)}:{InstanceLayout.InstanceHttpPort}/");
        int? healthCode = null;
        string? healthText = null;
        try
        {
            using var health = await http.GetAsync(new Uri(baseUri, "health/ready"), cancellationToken);
            healthCode = (int)health.StatusCode;
            healthText = (await health.Content.ReadAsStringAsync(cancellationToken)).Trim();
            if (healthText.Length > 200)
            {
                healthText = healthText[..200];
            }

            using var info = await http.GetAsync(new Uri(baseUri, "api/v1/server-info"), cancellationToken);
            if (!info.IsSuccessStatusCode)
            {
                return new ProbeResult(true, healthCode, healthText, null, null, null, $"server-info: HTTP {(int)info.StatusCode}");
            }

            var server = await info.Content.ReadFromJsonAsync<ServerInfo>(cancellationToken);
            return new ProbeResult(true, healthCode, healthText, server?.ServerVersion, server?.ServerName, server?.SetupRequired, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return new ProbeResult(healthCode is not null, healthCode, healthText, null, null, null, exception.Message);
        }
    }

    public async Task<NoticePushResult> CompleteSetupAsync(string slug, SuiteSetupRequest request, CancellationToken cancellationToken)
    {
        var uri = new Uri($"http://{InstanceLayout.WebContainer(slug)}:{InstanceLayout.InstanceHttpPort}/api/v1/setup");
        try
        {
            using var response = await http.PostAsJsonAsync(uri, request, cancellationToken);
            var code = (int)response.StatusCode;
            return response.IsSuccessStatusCode
                ? new NoticePushResult(true, code, null)
                : new NoticePushResult(false, code, $"HTTP {code}: {Shorten(await response.Content.ReadAsStringAsync(cancellationToken))}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new NoticePushResult(false, null, exception.Message);
        }
    }

    private sealed record AppDownloadDto(
        [property: JsonPropertyName("kind")] string? Kind,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("size")] long? Size);

    private sealed record AppCheckDto(
        [property: JsonPropertyName("latestVersion")] string? LatestVersion,
        [property: JsonPropertyName("minimumVersion")] string? MinimumVersion,
        [property: JsonPropertyName("download")] AppDownloadDto? Download);

    public async Task<AppPlatformInfo?> CheckAppAsync(string slug, string platform, CancellationToken cancellationToken)
    {
        // Version 0.0.0: „neueste“ und Mindestversion kommen immer mit, unabhängig vom Gerät.
        var uri = new Uri($"http://{InstanceLayout.WebContainer(slug)}:{InstanceLayout.InstanceHttpPort}/api/v1/app-updates/check?platform={platform}&version=0.0.0");
        using var response = await http.GetAsync(uri, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<AppCheckDto>(cancellationToken);
        var packageAvailable = string.Equals(dto?.Download?.Kind, "package", StringComparison.OrdinalIgnoreCase) && dto?.Download?.Url is not null;
        return new AppPlatformInfo(platform, dto?.LatestVersion, dto?.MinimumVersion, dto?.Download?.Kind, packageAvailable,
            packageAvailable ? dto?.Download?.Size : null);
    }

    private static string Shorten(string text) => text.Trim() is { Length: > 200 } long_ ? long_[..200] : text.Trim();

    public async Task<NoticePushResult> PushNoticesAsync(string slug, string token, string json, CancellationToken cancellationToken)
    {
        var uri = new Uri($"http://{InstanceLayout.WebContainer(slug)}:{InstanceLayout.InstanceHttpPort}/api/v1/system/notices");
        using var request = new HttpRequestMessage(HttpMethod.Put, uri)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            var code = (int)response.StatusCode;
            return response.IsSuccessStatusCode
                ? new NoticePushResult(true, code, null)
                : new NoticePushResult(false, code, code == 404
                    ? "Die Suite-Version dieser Instanz kennt noch keine Systemhinweise (bitte aktualisieren)."
                    : $"HTTP {code}: {Shorten(await response.Content.ReadAsStringAsync(cancellationToken))}");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new NoticePushResult(false, null, exception.Message);
        }
    }
}

/// <summary>Simulation: „erreichbar“, sobald der Web-Container im simulierten Docker läuft.</summary>
public sealed class FakeInstanceProbe(IDockerHost docker, InstanceFiles files) : IInstanceProbe
{
    /// <summary>Zuletzt an eine Instanz gesendete Systemhinweise (für Tests und Vorführung).</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, string> PushedNotices { get; } = new(StringComparer.Ordinal);

    /// <summary>Eingerichtete Instanzen (Simulation von <c>POST /api/v1/setup</c>).</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, SuiteSetupRequest> CompletedSetups { get; } = new(StringComparer.Ordinal);

    public async Task<NoticePushResult> CompleteSetupAsync(string slug, SuiteSetupRequest request, CancellationToken cancellationToken)
    {
        var containers = await docker.ListContainersAsync(InstanceLayout.ProjectName(slug), cancellationToken);
        if (!containers.Any(c => c.Service == "api" && c.State == "running"))
        {
            return new NoticePushResult(false, null, "Instanz läuft nicht (Simulation)");
        }

        if (request.Administrator.Password.Length < 10)
        {
            return new NoticePushResult(false, 422, "Passwort zu kurz (Simulation)");
        }

        return CompletedSetups.TryAdd(slug, request)
            ? new NoticePushResult(true, 201, null)
            : new NoticePushResult(false, 409, "EstrichONE ist bereits eingerichtet.");
    }

    public async Task<NoticePushResult> PushNoticesAsync(string slug, string token, string json, CancellationToken cancellationToken)
    {
        var containers = await docker.ListContainersAsync(InstanceLayout.ProjectName(slug), cancellationToken);
        if (!containers.Any(c => c.Service == "api" && c.State == "running"))
        {
            return new NoticePushResult(false, null, "Instanz läuft nicht (Simulation)");
        }

        PushedNotices[slug] = json;
        return new NoticePushResult(true, 204, null);
    }

    public async Task<AppPlatformInfo?> CheckAppAsync(string slug, string platform, CancellationToken cancellationToken)
    {
        var env = await files.ReadEnvAsync(slug, cancellationToken);
        var version = env.GetValueOrDefault("ESTRICHONE_VERSION", "0.0.0");
        // Simulation: Suite-Versionen mit Vorabkennung „preview.3“ kennen die App-Updates noch nicht.
        if (version.EndsWith("preview.3", StringComparison.Ordinal))
        {
            return null;
        }

        var app = version.Split('-')[0];
        return platform == "android"
            ? new AppPlatformInfo("android", app, "1.0.10", "package", true, 38_412_288)
            : new AppPlatformInfo("ios", app, "1.0.10", "store", false, null);
    }

    public async Task<ProbeResult> ProbeAsync(string slug, CancellationToken cancellationToken)
    {
        var containers = await docker.ListContainersAsync(InstanceLayout.ProjectName(slug), cancellationToken);
        if (!containers.Any(c => c.Service == "caddy" && c.State == "running"))
        {
            return new ProbeResult(false, null, null, null, null, null, "Verbindung abgelehnt (Simulation)");
        }

        var env = await files.ReadEnvAsync(slug, cancellationToken);
        if (env.GetValueOrDefault("ESTRICHONE_VERSION", string.Empty).Contains("ungesund", StringComparison.Ordinal))
        {
            // Simulation: Container laufen, die Datenbankmigration der neuen Version scheitert aber (Bereitschaft fehlt).
            return new ProbeResult(true, 503, "Unhealthy", null, null, null, null);
        }

        return new ProbeResult(true, 200, "Healthy", env.GetValueOrDefault("ESTRICHONE_VERSION"),
            env.GetValueOrDefault("ESTRICHONE_SERVER_NAME"), !CompletedSetups.ContainsKey(slug), null);
    }
}
