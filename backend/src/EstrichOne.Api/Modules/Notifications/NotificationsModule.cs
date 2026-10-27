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
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => NotificationEndpoints.Map(api);
}
