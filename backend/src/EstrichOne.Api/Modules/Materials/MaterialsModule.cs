using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Materials.Exports;

namespace EstrichOne.Api.Modules.Materials;

/// <summary>Material, Lieferanten und Bestellungen (AP-20).</summary>
public sealed class MaterialsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IBauakteHintSource, MaterialHints>();
        services.AddScoped<OrderPdf>();
        services.AddScoped<IMaterialDirectory, MaterialDirectory>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        MaterialEndpoints.Map(api);
        OrderEndpoints.Map(api);
        OrderPdfEndpoints.Map(api);
    }
}
