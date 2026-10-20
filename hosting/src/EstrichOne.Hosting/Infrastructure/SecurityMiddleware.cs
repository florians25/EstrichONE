using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EstrichOne.Hosting.Auth;

namespace EstrichOne.Hosting.Infrastructure;

/// <summary>Sicherheitsheader (strikte CSP: nur eigene Skripte, keine Fremdquellen) und CSRF-Schutz für die API.</summary>
public static class SecurityMiddleware
{
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self'; " +
        "connect-src 'self'; worker-src 'self' blob:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = ContentSecurityPolicy;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers["Referrer-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        if (context.Request.IsHttps)
        {
            headers.StrictTransportSecurity = "max-age=31536000";
        }

        if (context.Request.Path.StartsWithSegments("/api"))
        {
            headers.CacheControl = "no-store";
        }

        await next();
    });

    /// <summary>
    /// Ändernde API-Aufrufe: (1) Origin muss – falls vorhanden – zur eigenen Adresse passen; (2) angemeldete Aufrufe
    /// brauchen den CSRF-Token der Sitzung im Header <c>X-CSRF-Token</c>. Zusätzlich ist das Sitzungscookie SameSite=Strict.
    /// </summary>
    public static IApplicationBuilder UseCsrfProtection(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var request = context.Request;
        if (!request.Path.StartsWithSegments("/api") || HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
            || HttpMethods.IsOptions(request.Method))
        {
            await next();
            return;
        }

        if (request.Headers.Origin is { Count: > 0 } origin
            && (!Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var originUri)
                || !string.Equals(originUri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase)))
        {
            await Reject(context, "Anfrage von fremder Herkunft abgewiesen.");
            return;
        }

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var expected = context.User.FindFirstValue(AuthEndpoints.CsrfClaim) ?? string.Empty;
            var actual = request.Headers[AuthEndpoints.CsrfHeader].ToString();
            if (expected.Length == 0 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual)))
            {
                await Reject(context, "CSRF-Token fehlt oder ist ungültig. Bitte die Seite neu laden.");
                return;
            }
        }

        await next();
    });

    private static Task Reject(HttpContext context, string detail)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return context.Response.WriteAsJsonAsync(new { title = "Nicht erlaubt", status = 403, detail },
            options: null, contentType: "application/problem+json");
    }
}
