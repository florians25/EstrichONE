using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.AppUpdates.Exports;

namespace EstrichOne.Api.Modules.AppUpdates;

/// <summary>
/// Aktualisierung der iOS- und Android-App (docs/app-updates.md): Update-Prüfung, Auslieferung des Android-Pakets,
/// Mindestversion (426 <c>CLIENT_UPDATE_REQUIRED</c>), Neuerungen und Geräteübersicht für die Verwaltung.
/// </summary>
public sealed class AppUpdatesModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AppUpdateOptions>().Bind(configuration.GetSection(AppUpdateOptions.SectionName));
        services.AddSingleton<AppPackageCatalog>();
        services.AddSingleton<ReleaseNotesCatalog>();
        services.AddScoped<AppUpdateService>();
        services.AddScoped<IAppUpdatePolicy>(provider => provider.GetRequiredService<AppUpdateService>());
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => AppUpdateEndpoints.Map(api);
}
