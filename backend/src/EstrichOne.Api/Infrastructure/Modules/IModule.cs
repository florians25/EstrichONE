namespace EstrichOne.Api.Infrastructure.Modules;

/// <summary>
/// Ein Fachmodul registriert seine Dienste und Endpunkte. Module werden in <see cref="ModuleCatalog"/>
/// ausdrücklich aufgeführt – keine Reflexion, damit jederzeit sichtbar ist, was läuft.
/// </summary>
public interface IModule
{
    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <param name="api">Routengruppe <c>/api/v1</c>.</param>
    void MapEndpoints(IEndpointRouteBuilder api);
}
