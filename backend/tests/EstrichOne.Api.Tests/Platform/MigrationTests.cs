using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Platform;

public sealed class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrations_OnEmptyDatabase_LeaveNothingPending()
    {
        await using var factory = await ApiFactory.CreateMigratedAsync(postgres);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();

        (await db.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Model_HasNoChangesMissingAMigration()
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();

        db.Database.HasPendingModelChanges().ShouldBeFalse(
            "Das Datenmodell wurde geändert, ohne eine Migration anzulegen: " +
            "dotnet ef migrations add <Name> --project src/EstrichOne.Api --output-dir Infrastructure/Persistence/Migrations");
    }
}
