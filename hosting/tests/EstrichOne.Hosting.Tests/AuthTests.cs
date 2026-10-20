using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EstrichOne.Hosting.Auth;

namespace EstrichOne.Hosting.Tests;

public sealed class AuthTests
{
    [Fact]
    public async Task State_OhneAdministrator_MeldetEinrichtungErforderlich()
    {
        await using var console = await ConsoleApp.StartAsync();
        var state = await console.Client().GetFromJsonAsync<JsonElement>("/api/auth/state", TestContext.Current.CancellationToken);
        state.GetProperty("setupRequired").GetBoolean().ShouldBeTrue();
        state.GetProperty("authenticated").GetBoolean().ShouldBeFalse();
        state.GetProperty("simulation").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task Setup_MitFalschemCode_WirdAbgewiesen_DanachNurEinmal()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = console.Client();
        var code = await console.IssueSetupCodeAsync();
        var wrong = await client.PostAsJsonAsync("/api/auth/setup", new { setupCode = "AAAA-BBBB-CCCC", userName = "admin", password = ConsoleApp.Password },
            TestContext.Current.CancellationToken);
        wrong.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var weak = await client.PostAsJsonAsync("/api/auth/setup", new { setupCode = code, userName = "admin", password = "kurz" },
            TestContext.Current.CancellationToken);
        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var ok = await client.PostAsJsonAsync("/api/auth/setup", new { setupCode = code, userName = "Admin", password = ConsoleApp.Password },
            TestContext.Current.CancellationToken);
        ok.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var cookie = ok.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-estrichone-hosting=", StringComparison.Ordinal)).ToLowerInvariant();
        cookie.ShouldContain("secure");
        cookie.ShouldContain("httponly");
        cookie.ShouldContain("samesite=strict");

        var again = await console.Client().PostAsJsonAsync("/api/auth/setup", new { setupCode = code, userName = "zweiter", password = ConsoleApp.Password },
            TestContext.Current.CancellationToken);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var state = await client.GetFromJsonAsync<JsonElement>("/api/auth/state", TestContext.Current.CancellationToken);
        state.GetProperty("authenticated").GetBoolean().ShouldBeTrue();
        state.GetProperty("user").GetProperty("userName").GetString().ShouldBe("admin");
    }

    [Fact]
    public async Task Api_OhneAnmeldung_Liefert401()
    {
        await using var console = await ConsoleApp.StartAsync();
        var response = await console.Client().GetAsync("/api/tenants", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AendernderAufruf_OhneCsrfToken_Oder_FremdeHerkunft_WirdAbgewiesen()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var token = client.DefaultRequestHeaders.GetValues(AuthEndpoints.CsrfHeader).Single();
        client.DefaultRequestHeaders.Remove(AuthEndpoints.CsrfHeader);
        var missing = await client.PostAsJsonAsync("/api/plans", new { name = "X" }, TestContext.Current.CancellationToken);
        missing.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        client.DefaultRequestHeaders.Add(AuthEndpoints.CsrfHeader, token);
        using var foreign = new HttpRequestMessage(HttpMethod.Post, "/api/plans") { Content = JsonContent.Create(new { name = "X" }) };
        foreign.Headers.Add("Origin", "https://boese.example");
        (await client.SendAsync(foreign, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var ok = await client.PostAsJsonAsync("/api/plans", new { name = "Test", maxUsers = 3, storageGb = 5 }, TestContext.Current.CancellationToken);
        ok.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Login_FuenfFehlversuche_SperrenDasKonto()
    {
        await using var console = await ConsoleApp.StartAsync();
        await console.AdminClientAsync();
        var client = console.Client();
        for (var i = 0; i < AuthEndpoints.MaxFailedLogins; i++)
        {
            var failed = await client.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = "falsch-falsch-falsch" },
                TestContext.Current.CancellationToken);
            failed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var locked = await client.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = ConsoleApp.Password },
            TestContext.Current.CancellationToken);
        locked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);

        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { userName = "niemand", password = "x" }, TestContext.Current.CancellationToken);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_RateLimit_GreiftJeAdresse()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = console.Client();
        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 12; i++)
        {
            last = (await client.PostAsJsonAsync("/api/auth/login", new { userName = $"u{i}", password = "x" }, TestContext.Current.CancellationToken)).StatusCode;
        }

        last.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Totp_Einschalten_DanachIstCodePflicht()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var begin = await client.PostAsync("/api/account/totp/begin", null, TestContext.Current.CancellationToken);
        begin.StatusCode.ShouldBe(HttpStatusCode.OK);
        var setup = await begin.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var secret = setup.GetProperty("secret").GetString()!;
        setup.GetProperty("qrSvg").GetString()!.ShouldContain("<svg");

        var bad = await client.PostAsJsonAsync("/api/account/totp/enable", new { code = "000000" }, TestContext.Current.CancellationToken);
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var now = Totp.TimeStep(DateTimeOffset.UtcNow);
        var code = Totp.Compute(Totp.Base32Decode(secret), now);
        (await client.PostAsJsonAsync("/api/account/totp/enable", new { code }, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var login = console.Client();
        var withoutCode = await login.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = ConsoleApp.Password }, TestContext.Current.CancellationToken);
        withoutCode.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await withoutCode.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("code").GetString().ShouldBe("TOTP_REQUIRED");

        // Derselbe Code ist bereits verbraucht – der nächste Zeitschritt wird akzeptiert.
        var reused = await login.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = ConsoleApp.Password, code }, TestContext.Current.CancellationToken);
        reused.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var next = Totp.Compute(Totp.Base32Decode(secret), now + 1);
        var ok = await login.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = ConsoleApp.Password, code = next }, TestContext.Current.CancellationToken);
        ok.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Passwortaenderung_BeendetAndereSitzungen()
    {
        await using var console = await ConsoleApp.StartAsync();
        var first = await console.AdminClientAsync();
        var second = console.Client();
        (await second.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = ConsoleApp.Password }, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await second.GetAsync("/api/tenants", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var change = await first.PostAsJsonAsync("/api/account/password",
            new { currentPassword = ConsoleApp.Password, newPassword = "ein-ganz-neues-passwort" }, TestContext.Current.CancellationToken);
        change.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await second.GetAsync("/api/tenants", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await first.GetAsync("/api/tenants", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sicherheitsheader_SindGesetzt()
    {
        await using var console = await ConsoleApp.StartAsync();
        var response = await console.Client().GetAsync("/health", TestContext.Current.CancellationToken);
        response.Headers.GetValues("Content-Security-Policy").Single().ShouldStartWith("default-src 'self'; script-src 'self';");
        response.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
        response.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
    }
}
