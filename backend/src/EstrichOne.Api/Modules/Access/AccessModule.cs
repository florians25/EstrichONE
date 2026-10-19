using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Access.Exports;
using Microsoft.AspNetCore.Authorization;

namespace EstrichOne.Api.Modules.Access;

/// <summary>Rechtekatalog, Rollen, Rechteprüfung.</summary>
public sealed class AccessModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IUserPermissions, UserPermissions>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddScoped<IDataInitializer, StandardRoles>();
        services.AddScoped<IRoleAssignments, RoleAssignments>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => AccessEndpoints.Map(api);
}
