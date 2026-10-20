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
}

public sealed record NoticePushResult(bool Success, int? StatusCode, string? Error);

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

    public async Task<ProbeResult> ProbeAsync(string slug, CancellationToken cancellationToken)
    {
        var containers = await docker.ListContainersAsync(InstanceLayout.ProjectName(slug), cancellationToken);
        if (!containers.Any(c => c.Service == "caddy" && c.State == "running"))
        {
            return new ProbeResult(false, null, null, null, null, null, "Verbindung abgelehnt (Simulation)");
        }

        var env = await files.ReadEnvAsync(slug, cancellationToken);
        return new ProbeResult(true, 200, "Healthy", env.GetValueOrDefault("ESTRICHONE_VERSION"),
            env.GetValueOrDefault("ESTRICHONE_SERVER_NAME"), true, null);
    }
}
