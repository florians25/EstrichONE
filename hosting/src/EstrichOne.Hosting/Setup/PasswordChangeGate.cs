using System.Security.Claims;
using EstrichOne.Hosting.Data;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Hosting.Setup;

/// <summary>
/// Serverseitige Pflicht zum Passwortwechsel: Solange ein Konto noch das Startpasswort aus dem Installer bzw. aus
/// <c>reset-admin</c> hat, lehnt die Konsole alle API-Aufrufe mit 403 <c>PASSWORD_CHANGE_REQUIRED</c> ab – außer
/// Anmeldestatus/Abmelden (<c>/api/auth/*</c>), Passwort ändern und dem Status des Einrichtungsassistenten.
/// </summary>
public static class PasswordChangeGate
{
    public const string Code = "PASSWORD_CHANGE_REQUIRED";

    public static bool IsAllowed(HttpRequest request)
    {
        var path = request.Path;
        return !path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase)
            || (HttpMethods.IsPost(request.Method) && path.Equals("/api/account/password", StringComparison.OrdinalIgnoreCase))
            || (HttpMethods.IsGet(request.Method) && path.Equals("/api/setup-wizard", StringComparison.OrdinalIgnoreCase));
    }

    public static IApplicationBuilder UsePasswordChangeGate(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.User.Identity?.IsAuthenticated == true && !IsAllowed(context.Request)
            && Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            var db = context.RequestServices.GetRequiredService<HostingDbContext>();
            if (await db.Admins.AsNoTracking().AnyAsync(a => a.Id == id && a.MustChangePassword, context.RequestAborted))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new
                {
                    title = "Passwortwechsel erforderlich",
                    status = 403,
                    detail = "Bitte zuerst ein eigenes Passwort festlegen (Startpasswort ist noch aktiv).",
                    code = Code,
                }, context.RequestAborted);
                return;
            }
        }

        await next();
    });
}
