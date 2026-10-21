using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Access;

public sealed class UserAdministrationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _admin = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres);
        _admin = await _factory.CreateClientAsAsync("chef", StandardRoles.AdministratorId);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<UserWithPassword> CreateAsync(string userName, params Guid[] roles)
    {
        using var response = await _admin.PostAsJsonAsync("/api/v1/users",
            new { displayName = "Lukas Brandl", userName, email = (string?)null, phone = "0871 123", roleIds = roles }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<UserWithPassword>(TestJson.Options, Ct))!;
    }

    [Fact]
    public async Task CustomRole_CanBeCreatedChangedAndDeleted_AdminRoleKeepsAllRights()
    {
        var id = Guid.CreateVersion7();
        string[] planning = ["planning.read", "planning.manage"];
        string[] unknownRight = ["gibt.es.nicht"];
        string[] readOnly = ["planning.read"];
        using (var created = await _admin.PutAsJsonAsync($"/api/v1/access/roles/{id}",
                   new { name = "Disposition", description = "Planung ohne Stammdaten", permissions = planning }, Ct))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.OK, await created.Content.ReadAsStringAsync(Ct));
        }

        using (var unknown = await _admin.PutAsJsonAsync($"/api/v1/access/roles/{id}", new { name = "Disposition", permissions = unknownRight }, Ct))
        {
            unknown.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        using (var taken = await _admin.PutAsJsonAsync($"/api/v1/access/roles/{Guid.CreateVersion7()}", new { name = "disposition", permissions = Array.Empty<string>() }, Ct))
        {
            taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        var user = await CreateAsync("d.planer", id);
        using (var changed = await _admin.PutAsJsonAsync($"/api/v1/access/roles/{id}", new { name = "Disposition", permissions = readOnly }, Ct))
        {
            (await changed.Content.ReadFromJsonAsync<RoleResponse>(TestJson.Options, Ct))!.Permissions.ShouldBe(["planning.read"]);
        }

        using (var admin = await _admin.PutAsJsonAsync($"/api/v1/access/roles/{StandardRoles.AdministratorId}", new { name = "Administrator", permissions = readOnly }, Ct))
        {
            (await admin.Content.ReadFromJsonAsync<RoleResponse>(TestJson.Options, Ct))!.Permissions.Count.ShouldBe(Permissions.All.Count);
        }

        (await _admin.DeleteAsync(new Uri($"/api/v1/access/roles/{StandardRoles.OfficeId}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await _admin.DeleteAsync(new Uri($"/api/v1/access/roles/{id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var users = await _admin.GetFromJsonAsync<List<UserListItem>>("/api/v1/users", TestJson.Options, Ct);
        users!.Single(u => u.Id == user.User.Id).RoleIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreatedUser_SignsInWithOneTimePassword_AndMustChangeIt()
    {
        var created = await CreateAsync("l.brandl", StandardRoles.SiteWorkerId);
        created.User.RoleIds.ShouldBe([StandardRoles.SiteWorkerId]);
        created.User.Phone.ShouldBe("0871 123");
        created.OneTimePassword.Length.ShouldBe(15);

        using var client = _factory.CreateClient();
        var tokens = await client.LoginOkAsync("l.brandl", created.OneTimePassword);
        tokens.MustChangePassword.ShouldBeTrue();
    }

    [Fact]
    public async Task AccessLetter_IsAPdf_OnlyWithTheCurrentOneTimePassword()
    {
        var created = await CreateAsync("k.letter", StandardRoles.SiteWorkerId);
        created.LetterEmailed.ShouldBeFalse();
        using var letter = await _admin.PostAsJsonAsync($"/api/v1/users/{created.User.Id}/access-letter", new { password = created.OneTimePassword }, Ct);
        letter.StatusCode.ShouldBe(HttpStatusCode.OK);
        letter.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        (await letter.Content.ReadAsByteArrayAsync(Ct))[..4].ShouldBe("%PDF"u8.ToArray());

        using var wrong = await _admin.PostAsJsonAsync($"/api/v1/users/{created.User.Id}/access-letter", new { password = "falsch-falsch" }, Ct);
        wrong.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task DuplicateUserName_IsRejected_UnknownRoleCreatesNothing()
    {
        await CreateAsync("doppelt", StandardRoles.SiteWorkerId);
        using var duplicate = await _admin.PostAsJsonAsync("/api/v1/users",
            new { displayName = "X", userName = "DOPPELT", roleIds = Array.Empty<Guid>() }, Ct);
        (await duplicate.ReadProblemAsync()).Code.ShouldBe("USER_EXISTS");

        using var unknownRole = await _admin.PostAsJsonAsync("/api/v1/users",
            new { displayName = "Y", userName = "neu", roleIds = new[] { Guid.NewGuid() } }, Ct);
        (await unknownRole.ReadProblemAsync()).Code.ShouldBe("UNKNOWN_ROLE");
        var users = await _admin.GetFromJsonAsync<List<UserListItem>>("/api/v1/users", TestJson.Options, Ct);
        users!.ShouldNotContain(u => u.UserName == "neu");
    }

    [Fact]
    public async Task ResetPassword_EndsSessions_AndIssuesNewOneTimePassword()
    {
        var created = await CreateAsync("reset", StandardRoles.OfficeId);
        using var client = _factory.CreateClient();
        var tokens = await client.LoginOkAsync("reset", created.OneTimePassword);
        client.WithToken(tokens.AccessToken);

        using var reset = await _admin.PostAsync(new Uri($"/api/v1/users/{created.User.Id}/reset-password", UriKind.Relative), null, Ct);
        var result = (await reset.Content.ReadFromJsonAsync<UserWithPassword>(TestJson.Options, Ct))!;

        result.OneTimePassword.ShouldNotBe(created.OneTimePassword);
        (await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var oldPassword = await _factory.CreateClient().LoginAsync("reset", created.OneTimePassword);
        oldPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _factory.CreateClient().LoginOkAsync("reset", result.OneTimePassword)).MustChangePassword.ShouldBeTrue();
    }

    [Fact]
    public async Task Deactivate_EndsAccessImmediately_ActivateRestoresLogin()
    {
        var created = await CreateAsync("sperre", StandardRoles.OfficeId);
        using var client = _factory.CreateClient();
        client.WithToken((await client.LoginOkAsync("sperre", created.OneTimePassword)).AccessToken);

        (await _admin.PostAsync(new Uri($"/api/v1/users/{created.User.Id}/deactivate", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();
        (await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await _factory.CreateClient().LoginAsync("sperre", created.OneTimePassword)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var activated = await _admin.PostAsync(new Uri($"/api/v1/users/{created.User.Id}/activate", UriKind.Relative), null, Ct);
        (await activated.Content.ReadFromJsonAsync<UserListItem>(TestJson.Options, Ct))!.IsActive.ShouldBeTrue();
        (await _factory.CreateClient().LoginAsync("sperre", created.OneTimePassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LastAdministrator_AndOwnAccount_CannotBeDeactivated()
    {
        var users = await _admin.GetFromJsonAsync<List<UserListItem>>("/api/v1/users", TestJson.Options, Ct);
        var me = users!.Single(u => u.UserName == "chef");

        using var self = await _admin.PostAsync(new Uri($"/api/v1/users/{me.Id}/deactivate", UriKind.Relative), null, Ct);
        (await self.ReadProblemAsync()).Code.ShouldBe("CANNOT_DEACTIVATE_SELF");

        var second = await CreateAsync("zweiter-admin", StandardRoles.AdministratorId);
        using var secondClient = _factory.CreateClient();
        var temp = await secondClient.LoginOkAsync("zweiter-admin", second.OneTimePassword);
        secondClient.WithToken(temp.AccessToken);
        (await _admin.PostAsync(new Uri($"/api/v1/users/{second.User.Id}/deactivate", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();
        // „chef“ ist jetzt der einzige aktive Administrator.
        var third = await CreateAsync("dritter", StandardRoles.ItAdministratorId);
        third.User.IsActive.ShouldBeTrue();
    }

    [Fact]
    public async Task Update_ChangesNameAndContact()
    {
        var created = await CreateAsync("profil", StandardRoles.SiteWorkerId);

        using var response = await _admin.PutAsJsonAsync($"/api/v1/users/{created.User.Id}",
            new { displayName = "Lukas B.", email = "lukas@example.org", phone = (string?)null }, Ct);

        var updated = (await response.Content.ReadFromJsonAsync<UserListItem>(TestJson.Options, Ct))!;
        updated.DisplayName.ShouldBe("Lukas B.");
        updated.Email.ShouldBe("lukas@example.org");
        updated.Phone.ShouldBeNull();
    }

    [Fact]
    public async Task UserAdministration_RequiresPermission()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);

        (await office.GetAsync(new Uri("/api/v1/users", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
