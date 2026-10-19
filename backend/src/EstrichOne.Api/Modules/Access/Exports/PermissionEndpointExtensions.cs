using EstrichOne.Api.Modules.Identity.Exports;

namespace EstrichOne.Api.Modules.Access.Exports;

public static class PermissionEndpointExtensions
{
    /// <summary>Endpunkt nur mit dem angegebenen Recht (sonst 403 <c>PERMISSION_DENIED</c>).</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        if (!Permissions.Exists(permission))
        {
            throw new ArgumentException($"Unbekanntes Recht '{permission}'.", nameof(permission));
        }

        return builder.RequireAuthorization(policy => policy
            .RequireAuthenticatedUser()
            .RequireApprovedDevice()
            .AddRequirements(new PermissionRequirement(permission)));
    }
}
