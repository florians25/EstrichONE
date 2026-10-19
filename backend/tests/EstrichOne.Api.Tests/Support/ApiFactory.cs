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
    }

    public static async Task<ApiFactory> CreateMigratedAsync(PostgresFixture postgres,
        IReadOnlyDictionary<string, string>? settings = null)
    {
        var factory = new ApiFactory(await postgres.CreateDatabaseAsync(), settings);
        await factory.MigrateAsync();
        return factory;
    }

    public async Task MigrateAsync()
    {
        var logger = Services.GetRequiredService<ILoggerFactory>().CreateLogger("Tests");
        await CommandLine.MigrateAsync(Services, logger, CancellationToken.None);
    }
}
