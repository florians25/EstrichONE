using EstrichOne.Api.Infrastructure.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EstrichOne.Api.Tests.Support;

/// <summary>Startet die API im Speicher gegen eine eigene Testdatenbank.</summary>
public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", connectionString);
        builder.UseSetting("EstrichOne:ServerName", "Estrichbau Test GmbH");
        builder.UseSetting("Serilog:MinimumLevel:Default", "Warning");
    }

    public static async Task<ApiFactory> CreateMigratedAsync(PostgresFixture postgres)
    {
        var factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        await factory.MigrateAsync();
        return factory;
    }

    public async Task MigrateAsync()
    {
        var logger = Services.GetRequiredService<ILoggerFactory>().CreateLogger("Tests");
        await CommandLine.MigrateAsync(Services, logger, CancellationToken.None);
    }
}
