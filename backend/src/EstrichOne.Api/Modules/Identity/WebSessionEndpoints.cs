using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Identity.Passwords;
using EstrichOne.Api.Modules.Identity.Sessions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Identity;

/// <param name="TwoFactorCode">Code aus der Authenticator-App oder Wiederherstellungscode (siehe <see cref="LoginRequest"/>).</param>
public sealed record WebLoginRequest(
    [property: Required, MaxLength(320)] string Login,
    [property: Required, MaxLength(PasswordPolicy.MaxLength)] string Password,
    bool RememberMe = false,
    [property: MaxLength(32)] string? TwoFactorCode = null);

public sealed record WebSessionResponse(UserSummary User, bool MustChangePassword, DateTimeOffset AccessTokenExpiresAt);

/// <summary>
/// Anmeldung im Browser (docs/reengineering/17, Abschnitt 2): Tokens nur in HttpOnly-, Secure- (siehe UseSecureCookies), SameSite=Strict-Cookies,
/// nie im JavaScript, nie in <c>localStorage</c>. Zustandsändernde Anfragen mit Cookie verlangen zusätzlich den
/// CSRF-Token aus dem Cookie <c>XSRF-TOKEN</c> im Header <c>X-XSRF-TOKEN</c> (Doppel-Absendung; Angular sendet ihn
/// selbstständig).
/// </summary>
internal static class WebSessionEndpoints
{
    public const string AccessCookie = "eo_at";
    public const string RefreshCookie = "eo_rt";
    public const string CsrfCookie = "XSRF-TOKEN";
    public const string CsrfHeader = "X-XSRF-TOKEN";
    private const string RefreshPath = "/api/v1/auth/web";

    public static void Map(IEndpointRouteBuilder api)
    {
        var web = api.MapGroup("/auth/web").WithTags("Anmeldung");

        web.MapPost("/login", LoginAsync).WithName("WebLogin")
            .WithSummary("Anmeldung im Browser; setzt HttpOnly-Cookies statt Tokens zurückzugeben")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AllowAnonymous().RequireRateLimiting(AuthEndpoints.RateLimitPolicy);

        web.MapPost("/refresh", RefreshAsync).WithName("WebRefresh")
            .WithSummary("Sitzung im Browser verlängern (Refresh-Cookie, CSRF-Header erforderlich)")
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status409Conflict)
            .AllowAnonymous().RequireRateLimiting(AuthEndpoints.RefreshRateLimitPolicy);

        web.MapPost("/logout", LogoutAsync).WithName("WebLogout")
            .WithSummary("Abmelden im Browser; beendet die Sitzung und löscht die Cookies")
            .AllowAnonymous();
    }

    private static async Task<Results<Ok<WebSessionResponse>, ProblemHttpResult>> LoginAsync(
        WebLoginRequest request, LoginService login, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await login.LoginAsync(request.Login, request.Password, ClientKind.Web,
            Truncate(http.Request.Headers.UserAgent.ToString(), 100), http.Connection.RemoteIpAddress?.ToString(), cancellationToken,
            request.RememberMe, request.TwoFactorCode);
        if (AuthEndpoints.LoginProblem(result, http) is { } problem)
        {
            return problem;
        }

        var tokens = result.Tokens!;
        SetCookies(http, tokens.AccessToken, tokens.RefreshToken, tokens.RefreshTokenExpiresAt, newCsrfToken: true);
        return TypedResults.Ok(new WebSessionResponse(AuthEndpoints.Summarize(result.User!), result.MustChangePassword, tokens.AccessTokenExpiresAt));
    }

    private static async Task<Results<Ok<WebSessionResponse>, ProblemHttpResult>> RefreshAsync(
        HttpContext http, SessionService sessions, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (!HasValidCsrfToken(http.Request))
        {
            return AuthEndpoints.Problem(StatusCodes.Status403Forbidden, "CSRF_INVALID", "Anfrage abgelehnt",
                "Der Sicherheitstoken der Anfrage fehlt oder ist ungültig. Bitte die Seite neu laden.");
        }

        var refreshToken = http.Request.Cookies[RefreshCookie];
        var (tokens, failure) = refreshToken is null ? (null, null) : await sessions.RefreshAsync(refreshToken, null, cancellationToken);
        if (failure == RefreshFailure.Concurrent)
        {
            // Ein anderer Tab hat soeben erneuert; dessen Cookies gelten. Nichts löschen, der Client wiederholt einfach.
            return AuthEndpoints.Problem(StatusCodes.Status409Conflict, "REFRESH_IN_PROGRESS", "Bereits erneuert",
                "Die Sitzung wurde gerade in einem anderen Fenster erneuert.");
        }

        if (tokens is null)
        {
            ClearCookies(http);
            return AuthEndpoints.Problem(StatusCodes.Status401Unauthorized, "SESSION_EXPIRED", "Erneuerung fehlgeschlagen",
                "Die Sitzung ist abgelaufen. Bitte neu anmelden.");
        }

        SetCookies(http, tokens.AccessToken, tokens.RefreshToken, tokens.RefreshTokenExpiresAt, newCsrfToken: false);
        var user = await db.Set<Session>().Where(s => s.Id == tokens.SessionId)
            .Join(db.Set<User>(), s => s.UserId, u => u.Id, (_, u) => u)
            .AsNoTracking().SingleAsync(cancellationToken);
        return TypedResults.Ok(new WebSessionResponse(AuthEndpoints.Summarize(user), false, tokens.AccessTokenExpiresAt));
    }

    private static async Task<NoContent> LogoutAsync(HttpContext http, SessionService sessions, IAuditLog audit,
        CancellationToken cancellationToken)
    {
        if (http.User.Identity?.IsAuthenticated == true && http.User.FindFirst(TokenAuthenticationHandler.SessionClaim) is { } claim)
        {
            var sessionId = Guid.Parse(claim.Value);
            var userId = Guid.Parse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            audit.Record(AuditActions.Logout, userId, "session", sessionId.ToString());
            // Abmelden muss vollständig wirken, auch wenn der Browser die Anfrage beim Verlassen abbricht.
            await sessions.RevokeAsync(sessionId, "logout", userId, CancellationToken.None);
        }

        ClearCookies(http);
        return TypedResults.NoContent();
    }

    /// <summary>Doppel-Absendung: Header und Cookie müssen übereinstimmen (konstante Laufzeit).</summary>
    public static bool HasValidCsrfToken(HttpRequest request)
    {
        var cookie = request.Cookies[CsrfCookie];
        var header = request.Headers[CsrfHeader].ToString();
        return !string.IsNullOrEmpty(cookie) && !string.IsNullOrEmpty(header)
            && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(cookie), System.Text.Encoding.UTF8.GetBytes(header));
    }

    /// <summary>
    /// Das Zugriffs-Cookie lebt so lange wie die Sitzung: Ist nur das Token abgelaufen, meldet der Server
    /// ACCESS_TOKEN_EXPIRED und der Client erneuert still – statt ohne Cookie als „nicht angemeldet“ zu gelten.
    /// Der CSRF-Token bleibt beim Erneuern gleich, damit parallel laufende Anfragen nicht scheitern.
    /// </summary>
    private static void SetCookies(HttpContext http, string accessToken, string refreshToken, DateTimeOffset refreshExpires, bool newCsrfToken)
    {
        var cookies = http.Response.Cookies;
        cookies.Append(AccessCookie, accessToken, Options(http, "/api", refreshExpires, httpOnly: true));
        cookies.Append(RefreshCookie, refreshToken, Options(http, RefreshPath, refreshExpires, httpOnly: true));
        var csrf = !newCsrfToken && http.Request.Cookies[CsrfCookie] is { Length: 64 } existing
            ? existing
            : Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        cookies.Append(CsrfCookie, csrf, Options(http, "/", refreshExpires, httpOnly: false));
    }

    private static void ClearCookies(HttpContext http)
    {
        http.Response.Cookies.Delete(AccessCookie, Options(http, "/api", null, httpOnly: true));
        http.Response.Cookies.Delete(RefreshCookie, Options(http, RefreshPath, null, httpOnly: true));
        http.Response.Cookies.Delete(CsrfCookie, Options(http, "/", null, httpOnly: false));
    }

    /// <summary>
    /// Cookies sind immer host-only (ohne Domain-Attribut): Sie gelten genau für die Adresse, die der Browser aufruft –
    /// auch wenn ESTRICHONE_DOMAIN/PublicUrl davon abweicht. Laufzeit zusätzlich als Max-Age (relativ), damit eine
    /// falsch gehende Uhr auf Server oder Gerät die Cookies nicht sofort verfallen lässt.
    /// </summary>
    private static CookieOptions Options(HttpContext http, string path, DateTimeOffset? expires, bool httpOnly) => new()
    {
        Path = path,
        Expires = expires,
        MaxAge = expires is { } until ? Clamp(until - http.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow()) : null,
        HttpOnly = httpOnly,
        Secure = UseSecureCookies(http.Request),
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
    };

    private static TimeSpan Clamp(TimeSpan value) => value < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : value;

    /// <summary>
    /// <c>Secure</c> immer, außer der Browser ruft EstrichONE nachweislich über unverschlüsseltes HTTP auf
    /// (Anfrage selbst per HTTP und <c>Origin</c> bzw. <c>Referer</c> mit <c>http://</c>, z. B. <c>http://192.168.1.10:8181</c>).
    /// Dort würde der Browser Secure-Cookies verwerfen: Die Anmeldung gelänge, jede Folgeanfrage wäre aber nicht angemeldet
    /// (Sitzung „sofort beendet“). Über HTTPS – auch hinter Nginx Proxy Manager mit X-Forwarded-Proto – bleiben sie Secure.
    /// </summary>
    public static bool UseSecureCookies(HttpRequest request)
    {
        if (request.IsHttps)
        {
            return true;
        }

        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin) || origin == "null")
        {
            origin = request.Headers.Referer.ToString();
        }

        return !(Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp);
    }

    private static string? Truncate(string value, int length) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= length ? value : value[..length];
}
