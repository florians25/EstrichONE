using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Identity.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Identity.Passwords;

public enum LoginFailure
{
    InvalidCredentials,
    LockedOut,
}

public sealed record LoginResult(IssuedTokens? Tokens, User? User, bool MustChangePassword, LoginFailure? Failure, TimeSpan? RetryAfter);

/// <summary>
/// Anmeldung mit Anmeldename oder E-Mail und Passwort. Unbekanntes Konto, falsches Passwort und gesperrtes Konto
/// liefern dieselbe Antwort und dieselbe Rechenzeit (keine Kontenaufzählung).
/// </summary>
public sealed class LoginService(
    EstrichOneDbContext db,
    PasswordHasher hasher,
    SessionService sessions,
    TimeProvider clock,
    IOptions<AuthenticationSettings> options,
    IAuditLog audit)
{
    public async Task<LoginResult> LoginAsync(string login, string password, ClientKind clientKind, string? clientName,
        string? ipAddress, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();
        var normalized = User.Normalize(login);
        var windowStart = now - settings.FailedLoginWindow;

        var failuresForLogin = await db.Set<FailedLogin>()
            .CountAsync(f => f.NormalizedLogin == normalized && f.At > windowStart, cancellationToken);
        var failuresForAddress = ipAddress is null ? 0 : await db.Set<FailedLogin>()
            .CountAsync(f => f.IpAddress == ipAddress && f.At > windowStart, cancellationToken);
        if (failuresForLogin >= settings.MaxFailedLoginsPerAccount || failuresForAddress >= settings.MaxFailedLoginsPerAddress)
        {
            audit.Record(AuditActions.LoginLockedOut, null, details: new Dictionary<string, string?> { ["login"] = normalized });
            await db.SaveChangesAsync(cancellationToken);
            return new LoginResult(null, null, false, LoginFailure.LockedOut, settings.FailedLoginWindow);
        }

        var user = await db.Set<User>()
            .SingleOrDefaultAsync(u => u.NormalizedUserName == normalized || u.NormalizedEmail == normalized, cancellationToken);
        var credential = user is null ? null
            : await db.Set<LocalCredential>().SingleOrDefaultAsync(c => c.UserId == user.Id, cancellationToken);

        var passwordValid = hasher.Verify(password, credential?.PasswordHash);
        if (user is null || credential is null || !passwordValid || !user.IsActive)
        {
            db.Add(new FailedLogin(normalized, ipAddress, now));
            audit.Record(AuditActions.LoginFailed, user?.Id, details: new Dictionary<string, string?>
            {
                ["login"] = normalized,
                ["reason"] = user is null ? "unknown_login" : !user.IsActive ? "user_disabled" : "invalid_password",
            });
            await db.SaveChangesAsync(cancellationToken);
            return new LoginResult(null, null, false, LoginFailure.InvalidCredentials, null);
        }

        if (hasher.NeedsRehash(credential.PasswordHash))
        {
            credential.Rehash(hasher.Hash(password));
        }

        await db.Set<FailedLogin>().Where(f => f.NormalizedLogin == normalized).ExecuteDeleteAsync(cancellationToken);
        user.RecordLogin(now);
        var tokens = sessions.Start(user, clientKind, clientName);
        audit.Record(AuditActions.LoginSucceeded, user.Id, "session", tokens.SessionId.ToString(),
            new Dictionary<string, string?> { ["client"] = clientKind.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return new LoginResult(tokens, user, credential.MustChange, null, null);
    }
}
