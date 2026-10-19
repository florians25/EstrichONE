using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Infrastructure.Hosting;

/// <summary>
/// Betriebsbefehle neben dem normalen Serverstart:
/// <list type="bullet">
/// <item><c>migrate</c> – Datenbankschema aktualisieren und Erstzustand anlegen (Docker-Dienst <c>migrate</c>).</item>
/// <item><c>healthcheck</c> – prüft <c>/health/live</c> des laufenden Servers (für Docker; das Image hat keine Shell).</item>
/// </list>
/// </summary>
public static partial class CommandLine
{
    public static bool TryGetCommand(string[] args, out string command)
    {
        command = args.Length > 0 && !args[0].StartsWith('-') ? args[0].ToLowerInvariant() : string.Empty;
        return command.Length > 0;
    }

    public static async Task<int> RunAsync(string command, WebApplication app)
    {
        switch (command)
        {
            case "migrate":
                await MigrateAsync(app.Services, app.Logger, app.Lifetime.ApplicationStopping);
                return 0;
            default:
                LogUnknownCommand(app.Logger, command);
                return 2;
        }
    }

    public static async Task MigrateAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        LogPendingMigrations(logger, pending.Count, pending);
        await db.Database.MigrateAsync(cancellationToken);

        foreach (var initializer in scope.ServiceProvider.GetServices<IDataInitializer>().OrderBy(i => i.Order))
        {
            await initializer.InitializeAsync(cancellationToken);
        }

        LogDatabaseUpToDate(logger);
    }

    /// <summary>Läuft ohne Host, damit der Befehl schnell und ohne Datenbankzugriff antwortet.</summary>
    public static async Task<int> HealthCheckAsync()
    {
        var port = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS")?.Split(';')[0] ?? "8080";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}/health/live"));
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unbekannter Befehl {Command}. Erlaubt: migrate, healthcheck")]
    private static partial void LogUnknownCommand(ILogger logger, string command);

    [LoggerMessage(Level = LogLevel.Information, Message = "Ausstehende Migrationen: {Count} {Migrations}")]
    private static partial void LogPendingMigrations(ILogger logger, int count, IReadOnlyList<string> migrations);

    [LoggerMessage(Level = LogLevel.Information, Message = "Datenbank ist auf dem aktuellen Stand")]
    private static partial void LogDatabaseUpToDate(ILogger logger);
}
