using System.Threading.RateLimiting;
using EstrichOne.Api.Infrastructure.Modules;
using Microsoft.AspNetCore.RateLimiting;

namespace EstrichOne.Api.Modules.Devices;

/// <summary>Geräteidentität und IT Center (docs/reengineering/11, 13).</summary>
public sealed class DevicesModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DeviceSettings>().Bind(configuration.GetSection(DeviceSettings.SectionName));
        services.AddScoped<DeviceActions>();
        var perMinute = configuration.GetSection(DeviceSettings.SectionName).GetValue(nameof(DeviceSettings.RegistrationsPerMinutePerAddress), 10);
        services.Configure<RateLimiterOptions>(limiter => limiter.AddPolicy(DeviceSettings.RateLimitPolicy, http =>
            RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unbekannt",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => DeviceEndpoints.Map(api);
}
