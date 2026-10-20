using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Sync.Exports;

namespace EstrichOne.Api.Modules.Work;

/// <summary>Aufgaben und Mängel.</summary>
public sealed class WorkModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISyncOperationHandler, WorkSync>();
        services.AddScoped<Exports.IWorkItemReferences, WorkItemReferences>();
        services.AddScoped<Exports.IWorkReportSource, WorkReportSource>();
        services.AddScoped<IBauakteHintSource, WorkHints>();
        services.AddScoped<ISyncEntitySource, WorkItemSource>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => WorkEndpoints.Map(api);
}
