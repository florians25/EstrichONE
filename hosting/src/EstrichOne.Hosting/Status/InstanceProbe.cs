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
}

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
}

/// <summary>Simulation: „erreichbar“, sobald der Web-Container im simulierten Docker läuft.</summary>
public sealed class FakeInstanceProbe(IDockerHost docker, InstanceFiles files) : IInstanceProbe
{
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
