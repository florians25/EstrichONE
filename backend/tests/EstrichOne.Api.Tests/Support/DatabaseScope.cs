using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Support;

/// <summary>Migrierte Testdatenbank mit Zugriff auf frische DbContext-Instanzen (je Aufruf ein eigener Scope).</summary>
public sealed class DatabaseScope : IAsyncDisposable
{
    private readonly ApiFactory _factory;

    private DatabaseScope(ApiFactory factory) => _factory = factory;

    public IServiceProvider Services => _factory.Services;

    public static async Task<DatabaseScope> CreateAsync(PostgresFixture postgres) =>
        new(await ApiFactory.CreateMigratedAsync(postgres));

    /// <summary>Führt <paramref name="action"/> mit einem eigenen Scope (eigener DbContext) aus.</summary>
    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public Task WithDbAsync(Func<EstrichOneDbContext, Task> action) =>
        WithScopeAsync(async services =>
        {
            await action(services.GetRequiredService<EstrichOneDbContext>());
            return true;
        });

    public Task<T> WithDbAsync<T>(Func<EstrichOneDbContext, Task<T>> action) =>
        WithScopeAsync(services => action(services.GetRequiredService<EstrichOneDbContext>()));

    public async Task<User> CreateUserAsync(string name = "Max Huber")
    {
        var user = User.Create(Guid.CreateVersion7(), name, $"{name.Replace(' ', '.')}.{Guid.NewGuid():N}"[..40]);
        await WithDbAsync(async db =>
        {
            db.Add(user);
            await db.SaveChangesAsync();
        });
        return user;
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();
}
