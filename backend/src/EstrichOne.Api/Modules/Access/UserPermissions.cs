using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Access;

/// <summary>Lädt die Rechte des angemeldeten Benutzers einmal je Anfrage.</summary>
internal sealed class UserPermissions(EstrichOneDbContext db, ICurrentUser currentUser) : IUserPermissions
{
    private IReadOnlySet<string>? _cache;

    public async Task<IReadOnlySet<string>> GetAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        var userId = currentUser.UserId;
        var codes = await db.Set<UserRole>()
            .Where(userRole => userRole.UserId == userId)
            .Join(db.Set<RoleGrant>(), userRole => userRole.RoleId, grant => grant.RoleId, (_, grant) => grant.Permission)
            .Distinct()
            .ToListAsync(cancellationToken);
        _cache = codes.ToHashSet(StringComparer.Ordinal);
        return _cache;
    }
}

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

internal sealed class PermissionHandler(IUserPermissions permissions) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && await permissions.HasAsync(requirement.Permission, CancellationToken.None))
        {
            context.Succeed(requirement);
        }
    }
}
