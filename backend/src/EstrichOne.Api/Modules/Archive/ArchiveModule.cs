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
        services.AddScoped<Backup.Exports.IUpdatablePeerSource, ArchiveUpdatablePeers>();
        services.AddScoped<Documentation.Exports.IFileArchiveState, FileArchiveState>();
        services.AddSingleton<ArchiveSignal>();
        services.AddScoped<DossierService>();
        services.AddScoped<MonitoringService>();
        services.AddSingleton<SystemHealthMonitor>();
        if (!string.Equals(configuration["EstrichOne:Health:Worker"], "false", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHostedService<SystemHealthWorker>();
        }

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
        SystemHealthEndpoints.Map(api);
    }
}

/// <summary>Archivserver für die Fernaktualisierung (Auftrag und Anzeige im Backup-Modul).</summary>
internal sealed class ArchiveUpdatablePeers(ArchiveService archive) : Backup.Exports.IUpdatablePeerSource
{
    public Task<IReadOnlyList<Backup.Exports.UpdatablePeer>> ListAsync(CancellationToken cancellationToken) => archive.UpdatablePeersAsync(cancellationToken);
}
