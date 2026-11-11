using EstrichOne.Api.Infrastructure.Hosting;

namespace EstrichOne.Api.Infrastructure.Http;

/// <summary>
/// Kennung der App in jeder Anfrage: Kopfzeile <c>X-EstrichONE-Client: android/1.0.5</c> bzw. <c>ios/1.0.5</c>
/// (docs/app-updates.md). Damit prüft der Server die Mindestversion (426 <c>CLIENT_UPDATE_REQUIRED</c>) und führt die
/// App-Version des Geräts in der Geräteübersicht nach. Die Weboberfläche sendet keine Kennung.
/// </summary>
public sealed record ClientIdentity(string Platform, string Version)
{
    public const string HeaderName = "X-EstrichONE-Client";

    public static readonly string[] Platforms = ["android", "ios"];

    /// <summary>Liest die Kennung; <c>null</c> bei fehlender oder unbekannter Angabe (z. B. Web, ältere Apps).</summary>
    public static ClientIdentity? From(HttpRequest request) => Parse(request.Headers[HeaderName].ToString());

    public static ClientIdentity? Parse(string? header)
    {
        if (string.IsNullOrWhiteSpace(header) || header.Length > 100)
        {
            return null;
        }

        var slash = header.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0)
        {
            return null;
        }

        var platform = header[..slash].Trim().ToLowerInvariant();
        var version = header[(slash + 1)..].Trim().Split(' ', 2)[0];
        return Platforms.Contains(platform) && VersionComparer.IsValid(version)
            ? new ClientIdentity(platform, VersionComparer.Normalize(version))
            : null;
    }
}
