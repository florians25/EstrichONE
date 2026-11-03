using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Access;

/// <summary>
/// Keine Rechteausweitung über die Benutzer- und Rollenverwaltung:
/// <list type="bullet">
/// <item>Konten anderer: Rollen vergeben bzw. Konto bearbeiten, zurücksetzen, sperren nur, wenn man deren
/// Verwaltungsrechte (<see cref="Sensitive"/>) selbst hat – die IT-Administration verwaltet Büro- und Baustellenkonten,
/// aber kein Administratorkonto und vergibt keine Administratorrolle.</item>
/// <item>Eigenes Konto: nur Rollen, deren Rechte man vollständig schon hat (keine Selbstbeförderung).</item>
/// <item>Rollen: neue Rechte in eine Rolle nur aufnehmen, wenn man sie selbst hat.</item>
/// </list>
/// </summary>
internal sealed class PrivilegeGuard(EstrichOneDbContext db, IUserPermissions permissions, ICurrentUser current)
{
    /// <summary>Verwaltungsrechte plus Personalakte – was nur vergeben darf, wer es selbst hat.</summary>
    public static IReadOnlySet<string> Sensitive { get; } = new HashSet<string>(Permissions.Privileged.Append(Permissions.HrManage), StringComparer.Ordinal);

    public async Task<bool> MayAssignRolesAsync(Guid targetUserId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        var wanted = await db.Set<RoleGrant>().Where(g => roleIds.Contains(g.RoleId)).Select(g => g.Permission).Distinct().ToListAsync(cancellationToken);
        return await CoversAsync(targetUserId == current.UserId ? wanted : wanted.Where(Sensitive.Contains), cancellationToken);
    }

    public async Task<bool> MayManageUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == current.UserId)
        {
            return true;
        }

        var held = await db.Set<UserRole>().Where(r => r.UserId == userId)
            .Join(db.Set<RoleGrant>(), r => r.RoleId, g => g.RoleId, (_, g) => g.Permission).Distinct().ToListAsync(cancellationToken);
        return await CoversAsync(held.Where(Sensitive.Contains), cancellationToken);
    }

    public async Task<bool> CoversAsync(IEnumerable<string> wanted, CancellationToken cancellationToken)
    {
        var own = await permissions.GetAsync(cancellationToken);
        return wanted.All(own.Contains);
    }

    public static ProblemHttpResult Denied() => TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Nicht erlaubt",
        detail: "Konten und Rollen mit Verwaltungsrechten, die Sie selbst nicht haben, können Sie nicht verwalten oder vergeben.",
        extensions: new Dictionary<string, object?> { ["code"] = "PRIVILEGE_ESCALATION" });
}

internal sealed class TwoFactorPolicy(EstrichOneDbContext db, ISystemSettings settings) : ITwoFactorPolicy
{
    public async Task<bool> IsRequiredAsync(Guid userId, CancellationToken cancellationToken) =>
        (await settings.GetAsync(cancellationToken)).Session.RequireTwoFactorForAdmins
        && await db.Set<UserRole>().Where(r => r.UserId == userId)
            .Join(db.Set<RoleGrant>(), r => r.RoleId, g => g.RoleId, (_, g) => g.Permission)
            .AnyAsync(p => Permissions.Privileged.Contains(p), cancellationToken);
}
