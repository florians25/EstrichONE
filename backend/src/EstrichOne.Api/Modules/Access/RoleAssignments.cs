using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Access;

internal sealed class RoleAssignments(EstrichOneDbContext db) : IRoleAssignments
{
    public Guid AdministratorRoleId => StandardRoles.AdministratorId;

    public async Task SetRolesAsync(Guid userId, IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        var known = await db.Set<Role>().Where(role => roleIds.Contains(role.Id)).Select(role => role.Id).ToListAsync(cancellationToken);
        var unknown = roleIds.Except(known).FirstOrDefault();
        if (unknown != Guid.Empty)
        {
            throw new UnknownRoleException(unknown);
        }

        var current = await db.Set<UserRole>().Where(userRole => userRole.UserId == userId).ToListAsync(cancellationToken);
        var losesAdministrator = current.Any(userRole => userRole.RoleId == StandardRoles.AdministratorId)
            && !roleIds.Contains(StandardRoles.AdministratorId);
        if (losesAdministrator && !await OtherActiveAdministratorExistsAsync(userId, cancellationToken))
        {
            throw new LastAdministratorException();
        }

        db.RemoveRange(current.Where(userRole => !roleIds.Contains(userRole.RoleId)));
        db.AddRange(roleIds.Where(roleId => current.All(userRole => userRole.RoleId != roleId))
            .Select(roleId => new UserRole(userId, roleId)));
    }

    public async Task<IReadOnlyList<Guid>> GetRoleIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Set<UserRole>().Where(userRole => userRole.UserId == userId).Select(userRole => userRole.RoleId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string>> GetRoleNamesAsync(CancellationToken cancellationToken) =>
        await db.Set<Role>().AsNoTracking().ToDictionaryAsync(role => role.Id, role => role.Name, cancellationToken);

    private Task<bool> OtherActiveAdministratorExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Set<UserRole>()
            .Where(userRole => userRole.RoleId == StandardRoles.AdministratorId && userRole.UserId != userId)
            .Join(db.Set<User>().Where(user => user.IsActive), userRole => userRole.UserId, user => user.Id, (userRole, _) => userRole)
            .AnyAsync(cancellationToken);
}
