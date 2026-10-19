using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Identity.Passwords;

/// <summary>Benutzer mit lokalem Passwort anlegen und Passwörter ändern.</summary>
internal sealed class UserAccounts(EstrichOneDbContext db, PasswordHasher hasher, TimeProvider clock) : IUserAccounts
{
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
}
