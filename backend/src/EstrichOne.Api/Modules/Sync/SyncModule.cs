using EstrichOne.Api.Infrastructure.Modules;

namespace EstrichOne.Api.Modules.Sync;

/// <summary>Offline-Synchronisation (docs/sync-protokoll.md). Fachlogik liegt in den Handlern der Module.</summary>
public sealed class SyncModule : IModule
{
    /// <summary>Tabellen mit Änderungsfeed: Tabelle → (Entitätstyp, Spalte mit der Bauakte für die Sichtbarkeit).</summary>
    public static IReadOnlyList<(string Table, string EntityType, string? ScopeColumn)> TrackedTables { get; } =
    [
        ("bauakten", "bauakte", "id"),
        ("notes", "note", "bauakte_id"),
        ("files", "file", "bauakte_id"),
        ("work_items", "workItem", "bauakte_id"),
        ("contacts", "contact", null),
        ("screed_types", "screedType", null),
    ];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<SyncService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api) => SyncEndpoints.Map(api);
}
