using EstrichOne.Api.Infrastructure.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace EstrichOne.Api.Tests.Support;

/// <summary>Startet die API im Speicher gegen eine eigene Testdatenbank.</summary>
public sealed class ApiFactory(string connectionString, IReadOnlyDictionary<string, string>? settings = null)
    : WebApplicationFactory<Program>
{
    /// <summary>Steuerbare Uhr für Ablaufzeiten; startet bei der echten Zeit.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    /// <summary>Eigene Dateiablage je Testinstanz.</summary>
    public string FilesRoot { get; } = Path.Combine(Path.GetTempPath(), "estrichone-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock)));
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("EstrichOne:ServerName", "Estrichbau Test GmbH");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
        builder.UseSetting("EstrichOne:Files:RootPath", FilesRoot);
    }

    public static async Task<ApiFactory> CreateMigratedAsync(PostgresFixture postgres,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        var factory = new ApiFactory(await postgres.CreateDatabaseAsync(), settings);
        await factory.MigrateAsync();
        return factory;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await using (var connection = new Npgsql.NpgsqlConnection(connectionString))
        {
            Npgsql.NpgsqlConnection.ClearPool(connection);
        }

        // Datenbank der Testklasse wieder entfernen – sonst wächst der Testlauf auf mehrere GB (Plattenplatz, CI).
        try
        {
            var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
            var database = builder.Database;
            builder.Database = "postgres";
            builder.Pooling = false;
            await using var admin = new Npgsql.NpgsqlConnection(builder.ConnectionString);
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)";
            await command.ExecuteNonQueryAsync();
        }
        catch (Npgsql.NpgsqlException)
        {
            // Aufräumen ist nur ein Zusatz.
        }

        if (Directory.Exists(FilesRoot))
        {
            Directory.Delete(FilesRoot, recursive: true);
        }
    }

    public async Task MigrateAsync()
    {
        var logger = Services.GetRequiredService<ILoggerFactory>().CreateLogger("Tests");
        await CommandLine.MigrateAsync(Services, logger, CancellationToken.None);
    }
}
