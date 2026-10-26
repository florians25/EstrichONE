using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Documentation.Exports;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>Kolonnen, Geräte, Abwesenheiten, Einsätze, Terminkalender, „Wer fährt wohin“, Personalstamm (docs/reengineering/02: Planung).</summary>
public sealed class PlanningModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<PlanningService>();
        services.AddScoped<CalendarService>();
        services.AddScoped<HrService>();
        services.AddScoped<DispatchService>();
        services.AddScoped<IFileReadGrant, EmployeeDocumentGrant>();
        services.AddScoped<IFileReadGrant, ResourceFileGrant>();
        services.AddScoped<IDataInitializer, CalendarSeed>();
        services.AddScoped<IBauakteHintSource, PlanningHints>();
        services.AddScoped<IBauakteVisibilitySource, PlanningVisibility>();
        services.AddScoped<IBauakteScheduleSource, PlanningSchedule>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        PlanningEndpoints.Map(api);
        CalendarEndpoints.Map(api);
        DispatchEndpoints.Map(api);
        StaffEndpoints.Map(api);
        HrEndpoints.Map(api);
    }
}
