namespace EstrichOne.Api.Infrastructure.Http;

/// <summary>
/// Sicherheitsheader für alle API-Antworten. HSTS setzt der Reverse Proxy (Caddy),
/// weil nur er weiß, ob die Verbindung nach außen verschlüsselt ist.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            return Task.CompletedTask;
        });
        return next(context);
    }
}
