using EstrichOne.Api.Infrastructure.Modules;

namespace EstrichOne.Api.Modules.Devices;

/// <summary>Geräteidentität und IT Center (docs/reengineering/11, 13).</summary>
public sealed class DevicesModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DeviceSettings>().Bind(configuration.GetSection(DeviceSettings.SectionName));
        services.AddScoped<DeviceActions>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => DeviceEndpoints.Map(api);
}
