using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Mail.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Access;

/// <summary>Kontoanlage und Onboarding-Unterlagen für den Personalstamm (nutzt Konten, Rollen und den Zugangsbrief).</summary>
internal sealed class UserOnboarding(
    EstrichOneDbContext db, IUserAccounts accounts, IRoleAssignments roles, AccessLetterService letters, IMailService mail,
    ICurrentUser current, IAuditLog audit) : IUserOnboarding
{
    public string ServerUrl(HttpRequest request) => letters.ServerUrl(request);

    public async Task<OnboardingAccount> CreateAccountAsync(string displayName, string userName, string? email, IReadOnlyCollection<Guid> roleIds,
        CancellationToken cancellationToken)
    {
        var ids = roleIds.Distinct().ToList();
        if (ids.Count == 0 || await db.Set<Role>().CountAsync(r => ids.Contains(r.Id), cancellationToken) != ids.Count)
        {
            return new OnboardingAccount(null, null, null, "UNKNOWN_ROLE", "Bitte mindestens eine vorhandene Rolle wählen.");
        }

        var password = accounts.GenerateOneTimePassword();
        User user;
        try
        {
            try
            {
                user = await accounts.CreateLocalUserAsync(displayName, userName, string.IsNullOrWhiteSpace(email) ? null : email.Trim(), password, true, cancellationToken);
            }
            catch (PasswordRejectedException)
            {
                password = accounts.GenerateOneTimePassword();
                user = await accounts.CreateLocalUserAsync(displayName, userName, string.IsNullOrWhiteSpace(email) ? null : email.Trim(), password, true, cancellationToken);
            }
        }
        catch (DuplicateUserException exception)
        {
            return new OnboardingAccount(null, null, null, "USER_EXISTS", exception.Message);
        }
        catch (ArgumentException exception)
        {
            return new OnboardingAccount(null, null, null, "VALIDATION_FAILED", exception.Message);
        }

        await roles.SetRolesAsync(user.Id, ids, cancellationToken);
        audit.Record("users.created", current.UserId, "user", user.Id.ToString(), new Dictionary<string, string?> { ["via"] = "onboarding" });
        return new OnboardingAccount(user.Id, user.UserName, password, null, null);
    }

    public async Task<OnboardingAccount> NewPasswordAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return new OnboardingAccount(null, null, null, "USER_NOT_FOUND", "Benutzerkonto nicht gefunden.");
        }

        var password = await accounts.ResetPasswordAsync(userId, cancellationToken);
        audit.Record("users.password_reset", current.UserId, "user", userId.ToString(), new Dictionary<string, string?> { ["via"] = "onboarding" });
        return new OnboardingAccount(user.Id, user.UserName, password, null, null);
    }

    public async Task<byte[]> RenderAsync(Guid userId, string password, string serverUrl, OnboardingDetails details, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().AsNoTracking().SingleAsync(u => u.Id == userId, cancellationToken);
        return await letters.RenderAsync(user, password, serverUrl, reset: false, cancellationToken, details);
    }

    public async Task<bool> MailAsync(Guid userId, byte[] pdf, string serverUrl, CancellationToken cancellationToken)
    {
        var user = await db.Set<User>().AsNoTracking().SingleAsync(u => u.Id == userId, cancellationToken);
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return false;
        }

        var values = new Dictionary<string, string?>
        {
            ["name"] = user.DisplayName, ["benutzername"] = user.UserName, ["link"] = serverUrl, ["passwort"] = "siehe angehängte Onboarding-Unterlagen",
        };
        try
        {
            var id = await mail.QueueTemplateAsync(MailTemplateKeys.UserWelcome, user.Email, values, $"user:{user.Id}",
                [new MailAttachment($"Willkommen {user.DisplayName}.pdf", "application/pdf", pdf)], cancellationToken);
            return id is not null;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
