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
        var newCsrf = SetCookies(response).Single(c => c.Name == "XSRF-TOKEN").Value.ToString();
        newCsrf.ShouldNotBe(csrf);
        (await response.Content.ReadFromJsonAsync<WebSessionResponse>(TestJson.Options, Ct))!.User.UserName.ShouldBe("buero");

        // Nach Ablauf des Zugriffstokens trägt das erneuerte Cookie.
        _factory.Clock.Advance(TimeSpan.FromMinutes(11));
        (await browser.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var again = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/web/refresh");
        again.Headers.Add("X-XSRF-TOKEN", newCsrf);
        (await browser.SendAsync(again, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await browser.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
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
