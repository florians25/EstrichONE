using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Identity.Sessions;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Identity.Passwords;

/// <summary>Benutzer mit lokalem Passwort anlegen und Passwörter ändern.</summary>
internal sealed class UserAccounts(EstrichOneDbContext db, PasswordHasher hasher, SessionService sessions, TimeProvider clock) : IUserAccounts
{
    private const string OneTimeAlphabet = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<User> CreateLocalUserAsync(string displayName, string userName, string? email, string password,
        bool mustChangePassword, CancellationToken cancellationToken)
    {
        var problem = PasswordPolicy.Validate(password, userName, email);
        if (problem is not null)
        {
            throw new PasswordRejectedException(problem);
        }

        var user = User.Create(Guid.CreateVersion7(), displayName, userName, email);
        if (await db.Set<User>().AnyAsync(u => u.NormalizedUserName == user.NormalizedUserName
                || (user.NormalizedEmail != null && u.NormalizedEmail == user.NormalizedEmail), cancellationToken))
        {
            throw new DuplicateUserException();
        }

        db.Add(user);
        db.Add(new LocalCredential(user.Id, hasher.Hash(password), clock.GetUtcNow(), mustChangePassword));
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<bool> VerifyPasswordAsync(Guid userId, string password, CancellationToken cancellationToken)
    {
        var credential = await db.Set<LocalCredential>().AsNoTracking().SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);
        // Ohne Zugangsdaten trotzdem rechnen: gleiche Antwortzeit, keine Rückschlüsse.
        return hasher.Verify(password, credential?.PasswordHash) && credential is not null;
    }

    public async Task<string> ResetPasswordAsync(Guid userId, CancellationToken cancellationToken)
    {
        var password = GenerateOneTimePassword();
        var credential = await db.Set<LocalCredential>().SingleOrDefaultAsync(c => c.UserId == userId, cancellationToken);
        if (credential is null)
        {
            db.Add(new LocalCredential(userId, hasher.Hash(password), clock.GetUtcNow(), mustChange: true));
        }
        else
        {
            credential.Change(hasher.Hash(password), clock.GetUtcNow(), mustChange: true);
        }

        await sessions.RevokeAllAsync(userId, null, "password_reset", cancellationToken);
        return password;
    }

    public async Task SetActiveAsync(Guid userId, bool active, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().SingleAsync(u => u.Id == userId, cancellationToken);
        if (active)
        {
            user.Activate();
        }
        else
        {
            user.Deactivate();
            await sessions.RevokeAllAsync(userId, null, "user_disabled", cancellationToken);
        }
    }

    public string GenerateOneTimePassword()
    {
        // 14 Zeichen aus 55: ca. 81 Bit – auch ohne Pflichtwechsel nicht zu erraten.
        Span<char> chars = stackalloc char[14];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = OneTimeAlphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(OneTimeAlphabet.Length)];
        }

        return $"{chars[..7]}-{chars[7..]}";
    }
}
