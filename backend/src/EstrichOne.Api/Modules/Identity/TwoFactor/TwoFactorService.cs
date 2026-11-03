using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Identity.Sessions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Identity.TwoFactor;

public enum TwoFactorCheck
{
    /// <summary>Für dieses Konto ist keine Zwei-Faktor-Anmeldung eingerichtet.</summary>
    NotEnabled,

    /// <summary>Eingerichtet, aber kein Code angegeben.</summary>
    Missing,
    Invalid,
    Valid,

    /// <summary>Gültiger Wiederherstellungscode (ist damit verbraucht).</summary>
    RecoveryCodeUsed,
}

/// <summary>
/// Zwei-Faktor-Anmeldung mit TOTP und Wiederherstellungscodes. Das Geheimnis liegt nur verschlüsselt in der Datenbank
/// (Data Protection); Wiederherstellungscodes nur als SHA-256. Gespeichert wird mit dem nächsten
/// <c>SaveChangesAsync</c> des Aufrufers.
/// </summary>
public sealed class TwoFactorService(EstrichOneDbContext db, IDataProtectionProvider protection, TimeProvider clock, SessionService sessions)
    : ITwoFactorState
{
    public const int RecoveryCodeCount = 10;
    private const string ProtectorPurpose = "EstrichOne.Identity.TwoFactor.v1";
    private const string RecoveryAlphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    private IDataProtector Protector => protection.CreateProtector(ProtectorPurpose);

    public Task<UserTwoFactor?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<UserTwoFactor>().SingleOrDefaultAsync(t => t.UserId == userId, cancellationToken);

    public async Task<bool> IsEnabledAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Set<UserTwoFactor>().AnyAsync(t => t.UserId == userId && t.EnabledAt != null, cancellationToken);

    public async Task<IReadOnlySet<Guid>> EnabledUsersAsync(CancellationToken cancellationToken) =>
        (await db.Set<UserTwoFactor>().Where(t => t.EnabledAt != null).Select(t => t.UserId).ToListAsync(cancellationToken)).ToHashSet();

    public Task<int> RemainingRecoveryCodesAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<UserRecoveryCode>().CountAsync(c => c.UserId == userId && c.UsedAt == null, cancellationToken);

    /// <summary>Neues Geheimnis für die Einrichtung (noch nicht aktiv). Null, wenn bereits eingeschaltet.</summary>
    public async Task<(string Secret, string Uri)?> BeginSetupAsync(User user, string issuer, CancellationToken cancellationToken)
    {
        var existing = await FindAsync(user.Id, cancellationToken);
        if (existing?.IsEnabled == true)
        {
            return null;
        }

        var secret = Totp.NewSecret();
        var protectedSecret = Protector.Protect(Base32.Encode(secret));
        if (existing is null)
        {
            db.Add(new UserTwoFactor(user.Id, protectedSecret, clock.GetUtcNow()));
        }
        else
        {
            existing.Restart(protectedSecret, clock.GetUtcNow());
        }

        return (Base32.Encode(secret), Totp.OtpAuthUri(issuer, user.UserName, secret));
    }

    /// <summary>Bestätigt die Einrichtung mit dem ersten Code aus der App; liefert die Wiederherstellungscodes.</summary>
    public async Task<IReadOnlyList<string>?> EnableAsync(Guid userId, string code, CancellationToken cancellationToken)
    {
        var entry = await FindAsync(userId, cancellationToken);
        if (entry is null || entry.IsEnabled)
        {
            return null;
        }

        // Einrichtung nur 30 Minuten offen – danach neu beginnen (verhindert alte, abgegriffene QR-Codes).
        if (clock.GetUtcNow() - entry.CreatedAt > TimeSpan.FromMinutes(30))
        {
            return null;
        }

        var step = Totp.Verify(Secret(entry), code, clock.GetUtcNow(), 0);
        if (step is null)
        {
            return null;
        }

        entry.Enable(clock.GetUtcNow(), step.Value);
        return await ReplaceRecoveryCodesAsync(userId, cancellationToken);
    }

    /// <summary>Prüft einen TOTP- oder Wiederherstellungscode (Anmeldung, Bestätigung heikler Aktionen).</summary>
    public async Task<TwoFactorCheck> VerifyAsync(Guid userId, string? code, CancellationToken cancellationToken)
    {
        var entry = await FindAsync(userId, cancellationToken);
        if (entry is null || !entry.IsEnabled)
        {
            return TwoFactorCheck.NotEnabled;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return TwoFactorCheck.Missing;
        }

        var normalized = Totp.Normalize(code.Trim());
        if (normalized.Length == Totp.Digits && normalized.All(char.IsAsciiDigit))
        {
            var step = Totp.Verify(Secret(entry), normalized, clock.GetUtcNow(), entry.LastUsedStep);
            if (step is null)
            {
                return TwoFactorCheck.Invalid;
            }

            entry.Used(step.Value);
            return TwoFactorCheck.Valid;
        }

        var hash = HashRecoveryCode(normalized);
        var candidates = await db.Set<UserRecoveryCode>().Where(c => c.UserId == userId && c.UsedAt == null).ToListAsync(cancellationToken);
        var match = candidates.FirstOrDefault(c => CryptographicOperations.FixedTimeEquals(c.CodeHash, hash));
        if (match is null)
        {
            return TwoFactorCheck.Invalid;
        }

        match.MarkUsed(clock.GetUtcNow());
        return TwoFactorCheck.RecoveryCodeUsed;
    }

    public async Task<IReadOnlyList<string>> ReplaceRecoveryCodesAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Über den Kontext löschen (nicht ExecuteDelete): ein eben eingelöster Code ist bereits geändert und verfolgt.
        db.RemoveRange(await db.Set<UserRecoveryCode>().Where(c => c.UserId == userId).ToListAsync(cancellationToken));
        var codes = new List<string>(RecoveryCodeCount);
        var now = clock.GetUtcNow();
        for (var i = 0; i < RecoveryCodeCount; i++)
        {
            var code = NewRecoveryCode();
            codes.Add(code);
            db.Add(new UserRecoveryCode(userId, HashRecoveryCode(Totp.Normalize(code)), now));
        }

        return codes;
    }

    public async Task DisableAsync(Guid userId, CancellationToken cancellationToken)
    {
        db.RemoveRange(await db.Set<UserRecoveryCode>().Where(c => c.UserId == userId).ToListAsync(cancellationToken));
        if (await FindAsync(userId, cancellationToken) is { } entry)
        {
            db.Remove(entry);
        }
    }

    public async Task<bool> ResetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var enabled = await db.Set<UserTwoFactor>().AnyAsync(t => t.UserId == userId, cancellationToken);
        await DisableAsync(userId, cancellationToken);
        await sessions.RevokeAllAsync(userId, null, "two_factor_reset", cancellationToken);
        return enabled;
    }

    private byte[] Secret(UserTwoFactor entry) => Base32.Decode(Protector.Unprotect(entry.ProtectedSecret));

    private static string NewRecoveryCode()
    {
        Span<char> chars = stackalloc char[11];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = i == 5 ? '-' : RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)];
        }

        return new string(chars);
    }

    private static byte[] HashRecoveryCode(string normalized) => SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToLowerInvariant()));
}
