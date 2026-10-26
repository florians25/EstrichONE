using EstrichOne.Api.Infrastructure.Modules;

namespace EstrichOne.Api.Modules.Archive;

/// <summary>
/// Revisionssichere Archivierung auf einem gekoppelten EstrichONE-Archivserver (eigener Rechner oder Compose-Profil
/// <c>archive</c>): Regeln je Dokumentart, automatische Archivierung, Wiederholung, Nachweis, Prüfprotokoll, Export.
/// </summary>
public sealed class ArchiveModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ArchiveService>();
        services.AddSingleton<ArchiveSignal>();
        services.AddScoped<DossierService>();
        services.AddScoped<MonitoringService>();
        services.AddTransient<IStartupFilter, DossierLockStartupFilter>();
        // EstrichOne:Archive:Worker=false schaltet den Hintergrunddienst ab (Tests steuern den Ablauf selbst).
        if (!string.Equals(configuration["EstrichOne:Archive:Worker"], "false", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHostedService<ArchiveWorker>();
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        ArchiveEndpoints.Map(api);
        DossierEndpoints.Map(api);
        MonitoringEndpoints.Map(api);
    }
}
