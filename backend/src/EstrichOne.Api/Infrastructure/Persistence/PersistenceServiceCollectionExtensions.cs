using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public const string ConnectionStringName = "Default";

    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Die Verbindungszeichenfolge 'ConnectionStrings:{ConnectionStringName}' fehlt " +
                "(Umgebungsvariable ConnectionStrings__Default).");

        services.AddDbContext<EstrichOneDbContext>(options => Configure(options, connectionString));
        return services;
    }

    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention();
}
