using EstrichOne.Api.Modules.Access.Exports;
using Microsoft.AspNetCore.Authentication;

namespace EstrichOne.Api.Modules.Notices;

/// <summary>
/// Während einer Wartung beantwortet die API fachliche Aufrufe mit 503 (<c>MAINTENANCE</c>) – außer für Administratoren
/// (Recht <c>settings.manage</c>), sofern die Wartung das erlaubt. Anmeldung, Einrichtung, Serverinfo und die Hinweise
/// selbst bleiben erreichbar, damit Clients die Wartungsseite zeigen und Administratoren sich anmelden können.
/// </summary>
public sealed class MaintenanceGate(RequestDelegate next)
{
    private static readonly string[] OpenPrefixes =
        ["/api/v1/system/notices", "/api/v1/server-info", "/api/v1/auth", "/api/v1/setup", "/api/v1/web"];

    public async Task InvokeAsync(HttpContext context, NoticeStore store)
    {
        var path = context.Request.Path;
        if (!path.StartsWithSegments("/api/v1") || OpenPrefixes.Any(p => path.StartsWithSegments(p))
            || store.ActiveMaintenance() is not { } window)
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
            extensions: new Dictionary<string, object?> { ["code"] = "MAINTENANCE", ["endsAt"] = window.EndsAt }).ExecuteAsync(context);
    }
}
