using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Contacts;

/// <summary>Migration „CustomerNumbering“: Kunden ohne Nummer erhalten Nummern ab 600001 in Anlagereihenfolge, vorhandene bleiben.</summary>
public sealed class CustomerNumberMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ExistingCustomersWithoutNumber_GetNumbersInCreationOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var target = all.Single(m => m.EndsWith("_CustomerNumbering", StringComparison.Ordinal));
        await migrator.MigrateAsync(all[all.IndexOf(target) - 1], ct);

        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO contacts (id, kind, name, customer_number, version, created_at, updated_at, phones, emails)
            VALUES ('01900000-0000-7000-8000-000000000001', 'Person', 'Zweiter', NULL, 1, '2024-02-01', '2024-02-01', '[]', '[]'),
                   ('01900000-0000-7000-8000-000000000002', 'Person', 'Erster', NULL, 1, '2024-01-01', '2024-01-01', '[]', '[]'),
                   ('01900000-0000-7000-8000-000000000003', 'Company', 'Alt mit Nummer', '600007', 1, '2023-01-01', '2023-01-01', '[]', '[]'),
                   ('01900000-0000-7000-8000-000000000004', 'Company', 'Fremdes Schema', '4711', 1, '2023-06-01', '2023-06-01', '[]', '[]');
            """, ct);

        await migrator.MigrateAsync(target, ct);

        var numbers = await db.Database.SqlQueryRaw<string>("SELECT name || '=' || customer_number AS \"Value\" FROM contacts ORDER BY name").ToListAsync(ct);
        numbers.ShouldBe(["Alt mit Nummer=600007", "Erster=600008", "Fremdes Schema=4711", "Zweiter=600009"]);
        var last = await db.Database.SqlQueryRaw<long>("SELECT last_number AS \"Value\" FROM customer_number_sequences WHERE id = 1").SingleAsync(ct);
        last.ShouldBe(9);
    }
}
