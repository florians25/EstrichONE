using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Platform;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Platform;

public sealed class ServerInfoTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ServerInfo_AfterMigration_DescribesServer()
    {
        await using var factory = await ApiFactory.CreateMigratedAsync(postgres);
        using var client = factory.CreateClient();

        var info = await client.GetFromJsonAsync<ServerInfo>("/api/v1/server-info", TestJson.Options, TestContext.Current.CancellationToken);

        info.ShouldNotBeNull();
        info.Product.ShouldBe("EstrichONE");
        info.ServerName.ShouldBe("Estrichbau Test GmbH");
        info.ApiVersions.ShouldBe(["v1"]);
        info.InstanceEpoch.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task ServerInfo_InstanceEpoch_StaysStableAcrossRepeatedMigrations()
    {
        await using var factory = await ApiFactory.CreateMigratedAsync(postgres);
        using var client = factory.CreateClient();
        var first = await client.GetFromJsonAsync<ServerInfo>("/api/v1/server-info", TestJson.Options, TestContext.Current.CancellationToken);

        await factory.MigrateAsync();
        var second = await client.GetFromJsonAsync<ServerInfo>("/api/v1/server-info", TestJson.Options, TestContext.Current.CancellationToken);

        second!.InstanceEpoch.ShouldBe(first!.InstanceEpoch);
    }

    [Fact]
    public async Task ServerInfo_WithoutMigration_ReturnsServiceUnavailableProblem()
    {
        await using var factory = new ApiFactory(await postgres.CreateDatabaseAsync());
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(new Uri("/api/v1/server-info", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var problem = await response.ReadProblemAsync();
        problem.Code.ShouldBe("SERVER_NOT_INITIALIZED");
    }
}
