using System.Security.Claims;
using EstrichOne.Hosting.Data;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Hosting.Auth;

/// <summary>
/// Rolle „nur lesen“: Konten dieser Rolle sehen alles, was die Konsole anzeigt, ändern aber nichts. Serverseitig
/// abgelehnt (403 <c>READ_ONLY</c>) werden alle ändernden API-Aufrufe – außer Anmeldung/Abmeldung, dem eigenen Konto
/// (Passwort, Zwei-Faktor, Darstellung), „Jetzt prüfen“ und dem Gelesen-Markieren von Benachrichtigungen. Zusätzlich
/// gesperrt sind Downloads (Export mit Sicherungsschlüssel).
/// </summary>
public static class RoleGate
{
    public const string Code = "READ_ONLY";

    public static bool IsAllowedForViewer(HttpRequest request)
    {
        var path = request.Path;
        if (!path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.StartsWithSegments("/api/downloads", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        {
            return true;
        }

        return path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/account", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/notifications", StringComparison.OrdinalIgnoreCase)
            || (HttpMethods.IsPost(request.Method) && path.Value is { } value
                && value.StartsWith("/api/tenants/", StringComparison.OrdinalIgnoreCase)
                && value.EndsWith("/refresh", StringComparison.OrdinalIgnoreCase));
    }

    public static IApplicationBuilder UseRoleGate(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.User.Identity?.IsAuthenticated == true && !IsAllowedForViewer(context.Request)
            && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            var db = context.RequestServices.GetRequiredService<HostingDbContext>();
            if (await db.Admins.AsNoTracking().AnyAsync(a => a.Id == id && a.Role == AdminRole.Viewer, context.RequestAborted))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new
                {
                    title = "Nur Lesezugriff",
                    status = 403,
                    detail = "Ihr Konto hat nur Lesezugriff. Änderungen nimmt ein Administrator der Konsole vor.",
                    code = Code,
                }, context.RequestAborted);
                return;
            }
        }

        await next();
    });
}
