using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Identity;
using EstrichOne.Api.Modules.Identity.TwoFactor;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Identity;

/// <summary>Zwei-Faktor-Anmeldung (TOTP, RFC 6238) mit Wiederherstellungscodes und Pflicht für Administratoren.</summary>
public sealed class TwoFactorTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
    {
        ["EstrichOne:Authentication:RequestsPerMinutePerAddress"] = "10000",
    });

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Totp_MatchesRfc6238TestVectors(long unixSeconds, string expected)
    {
        var secret = Encoding.ASCII.GetBytes("12345678901234567890");
        Totp.Code(secret, Totp.TimeStep(DateTimeOffset.FromUnixTimeSeconds(unixSeconds))).ShouldBe(expected);
    }

    [Fact]
    public void Base32_RoundTrips_AndUriHasAppParameters()
    {
        var secret = Totp.NewSecret();
        Base32.Decode(Base32.Encode(secret)).ShouldBe(secret);
        Base32.Encode(Encoding.ASCII.GetBytes("foobar")).ShouldBe("MZXW6YTBOI");
        var uri = Totp.OtpAuthUri("EstrichONE Test", "m.huber", secret);
        uri.ShouldStartWith("otpauth://totp/EstrichONE%20Test%3Am.huber?secret=");
        uri.ShouldContain("&issuer=EstrichONE%20Test&algorithm=SHA1&digits=6&period=30");
    }

    private string CodeFor(string secret, int stepOffset = 0) =>
        Totp.Code(Base32.Decode(secret), Totp.TimeStep(_factory.Clock.GetUtcNow()) + stepOffset);

    private async Task<(HttpClient Client, string Secret, List<string> Codes)> EnabledAsync(string userName, params Guid[] roles)
    {
        var client = await _factory.CreateClientAsAsync(userName, roles);
        using var wrongPassword = await client.PostAsJsonAsync("/api/v1/auth/two-factor/setup", new { currentPassword = "Falsches-Passwort-1" }, Ct);
        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var setup = await client.PostAsJsonAsync("/api/v1/auth/two-factor/setup", new { currentPassword = AuthClient.DefaultPassword }, Ct);
        setup.StatusCode.ShouldBe(HttpStatusCode.OK, await setup.Content.ReadAsStringAsync(Ct));
        var started = (await setup.Content.ReadFromJsonAsync<TwoFactorSetupResponse>(TestJson.Options, Ct))!;
        started.OtpAuthUri.ShouldStartWith("otpauth://totp/");
        started.Secret.Length.ShouldBe(32); // 160 Bit

        using var wrong = await client.PostAsJsonAsync("/api/v1/auth/two-factor/enable", new { code = "000000" }, Ct);
        wrong.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var enable = await client.PostAsJsonAsync("/api/v1/auth/two-factor/enable", new { code = CodeFor(started.Secret) }, Ct);
        enable.StatusCode.ShouldBe(HttpStatusCode.OK, await enable.Content.ReadAsStringAsync(Ct));
        var codes = (await enable.Content.ReadFromJsonAsync<TwoFactorRecoveryCodes>(TestJson.Options, Ct))!.RecoveryCodes.ToList();
        codes.Count.ShouldBe(10);
        codes.Distinct().Count().ShouldBe(10);
        return (client, started.Secret, codes);
    }

    private Task<HttpResponseMessage> LoginAsync(string userName, string? code, string client = "web") =>
        _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
            new { login = userName, password = AuthClient.DefaultPassword, client, clientName = "Test", twoFactorCode = code }, Ct);

    [Fact]
    public async Task Login_WithTwoFactor_NeedsCurrentCode_RejectsReplay_AcceptsRecoveryCodeOnce()
    {
        var (client, secret, codes) = await EnabledAsync("m.huber");
        _factory.Clock.Advance(TimeSpan.FromSeconds(31)); // der Einrichtungscode ist verbraucht

        using (var missing = await LoginAsync("m.huber", null))
        {
            missing.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await missing.ReadProblemAsync()).Code.ShouldBe("TWO_FACTOR_REQUIRED");
        }

        using (var wrong = await LoginAsync("m.huber", "123456"))
        {
            (await wrong.ReadProblemAsync()).Code.ShouldBe("TWO_FACTOR_INVALID");
        }

        // Passwort falsch bleibt „falsche Anmeldedaten“ – auch mit richtigem Code.
        using (var badPassword = await _factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login",
                   new { login = "m.huber", password = "Falsches-Passwort-1", client = "web", twoFactorCode = CodeFor(secret) }, Ct))
        {
            (await badPassword.ReadProblemAsync()).Code.ShouldBe("INVALID_CREDENTIALS");
        }

        var code = CodeFor(secret);
        using (var ok = await LoginAsync("m.huber", code))
        {
            ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var replay = await LoginAsync("m.huber", code))
        {
            (await replay.ReadProblemAsync()).Code.ShouldBe("TWO_FACTOR_INVALID");
        }

        using (var recovery = await LoginAsync("m.huber", codes[3].ToUpperInvariant()))
        {
            recovery.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        using (var recoveryAgain = await LoginAsync("m.huber", codes[3]))
        {
            (await recoveryAgain.ReadProblemAsync()).Code.ShouldBe("TWO_FACTOR_INVALID");
        }

        var status = (await client.GetFromJsonAsync<TwoFactorStatus>("/api/v1/auth/two-factor", TestJson.Options, Ct))!;
        status.Enabled.ShouldBeTrue();
        status.RecoveryCodesRemaining.ShouldBe(9);
    }

    [Fact]
    public async Task WrongCodes_CountAsFailures_AndLockTheAccount()
    {
        await EnabledAsync("m.huber");
        for (var i = 0; i < 5; i++)
        {
            using var _ = await LoginAsync("m.huber", "111111");
        }

        using var locked = await LoginAsync("m.huber", null);
        locked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await locked.ReadProblemAsync()).Code.ShouldBe("LOGIN_LOCKED");
    }

    [Fact]
    public async Task ExistingAndNewSessions_KeepRefreshingWithoutCode_AppsKeepWorking()
    {
        await _factory.CreateUserAsync("m.huber");
        // App-Sitzung von vor der Einrichtung (z. B. iOS-App ohne Code-Feld).
        using var app = _factory.CreateClient();
        var before = await app.LoginOkAsync("m.huber", clientKind: "desktop");

        var browser = _factory.CreateClient();
        browser.WithToken((await browser.LoginOkAsync("m.huber")).AccessToken);
        using var setup = await browser.PostAsJsonAsync("/api/v1/auth/two-factor/setup", new { currentPassword = AuthClient.DefaultPassword }, Ct);
        var secret = (await setup.Content.ReadFromJsonAsync<TwoFactorSetupResponse>(TestJson.Options, Ct))!.Secret;
        (await browser.PostAsJsonAsync("/api/v1/auth/two-factor/enable", new { code = CodeFor(secret) }, Ct)).EnsureSuccessStatusCode();

        // Die bestehende Sitzung läuft weiter und erneuert sich ohne Code.
        (await app.WithToken(before.AccessToken).GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        _factory.Clock.Advance(TimeSpan.FromMinutes(11));
        using var refreshed = await app.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = before.RefreshToken }, Ct);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Neue Anmeldung einmal mit Code – danach Erneuerungen ohne Code.
        using var login = await LoginAsync("m.huber", CodeFor(secret), "desktop");
        var tokens = (await login.Content.ReadFromJsonAsync<TokenResponse>(TestJson.Options, Ct))!;
        _factory.Clock.Advance(TimeSpan.FromMinutes(11));
        using var again = await app.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken }, Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Disable_NeedsPasswordAndCode_ThenLoginWithoutCode()
    {
        var (client, secret, _) = await EnabledAsync("m.huber");
        _factory.Clock.Advance(TimeSpan.FromSeconds(31));
        using (var noCode = await client.PostAsJsonAsync("/api/v1/auth/two-factor/disable", new { currentPassword = AuthClient.DefaultPassword, code = "000000" }, Ct))
        {
            noCode.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var noPassword = await client.PostAsJsonAsync("/api/v1/auth/two-factor/disable", new { currentPassword = "Falsches-Passwort-1", code = CodeFor(secret) }, Ct))
        {
            noPassword.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await client.PostAsJsonAsync("/api/v1/auth/two-factor/disable", new { currentPassword = AuthClient.DefaultPassword, code = CodeFor(secret) }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var login = await LoginAsync("m.huber", null);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RecoveryCodes_Renewed_OldOnesInvalid()
    {
        var (client, secret, old) = await EnabledAsync("m.huber");
        _factory.Clock.Advance(TimeSpan.FromSeconds(31));
        using var renew = await client.PostAsJsonAsync("/api/v1/auth/two-factor/recovery-codes", new { currentPassword = AuthClient.DefaultPassword, code = CodeFor(secret) }, Ct);
        renew.StatusCode.ShouldBe(HttpStatusCode.OK);
        var fresh = (await renew.Content.ReadFromJsonAsync<TwoFactorRecoveryCodes>(TestJson.Options, Ct))!.RecoveryCodes;
        fresh.ShouldNotContain(old[0]);
        (await (await LoginAsync("m.huber", old[0])).ReadProblemAsync()).Code.ShouldBe("TWO_FACTOR_INVALID");
        (await LoginAsync("m.huber", fresh[0])).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Policy_AdministratorsWithoutTwoFactor_LoseAdminRightsUntilSetUp()
    {
        var (chefin, _, _) = await EnabledAsync("chefin", StandardRoles.AdministratorId);
        var admin = await _factory.CreateClientAsAsync("zweite", StandardRoles.AdministratorId);
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);

        // Wer die Pflicht einschaltet, muss sie selbst erfüllen (sonst sperrt man sich aus).
        var settings = (await admin.GetFromJsonAsync<JsonObject>("/api/v1/settings", TestJson.Options, Ct))!;
        settings["settings"]!["session"]!["requireTwoFactorForAdmins"] = true;
        using (var refused = await admin.PutAsJsonAsync("/api/v1/settings", new { settings = settings["settings"], version = settings["version"] }, Ct))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await refused.ReadProblemAsync()).Code.ShouldBe("TWO_FACTOR_SETUP_FIRST");
        }

        (await chefin.PutAsJsonAsync("/api/v1/settings", new { settings = settings["settings"], version = settings["version"] }, Ct)).EnsureSuccessStatusCode();
        (await chefin.GetAsync(new Uri("/api/v1/users", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Verwaltung gesperrt, Fachrechte (Bauakten) bleiben; Anmeldung und Einrichtung funktionieren.
        (await admin.GetAsync(new Uri("/api/v1/users", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var permissions = (await admin.GetFromJsonAsync<List<string>>("/api/v1/access/me/permissions", TestJson.Options, Ct))!;
        permissions.ShouldNotContain("users.manage");
        permissions.ShouldContain("bauakten.read_all");
        var status = (await admin.GetFromJsonAsync<TwoFactorStatus>("/api/v1/auth/two-factor", TestJson.Options, Ct))!;
        status.RequiredByPolicy.ShouldBeTrue();
        status.Restricted.ShouldBeTrue();

        // Mitarbeitende ohne Verwaltungsrechte betrifft die Pflicht nicht.
        (await worker.GetFromJsonAsync<TwoFactorStatus>("/api/v1/auth/two-factor", TestJson.Options, Ct))!.RequiredByPolicy.ShouldBeFalse();

        using var setup = await admin.PostAsJsonAsync("/api/v1/auth/two-factor/setup", new { currentPassword = AuthClient.DefaultPassword }, Ct);
        var secret = (await setup.Content.ReadFromJsonAsync<TwoFactorSetupResponse>(TestJson.Options, Ct))!.Secret;
        (await admin.PostAsJsonAsync("/api/v1/auth/two-factor/enable", new { code = CodeFor(secret) }, Ct)).EnsureSuccessStatusCode();
        (await admin.GetAsync(new Uri("/api/v1/users", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        _factory.Clock.Advance(TimeSpan.FromSeconds(31));
        using var disable = await admin.PostAsJsonAsync("/api/v1/auth/two-factor/disable", new { currentPassword = AuthClient.DefaultPassword, code = CodeFor(secret) }, Ct);
        disable.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await disable.ReadProblemAsync()).Code.ShouldBe("TWO_FACTOR_REQUIRED_BY_POLICY");
    }

    [Fact]
    public async Task AdminReset_EndsSessions_ButNotForHigherPrivilegedAccounts()
    {
        var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        using var it = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        var (user, _, _) = await EnabledAsync("m.huber", StandardRoles.SiteWorkerId);
        var users = (await admin.GetFromJsonAsync<List<UserListItem>>("/api/v1/users", TestJson.Options, Ct))!;
        var target = users.Single(u => u.UserName == "m.huber");
        target.TwoFactorEnabled.ShouldBeTrue();
        var chefin = users.Single(u => u.UserName == "chefin");

        // IT-Administration darf das Konto der Administratorin nicht zurücksetzen (Rechteausweitung).
        using (var escalate = await it.PostAsync(new Uri($"/api/v1/users/{chefin.Id}/two-factor/reset", UriKind.Relative), null, Ct))
        {
            escalate.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await escalate.ReadProblemAsync()).Code.ShouldBe("PRIVILEGE_ESCALATION");
        }

        using var reset = await it.PostAsync(new Uri($"/api/v1/users/{target.Id}/two-factor/reset", UriKind.Relative), null, Ct);
        reset.StatusCode.ShouldBe(HttpStatusCode.OK, await reset.Content.ReadAsStringAsync(Ct));
        (await user.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await LoginAsync("m.huber", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
