using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Setup.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace EstrichOne.Api.Modules.Platform;

public sealed class PlatformModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IDataInitializer, SystemStateInitializer>();
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        api.MapGet("/server-info", GetServerInfoAsync)
            .WithName("GetServerInfo")
            .WithTags("Plattform")
            .WithSummary("Öffentliche Serverbeschreibung für die Einrichtung der Apps")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .AllowAnonymous();

        api.MapGet("/system/address", GetAddressCheck)
            .WithName("GetAddressCheck")
            .WithTags("Plattform")
            .WithSummary("Vergleicht die eingestellte öffentliche Adresse (ESTRICHONE_DOMAIN/PUBLIC_URL) mit der tatsächlich aufgerufenen")
            .RequirePermission(Permissions.SettingsManage);
    }

    /// <summary>
    /// Hinweis für Administratoren, wenn die Suite unter einer anderen Adresse aufgerufen wird als eingestellt
    /// (z. B. Installer mit estrichone.firma.de, Nginx Proxy Manager liefert app.firma.de aus). Die Anmeldung
    /// funktioniert trotzdem (Cookies sind host-only); Links in E-Mails, QR-Codes und Pushnachrichten zeigen aber
    /// auf die eingestellte Adresse.
    /// </summary>
    private static Ok<AddressCheck> GetAddressCheck(HttpContext http, IOptions<EstrichOneOptions> options)
    {
        var configured = options.Value.PublicUrl;
        var request = http.Request;
        var actual = $"{request.Scheme}://{request.Host}";
        var configuredHost = configured is null ? null : configured.IsDefaultPort ? configured.Host : $"{configured.Host}:{configured.Port}";
        var matches = configuredHost is null
            || PublicAddress.IsLoopback(configured!.Host)
            || string.Equals(configuredHost, request.Host.Value, StringComparison.OrdinalIgnoreCase)
            || string.Equals(configured.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase);
        return TypedResults.Ok(new AddressCheck(configured?.ToString().TrimEnd('/'), actual, matches, request.IsHttps));
    }

    private static async Task<Results<Ok<ServerInfo>, ProblemHttpResult>> GetServerInfoAsync(
        EstrichOneDbContext db,
        IOptions<EstrichOneOptions> options,
        ISetupState setup,
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
            InstanceEpoch: state.InstanceEpoch,
            SetupRequired: await setup.IsSetupRequiredAsync(cancellationToken)));
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
