using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Bauakten;

public sealed class BauakteNumberGeneratorTests(PostgresFixture postgres) : IAsyncLifetime
{
    private DatabaseScope _database = null!;

    public async ValueTask InitializeAsync() => _database = await DatabaseScope.CreateAsync(postgres);

    public ValueTask DisposeAsync() => _database.DisposeAsync();

    [Theory]
    [InlineData(2026, 1, "2026-001")]
    [InlineData(2026, 42, "2026-042")]
    [InlineData(2026, 1234, "2026-1234")]
    public void Format_PadsToThreeDigits(int year, int number, string expected) =>
        BauakteNumberGenerator.Format(year, number).ShouldBe(expected);

    [Fact]
    public async Task NextAsync_CountsUpWithinTheYear()
    {
        var first = await NextAsync();
        var second = await NextAsync();

        second.ShouldBe(BauakteNumberGenerator.Format(Year(first), Sequence(first) + 1));
    }

    [Fact]
    public async Task NextAsync_UnderParallelLoad_NeverRepeatsAndLeavesNoGaps()
    {
        const int parallel = 50;

        var numbers = await Task.WhenAll(Enumerable.Range(0, parallel).Select(_ => NextAsync()));

        numbers.Distinct().Count().ShouldBe(parallel);
        numbers.Select(Sequence).Order().ShouldBe(Enumerable.Range(1, parallel));
    }

    [Fact]
    public async Task NextAsync_RolledBackTransaction_DoesNotConsumeANumber()
    {
        await _database.WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<EstrichOneDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await services.GetRequiredService<BauakteNumberGenerator>().NextAsync(CancellationToken.None);
            await transaction.RollbackAsync();
            return true;
        });

        var number = await NextAsync();

        Sequence(number).ShouldBe(1);
    }

    private Task<string> NextAsync() =>
        _database.WithScopeAsync(services =>
            services.GetRequiredService<BauakteNumberGenerator>().NextAsync(CancellationToken.None));

    private static int Year(string number) => int.Parse(number[..4], System.Globalization.CultureInfo.InvariantCulture);

    private static int Sequence(string number) => int.Parse(number[5..], System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("{JJJJ}-{NNN}", 2026, 7, "2026-007")]
    [InlineData("BA-{JJ}/{NNNN}", 2026, 42, "BA-26/0042")]
    [InlineData("{JJJJ}{NN}", 2026, 123, "2026123")]
    public void Pattern_FromSettings(string pattern, int year, int number, string expected) =>
        BauakteNumberGenerator.Format(pattern, year, number).ShouldBe(expected);
}
