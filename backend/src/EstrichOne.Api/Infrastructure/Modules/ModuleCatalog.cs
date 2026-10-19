using EstrichOne.Api.Modules.Platform;

namespace EstrichOne.Api.Infrastructure.Modules;

public static class ModuleCatalog
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new PlatformModule(),
    ];

    public static IServiceCollection AddModules(this IServiceCollection services, IConfiguration configuration)
    {
        foreach (var module in All)
        {
            module.AddServices(services, configuration);
        }

        return services;
    }

    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1");
        foreach (var module in All)
        {
            module.MapEndpoints(api);
        }

        return app;
    }
}
