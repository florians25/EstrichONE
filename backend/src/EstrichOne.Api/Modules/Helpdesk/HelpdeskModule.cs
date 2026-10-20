using System.Threading.RateLimiting;
using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Helpdesk.Exports;
using EstrichOne.Api.Modules.Sync.Exports;
using Microsoft.AspNetCore.RateLimiting;

namespace EstrichOne.Api.Modules.Helpdesk;

/// <summary>Internes Ticketsystem für IT-Probleme (docs/reengineering/14).</summary>
public sealed class HelpdeskModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<TicketService>();
        services.AddScoped<ITicketAttachments>(provider => provider.GetRequiredService<TicketService>());
        services.AddScoped<ISyncOperationHandler, TicketSync>();
        services.AddHostedService<TicketMaintenance>();
        // Meldung ohne Anmeldung: höchstens 5 je Stunde und Adresse (14, Abschnitt 5).
        services.Configure<RateLimiterOptions>(limiter => limiter.AddPolicy(TicketEndpoints.DeviceRateLimitPolicy, http =>
            RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unbekannt",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromHours(1), QueueLimit = 0 })));
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => TicketEndpoints.Map(api);
}
