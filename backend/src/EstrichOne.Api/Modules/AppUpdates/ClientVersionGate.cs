using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Http;

namespace EstrichOne.Api.Modules.AppUpdates;

/// <summary>
/// Apps unter der Mindestversion (technisch oder in Systemeinstellungen › App-Updates erzwungen) erhalten auf jede
/// fachliche Anfrage 426 <c>CLIENT_UPDATE_REQUIRED</c> mit Mindestversion und Bezugsquelle – die Apps zeigen dann den
/// Update-Bildschirm statt eines allgemeinen Fehlers. Die Outbox bleibt auf dem Gerät und wird nach dem Update übertragen.
/// Ohne Kennung (Web, ältere Apps) greift die Sperre nicht.
/// </summary>
public sealed class ClientVersionGate(RequestDelegate next)
{
    public const string Code = "CLIENT_UPDATE_REQUIRED";

    /// <summary>Bleiben erreichbar: Serverinfo und Update-Prüfung/Download, Abmelden.</summary>
    private static readonly string[] OpenPrefixes = ["/api/v1/server-info", "/api/v1/app-updates", "/api/v1/auth/logout"];

    public async Task InvokeAsync(HttpContext context, AppUpdateService service)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api/v1") || OpenPrefixes.Any(p => path.StartsWithSegments(p))
            || ClientIdentity.From(context.Request) is not { } client)
        {
            await next(context);
            return;
        }

        var info = service.Info(client.Platform, await service.SettingsAsync(context.RequestAborted));
        if (VersionComparer.Compare(client.Version, info.MinimumVersion) >= 0)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        await Results.Problem(statusCode: StatusCodes.Status426UpgradeRequired, title: "App-Update erforderlich",
            detail: $"Diese App-Version ({client.Version}) wird vom Server nicht mehr unterstützt. Bitte auf Version {info.LatestVersion ?? info.MinimumVersion} aktualisieren. Nicht übertragene Änderungen bleiben auf dem Gerät und werden nach dem Update gesendet.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = Code, ["platform"] = client.Platform, ["installedVersion"] = client.Version,
                ["minimumVersion"] = info.MinimumVersion, ["latestVersion"] = info.LatestVersion,
            }).ExecuteAsync(context);
    }
}

public static class ClientVersionGateExtensions
{
    public static IApplicationBuilder UseClientVersionGate(this IApplicationBuilder app) => app.UseMiddleware<ClientVersionGate>();
}
