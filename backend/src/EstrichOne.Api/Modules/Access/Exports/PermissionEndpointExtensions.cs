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

    /// <summary>Endpunkt mit einem der angegebenen Rechte (z. B. Verkauf: ansehen einer beliebigen Belegart; die Belegart prüft der Endpunkt selbst).</summary>
    public static TBuilder RequireAnyPermission<TBuilder>(this TBuilder builder, params string[] anyOf)
        where TBuilder : IEndpointConventionBuilder
    {
        if (anyOf.Length == 0 || anyOf.Any(p => !Permissions.Exists(p)))
        {
            throw new ArgumentException("Unbekanntes Recht.", nameof(anyOf));
        }

        return builder.RequireAuthorization(policy => policy
            .RequireAuthenticatedUser()
            .RequireApprovedDevice()
            .AddRequirements(new PermissionRequirement(anyOf)));
    }
}
