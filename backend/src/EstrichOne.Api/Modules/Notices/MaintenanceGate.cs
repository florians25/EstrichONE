using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Backup.Exports;
using Microsoft.AspNetCore.Authentication;

namespace EstrichOne.Api.Modules.Notices;

/// <summary>
/// Während einer Wartung beantwortet die API fachliche Aufrufe mit 503 (<c>MAINTENANCE</c>) – außer für Administratoren
/// (Recht <c>settings.manage</c>), sofern die Wartung das erlaubt. Anmeldung, Einrichtung, Serverinfo und die Hinweise
/// selbst bleiben erreichbar, damit Clients die Wartungsseite zeigen und Administratoren sich anmelden können.
/// Gilt für alle Clients (Web, Apps): Sync und Uploads werden abgewiesen, die Apps behalten ihre Outbox und senden nach dem
/// Ende erneut. Während einer Wiederherstellung aus dem Backup Center gilt die Sperre auch für Administratoren.
/// </summary>
public sealed class MaintenanceGate(RequestDelegate next)
{
    private static readonly string[] OpenPrefixes =
        ["/api/v1/system/notices", "/api/v1/system/health-banner", "/api/v1/server-info", "/api/v1/auth", "/api/v1/setup", "/api/v1/web",
            "/api/v1/backup/mirror"];

    public async Task InvokeAsync(HttpContext context, NoticeStore store, IBackupMonitoring backup)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api/v1") || OpenPrefixes.Any(p => path.StartsWithSegments(p)))
        {
            await next(context);
            return;
        }

        if (backup.ActiveRestore() is { } restore)
        {
            if (HttpMethods.IsGet(context.Request.Method) && path.StartsWithSegments("/api/v1/backup/restore"))
            {
                await next(context);
                return;
            }

            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers.RetryAfter = "60";
            await Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Wiederherstellung läuft",
                detail: $"EstrichONE wird gerade aus einer Sicherung wiederhergestellt ({restore.Phase}, {restore.Percent} %). Bitte in wenigen Minuten erneut versuchen.",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "MAINTENANCE", ["reason"] = "restore", ["startsAt"] = restore.At, ["endsAt"] = null, ["adminsMayWork"] = false,
                }).ExecuteAsync(context);
            return;
        }

        if (store.ActiveMaintenance() is not { } window)
        {
            await next(context);
            return;
        }

        if (window.AdminsMayWork)
        {
            var result = await context.AuthenticateAsync();
            if (result.Succeeded)
            {
                context.User = result.Principal;
                var permissions = context.RequestServices.GetRequiredService<IUserPermissions>();
                if (await permissions.HasAsync(Permissions.SettingsManage, context.RequestAborted))
                {
                    await next(context);
                    return;
                }
            }
        }

        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        if (window.EndsAt is { } end)
        {
            context.Response.Headers.RetryAfter = Math.Max(60, (int)(end - DateTimeOffset.UtcNow).TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        await Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Wartungsarbeiten",
            detail: window.Message ?? "EstrichONE wird gerade gewartet. Bitte später erneut versuchen.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "MAINTENANCE", ["reason"] = "maintenance", ["startsAt"] = window.StartsAt, ["endsAt"] = window.EndsAt,
                ["adminsMayWork"] = window.AdminsMayWork,
            }).ExecuteAsync(context);
    }
}
