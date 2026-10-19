using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Bauakten.Exports;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>Bauakten, Bereiche, Räume, Estrichaufbau, Notizen, Nummernkreis.</summary>
public sealed class BauaktenModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<BauakteNumberGenerator>();
        services.AddScoped<IBauakteAccess, BauakteAccess>();
        services.AddScoped<IDataInitializer, ScreedTypeSeed>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => BauakteEndpoints.Map(api);
}
