using EstrichOne.Api.Infrastructure.Modules;

namespace EstrichOne.Api.Modules.Calls;

/// <summary>Anrufe und Anfragen im Büro, Umwandlung von Anfragen in Bauakten.</summary>
public sealed class CallsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => CallEndpoints.Map(api);
}
