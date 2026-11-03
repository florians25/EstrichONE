using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Identity.Sessions;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Identity;

/// <summary>Eigene Anmeldungen ansehen, einzeln beenden, „überall abmelden“.</summary>
public sealed class SessionManagementTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task List_ShowsOwnSessions_RevokeOne_ThenEverywhereElse()
    {
        await _factory.CreateUserAsync("m.huber");
        await _factory.CreateUserAsync("fremd");
        using var web = _factory.CreateClient();
        web.WithToken((await web.LoginOkAsync("m.huber")).AccessToken);
        using var desktop = _factory.CreateClient();
        desktop.WithToken((await desktop.LoginOkAsync("m.huber", clientKind: "desktop")).AccessToken);
        using var tablet = _factory.CreateClient();
        tablet.WithToken((await tablet.LoginOkAsync("m.huber", clientKind: "desktop")).AccessToken);
        using var other = _factory.CreateClient();
        other.WithToken((await other.LoginOkAsync("fremd")).AccessToken);

        var sessions = (await web.GetFromJsonAsync<List<SessionInfo>>("/api/v1/auth/sessions", TestJson.Options, Ct))!;
        sessions.Count.ShouldBe(3);
        sessions.Count(s => s.Current).ShouldBe(1);
        sessions.Single(s => s.Current).Client.ShouldBe(ClientKind.Web);

        // Fremde Sitzungen sind nicht erreichbar.
        var foreign = (await other.GetFromJsonAsync<List<SessionInfo>>("/api/v1/auth/sessions", TestJson.Options, Ct))!.Single();
        (await web.DeleteAsync(new Uri($"/api/v1/auth/sessions/{foreign.Id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var desktopSession = (await desktop.GetFromJsonAsync<List<SessionInfo>>("/api/v1/auth/sessions", TestJson.Options, Ct))!.Single(s => s.Current);
        (await web.DeleteAsync(new Uri($"/api/v1/auth/sessions/{desktopSession.Id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await desktop.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var everywhere = await web.PostAsync(new Uri("/api/v1/auth/sessions/revoke-others", UriKind.Relative), null, Ct);
        (await everywhere.Content.ReadFromJsonAsync<RevokedSessions>(TestJson.Options, Ct))!.Count.ShouldBe(1);
        (await tablet.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await web.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await web.GetFromJsonAsync<List<SessionInfo>>("/api/v1/auth/sessions", TestJson.Options, Ct))!.ShouldHaveSingleItem().Current.ShouldBeTrue();
    }
}
