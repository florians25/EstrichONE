using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Http;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Identity.Passwords;
using EstrichOne.Api.Modules.Identity.Sessions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Identity;

public sealed record LoginRequest(
    [property: Required, MaxLength(320)] string Login,
    [property: Required, MaxLength(PasswordPolicy.MaxLength)] string Password,
    ClientKind Client,
    [property: MaxLength(100)] string? ClientName);

/// <param name="DeviceSignature">Base64; Pflicht, wenn die Sitzung an ein Gerät gebunden ist.</param>
public sealed record RefreshRequest(
    [property: Required, MaxLength(200)] string RefreshToken,
    [property: MaxLength(200)] string? DeviceSignature = null);

public sealed record ChangePasswordRequest(
    [property: Required, MaxLength(PasswordPolicy.MaxLength)] string CurrentPassword,
    [property: Required, MaxLength(PasswordPolicy.MaxLength)] string NewPassword);

public sealed record UserSummary(Guid Id, string DisplayName, string UserName, string? Email);

public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserSummary User,
    bool MustChangePassword);

public sealed record RefreshResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

internal static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    public static void Map(IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Anmeldung");

        auth.MapPost("/login", LoginAsync)
            .WithName("Login").WithSummary("Anmeldung mit Anmeldename oder E-Mail und Passwort")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicy);

        auth.MapPost("/refresh", RefreshAsync)
            .WithName("RefreshTokens").WithSummary("Zugriffstoken erneuern (Refresh-Token wird dabei ersetzt)")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .AllowAnonymous().RequireRateLimiting(RateLimitPolicy);

        auth.MapPost("/logout", LogoutAsync)
            .WithName("Logout").WithSummary("Aktuelle Sitzung beenden")
            .AllowPendingDevice();

        auth.MapGet("/me", GetMeAsync)
            .WithName("GetCurrentUser").WithSummary("Angemeldeter Benutzer")
            .AllowPendingDevice();

        auth.MapPost("/change-password", ChangePasswordAsync)
            .WithName("ChangePassword").WithSummary("Eigenes Passwort ändern; beendet alle anderen Sitzungen")
            .ProducesValidationProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireRateLimiting(RateLimitPolicy);
    }

    private static async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request, LoginService login, HttpContext http, CancellationToken cancellationToken)
    {
        var result = await login.LoginAsync(request.Login, request.Password, request.Client, request.ClientName,
            http.Connection.RemoteIpAddress?.ToString(), cancellationToken);

        if (result.Failure == LoginFailure.LockedOut)
        {
            http.Response.Headers.RetryAfter = ((int)result.RetryAfter!.Value.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Problem(StatusCodes.Status429TooManyRequests, "LOGIN_LOCKED",
                "Zu viele Fehlversuche", "Die Anmeldung ist vorübergehend gesperrt. Bitte später erneut versuchen.");
        }

        if (result.Failure is not null)
        {
            return Problem(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS",
                "Anmeldung fehlgeschlagen", "Anmeldename oder Passwort ist nicht korrekt.");
        }

        var tokens = result.Tokens!;
        return TypedResults.Ok(new TokenResponse(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken,
            tokens.RefreshTokenExpiresAt, Summarize(result.User!), result.MustChangePassword));
    }

    private static async Task<Results<Ok<RefreshResponse>, ProblemHttpResult>> RefreshAsync(
        RefreshRequest request, SessionService sessions, CancellationToken cancellationToken)
    {
        byte[]? signature = null;
        if (request.DeviceSignature is not null && !TryDecodeBase64(request.DeviceSignature, out signature))
        {
            return Problem(StatusCodes.Status401Unauthorized, "DEVICE_PROOF_INVALID", "Erneuerung fehlgeschlagen", "Die Gerätesignatur ist ungültig.");
        }

        var (tokens, failure) = await sessions.RefreshAsync(request.RefreshToken, signature, cancellationToken);
        if (tokens is null)
        {
            var (code, detail) = failure switch
            {
                RefreshFailure.Reused => ("REFRESH_TOKEN_REUSED", "Der Token wurde bereits verwendet. Die Sitzung wurde aus Sicherheitsgründen beendet."),
                RefreshFailure.UserDisabled => ("USER_DISABLED", "Das Benutzerkonto ist gesperrt."),
                RefreshFailure.DeviceProofRequired => ("DEVICE_PROOF_INVALID", "Die Gerätesignatur fehlt oder ist ungültig."),
                RefreshFailure.DeviceLocked => ("DEVICE_LOCKED", "Dieses Gerät ist gesperrt."),
                RefreshFailure.DeviceRevoked => ("DEVICE_REVOKED", "Dieses Gerät wurde aus EstrichONE entfernt."),
                RefreshFailure.SessionEnded or RefreshFailure.Expired => ("SESSION_EXPIRED", "Die Sitzung ist abgelaufen. Bitte neu anmelden."),
                _ => ("INVALID_REFRESH_TOKEN", "Der Token ist ungültig."),
            };
            return Problem(StatusCodes.Status401Unauthorized, code, "Erneuerung fehlgeschlagen", detail);
        }

        return TypedResults.Ok(new RefreshResponse(tokens.AccessToken, tokens.AccessTokenExpiresAt, tokens.RefreshToken, tokens.RefreshTokenExpiresAt));
    }

    private static async Task<NoContent> LogoutAsync(ICurrentUser current, SessionService sessions, IAuditLog audit,
        CancellationToken cancellationToken)
    {
        audit.Record(AuditActions.Logout, current.UserId, "session", current.SessionId.ToString());
        await sessions.RevokeAsync(current.SessionId, "logout", current.UserId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<UserSummary>> GetMeAsync(ICurrentUser current, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().AsNoTracking().SingleAsync(u => u.Id == current.UserId, cancellationToken);
        return TypedResults.Ok(Summarize(user));
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> ChangePasswordAsync(
        ChangePasswordRequest request, ICurrentUser current, EstrichOneDbContext db, PasswordHasher hasher,
        SessionService sessions, IAuditLog audit, TimeProvider clock, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleAsync(u => u.Id == current.UserId, cancellationToken);
        var credential = await db.Set<LocalCredential>().SingleOrDefaultAsync(c => c.UserId == user.Id, cancellationToken);
        if (credential is null || !hasher.Verify(request.CurrentPassword, credential.PasswordHash))
        {
            return Problem(StatusCodes.Status403Forbidden, "INVALID_CURRENT_PASSWORD", "Passwort nicht geändert",
                "Das bisherige Passwort ist nicht korrekt.");
        }

        var problem = PasswordPolicy.Validate(request.NewPassword, user.UserName, user.Email);
        if (problem is not null)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["newPassword"] = [problem] },
                title: "Passwort entspricht nicht den Regeln",
                extensions: new Dictionary<string, object?> { ["code"] = ErrorCodes.ValidationFailed });
        }

        credential.Change(hasher.Hash(request.NewPassword), clock.GetUtcNow(), mustChange: false);
        await sessions.RevokeAllAsync(user.Id, current.SessionId, "password_changed", cancellationToken);
        audit.Record(AuditActions.PasswordChanged, user.Id, "user", user.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static bool TryDecodeBase64(string value, out byte[]? bytes)
    {
        bytes = new byte[value.Length];
        if (Convert.TryFromBase64String(value, bytes, out var written))
        {
            bytes = bytes[..written];
            return true;
        }

        bytes = null;
        return false;
    }

    private static UserSummary Summarize(User user) => new(user.Id, user.DisplayName, user.UserName, user.Email);

    private static ProblemHttpResult Problem(int status, string code, string title, string detail) =>
        TypedResults.Problem(statusCode: status, title: title, detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
