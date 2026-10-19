using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Platform;
using EstrichOne.Api.Modules.Setup;
using EstrichOne.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Setup;

public sealed class SetupTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres);
        _client = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task FreshInstallation_ReportsSetupRequired()
    {
        var info = await _client.GetFromJsonAsync<ServerInfo>("/api/v1/server-info", TestJson.Options, TestContext.Current.CancellationToken);
        var status = await _client.GetFromJsonAsync<SetupStatus>("/api/v1/setup/status", TestJson.Options, TestContext.Current.CancellationToken);

        info!.SetupRequired.ShouldBeTrue();
        status!.SetupRequired.ShouldBeTrue();
    }

    [Fact]
    public async Task Setup_WithWrongCode_IsRejected()
    {
        await IssueCodeAsync();

        using var response = await CompleteAsync("AAAA-BBBB-CCCC");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.ReadProblemAsync()).Code.ShouldBe("INVALID_SETUP_CODE");
    }

    [Fact]
    public async Task Setup_WithCode_CreatesAdministratorOnce()
    {
        var code = await IssueCodeAsync();

        using var response = await CompleteAsync(code.ToLowerInvariant());
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var tokens = await _client.LoginOkAsync("admin", "Estrich trocknet langsam 7");
        var permissions = await _factory.CreateClient().WithToken(tokens.AccessToken)
            .GetFromJsonAsync<List<string>>("/api/v1/access/me/permissions", TestJson.Options, TestContext.Current.CancellationToken);
        permissions.ShouldNotBeNull();
        permissions.ShouldBe(Permissions.All.Select(p => p.Code), ignoreOrder: true);

        using var again = await CompleteAsync(code);
        (await again.ReadProblemAsync()).Code.ShouldBe("SETUP_ALREADY_COMPLETED");
        (await _client.GetFromJsonAsync<SetupStatus>("/api/v1/setup/status", TestJson.Options, TestContext.Current.CancellationToken))!.SetupRequired.ShouldBeFalse();
    }

    [Fact]
    public async Task Setup_RunningTwiceInParallel_CreatesExactlyOneAdministrator()
    {
        var code = await IssueCodeAsync();

        var responses = await Task.WhenAll(CompleteAsync(code), CompleteAsync(code, userName: "admin2"));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task Setup_WithWeakPassword_ReturnsValidationProblem()
    {
        var code = await IssueCodeAsync();

        using var response = await CompleteAsync(code, password: "passwort123");

        (await response.ReadProblemAsync()).Code.ShouldBe("VALIDATION_FAILED");
        (await _client.GetFromJsonAsync<SetupStatus>("/api/v1/setup/status", TestJson.Options, TestContext.Current.CancellationToken))!.SetupRequired.ShouldBeTrue();
    }

    private async Task<string> IssueCodeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<SetupService>().IssueCodeIfRequiredAsync(CancellationToken.None))!;
    }

    private Task<HttpResponseMessage> CompleteAsync(string code, string userName = "admin", string password = "Estrich trocknet langsam 7") =>
        _client.PostAsJsonAsync("/api/v1/setup", new
        {
            setupCode = code,
            companyName = "Estrichbau Muster GmbH",
            administrator = new { displayName = "Anna Admin", userName, email = (string?)null, password },
        });
}
