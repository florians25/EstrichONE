using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Http;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Devices.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Notifications.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace EstrichOne.Api.Modules.AppUpdates;

/// <summary>Antwort auf die Update-Prüfung einer App.</summary>
/// <param name="Status">current · recommended · required · newerThanServer.</param>
/// <param name="Message">Hinweis der IT (Systemeinstellungen › App-Updates).</param>
/// <param name="ReleaseNotes">Neuerungen der Versionen zwischen installiert und neuester (höchstens fünf, neueste zuerst).</param>
public sealed record AppUpdateCheckResponse(string Platform, string InstalledVersion, string ServerVersion, string? LatestVersion,
    string MinimumVersion, AppUpdateState Status, string? Message, AppDownload Download, IReadOnlyList<AppReleaseNote> ReleaseNotes);

/// <summary>Gerät in der Übersicht „App-Updates“.</summary>
/// <param name="State"><c>null</c>, wenn das Gerät keine Version gemeldet hat.</param>
public sealed record AppUpdateDevice(Guid Id, string Name, DevicePlatform Platform, DeviceKind Kind, DeviceStatus Status, string? Model,
    string? AppVersion, AppUpdateState? State, string? AssignedUserName, DateTimeOffset? LastSeenAt);

/// <summary>Android-Paket dieses Servers (Verwaltung).</summary>
public sealed record AppPackageInfo(string Version, int VersionCode, long Size, string Sha256, bool ReleaseSigned, string? SignerSha256, bool Valid, string? Error);

public sealed record AppUpdateCounts(int Total, int Current, int Recommended, int Required, int NewerThanServer, int Unknown);

/// <summary>Übersicht für Administratoren: Regeln je Plattform, Paket, Geräte mit App-Version.</summary>
public sealed record AppUpdateOverview(string ServerVersion, AppUpdateSettings Settings, PlatformUpdateInfo Android, PlatformUpdateInfo Ios,
    AppPackageInfo? AndroidPackage, IReadOnlyList<AppUpdateDevice> Devices, AppUpdateCounts Counts);

internal static class AppUpdateEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/app-updates").WithTags("App-Updates");

        group.MapGet("/check", CheckAsync).WithName("CheckAppUpdate")
            .WithSummary("Update-Prüfung der App: neueste und Mindestversion, Bezugsquelle, Neuerungen (ohne Anmeldung)")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AllowAnonymous();

        group.MapGet("/release-notes", ReleaseNotesAsync).WithName("GetAppReleaseNotes")
            .WithSummary("„Neu in dieser Version“: Neuerungen nach einer Version bis einschließlich einer anderen (ohne Anmeldung)")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AllowAnonymous();

        group.MapMethods("/android/package", [HttpMethods.Get, HttpMethods.Head], DownloadAndroid).WithName("DownloadAndroidApp")
            .WithSummary("Android-App (APK) dieses Servers; mit Prüfsumme und fortsetzbar (Range)")
            .Produces(StatusCodes.Status200OK, contentType: AndroidContentType)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AllowAnonymous();

        group.MapGet("/overview", OverviewAsync).WithName("GetAppUpdateOverview")
            .WithSummary("Übersicht App-Updates: Regeln, Android-Paket, Geräte mit App-Version")
            .RequirePermission(Permissions.DevicesRead);

        group.MapPost("/devices/{id:guid}/request-update", RequestUpdateAsync).WithName("RequestDeviceAppUpdate")
            .WithSummary("Person des Geräts per Benachrichtigung bitten, die App zu aktualisieren")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.DevicesManage);
    }

    private const string AndroidContentType = "application/vnd.android.package-archive";

    private static async Task<Results<Ok<AppUpdateCheckResponse>, ProblemHttpResult>> CheckAsync(
        HttpContext http, AppUpdateService service, ReleaseNotesCatalog notes, string? platform, string? version,
        CancellationToken cancellationToken)
    {
        var client = Identify(http, platform, version);
        if (client is null)
        {
            return Invalid();
        }

        var settings = await service.SettingsAsync(cancellationToken);
        var info = service.Info(client.Platform, settings);
        var state = AppUpdatePolicy.Evaluate(client.Version, info, service.ServerVersion, settings.RecommendUpdates);
        var releaseNotes = info.LatestVersion is { } latest && VersionComparer.Compare(client.Version, latest) < 0
            ? notes.Between(client.Version, latest, client.Platform)
            : [];
        return TypedResults.Ok(new AppUpdateCheckResponse(client.Platform, client.Version, service.ServerVersion, info.LatestVersion,
            info.MinimumVersion, state, settings.Message, info.Download, releaseNotes));
    }

    private static Results<Ok<IReadOnlyList<AppReleaseNote>>, ProblemHttpResult> ReleaseNotesAsync(
        ReleaseNotesCatalog notes, string? platform, string? from, string? to)
    {
        if (to is null || !VersionComparer.IsValid(to) || (from is not null && !VersionComparer.IsValid(from))
            || (platform is not null && !ClientIdentity.Platforms.Contains(platform)))
        {
            return Invalid();
        }

        // Apps aus App Store/TestFlight tragen keine Vorabkennung; 1.1.0 findet dann auch 1.1.0-preview.3 nicht – gewollt.
        return TypedResults.Ok(notes.Between(from is null ? null : VersionComparer.Normalize(from), VersionComparer.Normalize(to), platform));
    }

    private static IResult DownloadAndroid(HttpContext http, AppPackageCatalog packages)
    {
        if (packages.Android() is not { IsValid: true } package)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Kein Android-Paket",
                detail: "Dieser Server liefert keine Android-App aus. Bitte an die IT wenden.",
                extensions: new Dictionary<string, object?> { ["code"] = "APP_PACKAGE_MISSING" });
        }

        var m = package.Manifest;
        http.Response.Headers["X-Checksum-SHA256"] = m.Sha256;
        http.Response.Headers.CacheControl = "no-cache";
        return TypedResults.PhysicalFile(package.Path, AndroidContentType, $"EstrichONE-{m.Version}.apk",
            lastModified: File.GetLastWriteTimeUtc(package.Path), entityTag: new EntityTagHeaderValue($"\"{m.Sha256}\""),
            enableRangeProcessing: true);
    }

    private static async Task<Ok<AppUpdateOverview>> OverviewAsync(AppUpdateService service, AppPackageCatalog packages,
        EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var settings = await service.SettingsAsync(cancellationToken);
        var android = service.Info("android", settings);
        var ios = service.Info("ios", settings);
        var devices = await db.Set<Device>().AsNoTracking()
            .Where(d => d.Status != DeviceStatus.Retired && (d.Platform == DevicePlatform.Android || d.Platform == DevicePlatform.Ios))
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);
        var userIds = devices.Where(d => d.AssignedUserId is not null).Select(d => d.AssignedUserId!.Value).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        var rows = devices.Select(d =>
        {
            var info = d.Platform == DevicePlatform.Ios ? ios : android;
            AppUpdateState? state = d.AppVersion is { } v && VersionComparer.IsValid(v)
                ? AppUpdatePolicy.Evaluate(v, info, service.ServerVersion, recommend: true)
                : null;
            return new AppUpdateDevice(d.Id, d.Name, d.Platform, d.Kind, d.Status, d.Model, d.AppVersion, state,
                d.AssignedUserId is { } user && names.TryGetValue(user, out var name) ? name : null, d.LastSeenAt);
        }).ToList();
        var counts = new AppUpdateCounts(rows.Count, rows.Count(r => r.State == AppUpdateState.Current),
            rows.Count(r => r.State == AppUpdateState.Recommended), rows.Count(r => r.State == AppUpdateState.Required),
            rows.Count(r => r.State == AppUpdateState.NewerThanServer), rows.Count(r => r.State is null));
        var package = packages.Android() is { } p
            ? new AppPackageInfo(p.Manifest.Version, p.Manifest.VersionCode, p.Manifest.Size, p.Manifest.Sha256, p.Manifest.ReleaseSigned,
                p.Manifest.SignerSha256, p.IsValid, p.Error)
            : null;
        return TypedResults.Ok(new AppUpdateOverview(service.ServerVersion, settings, android, ios, package, rows, counts));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RequestUpdateAsync(Guid id, AppUpdateService service,
        EstrichOneDbContext db, INotifier notifier, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var device = await db.Set<Device>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (device is null || device.Status == DeviceStatus.Retired || device.Platform is not (DevicePlatform.Android or DevicePlatform.Ios))
        {
            return Problem(StatusCodes.Status404NotFound, "DEVICE_NOT_FOUND", "Gerät nicht gefunden.");
        }

        if (device.AssignedUserId is not { } userId)
        {
            return Problem(StatusCodes.Status409Conflict, "DEVICE_HAS_NO_USER",
                "Poolgeräte haben keine feste Person. Die App zeigt den Hinweis beim nächsten Start allen, die sich anmelden.");
        }

        var info = service.Info(device.Platform == DevicePlatform.Ios ? "ios" : "android", await service.SettingsAsync(cancellationToken));
        if (info.LatestVersion is null || (device.AppVersion is { } v && VersionComparer.IsValid(v) && VersionComparer.Compare(v, info.LatestVersion) >= 0))
        {
            return Problem(StatusCodes.Status409Conflict, "APP_UP_TO_DATE", "Auf diesem Gerät ist bereits die neueste App-Version.");
        }

        var how = device.Platform == DevicePlatform.Ios
            ? "Bitte über App Store, TestFlight bzw. die Geräteverwaltung aktualisieren"
            : "Bitte in EstrichONE unter Mehr › Info „Aktualisieren“ wählen";
        await notifier.NotifyAsync([userId], new NotificationRequest(NotificationCategory.System,
            $"Bitte EstrichONE auf Version {info.LatestVersion} aktualisieren",
            $"{how} – vorher werden alle Änderungen übertragen. Gerät: {device.Name}.",
            Urgency: NotificationUrgency.High, Event: NotificationEvents.AppUpdateRequested,
            DedupKey: $"app-update:{device.Id}:{info.LatestVersion}"), cancellationToken);
        audit.Record("devices.app_update_requested", currentUser.UserId, "device", device.Id.ToString(),
            new Dictionary<string, string?> { ["from"] = device.AppVersion, ["to"] = info.LatestVersion });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Plattform und Version aus der Anfrage (Parameter) oder aus der Kopfzeile der App.</summary>
    private static ClientIdentity? Identify(HttpContext http, string? platform, string? version) =>
        platform is not null || version is not null
            ? ClientIdentity.Parse($"{platform}/{version}")
            : ClientIdentity.From(http.Request);

    private static ProblemHttpResult Invalid() => Problem(StatusCodes.Status400BadRequest, "APP_VERSION_INVALID",
        "Plattform (android oder ios) und Version (z. B. 1.0.5) angeben.");

    private static ProblemHttpResult Problem(int status, string code, string title) =>
        TypedResults.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
