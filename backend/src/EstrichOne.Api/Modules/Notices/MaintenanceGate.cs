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
/// Update-Ablauf (<see cref="UpdateMaintenanceState"/>): Jede Antwort trägt <c>X-EstrichONE-Update: &lt;Phase&gt;</c>, damit
/// Clients den Countdown sofort zeigen. In der Ankündigung sind nur neue Anmeldungen gesperrt; ab der Update-Phase ist alles
/// gesperrt (auch Anmeldung, Erneuern und Administratoren), und alle Sitzungen werden einmal beendet.
/// </summary>
public sealed class MaintenanceGate(RequestDelegate next)
{
    private static readonly string[] OpenPrefixes =
        ["/api/v1/system/notices", "/api/v1/system/modules", "/api/v1/system/health-banner", "/api/v1/server-info", "/api/v1/auth", "/api/v1/setup", "/api/v1/web",
            "/api/v1/backup/mirror"];

    /// <summary>Bleiben während der Update-Phase offen: Hinweise, Serverinfo, Abmelden und ziehende Backup-Ziele.</summary>
    private static readonly string[] UpdateOpenPrefixes =
        ["/api/v1/system/notices", "/api/v1/system/modules", "/api/v1/system/health-banner", "/api/v1/server-info", "/api/v1/auth/logout",
            "/api/v1/auth/web/logout", "/api/v1/backup/mirror"];

    private static readonly string[] LoginPaths = ["/api/v1/auth/login", "/api/v1/auth/web/login"];

    public const string UpdateHeader = "X-EstrichONE-Update";

    public async Task InvokeAsync(HttpContext context, NoticeStore store, IBackupMonitoring backup, UpdateMaintenanceReader updates,
        UpdateSessionTerminator terminator)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/api/v1") && updates.Current() is { } update)
        {
            context.Response.Headers[UpdateHeader] = update.Phase;
            context.Response.Headers.CacheControl = "no-store";
            if (update.Blocking)
            {
                await terminator.EnsureAsync(update, context.RequestAborted);
            }

            var blocked = update.Blocking
                ? !UpdateOpenPrefixes.Any(p => path.StartsWithSegments(p))
                : HttpMethods.IsPost(context.Request.Method) && LoginPaths.Any(p => path.Equals(p, StringComparison.OrdinalIgnoreCase));
            if (blocked)
            {
                await UpdateProblemAsync(context, update);
                return;
            }
        }

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

    private static Task UpdateProblemAsync(HttpContext context, UpdateMaintenanceState update)
    {
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.RetryAfter = "30";
        var version = update.TargetVersion is { Length: > 0 } v ? $" auf Version {v}" : string.Empty;
        var detail = update.Blocking
            ? $"EstrichONE wird gerade{version} aktualisiert. Danach bitte erneut anmelden."
            : $"Das Update{version} beginnt in Kürze. Anmelden ist danach wieder möglich.";
        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: UpdatePhases.Title(update.Phase), detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "MAINTENANCE", ["reason"] = "update", ["phase"] = update.Phase, ["targetVersion"] = update.TargetVersion,
                ["startsAt"] = update.StartedAt, ["endsAt"] = update.PhaseEndsAt, ["adminsMayWork"] = false,
            }).ExecuteAsync(context);
    }
}
