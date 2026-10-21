using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Identity;
using EstrichOne.Api.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;

namespace EstrichOne.Api.Tests.Identity;

/// <summary>Anmeldung im Browser mit HttpOnly-Cookies und CSRF-Schutz (docs/reengineering/17, Abschnitt 2).</summary>
public sealed class WebSessionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres);
        await _factory.CreateUserAsync("buero", roleIds: StandardRoles.OfficeId);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    /// <summary>Browser-ähnlicher Client: HTTPS, Cookies werden gespeichert und mitgesendet.</summary>
    private HttpClient Browser() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
    });

    private async Task<(HttpClient Client, string Csrf)> SignedInAsync()
    {
        var browser = Browser();
        using var response = await browser.PostAsJsonAsync("/api/v1/auth/web/login",
            new { login = "buero", password = AuthClient.DefaultPassword }, Ct);
        response.EnsureSuccessStatusCode();
        var csrf = SetCookies(response).Single(c => c.Name == "XSRF-TOKEN").Value.ToString();
        return (browser, csrf);
    }

    private static IList<SetCookieHeaderValue> SetCookies(HttpResponseMessage response) =>
        SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToList());

    [Fact]
    public async Task Login_SetsHttpOnlyStrictCookies_AndReturnsNoTokens()
    {
        using var browser = Browser();
        using var response = await browser.PostAsJsonAsync("/api/v1/auth/web/login",
            new { login = "buero", password = AuthClient.DefaultPassword }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldNotContain("eo1_");
        body.ShouldNotContain("eor1_");

        var cookies = SetCookies(response).ToDictionary(c => c.Name.ToString());
        cookies["eo_at"].HttpOnly.ShouldBeTrue();
        cookies["eo_at"].Secure.ShouldBeTrue();
        cookies["eo_at"].SameSite.ShouldBe(SameSiteMode.Strict);
        cookies["eo_at"].Path.ToString().ShouldBe("/api");
        cookies["eo_rt"].HttpOnly.ShouldBeTrue();
        cookies["eo_rt"].Path.ToString().ShouldBe("/api/v1/auth/web");
        cookies["XSRF-TOKEN"].HttpOnly.ShouldBeFalse();
        cookies["XSRF-TOKEN"].SameSite.ShouldBe(SameSiteMode.Strict);
    }

    [Fact]
    public async Task CookieSession_ReadsWithoutCsrf_ButWritesOnlyWithCsrfHeader()
    {
        var (browser, csrf) = await SignedInAsync();
        using var _ = browser;

        (await browser.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var bauakte = new { id = Guid.CreateVersion7(), title = "Web-Anlage" };
        using var withoutHeader = await browser.PostAsJsonAsync("/api/v1/bauakten", bauakte, Ct);
        withoutHeader.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await withoutHeader.ReadProblemAsync()).Code.ShouldBe("CSRF_INVALID");

        using var wrongHeader = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bauakten") { Content = JsonContent.Create(bauakte) };
        wrongHeader.Headers.Add("X-XSRF-TOKEN", "falsch");
        (await browser.SendAsync(wrongHeader, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var withHeader = new HttpRequestMessage(HttpMethod.Post, "/api/v1/bauakten") { Content = JsonContent.Create(bauakte) };
        withHeader.Headers.Add("X-XSRF-TOKEN", csrf);
        (await browser.SendAsync(withHeader, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Refresh_RotatesCookies_RequiresCsrf()
    {
        var (browser, csrf) = await SignedInAsync();
        using var _ = browser;

        (await browser.PostAsync(new Uri("/api/v1/auth/web/refresh", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/web/refresh");
        refresh.Headers.Add("X-XSRF-TOKEN", csrf);
        using var response = await browser.SendAsync(refresh, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        // Der CSRF-Token bleibt beim Erneuern gleich (parallel laufende Anfragen scheitern sonst).
        var newCsrf = SetCookies(response).Single(c => c.Name == "XSRF-TOKEN").Value.ToString();
        newCsrf.ShouldBe(csrf);
        (await response.Content.ReadFromJsonAsync<WebSessionResponse>(TestJson.Options, Ct))!.User.UserName.ShouldBe("buero");

        // Nach Ablauf des Zugriffstokens trägt das erneuerte Cookie.
        _factory.Clock.Advance(TimeSpan.FromMinutes(11));
        using var expired = await browser.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), Ct);
        expired.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await expired.ReadProblemAsync()).Code.ShouldBe("ACCESS_TOKEN_EXPIRED");
        using var again = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/web/refresh");
        again.Headers.Add("X-XSRF-TOKEN", newCsrf);
        (await browser.SendAsync(again, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await browser.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AccessCookie_LivesAsLongAsTheSession_SoTheClientCanRefreshAfterIdle()
    {
        using var browser = Browser();
        using var response = await browser.PostAsJsonAsync("/api/v1/auth/web/login",
            new { login = "buero", password = AuthClient.DefaultPassword }, Ct);
        var cookies = SetCookies(response).ToDictionary(c => c.Name.ToString());

        cookies["eo_at"].Expires.ShouldBe(cookies["eo_rt"].Expires);
        // Standard ohne „angemeldet bleiben“: 12 Stunden Leerlauf.
        (cookies["eo_rt"].Expires!.Value - _factory.Clock.GetUtcNow()).ShouldBe(TimeSpan.FromHours(12), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Session_SlidesWithActivity_EndsAfterIdle_RememberMeKeepsLonger()
    {
        var (browser, csrf) = await SignedInAsync();
        using var _ = browser;

        async Task<HttpStatusCode> RefreshAsync(HttpClient client, string token)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/web/refresh");
            request.Headers.Add("X-XSRF-TOKEN", token);
            using var response = await client.SendAsync(request, Ct);
            return response.StatusCode;
        }

        // Aktiv: alle 11 Stunden erneuert – länger als die Leerlaufzeit am Stück, trotzdem angemeldet.
        _factory.Clock.Advance(TimeSpan.FromHours(11));
        (await RefreshAsync(browser, csrf)).ShouldBe(HttpStatusCode.OK);
        _factory.Clock.Advance(TimeSpan.FromHours(11));
        (await RefreshAsync(browser, csrf)).ShouldBe(HttpStatusCode.OK);

        // Leerlauf über 12 Stunden: abgelaufen.
        _factory.Clock.Advance(TimeSpan.FromHours(13));
        (await RefreshAsync(browser, csrf)).ShouldBe(HttpStatusCode.Unauthorized);

        using var remembered = Browser();
        using var login = await remembered.PostAsJsonAsync("/api/v1/auth/web/login",
            new { login = "buero", password = AuthClient.DefaultPassword, rememberMe = true }, Ct);
        var token = SetCookies(login).Single(c => c.Name == "XSRF-TOKEN").Value.ToString();
        _factory.Clock.Advance(TimeSpan.FromDays(5));
        (await RefreshAsync(remembered, token)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConcurrentRefresh_FromSecondTab_DoesNotEndTheSession()
    {
        // Zwei Tabs senden denselben Refresh-Token fast gleichzeitig.
        using var tab = Browser();
        using var login = await tab.PostAsJsonAsync("/api/v1/auth/web/login",
            new { login = "buero", password = AuthClient.DefaultPassword }, Ct);
        var tabCsrf = SetCookies(login).Single(c => c.Name == "XSRF-TOKEN").Value.ToString();
        var rt = SetCookies(login).Single(c => c.Name == "eo_rt").Value.ToString();

        HttpRequestMessage Refresh()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/web/refresh");
            request.Headers.Add("X-XSRF-TOKEN", tabCsrf);
            request.Headers.Add("Cookie", $"eo_rt={rt}; XSRF-TOKEN={tabCsrf}");
            return request;
        }

        using var raw = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        using var r1 = Refresh();
        (await raw.SendAsync(r1, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var r2 = Refresh();
        using var second = await raw.SendAsync(r2, Ct);
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        second.Headers.Contains("Set-Cookie").ShouldBeFalse();
    }

    [Fact]
    public async Task Logout_EndsSession_AndClearsCookies()
    {
        var (browser, csrf) = await SignedInAsync();
        using var _ = browser;

        using var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/web/logout");
        logout.Headers.Add("X-XSRF-TOKEN", csrf);
        using var response = await browser.SendAsync(logout, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        SetCookies(response).ShouldContain(c => c.Name == "eo_at" && c.Expires < DateTimeOffset.UtcNow);
        (await browser.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WrongPassword_SetsNoCookies()
    {
        using var browser = Browser();
        using var response = await browser.PostAsJsonAsync("/api/v1/auth/web/login", new { login = "buero", password = "falsch" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
    }

    [Fact]
    public async Task BearerClients_NeedNoCsrfHeader()
    {
        using var client = await _factory.CreateClientAsAsync("api-nutzer", StandardRoles.OfficeId);

        using var response = await client.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title = "Per Token" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }
}
