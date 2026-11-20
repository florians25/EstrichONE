using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Documentation.Exports;
using EstrichOne.Api.Modules.Sync.Exports;

namespace EstrichOne.Api.Modules.Fuel;

/// <summary>
/// Tankbelege (Einstellungen › Module „Tankbelege“): in den Apps fotografieren und erfassen, im Büro prüfen, freigeben,
/// erstatten bzw. buchen, Auswertung je Fahrzeug und CSV für die Buchhaltung. Fahrzeuge kommen aus „Geräte &amp; Fahrzeuge“.
/// </summary>
public sealed class FuelModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<FuelService>();
        services.AddScoped<ISyncOperationHandler, FuelSync>();
        services.AddScoped<IFileReadGrant, FuelReceiptFileGrant>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => FuelEndpoints.Map(api);
}
