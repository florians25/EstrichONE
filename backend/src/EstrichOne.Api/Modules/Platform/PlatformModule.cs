using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EstrichOne.Api.Modules.Platform;

public sealed class PlatformModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<SystemStateInitializer>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        api.MapGet("/server-info", GetServerInfoAsync)
            .WithName("GetServerInfo")
            .WithTags("Plattform")
            .WithSummary("Öffentliche Serverbeschreibung für die Einrichtung der Apps")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .AllowAnonymous();
    }

    private static async Task<Results<Ok<ServerInfo>, ProblemHttpResult>> GetServerInfoAsync(
        EstrichOneDbContext db,
        IOptions<EstrichOneOptions> options,
        CancellationToken cancellationToken)
    {
        var state = await TryReadStateAsync(db, cancellationToken);
        if (state is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Server nicht eingerichtet",
                detail: "Die Datenbank wurde noch nicht initialisiert. Bitte den Befehl 'migrate' ausführen.",
                extensions: new Dictionary<string, object?> { ["code"] = "SERVER_NOT_INITIALIZED" });
        }

        var settings = options.Value;
        return TypedResults.Ok(new ServerInfo(
            Product: ServerInfo.ProductName,
            ServerName: settings.ServerName,
            ServerVersion: ApplicationVersion.Current,
            ApiVersions: ["v1"],
            MinClientVersion: settings.MinClientVersion,
            // Microsoft Entra ID kommt als optionales Verfahren hinzu, wenn es konfiguriert ist (docs/reengineering/12).
            AuthMethods: ["password"],
            InstanceEpoch: state.InstanceEpoch));
    }

    /// <summary>Liefert <c>null</c>, solange die Datenbank nicht migriert ist (Tabelle fehlt oder ist leer).</summary>
    private static async Task<SystemState?> TryReadStateAsync(EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            return await db.Set<SystemState>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return null;
        }
    }
}
