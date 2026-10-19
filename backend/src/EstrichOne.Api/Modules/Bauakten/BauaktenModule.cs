using EstrichOne.Api.Infrastructure.Modules;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>Bauakten, Bereiche, Räume, Estrichaufbau, Notizen, Nummernkreis.</summary>
public sealed class BauaktenModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<BauakteNumberGenerator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
    }
}
