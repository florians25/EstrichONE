using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Modules.AppUpdates.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.AppUpdates;

/// <summary>Stand einer App gegenüber diesem Server.</summary>
public enum AppUpdateState
{
    /// <summary>Aktuell (oder kein Hinweis gewünscht).</summary>
    Current,

    /// <summary>Neuere Version verfügbar: Hinweis, Arbeiten bleibt möglich.</summary>
    Recommended,

    /// <summary>Unter der Mindestversion: Der Server lehnt Anfragen mit 426 <c>CLIENT_UPDATE_REQUIRED</c> ab.</summary>
    Required,

    /// <summary>App ist neuer als der Server: funktioniert, die IT sollte den Server aktualisieren.</summary>
    NewerThanServer,
}

/// <summary>Bezugsquelle des Updates.</summary>
/// <param name="Kind">
/// <c>package</c>: APK vom Server (Pfad in <paramref name="Url"/>, mit Prüfsumme) · <c>link</c>: Download im Browser ·
/// <c>store</c>: App Store, TestFlight oder MDM-Portal (<paramref name="Url"/> kann fehlen) · <c>none</c>: keine Quelle.
/// </param>
public sealed record AppDownload(string Kind, string? Url, string? Sha256 = null, long? Size = null, int? VersionCode = null,
    string? SignerSha256 = null, bool ReleaseSigned = false);

/// <summary>Versionsstand einer Plattform: neueste verfügbare Version, Mindestversion, Bezugsquelle.</summary>
public sealed record PlatformUpdateInfo(string Platform, string? LatestVersion, string MinimumVersion, AppDownload Download);

/// <summary>Regeln für Hinweis, Sperre und Bezugsquelle (rein, ohne Datenbank – siehe Tests).</summary>
public static class AppUpdatePolicy
{
    public const string PackagePath = "/api/v1/app-updates/android/package";

    public static PlatformUpdateInfo ForPlatform(string platform, string serverVersion, string technicalMinimum, AppUpdateSettings settings,
        AndroidPackage? package, string? androidFallbackUrl)
    {
        string? latest;
        AppDownload download;
        if (platform == "ios")
        {
            // App Store und TestFlight kennen keine Vorabkennung: 1.1.0-preview.2 → 1.1.0.
            latest = VersionComparer.Core(settings.IosLatestVersion ?? serverVersion);
            download = new AppDownload("store", settings.IosUpdateUrl);
        }
        else if (settings.AndroidUpdateUrl is { } own)
        {
            latest = serverVersion;
            download = new AppDownload("link", own);
        }
        else if (package is { IsValid: true } valid)
        {
            var m = valid.Manifest;
            latest = m.Version;
            download = new AppDownload("package", PackagePath, m.Sha256, m.Size, m.VersionCode, m.SignerSha256, m.ReleaseSigned);
        }
        else if (!string.IsNullOrWhiteSpace(androidFallbackUrl))
        {
            latest = serverVersion;
            download = new AppDownload("link", androidFallbackUrl.Replace("{version}", serverVersion, StringComparison.Ordinal));
        }
        else
        {
            latest = null;
            download = new AppDownload("none", null);
        }

        var minimum = VersionComparer.Normalize(technicalMinimum);
        if (settings.EnforceMinimum && latest is not null)
        {
            // Nie mehr verlangen, als bereitsteht – sonst wären alle Geräte ohne Ausweg gesperrt.
            var wanted = settings.MinimumVersion ?? serverVersion;
            if (platform == "ios")
            {
                wanted = VersionComparer.Core(wanted);
            }

            minimum = VersionComparer.Max(minimum, VersionComparer.Min(wanted, latest));
        }

        return new PlatformUpdateInfo(platform, latest, minimum, download);
    }

    public static AppUpdateState Evaluate(string installed, PlatformUpdateInfo info, string serverVersion, bool recommend)
    {
        if (VersionComparer.Compare(installed, info.MinimumVersion) < 0)
        {
            return AppUpdateState.Required;
        }

        if (VersionComparer.Compare(VersionComparer.Core(installed), VersionComparer.Core(serverVersion)) > 0)
        {
            return AppUpdateState.NewerThanServer;
        }

        return recommend && info.LatestVersion is { } latest && VersionComparer.Compare(installed, latest) < 0
            ? AppUpdateState.Recommended
            : AppUpdateState.Current;
    }
}

/// <summary>Versionsstand mit den aktuellen Einstellungen und dem ausgelieferten Paket.</summary>
public sealed class AppUpdateService(ISystemSettings settings, AppPackageCatalog packages, IOptions<AppUpdateOptions> options,
    IOptions<EstrichOneOptions> server) : IAppUpdatePolicy
{
    public string ServerVersion => VersionComparer.Normalize(options.Value.ServerVersion ?? ApplicationVersion.Current);

    public async Task<AppUpdateSettings> SettingsAsync(CancellationToken cancellationToken) =>
        (await settings.GetAsync(cancellationToken)).AppUpdates;

    public PlatformUpdateInfo Info(string platform, AppUpdateSettings current) => AppUpdatePolicy.ForPlatform(platform, ServerVersion,
        server.Value.MinClientVersion, current, packages.Android(), options.Value.AndroidFallbackUrl);

    public async Task<string> MinimumVersionAsync(string platform, CancellationToken cancellationToken) =>
        Info(platform, await SettingsAsync(cancellationToken)).MinimumVersion;
}
