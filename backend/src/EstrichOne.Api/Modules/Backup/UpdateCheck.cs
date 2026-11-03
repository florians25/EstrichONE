using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Modules.Access.Exports;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Backup;

/// <summary>Abschnitt <c>EstrichOne:Updates</c>: Prüfung auf neue Versionen (docs/sicherheit.md, „Sicherheitsupdates“).</summary>
public sealed class UpdateCheckOptions
{
    public const string SectionName = "EstrichOne:Updates";

    /// <summary>false = keine Abfrage des Release-Feeds (Server ohne Internet); der Stand aus update.sh wird weiter gezeigt.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Release-Feed im Format der GitHub-API (<c>https://…</c> oder <c>file://…</c>).</summary>
    public string Feed { get; set; } = "https://api.github.com/repos/florians25/EstrichONE/releases?per_page=30";

    /// <summary>So lange gilt ein abgerufener Feed.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Nur für Tests: installierte Version statt der Programmversion.</summary>
    public string? InstalledVersion { get; set; }
}

/// <param name="Mode"><c>aus</c>, <c>sicherheit</c> oder <c>patch</c> (update.sh --automatik).</param>
/// <param name="Result">Letzte Automatik: <c>succeeded</c> oder <c>failed</c>.</param>
public sealed record AutoUpdateStatus(string Mode, string Window, DateTimeOffset? CheckedAt, DateTimeOffset? LastAt, string? LastFrom,
    string? LastTo, string? Result, string? Message);

/// <summary>
/// Stand der Aktualisierung für Administratoren (Hinweis „Sicherheitsupdate verfügbar“ im Web).
/// </summary>
/// <param name="Available">Neueste Version derselben Versionslinie (x.y), die neuer als die installierte ist; sonst null.</param>
/// <param name="SecurityUpdate">Mindestens eine Version zwischen installiert und <paramref name="Available"/> ist als Sicherheitsupdate markiert.</param>
/// <param name="Latest">Neueste veröffentlichte Version überhaupt (auch neuere Versionslinie), nur zur Information.</param>
/// <param name="Source"><c>feed</c> (Release-Feed), <c>server</c> (Prüfung durch update.sh auf dem Server) oder <c>none</c>.</param>
/// <param name="Command">Befehl auf dem Server (Docker-Installer).</param>
public sealed record UpdateStatusResponse(string Installed, string? Available, bool SecurityUpdate, string? Latest, string? ReleaseName,
    string? ReleaseUrl, DateTimeOffset? PublishedAt, DateTimeOffset? CheckedAt, string Source, string? Error, string Command, AutoUpdateStatus? Auto);

/// <summary>Release aus dem Feed.</summary>
public sealed record UpdateRelease(string Version, bool Prerelease, bool Security, string? Name, string? Url, DateTimeOffset? PublishedAt);

/// <summary>Versionsvergleich nach Semantic Versioning (0.1.0-preview.9 &lt; 0.1.0-preview.10 &lt; 0.1.0 &lt; 0.1.1), wie update.sh.</summary>
public static class SemanticVersion
{
    public static int Compare(string a, string b)
    {
        static (int[] Core, string[] Pre) Split(string value)
        {
            var text = value.Trim().TrimStart('v').Split('+')[0];
            var dash = text.IndexOf('-', StringComparison.Ordinal);
            var core = (dash < 0 ? text : text[..dash]).Split('.');
            var numbers = new int[3];
            for (var i = 0; i < 3 && i < core.Length; i++)
            {
                _ = int.TryParse(core[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]);
            }

            return (numbers, dash < 0 ? [] : text[(dash + 1)..].Split('.'));
        }

        var (ca, pa) = Split(a);
        var (cb, pb) = Split(b);
        for (var i = 0; i < 3; i++)
        {
            if (ca[i] != cb[i])
            {
                return ca[i].CompareTo(cb[i]);
            }
        }

        if (pa.Length == 0 || pb.Length == 0)
        {
            // Ohne Vorabkennung ist die Version neuer als jede Vorabversion derselben Nummer.
            return pa.Length == pb.Length ? 0 : pa.Length == 0 ? 1 : -1;
        }

        for (var i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            var na = int.TryParse(pa[i], NumberStyles.None, CultureInfo.InvariantCulture, out var ia);
            var nb = int.TryParse(pb[i], NumberStyles.None, CultureInfo.InvariantCulture, out var ib);
            var result = na && nb ? ia.CompareTo(ib) : na != nb ? (na ? -1 : 1) : string.CompareOrdinal(pa[i], pb[i]);
            if (result != 0)
            {
                return Math.Sign(result);
            }
        }

        return Math.Sign(pa.Length.CompareTo(pb.Length));
    }

    public static bool IsPrerelease(string version) => version.Contains('-', StringComparison.Ordinal);

    /// <summary>Versionslinie „Hauptversion.Nebenversion“.</summary>
    public static string Line(string version)
    {
        var core = version.TrimStart('v').Split('-', '+')[0].Split('.');
        return core.Length >= 2 ? $"{core[0]}.{core[1]}" : core[0];
    }

    /// <summary>
    /// Kandidat für das Update (dieselbe Regel wie update.sh --automatik): dieselbe Versionslinie, neuer als installiert,
    /// Vorabversionen nur, wenn bereits eine Vorabversion läuft. Sicherheitsupdate, wenn eine Version dazwischen markiert ist.
    /// </summary>
    public static (UpdateRelease? Release, bool Security) Candidate(string installed, IEnumerable<UpdateRelease> releases)
    {
        var newer = releases
            .Where(r => Line(r.Version) == Line(installed) && (!r.Prerelease || IsPrerelease(installed)) && Compare(r.Version, installed) > 0)
            .ToList();
        var best = newer.Count == 0 ? null : newer.Aggregate((x, y) => Compare(x.Version, y.Version) >= 0 ? x : y);
        return (best, newer.Any(r => r.Security));
    }
}

/// <summary>
/// Liest den Release-Feed (zwischengespeichert, höchstens alle <see cref="UpdateCheckOptions.Interval"/>) und den Stand, den
/// update.sh auf dem Server in <c>backup-status/update.json</c> schreibt (Automatik, letzter Lauf).
/// </summary>
public sealed class UpdateCheckService(IHttpClientFactory http, IOptions<UpdateCheckOptions> options, IOptions<BackupSettings> backup,
    TimeProvider clock, ILogger<UpdateCheckService> logger) : IDisposable
{
    public const string HttpClientName = "updates";
    private static readonly string[] SecurityMarkers = ["sicherheitsupdate", "security update", "[security]"];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (DateTimeOffset At, IReadOnlyList<UpdateRelease>? Releases, string? Error)? _feed;

    public void Dispose() => _gate.Dispose();

    public string Installed => string.IsNullOrWhiteSpace(options.Value.InstalledVersion) ? ApplicationVersion.Current : options.Value.InstalledVersion!;

    public async Task<UpdateStatusResponse> GetAsync(bool refresh, CancellationToken cancellationToken)
    {
        var installed = Installed;
        var auto = ReadServerStatus(out var serverAvailable, out var serverSecurity);
        var command = "sudo /opt/estrichone/update.sh";
        if (!options.Value.Enabled)
        {
            return serverAvailable is not null && SemanticVersion.Compare(serverAvailable, installed) > 0
                ? new UpdateStatusResponse(installed, serverAvailable, serverSecurity, null, null, null, null, auto?.CheckedAt, "server", null,
                    $"{command} {serverAvailable}", auto)
                : new UpdateStatusResponse(installed, null, false, null, null, null, null, auto?.CheckedAt, auto is null ? "none" : "server", null, command, auto);
        }

        var (at, releases, error) = await FeedAsync(refresh, cancellationToken);
        if (releases is null)
        {
            var available = serverAvailable is not null && SemanticVersion.Compare(serverAvailable, installed) > 0 ? serverAvailable : null;
            return new UpdateStatusResponse(installed, available, available is not null && serverSecurity, null, null, null, null,
                auto?.CheckedAt, auto is null ? "none" : "server", error, available is null ? command : $"{command} {available}", auto);
        }

        var (candidate, security) = SemanticVersion.Candidate(installed, releases);
        var latest = releases.Where(r => !r.Prerelease || SemanticVersion.IsPrerelease(installed))
            .Select(r => r.Version).Aggregate((string?)null, (x, y) => x is null || SemanticVersion.Compare(y, x) > 0 ? y : x);
        return new UpdateStatusResponse(installed, candidate?.Version, candidate is not null && security, latest, candidate?.Name,
            candidate?.Url, candidate?.PublishedAt, at, "feed", null, candidate is null ? command : $"{command} {candidate.Version}", auto);
    }

    private async Task<(DateTimeOffset At, IReadOnlyList<UpdateRelease>? Releases, string? Error)> FeedAsync(bool refresh, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (!refresh && _feed is { } cached && now - cached.At < (cached.Releases is null ? TimeSpan.FromMinutes(15) : options.Value.Interval))
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!refresh && _feed is { } again && now - again.At < TimeSpan.FromSeconds(5))
            {
                return again;
            }

            try
            {
                var feed = new Uri(options.Value.Feed);
                string json;
                if (feed.IsFile)
                {
                    json = await File.ReadAllTextAsync(feed.LocalPath, cancellationToken);
                }
                else
                {
                    using var client = http.CreateClient(HttpClientName);
                    using var request = new HttpRequestMessage(HttpMethod.Get, feed);
                    request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
                    request.Headers.TryAddWithoutValidation("User-Agent", $"EstrichONE/{ApplicationVersion.Current}");
                    using var response = await client.SendAsync(request, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    json = await response.Content.ReadAsStringAsync(cancellationToken);
                }

                _feed = (now, Parse(json), null);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or IOException or UriFormatException)
            {
                logger.LogInformation(exception, "Release-Feed nicht abrufbar");
                _feed = (now, _feed?.Releases, "Release-Feed nicht erreichbar.");
                if (_feed.Value.Releases is not null)
                {
                    return _feed.Value with { Error = null };
                }
            }

            return _feed.Value;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static IReadOnlyList<UpdateRelease> Parse(string json)
    {
        var list = new List<UpdateRelease>();
        foreach (var item in JsonSerializer.Deserialize<List<FeedItem>>(json) ?? [])
        {
            if (item.Draft || string.IsNullOrWhiteSpace(item.TagName) || !item.TagName.StartsWith('v'))
            {
                continue;
            }

            var text = $"{item.Name}\n{item.Body}".ToLowerInvariant();
            list.Add(new UpdateRelease(item.TagName[1..], item.Prerelease, SecurityMarkers.Any(m => text.Contains(m, StringComparison.Ordinal)),
                item.Name, item.HtmlUrl, item.PublishedAt));
        }

        return list;
    }

    private AutoUpdateStatus? ReadServerStatus(out string? available, out bool security)
    {
        available = null;
        security = false;
        var dir = backup.Value.StatusPath;
        var file = string.IsNullOrWhiteSpace(dir) ? null : Path.Combine(dir, "update.json");
        if (file is null || !File.Exists(file))
        {
            return null;
        }

        try
        {
            var status = JsonSerializer.Deserialize<ServerStatusFile>(File.ReadAllText(file));
            if (status is null)
            {
                return null;
            }

            available = string.IsNullOrWhiteSpace(status.Available) ? null : status.Available;
            security = status.SecurityUpdate;
            var last = status.LastAutoUpdate;
            return new AutoUpdateStatus(status.AutoUpdate ?? "aus", status.Window ?? "02:00-05:00", status.CheckedAt, last?.At, last?.From, last?.To,
                last?.Result, last?.Message);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            logger.LogInformation(exception, "update.json nicht lesbar");
            return null;
        }
    }

    private sealed record FeedItem(
        [property: JsonPropertyName("tag_name")] string? TagName,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt);

    private sealed record ServerStatusFile(
        [property: JsonPropertyName("checkedAt")] DateTimeOffset? CheckedAt,
        [property: JsonPropertyName("available")] string? Available,
        [property: JsonPropertyName("securityUpdate")] bool SecurityUpdate,
        [property: JsonPropertyName("autoUpdate")] string? AutoUpdate,
        [property: JsonPropertyName("window")] string? Window,
        [property: JsonPropertyName("lastAutoUpdate")] ServerLastRun? LastAutoUpdate);

    private sealed record ServerLastRun(
        [property: JsonPropertyName("at")] DateTimeOffset? At,
        [property: JsonPropertyName("from")] string? From,
        [property: JsonPropertyName("to")] string? To,
        [property: JsonPropertyName("result")] string? Result,
        [property: JsonPropertyName("message")] string? Message);
}

internal static class UpdateCheckEndpoints
{
    public static void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<UpdateCheckOptions>().Bind(configuration.GetSection(UpdateCheckOptions.SectionName));
        services.AddHttpClient(UpdateCheckService.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<UpdateCheckService>();
    }

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/system/updates", async (UpdateCheckService service, bool? refresh, CancellationToken ct) =>
                TypedResults.Ok(await service.GetAsync(refresh == true, ct)))
            .WithName("GetUpdateStatus").WithTags("Backup Center")
            .WithSummary("Stand der Aktualisierung: verfügbare (Sicherheits-)Updates derselben Versionslinie und automatische Updates des Servers")
            .RequirePermission(Permissions.BackupManage);
    }
}
