using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Bauakten.Exports;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>Kolonnen, Geräte, Abwesenheiten, Einsätze (docs/reengineering/02: Planung).</summary>
public sealed class PlanningModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<PlanningService>();
        services.AddScoped<IBauakteHintSource, PlanningHints>();
        services.AddScoped<IBauakteVisibilitySource, PlanningVisibility>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => PlanningEndpoints.Map(api);
}
