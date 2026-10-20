using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Organization.Exports;

namespace EstrichOne.Api.Modules.Organization;

/// <summary>Firmendaten, Einstellungen; später Mitarbeiter, Kolonnen und Ressourcen.</summary>
public sealed class OrganizationModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICompanyProfiles, CompanyProfiles>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => CompanyEndpoints.Map(api);
}
