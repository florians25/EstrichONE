using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Sync.Exports;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>Bauakten, Bereiche, Räume, Estrichaufbau, Notizen, Nummernkreis.</summary>
public sealed class BauaktenModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<BauakteNumberGenerator>();
        services.AddScoped<IBauakteAccess, BauakteAccess>();
        services.AddScoped<IDataInitializer, ScreedTypeSeed>();
        services.AddScoped<BauakteProjectValidator>();
        services.AddScoped<IBauakteDataFill, BauakteDataFill>();
        services.AddScoped<IBauakteHistory, BauakteHistory>();
        services.AddScoped<BauakteCommands>();
        services.AddScoped<IBauakteHintSource, BauakteOwnHints>();
        services.AddScoped<IBauakteSummaries, BauakteSummaries>();
        services.AddScoped<IBauakteFactory, BauakteFactory>();
        services.AddScoped<IBauakteAutoStatus, BauakteAutoStatus>();
        services.AddSingleton<BauakteAutoStartJob>();
        services.AddHostedService(sp => sp.GetRequiredService<BauakteAutoStartJob>());
        services.AddScoped<IBauakteRooms, BauakteRooms>();
        services.AddScoped<IBauakteReportSource, BauakteReportSource>();
        services.AddScoped<ISyncOperationHandler, BauakteSync>();
        services.AddScoped<ISyncEntitySource, BauakteSource>();
        services.AddScoped<ISyncEntitySource, NoteSource>();
        services.AddScoped<ISyncEntitySource, ScreedTypeSource>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        BauakteEndpoints.Map(api);
        BauakteHistoryEndpoints.Map(api);
        OfficeEndpoints.Map(api);
        BauakteTeamEndpoints.Map(api);
        BauaktePartyEndpoints.Map(api);
    }
}
