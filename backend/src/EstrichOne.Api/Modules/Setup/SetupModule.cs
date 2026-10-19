using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Setup.Exports;

namespace EstrichOne.Api.Modules.Setup;

/// <summary>Ersteinrichtung einer frischen Installation (docs/reengineering/17, Abschnitt 2).</summary>
public sealed class SetupModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<SetupService>();
        services.AddScoped<ISetupState>(provider => provider.GetRequiredService<SetupService>());
        services.AddHostedService<SetupCodeAnnouncer>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => SetupEndpoints.Map(api);
}
