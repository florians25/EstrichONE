using System.Net.Http.Json;
using System.Text.Json.Serialization;
using EstrichOne.Hosting.Domain;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Infrastructure;

public sealed record VersionList(IReadOnlyList<string> Versions, string? Latest, string Source, string? Error);

/// <summary>
/// Verfügbare Versionen der Suite: Tags des API-Images in der Registry (GHCR, anonymer Lesezugriff).
/// Ergebnis wird 15 Minuten zwischengespeichert; ohne Registry-Zugriff liefert die Konsole nur bekannte Versionen.
/// </summary>
public sealed class VersionCatalog(HttpClient http, IOptions<HostingOptions> options, TimeProvider clock, ILogger<VersionCatalog> logger)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(15);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (VersionList List, DateTimeOffset At)> cache = new(StringComparer.Ordinal);

    private sealed record TokenResponse([property: JsonPropertyName("token")] string? Token);

    private sealed record TagsResponse([property: JsonPropertyName("tags")] List<string>? Tags);

    /// <param name="image">Image-Name in der Registry: <c>estrichone-api</c> (Suite) oder <c>estrichone-hosting</c> (Konsole).</param>
    public async Task<VersionList> GetAsync(IEnumerable<string> known, CancellationToken cancellationToken, string image = "estrichone-api")
    {
        var knownList = known.Append(image == "estrichone-api" ? options.Value.DefaultVersion : ConsoleVersion.Current).ToList();
        if (!cache.TryGetValue(image, out var entry) || clock.GetUtcNow() - entry.At > CacheDuration)
        {
            entry = (await FetchAsync(image, cancellationToken), clock.GetUtcNow());
            cache[image] = entry;
        }

        var cached = entry.List;

        var versions = cached.Versions.Concat(knownList)
            .Where(v => Validation.CheckVersion(v) is null)
            .Distinct(StringComparer.Ordinal)
            .Select(v => (Text: v, Parsed: SemanticVersion.TryParse(v)))
            .Where(v => v.Parsed is not null)
            .OrderByDescending(v => v.Parsed)
            .Select(v => v.Text)
            .ToList();
        return cached with { Versions = versions, Latest = versions.FirstOrDefault() };
    }

    private async Task<VersionList> FetchAsync(string image, CancellationToken cancellationToken)
    {
        if (options.Value.IsFake)
        {
            return new VersionList(["0.1.0-preview.3", "0.1.0-preview.4", "0.1.0-preview.5"], null, "Simulation", null);
        }

        var registry = options.Value.Registry.TrimEnd('/');
        var slash = registry.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0)
        {
            return new VersionList([], null, "bekannt", "Registry-Angabe ohne Namensraum.");
        }

        var host = registry[..slash];
        var repository = $"{registry[(slash + 1)..]}/{image}";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var token = await http.GetFromJsonAsync<TokenResponse>(
                new Uri($"https://{host}/token?scope=repository:{repository}:pull&service={host}"), timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"https://{host}/v2/{repository}/tags/list?n=1000"));
            if (!string.IsNullOrEmpty(token?.Token))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Token);
            }

            using var response = await http.SendAsync(request, timeout.Token);
            response.EnsureSuccessStatusCode();
            var tags = await response.Content.ReadFromJsonAsync<TagsResponse>(timeout.Token);
            return new VersionList(tags?.Tags?.Where(t => Validation.CheckVersion(t) is null).ToList() ?? [], null, host, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning("Versionen aus {Registry} nicht abrufbar: {Message}", registry, exception.Message);
            return new VersionList([], null, "bekannt", $"Registry nicht erreichbar: {exception.Message}");
        }
    }
}
