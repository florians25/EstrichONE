using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Identity;

/// <summary>Einmalpasswort der Administration: Bis zum Wechsel sperrt der Server alles außer Wechsel, Abmelden, Status und 2FA.</summary>
public sealed class PasswordChangeGateTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Theory]
    [InlineData("web")]
    [InlineData("ios")]
    [InlineData("android")]
    public async Task MustChangePassword_BlocksEverythingExceptChangeLogoutStatusAndTwoFactor(string clientKind)
    {
        var user = $"wechsel.{clientKind}";
        await _factory.CreateUserAsync(user, mustChangePassword: true, roleIds: [StandardRoles.OfficeId]);
        using var client = _factory.CreateClient();
        var tokens = await client.LoginOkAsync(user, clientKind: clientKind);
        tokens.MustChangePassword.ShouldBeTrue();
        client.WithToken(tokens.AccessToken);

        foreach (var path in new[] { "/api/v1/access/me/permissions", "/api/v1/bauakten", "/api/v1/directory/users", "/api/v1/auth/sessions" })
        {
            using var blocked = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
            blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden, path);
            (await blocked.ReadProblemAsync()).Code.ShouldBe("PASSWORD_CHANGE_REQUIRED", path);
        }

        using var devices = await client.PostAsJsonAsync("/api/v1/devices/register", new { name = "x", platform = "ios", kind = "personal", publicKey = "AAAA" }, Ct);
        (await devices.ReadProblemAsync()).Code.ShouldBe("PASSWORD_CHANGE_REQUIRED");

        // Erlaubt: Anmeldestatus, Zwei-Faktor-Status und -Einrichtung, öffentliche Endpunkte.
        (await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync(new Uri("/api/v1/auth/two-factor", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var setup = await client.PostAsJsonAsync("/api/v1/auth/two-factor/setup", new { currentPassword = AuthClient.DefaultPassword }, Ct);
        setup.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync(new Uri("/api/v1/server-info", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Passwortwechsel ist auch vor der Gerätefreigabe möglich; danach gilt dieselbe Sitzung sofort voll.
        using var changed = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = AuthClient.DefaultPassword, newPassword = "Eigene Passphrase 2026" }, Ct);
        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var after = await client.GetAsync(new Uri("/api/v1/auth/sessions", UriKind.Relative), Ct);
        if (clientKind == "web")
        {
            after.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        else
        {
            // Apps: als Nächstes greift die Gerätefreigabe, nicht mehr die Passwortpflicht.
            (await after.ReadProblemAsync()).Code.ShouldBe("DEVICE_NOT_APPROVED");
        }

        (await client.LoginOkAsync(user, "Eigene Passphrase 2026", clientKind)).MustChangePassword.ShouldBeFalse();
    }

    [Fact]
    public async Task MustChangePassword_AllowsLogout()
    {
        await _factory.CreateUserAsync("wechsel.abmelden", mustChangePassword: true);
        using var client = _factory.CreateClient();
        var tokens = await client.LoginOkAsync("wechsel.abmelden");
        client.WithToken(tokens.AccessToken);

        (await client.PostAsync(new Uri("/api/v1/auth/logout", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WithoutOneTimePassword_NothingIsBlocked()
    {
        await _factory.CreateUserAsync("normal", roleIds: [StandardRoles.OfficeId]);
        using var client = _factory.CreateClient();
        client.WithToken((await client.LoginOkAsync("normal")).AccessToken);
        (await client.GetAsync(new Uri("/api/v1/access/me/permissions", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
