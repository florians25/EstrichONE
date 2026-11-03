using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Tests.Support;

#pragma warning disable CA1861 // Anfragekörper im Test

namespace EstrichOne.Api.Tests.Access;

/// <summary>
/// Wer Benutzer verwaltet (z. B. IT-Administration), darf sich nicht selbst zum Administrator machen und keine Konten mit
/// höheren Rechten übernehmen (Passwort zurücksetzen, E-Mail ändern, sperren, Rollen ändern).
/// </summary>
public sealed class PrivilegeEscalationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static async Task Denied(HttpResponseMessage response)
    {
        using (response)
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync(Ct));
            (await response.ReadProblemAsync()).Code.ShouldBe("PRIVILEGE_ESCALATION");
        }
    }

    [Fact]
    public async Task UserManager_CannotEscalate_ButManagesLowerAccounts()
    {
        var chefin = await _factory.CreateUserAsync("chefin", roleIds: StandardRoles.AdministratorId);
        var it = await _factory.CreateUserAsync("it", roleIds: StandardRoles.ItAdministratorId);
        var worker = await _factory.CreateUserAsync("baustelle", roleIds: StandardRoles.SiteWorkerId);
        using var client = _factory.CreateClient();
        client.WithToken((await client.LoginOkAsync("it")).AccessToken);

        await Denied(await client.PutAsJsonAsync($"/api/v1/access/users/{it.Id}/roles", new { roleIds = new[] { StandardRoles.AdministratorId } }, Ct));
        await Denied(await client.PutAsJsonAsync($"/api/v1/access/users/{chefin.Id}/roles", new { roleIds = new[] { StandardRoles.SiteWorkerId } }, Ct));
        await Denied(await client.PostAsync(new Uri($"/api/v1/users/{chefin.Id}/reset-password", UriKind.Relative), null, Ct));
        await Denied(await client.PostAsync(new Uri($"/api/v1/users/{chefin.Id}/deactivate", UriKind.Relative), null, Ct));
        await Denied(await client.PutAsJsonAsync($"/api/v1/users/{chefin.Id}", new { displayName = "X", email = "angreifer@example.org" }, Ct));
        await Denied(await client.PostAsJsonAsync("/api/v1/users", new { displayName = "Neu", userName = "neu.admin", roleIds = new[] { StandardRoles.AdministratorId } }, Ct));

        // Keine Selbstbeförderung – auch nicht in eine Fachrolle mit Rechten, die man nicht hat.
        await Denied(await client.PutAsJsonAsync($"/api/v1/access/users/{it.Id}/roles", new { roleIds = new[] { StandardRoles.ItAdministratorId, StandardRoles.OfficeId } }, Ct));

        // Konten mit weniger Rechten verwaltet die IT-Administration weiterhin (auch Büro- und Baustellenrollen vergeben).
        (await client.PostAsync(new Uri($"/api/v1/users/{worker.Id}/reset-password", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostAsJsonAsync("/api/v1/users", new { displayName = "Neu", userName = "neu.baustelle", roleIds = new[] { StandardRoles.SiteWorkerId } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await client.PutAsJsonAsync($"/api/v1/access/users/{worker.Id}/roles", new { roleIds = new[] { StandardRoles.OfficeId } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.PutAsJsonAsync($"/api/v1/users/{worker.Id}", new { displayName = "Max Huber", email = "max@example.org" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        // Die Administratorin darf alles.
        using var admin = _factory.CreateClient();
        admin.WithToken((await admin.LoginOkAsync("chefin")).AccessToken);
        (await admin.PostAsync(new Uri($"/api/v1/users/{it.Id}/reset-password", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RoleManager_CannotGrantPermissionsHeDoesNotHave()
    {
        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        var roleId = Guid.CreateVersion7();
        (await admin.PutAsJsonAsync($"/api/v1/access/roles/{roleId}", new { name = "Rollenpflege", permissions = new[] { "roles.manage", "users.manage" } }, Ct))
            .EnsureSuccessStatusCode();
        using var manager = await _factory.CreateClientAsAsync("rollen", roleId);

        await Denied(await manager.PutAsJsonAsync($"/api/v1/access/roles/{roleId}", new { name = "Rollenpflege", permissions = new[] { "roles.manage", "users.manage", "settings.manage" } }, Ct));
        await Denied(await manager.PutAsJsonAsync($"/api/v1/access/roles/{Guid.CreateVersion7()}", new { name = "Hintertür", permissions = new[] { "backup.manage" } }, Ct));
        (await manager.PutAsJsonAsync($"/api/v1/access/roles/{Guid.CreateVersion7()}", new { name = "Nur Benutzer", permissions = new[] { "users.manage" } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
