using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Platform;
using EstrichOne.Api.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Net.Http.Headers;

namespace EstrichOne.Api.Tests.Identity;

/// <summary>
/// Anmeldung im Browser bei abweichender bzw. unverschlüsselter Adresse (Produktionsfehler 0.1.0: Installer mit
/// ESTRICHONE_DOMAIN=estrichone.deinefirma.de, Aufruf über Nginx Proxy Manager als app.estrichone.de bzw. direkt per
/// http://IP:8080 – Anmeldung 200, jede Folgeanfrage 401, die Oberfläche hing im Anmeldebildschirm).
/// </summary>
public sealed class WebSessionAddressTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
        {
            ["EstrichOne:PublicUrl"] = "https://estrichone.deinefirma.de",
            ["EstrichOne:TrustForwardedHeaders"] = "true",
        });
        await _factory.CreateUserAsync("chefin", roleIds: StandardRoles.AdministratorId);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    /// <summary>Browser-ähnlich: speichert Cookies und sendet Secure-Cookies (wie ein Browser) nur über HTTPS.</summary>
    private HttpClient Browser(string baseAddress) => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri(baseAddress),
        HandleCookies = true,
    });

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient browser, string? origin, string? forwardedProto = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/web/login")
        {
            Content = JsonContent.Create(new { login = "chefin", password = AuthClient.DefaultPassword }),
        };
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (forwardedProto is not null)
        {
            request.Headers.Add("X-Forwarded-Proto", forwardedProto);
        }

        return await browser.SendAsync(request, Ct);
    }

    private static IList<SetCookieHeaderValue> SetCookies(HttpResponseMessage response) =>
        SetCookieHeaderValue.ParseList(response.Headers.GetValues("Set-Cookie").ToList());

    [Fact]
    public async Task OtherHostThanPublicUrl_CookiesAreHostOnly_SessionWorks()
    {
        using var browser = Browser("https://app.estrichone.de");
        using var login = await LoginAsync(browser, "https://app.estrichone.de");

        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var cookie in SetCookies(login))
        {
            cookie.Domain.HasValue.ShouldBeFalse();
            cookie.Secure.ShouldBeTrue();
            cookie.MaxAge.ShouldNotBeNull();
            cookie.MaxAge!.Value.ShouldBeGreaterThan(TimeSpan.FromHours(1));
        }

        (await browser.GetAsync(new Uri("/api/v1/value-lists", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await browser.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PlainHttpFromBrowser_CookiesWithoutSecure_SoTheBrowserKeepsThem()
    {
        // http://IP:8080 direkt (an Nginx Proxy Manager vorbei): Secure-Cookies würde der Browser verwerfen.
        using var browser = Browser("http://192.0.2.10:8080");
        using var login = await LoginAsync(browser, "http://192.0.2.10:8080");

        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        SetCookies(login).ShouldAllBe(c => !c.Secure);
        (await browser.GetAsync(new Uri("/api/v1/value-lists", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BehindTlsProxy_OrWithoutOrigin_CookiesStaySecure()
    {
        // Nginx Proxy Manager → Caddy → API: Anfrage per HTTP, aber X-Forwarded-Proto https.
        using var proxied = Browser("http://app.estrichone.de");
        using var viaProxy = await LoginAsync(proxied, "https://app.estrichone.de", forwardedProto: "https");
        SetCookies(viaProxy).ShouldAllBe(c => c.Secure);

        using var noOrigin = await LoginAsync(Browser("http://app.estrichone.de"), origin: null);
        SetCookies(noOrigin).ShouldAllBe(c => c.Secure);

        // Fremde Seite per HTTP kann über HTTPS keine unsicheren Cookies erzwingen.
        using var https = await LoginAsync(Browser("https://app.estrichone.de"), "http://angreifer.example");
        SetCookies(https).ShouldAllBe(c => c.Secure);
    }

    [Fact]
    public async Task AddressCheck_ReportsMismatch_ForAdministrators()
    {
        using var browser = Browser("https://app.estrichone.de");
        using var login = await LoginAsync(browser, "https://app.estrichone.de");
        login.EnsureSuccessStatusCode();

        var check = await browser.GetFromJsonAsync<AddressCheck>("/api/v1/system/address", TestJson.Options, Ct);

        check.ShouldNotBeNull();
        check.ConfiguredUrl.ShouldBe("https://estrichone.deinefirma.de");
        check.RequestUrl.ShouldBe("https://app.estrichone.de");
        check.Matches.ShouldBeFalse();
        check.Https.ShouldBeTrue();

        using var same = Browser("https://estrichone.deinefirma.de");
        (await LoginAsync(same, "https://estrichone.deinefirma.de")).EnsureSuccessStatusCode();
        (await same.GetFromJsonAsync<AddressCheck>("/api/v1/system/address", TestJson.Options, Ct))!.Matches.ShouldBeTrue();
    }
}
