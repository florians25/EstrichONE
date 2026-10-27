using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Notifications.Exports;

namespace EstrichOne.Api.Modules.Notifications;

/// <summary>Postfach, Pushnachrichten (Web Push, APNs) und Versand durch das Büro.</summary>
public sealed class NotificationsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PushOptions>().Bind(configuration.GetSection(PushOptions.SectionName));
        services.AddSingleton<PushSignal>();
        services.AddScoped<INotifier, Notifier>();
        services.AddScoped<PushKeys>();
        services.AddScoped<PushGateway>();
        services.AddHttpClient(PushGateway.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(20));
        services.AddHostedService<NotificationDispatcher>();
        services.AddHostedService<DeadlineScanner>();

        // Bremse gegen versehentliche Massenversendung bzw. Missbrauch: je Benutzer 20 Nachrichten in 10 Minuten,
        // 30 Geräte-Registrierungen je Minute.
        services.Configure<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>(limiter =>
        {
            limiter.AddPolicy(SendRateLimitPolicy, http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unbekannt",
                _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(10), QueueLimit = 0 }));
            limiter.AddPolicy(DeviceRateLimitPolicy, http => System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? http.Connection.RemoteIpAddress?.ToString() ?? "unbekannt",
                _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
    }

    public const string SendRateLimitPolicy = "notifications-send";
    public const string DeviceRateLimitPolicy = "notifications-devices";

    public void MapEndpoints(IEndpointRouteBuilder api) => NotificationEndpoints.Map(api);
}
