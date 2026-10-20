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

public sealed record WebLoginRequest(
    [property: Required, MaxLength(320)] string Login,
    [property: Required, MaxLength(PasswordPolicy.MaxLength)] string Password);

public sealed record WebSessionResponse(UserSummary User, bool MustChangePassword, DateTimeOffset AccessTokenExpiresAt);

/// <summary>
/// Anmeldung im Browser (docs/reengineering/17, Abschnitt 2): Tokens nur in HttpOnly-, Secure-, SameSite=Strict-Cookies,
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
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .AllowAnonymous().RequireRateLimiting(AuthEndpoints.RateLimitPolicy);

        web.MapPost("/logout", LogoutAsync).WithName("WebLogout")
            .WithSummary("Abmelden im Browser; beendet die Sitzung und löscht die Cookies")
            .AllowAnonymous();
    }

    private static async Task<Results<Ok<WebSessionResponse>, ProblemHttpResult>> LoginAsync(
        WebLoginRequest request, LoginService login, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await login.LoginAsync(request.Login, request.Password, ClientKind.Web,
            Truncate(http.Request.Headers.UserAgent.ToString(), 100), http.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        if (result.Failure == LoginFailure.LockedOut)
        {
            http.Response.Headers.RetryAfter = ((int)result.RetryAfter!.Value.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return AuthEndpoints.Problem(StatusCodes.Status429TooManyRequests, "LOGIN_LOCKED",
                "Zu viele Fehlversuche", "Die Anmeldung ist vorübergehend gesperrt. Bitte später erneut versuchen.");
        }

        if (result.Failure is not null)
        {
            return AuthEndpoints.Problem(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS",
                "Anmeldung fehlgeschlagen", "Anmeldename oder Passwort ist nicht korrekt.");
        }

        var tokens = result.Tokens!;
        SetCookies(http, tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
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
        var (tokens, _) = refreshToken is null ? (null, null) : await sessions.RefreshAsync(refreshToken, null, cancellationToken);
        if (tokens is null)
        {
            ClearCookies(http);
            return AuthEndpoints.Problem(StatusCodes.Status401Unauthorized, "SESSION_EXPIRED", "Erneuerung fehlgeschlagen",
                "Die Sitzung ist abgelaufen. Bitte neu anmelden.");
        }

        SetCookies(http, tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken, tokens.RefreshTokenExpiresAt);
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

    private static void SetCookies(HttpContext http, string accessToken, DateTimeOffset accessExpires, string refreshToken, DateTimeOffset refreshExpires)
    {
        var cookies = http.Response.Cookies;
        cookies.Append(AccessCookie, accessToken, Options("/api", accessExpires, httpOnly: true));
        cookies.Append(RefreshCookie, refreshToken, Options(RefreshPath, refreshExpires, httpOnly: true));
        cookies.Append(CsrfCookie, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), Options("/", refreshExpires, httpOnly: false));
    }

    private static void ClearCookies(HttpContext http)
    {
        http.Response.Cookies.Delete(AccessCookie, Options("/api", null, httpOnly: true));
        http.Response.Cookies.Delete(RefreshCookie, Options(RefreshPath, null, httpOnly: true));
        http.Response.Cookies.Delete(CsrfCookie, Options("/", null, httpOnly: false));
    }

    private static CookieOptions Options(string path, DateTimeOffset? expires, bool httpOnly) => new()
    {
        Path = path,
        Expires = expires,
        HttpOnly = httpOnly,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        IsEssential = true,
    };

    private static string? Truncate(string value, int length) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= length ? value : value[..length];
}
