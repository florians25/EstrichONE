using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Modules.Organization.Exports;
using EstrichOne.Api.Modules.Sync.Exports;

namespace EstrichOne.Api.Modules.Organization;

/// <summary>Firmendaten, Einstellungen; später Mitarbeiter, Kolonnen und Ressourcen.</summary>
public sealed class OrganizationModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICompanyProfiles, CompanyProfiles>();
        services.AddScoped<ISystemSettings, SystemSettingsStore>();
        services.AddScoped<IPdfBranding, PdfBranding>();
        services.AddScoped<IValueLists, ValueListStore>();
        services.AddScoped<IDataInitializer, ValueListSeed>();
        services.AddScoped<IDataInitializer, FormSchemaSeed>();
        services.AddScoped<ISyncEntitySource, ValueListItemSource>();
        services.AddScoped<ISyncEntitySource, FormSchemaSource>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        CompanyEndpoints.Map(api);
        CatalogEndpoints.Map(api);
        SystemSettingsEndpoints.Map(api);
        CompanyLogoEndpoints.Map(api);
        ValueListEndpoints.Map(api);
        FormSchemaEndpoints.Map(api);
    }
}
