using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Identity.Sessions;
using EstrichOne.Api.Modules.Identity.TwoFactor;
using EstrichOne.Api.Modules.Notifications.Exports;
using EstrichOne.Api.Modules.Access.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Identity.Passwords;

public enum LoginFailure
{
    InvalidCredentials,
    LockedOut,

    /// <summary>Passwort richtig, Zwei-Faktor-Code fehlt (Client fragt ihn ab und sendet die Anmeldung erneut).</summary>
    TwoFactorRequired,

    /// <summary>Passwort richtig, Zwei-Faktor-Code falsch (zählt als Fehlversuch).</summary>
    TwoFactorInvalid,
}

public sealed record LoginResult(IssuedTokens? Tokens, User? User, bool MustChangePassword, LoginFailure? Failure, TimeSpan? RetryAfter);

/// <summary>
/// Anmeldung mit Anmeldename oder E-Mail und Passwort. Unbekanntes Konto, falsches Passwort und gesperrtes Konto
/// liefern dieselbe Antwort und dieselbe Rechenzeit (keine Kontenaufzählung).
/// Fehlversuche zählen je Konto (Anmeldename und E-Mail gemeinsam) und je IP-Adresse; beim Erreichen der Sperre
/// werden die Administratoren benachrichtigt (höchstens einmal je Stunde und Konto bzw. Adresse).
/// </summary>
public sealed class LoginService(
    EstrichOneDbContext db,
    PasswordHasher hasher,
    SessionService sessions,
    TimeProvider clock,
    IOptions<AuthenticationSettings> options,
    IAuditLog audit,
    TwoFactorService twoFactor,
    IServiceProvider services)
{
    public async Task<LoginResult> LoginAsync(string login, string password, ClientKind clientKind, string? clientName,
        string? ipAddress, CancellationToken cancellationToken, bool rememberMe = false, string? twoFactorCode = null)
    {
        var settings = options.Value;
        var now = clock.GetUtcNow();
        var normalized = User.Normalize(login);
        var windowStart = now - settings.FailedLoginWindow;

        var user = await db.Set<User>()
            .SingleOrDefaultAsync(u => u.NormalizedUserName == normalized || u.NormalizedEmail == normalized, cancellationToken);

        // Ein Zähler je Konto, egal ob mit Anmeldename oder E-Mail versucht (sonst doppelt so viele Versuche).
        var key = user?.NormalizedUserName ?? normalized;
        var failuresForLogin = await db.Set<FailedLogin>()
            .CountAsync(f => (f.NormalizedLogin == key || f.NormalizedLogin == normalized) && f.At > windowStart, cancellationToken);
        var failuresForAddress = ipAddress is null ? 0 : await db.Set<FailedLogin>()
            .CountAsync(f => f.IpAddress == ipAddress && f.At > windowStart, cancellationToken);
        if (failuresForLogin >= settings.MaxFailedLoginsPerAccount || failuresForAddress >= settings.MaxFailedLoginsPerAddress)
        {
            audit.Record(AuditActions.LoginLockedOut, null, details: new Dictionary<string, string?> { ["login"] = key, ["ip"] = ipAddress });
            await db.SaveChangesAsync(cancellationToken);
            return new LoginResult(null, null, false, LoginFailure.LockedOut, settings.FailedLoginWindow);
        }

        var credential = user is null ? null
            : await db.Set<LocalCredential>().SingleOrDefaultAsync(c => c.UserId == user.Id, cancellationToken);

        var passwordValid = hasher.Verify(password, credential?.PasswordHash);
        if (user is null || credential is null || !passwordValid || !user.IsActive)
        {
            var reason = user is null ? "unknown_login" : !user.IsActive ? "user_disabled" : "invalid_password";
            await RecordFailureAsync(key, user, ipAddress, reason, failuresForLogin, failuresForAddress, cancellationToken);
            return new LoginResult(null, null, false, LoginFailure.InvalidCredentials, null);
        }

        switch (await twoFactor.VerifyAsync(user.Id, twoFactorCode, cancellationToken))
        {
            case TwoFactorCheck.Missing:
                // Kein Fehlversuch: Der Client fragt jetzt den Code ab und meldet sich damit erneut an.
                return new LoginResult(null, user, false, LoginFailure.TwoFactorRequired, null);
            case TwoFactorCheck.Invalid:
                await RecordFailureAsync(key, user, ipAddress, "invalid_two_factor", failuresForLogin, failuresForAddress, cancellationToken);
                return new LoginResult(null, user, false, LoginFailure.TwoFactorInvalid, null);
            case TwoFactorCheck.RecoveryCodeUsed:
                audit.Record(AuditActions.RecoveryCodeUsed, user.Id, "user", user.Id.ToString());
                break;
        }

        if (hasher.NeedsRehash(credential.PasswordHash))
        {
            credential.Rehash(hasher.Hash(password));
        }

        await db.Set<FailedLogin>().Where(f => f.NormalizedLogin == key || f.NormalizedLogin == normalized).ExecuteDeleteAsync(cancellationToken);
        user.RecordLogin(now);
        var tokens = await sessions.StartAsync(user, clientKind, clientName, rememberMe, cancellationToken);
        audit.Record(AuditActions.LoginSucceeded, user.Id, "session", tokens.SessionId.ToString(),
            new Dictionary<string, string?> { ["client"] = clientKind.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return new LoginResult(tokens, user, credential.MustChange, null, null);
    }

    private async Task RecordFailureAsync(string key, User? user, string? ipAddress, string reason, int failuresForLogin,
        int failuresForAddress, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        db.Add(new FailedLogin(key, ipAddress, clock.GetUtcNow()));
        audit.Record(AuditActions.LoginFailed, user?.Id, details: new Dictionary<string, string?>
        {
            ["login"] = key,
            ["reason"] = reason,
            ["ip"] = ipAddress,
        });

        // Genau beim Erreichen der Sperre: Angriff protokollieren und Administratoren informieren.
        var accountLocked = user is not null && failuresForLogin + 1 == settings.MaxFailedLoginsPerAccount;
        var addressLocked = ipAddress is not null && failuresForAddress + 1 == settings.MaxFailedLoginsPerAddress;
        if (accountLocked || addressLocked)
        {
            audit.Record(AuditActions.BruteForceDetected, user?.Id, "user", user?.Id.ToString(), new Dictionary<string, string?>
            {
                ["login"] = accountLocked ? key : null,
                ["ip"] = ipAddress,
                ["scope"] = accountLocked ? "account" : "address",
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        if (accountLocked || addressLocked)
        {
            await AlertAdministratorsAsync(accountLocked ? user!.UserName : null, ipAddress, cancellationToken);
        }
    }

    private async Task AlertAdministratorsAsync(string? userName, string? ipAddress, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var admins = await services.GetRequiredService<IRoleAssignments>().GetUserIdsWithPermissionAsync(Permissions.UsersManage, cancellationToken);
        var minutes = (int)settings.FailedLoginWindow.TotalMinutes;
        var body = userName is not null
            ? $"Konto „{userName}“: {settings.MaxFailedLoginsPerAccount} falsche Anmeldungen in {minutes} Minuten (zuletzt von {ipAddress ?? "unbekannt"}). Die Anmeldung ist vorübergehend gesperrt."
            : $"Adresse {ipAddress}: {settings.MaxFailedLoginsPerAddress} falsche Anmeldungen in {minutes} Minuten. Anmeldungen von dort sind vorübergehend gesperrt.";
        var hour = clock.GetUtcNow().ToString("yyyyMMddHH", System.Globalization.CultureInfo.InvariantCulture);
        await services.GetRequiredService<INotifier>().NotifyAsync(admins, new NotificationRequest(NotificationCategory.System,
            "Anmeldeangriff erkannt", body, "/system/protokoll", Urgency: NotificationUrgency.High,
            DedupKey: $"login-attack:{userName ?? ipAddress}:{hour}", Event: NotificationEvents.LoginAttack), cancellationToken);
    }
}
