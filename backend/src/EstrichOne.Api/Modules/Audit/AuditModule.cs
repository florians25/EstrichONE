using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Audit.Exports;

namespace EstrichOne.Api.Modules.Audit;

/// <summary>Nur anfügbares Audit-Log (docs/reengineering/17).</summary>
public sealed class AuditModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IAuditLog, AuditLog>();
        services.AddScoped<IAuditQueries, AuditQueries>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
    }
}
