using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EstrichOne.Api.Tests.Access;

public sealed class StandardRolesTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Migrate_CreatesStandardRoles_AndAdministratorHasEveryPermission()
    {
        await CommandLine.MigrateAsync(_factory.Services, NullLogger.Instance, CancellationToken.None);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var roles = await db.Set<Role>().Include(r => r.Permissions).ToListAsync(TestContext.Current.CancellationToken);

        roles.Count.ShouldBe(4);
        roles.Single(r => r.Id == StandardRoles.AdministratorId).Permissions.Select(p => p.Permission)
            .ShouldBe(Permissions.All.Select(p => p.Code), ignoreOrder: true);
        roles.ShouldAllBe(r => r.Permissions.All(p => Permissions.Exists(p.Permission)));
    }

    [Fact]
    public async Task MyPermissions_ReflectAssignedRole()
    {
        using var client = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);

        var permissions = await client.GetFromJsonAsync<List<string>>("/api/v1/access/me/permissions", TestContext.Current.CancellationToken);

        permissions.ShouldNotBeNull();
        permissions.ShouldContain(Permissions.BauaktenCreate);
        permissions.ShouldNotContain(Permissions.BauaktenReadAll);
        permissions.ShouldNotContain(Permissions.UsersManage);
    }

    [Fact]
    public async Task SetRoles_CannotRemoveLastAdministrator()
    {
        var admin = await _factory.CreateUserAsync("admin", roleIds: StandardRoles.AdministratorId);
        using var client = _factory.CreateClient().WithToken((await _factory.CreateClient().LoginOkAsync("admin")).AccessToken);

        using var response = await client.PutAsJsonAsync($"/api/v1/access/users/{admin.Id}/roles",
            new { roleIds = new[] { StandardRoles.OfficeId } }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Code.ShouldBe("LAST_ADMINISTRATOR");
    }

    [Fact]
    public async Task SetRoles_WithoutPermission_IsForbidden()
    {
        var other = await _factory.CreateUserAsync("kollege");
        using var client = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);

        using var response = await client.PutAsJsonAsync($"/api/v1/access/users/{other.Id}/roles",
            new { roleIds = new[] { StandardRoles.AdministratorId } }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync()).Code.ShouldBe("PERMISSION_DENIED");
    }
}
