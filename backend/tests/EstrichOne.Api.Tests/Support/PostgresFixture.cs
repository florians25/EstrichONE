using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(EstrichOne.Api.Tests.Support.PostgresFixture))]

namespace EstrichOne.Api.Tests.Support;

/// <summary>
/// Eine PostgreSQL-Instanz für den gesamten Testlauf (ADR 0010). Jede Testklasse erhält eine eigene, leere
/// Datenbank, damit Tests sich nicht gegenseitig beeinflussen.
/// Mit <c>ESTRICHONE_TEST_DATABASE</c> (Verbindungszeichenfolge mit Recht CREATEDB) läuft es ohne Docker.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private string _adminConnectionString = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("ESTRICHONE_TEST_DATABASE");
        if (!string.IsNullOrWhiteSpace(external))
        {
            _adminConnectionString = external;
            return;
        }

        _container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        await _container.StartAsync();
        _adminConnectionString = _container.GetConnectionString();
    }

    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_adminConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = name }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
