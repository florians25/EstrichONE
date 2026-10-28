using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit;
using EstrichOne.Api.Modules.Identity;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Identity;

public sealed class AuthenticationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
        {
            // Wiederverwendung eines Refresh-Tokens sofort als Angriff werten (keine Gnadenfrist im Test).
            ["EstrichOne:Authentication:RefreshReuseGracePeriod"] = "00:00:00",
        });
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Login_WithUserNameOrEmail_ReturnsTokensAndGrantsAccess()
    {
        await _factory.CreateUserAsync("m.huber");

        var byName = await _client.LoginOkAsync("M.Huber");
        var byEmail = await _client.LoginOkAsync("m.huber@EXAMPLE.org");

        byName.AccessToken.ShouldStartWith("eo1_");
        byName.RefreshToken.ShouldStartWith("eor1_");
        byEmail.User.UserName.ShouldBe("m.huber");
        var me = await _client.WithToken(byName.AccessToken).GetFromJsonAsync<UserSummary>("/api/v1/auth/me", TestJson.Options, TestContext.Current.CancellationToken);
        me!.DisplayName.ShouldBe("Max Huber");
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401Problem()
    {
        using var response = await _client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ReadProblemAsync()).Code.ShouldBe("UNAUTHORIZED");
    }

    [Fact]
    public async Task Login_WrongPasswordAndUnknownUser_GetIdenticalAnswer()
    {
        await _factory.CreateUserAsync("m.huber");

        using var wrongPassword = await _client.LoginAsync("m.huber", "Falsches-Passwort-1");
        using var unknownUser = await _client.LoginAsync("niemand", "Falsches-Passwort-1");

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownUser.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var first = await wrongPassword.ReadProblemAsync();
        var second = await unknownUser.ReadProblemAsync();
        first.Code.ShouldBe("INVALID_CREDENTIALS");
        second.Code.ShouldBe(first.Code);
        second.Detail.ShouldBe(first.Detail);
    }

    [Fact]
    public async Task Login_AfterFiveFailures_IsLockedEvenWithCorrectPassword()
    {
        await _factory.CreateUserAsync("m.huber");
        for (var i = 0; i < 5; i++)
        {
            using var _ = await _client.LoginAsync("m.huber", "Falsches-Passwort-1");
        }

        using var response = await _client.LoginAsync("m.huber");

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await response.ReadProblemAsync()).Code.ShouldBe("LOGIN_LOCKED");
        response.Headers.RetryAfter.ShouldNotBeNull();

        _factory.Clock.Advance(TimeSpan.FromMinutes(16));
        using var later = await _client.LoginAsync("m.huber");
        later.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_MissingFields_ReturnsValidationProblem()
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { login = "m.huber" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).Code.ShouldBe("VALIDATION_FAILED");
    }

    [Fact]
    public async Task AccessToken_AfterExpiry_IsRejectedAndRefreshIssuesNewOne()
    {
        await _factory.CreateUserAsync("m.huber");
        var tokens = await _client.LoginOkAsync("m.huber");

        _factory.Clock.Advance(TimeSpan.FromMinutes(11));
        using var expired = await _client.WithToken(tokens.AccessToken).GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), TestContext.Current.CancellationToken);
        expired.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await expired.ReadProblemAsync()).Code.ShouldBe("ACCESS_TOKEN_EXPIRED");

        var refreshed = await RefreshOkAsync(tokens.RefreshToken);
        refreshed.RefreshToken.ShouldNotBe(tokens.RefreshToken);
        using var me = await _client.WithToken(refreshed.AccessToken).GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), TestContext.Current.CancellationToken);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RefreshToken_UsedTwice_EndsTheWholeSession()
    {
        await _factory.CreateUserAsync("m.huber");
        var tokens = await _client.LoginOkAsync("m.huber");
        var refreshed = await RefreshOkAsync(tokens.RefreshToken);

        using var replay = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken }, TestContext.Current.CancellationToken);

        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await replay.ReadProblemAsync()).Code.ShouldBe("REFRESH_TOKEN_REUSED");
        using var current = await _client.WithToken(refreshed.AccessToken).GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), TestContext.Current.CancellationToken);
        current.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var newer = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = refreshed.RefreshToken }, TestContext.Current.CancellationToken);
        newer.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_InvalidatesAccessAndRefreshToken()
    {
        await _factory.CreateUserAsync("m.huber");
        var tokens = await _client.LoginOkAsync("m.huber");

        using var logout = await _client.WithToken(tokens.AccessToken).PostAsync(new Uri("/api/v1/auth/logout", UriKind.Relative), null, TestContext.Current.CancellationToken);
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var me = await _client.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), TestContext.Current.CancellationToken);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var refresh = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = tokens.RefreshToken }, TestContext.Current.CancellationToken);
        (await refresh.ReadProblemAsync()).Code.ShouldBe("SESSION_REVOKED");
    }

    [Fact]
    public async Task DisabledUser_LosesAccessImmediately()
    {
        var user = await _factory.CreateUserAsync("m.huber");
        var tokens = await _client.LoginOkAsync("m.huber");

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
            (await db.Set<User>().SingleAsync(u => u.Id == user.Id, TestContext.Current.CancellationToken)).Deactivate();
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var me = await _client.WithToken(tokens.AccessToken).GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), TestContext.Current.CancellationToken);
        me.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await me.ReadProblemAsync()).Code.ShouldBe("USER_DISABLED");
    }

    [Fact]
    public async Task ChangePassword_RevokesOtherSessionsButKeepsCurrent()
    {
        await _factory.CreateUserAsync("m.huber");
        var phone = await _client.LoginOkAsync("m.huber");
        var office = await _client.LoginOkAsync("m.huber");
        using var officeClient = _factory.CreateClient().WithToken(office.AccessToken);

        using var weak = await officeClient.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = AuthClient.DefaultPassword, newPassword = "passwort123" }, TestContext.Current.CancellationToken);
        (await weak.ReadProblemAsync()).Code.ShouldBe("VALIDATION_FAILED");

        using var wrong = await officeClient.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = "Falsches-Passwort-1", newPassword = "Neue Passphrase 2026" }, TestContext.Current.CancellationToken);
        (await wrong.ReadProblemAsync()).Code.ShouldBe("INVALID_CURRENT_PASSWORD");

        using var changed = await officeClient.PostAsJsonAsync("/api/v1/auth/change-password",
            new { currentPassword = AuthClient.DefaultPassword, newPassword = "Neue Passphrase 2026" }, TestContext.Current.CancellationToken);
        changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var phoneClient = _factory.CreateClient().WithToken(phone.AccessToken);
        (await phoneClient.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await officeClient.GetAsync(new Uri("/api/v1/auth/me", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        await _client.LoginOkAsync("m.huber", "Neue Passphrase 2026");
    }

    [Fact]
    public async Task SecurityEvents_AreAudited_AndAuditLogIsAppendOnly()
    {
        await _factory.CreateUserAsync("m.huber");
        using (var _ = await _client.LoginAsync("m.huber", "Falsches-Passwort-1"))
        {
        }

        await _client.LoginOkAsync("m.huber");

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var actions = await db.Set<AuditEvent>().Select(e => e.Action).ToListAsync(TestContext.Current.CancellationToken);
        actions.ShouldContain("auth.login.failed");
        actions.ShouldContain("auth.login.succeeded");
        var leaked = await db.Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM audit_events WHERE details::text LIKE '%Falsches%'")
            .ToListAsync(TestContext.Current.CancellationToken);
        leaked.Single().ShouldBe(0, "Passwörter dürfen nie im Audit-Log landen.");

        await Should.ThrowAsync<Npgsql.PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("UPDATE audit_events SET action = 'manipuliert'", TestContext.Current.CancellationToken));
        await Should.ThrowAsync<Npgsql.PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("DELETE FROM audit_events", TestContext.Current.CancellationToken));
    }

    private async Task<RefreshResponse> RefreshOkAsync(string refreshToken)
    {
        using var response = await _client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RefreshResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
    }
}
