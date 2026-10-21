using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Organization.Exports;

namespace EstrichOne.Api.Modules.Organization;

/// <summary>Firmendaten, Einstellungen; später Mitarbeiter, Kolonnen und Ressourcen.</summary>
public sealed class OrganizationModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICompanyProfiles, CompanyProfiles>();
        services.AddScoped<ISystemSettings, SystemSettingsStore>();
        services.AddScoped<IPdfBranding, PdfBranding>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        CompanyEndpoints.Map(api);
        CatalogEndpoints.Map(api);
        SystemSettingsEndpoints.Map(api);
        CompanyLogoEndpoints.Map(api);
    }
}
