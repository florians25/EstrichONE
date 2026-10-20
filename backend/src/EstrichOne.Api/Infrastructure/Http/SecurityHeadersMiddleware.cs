namespace EstrichOne.Api.Infrastructure.Http;

/// <summary>
/// Sicherheitsheader für alle API-Antworten. HSTS setzt der Reverse Proxy (Caddy),
/// weil nur er weiß, ob die Verbindung nach außen verschlüsselt ist.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Setzt ein Endpunkt diesen Eintrag in <see cref="HttpContext.Items"/>, darf die Antwort in einem Rahmen derselben
    /// Herkunft angezeigt werden (Dokumentvorschau im Web). Alle anderen Antworten bleiben unrahmbar.
    /// </summary>
    public const string AllowSameOriginFramingKey = "EstrichOne.AllowSameOriginFraming";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "no-referrer";
            if (context.Items.ContainsKey(AllowSameOriginFramingKey))
            {
                headers.XFrameOptions = "SAMEORIGIN";
                headers.ContentSecurityPolicy = "default-src 'none'; img-src 'self' data:; media-src 'self'; style-src 'unsafe-inline'; object-src 'self'; frame-ancestors 'self'";
            }
            else
            {
                headers.XFrameOptions = "DENY";
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            }

            headers["Cross-Origin-Resource-Policy"] = "same-origin";
            return Task.CompletedTask;
        });
        return next(context);
    }
}
