using EstrichOne.Api.Infrastructure.Modules;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>Kolonnen, Geräte, Abwesenheiten, Einsätze (docs/reengineering/02: Planung).</summary>
public sealed class PlanningModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration) => services.AddScoped<PlanningService>();

    public void MapEndpoints(IEndpointRouteBuilder api) => PlanningEndpoints.Map(api);
}
