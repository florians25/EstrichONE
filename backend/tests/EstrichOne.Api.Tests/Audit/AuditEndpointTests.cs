using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Audit;
using EstrichOne.Api.Modules.Sync;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;

namespace EstrichOne.Api.Tests.Audit;

public sealed class AuditEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Audit_FiltersByActionPrefixAndActor_AndPagesNewestFirst()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        for (var i = 0; i < 3; i++)
        {
            _factory.Clock.Advance(TimeSpan.FromMinutes(1));
            var result = await office.PushAsync(SyncClient.Op("bauakte.create", Guid.CreateVersion7(), new { title = $"Bauakte {i}" }));
            result.Results.ShouldAllBe(r => r.Status == SyncOperationStatus.Applied);
        }

        var first = await admin.GetFromJsonAsync<AuditLogPage>("/api/v1/audit?action=bauakte.&limit=2", TestJson.Options, TestContext.Current.CancellationToken);
        first!.Items.Count.ShouldBe(2);
        first.Items.ShouldAllBe(e => e.Action == "bauakte.created" && e.ActorName == "Max Huber" && e.EntityType == "bauakte");
        first.Items[0].OccurredAt.ShouldBeGreaterThan(first.Items[1].OccurredAt);
        first.NextCursor.ShouldNotBeNull();

        var second = await admin.GetFromJsonAsync<AuditLogPage>(
            $"/api/v1/audit?action=bauakte.&limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}", TestJson.Options, TestContext.Current.CancellationToken);
        second!.Items.Count.ShouldBe(1);
        second.NextCursor.ShouldBeNull();

        var logins = await admin.GetFromJsonAsync<AuditLogPage>("/api/v1/audit?action=auth.login", TestJson.Options, TestContext.Current.CancellationToken);
        logins!.Items.ShouldNotBeEmpty();
        logins.Items.ShouldAllBe(e => e.Action.StartsWith("auth.login"));
    }

    [Fact]
    public async Task Audit_RequiresPermission()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);

        using var response = await office.GetAsync(new Uri("/api/v1/audit", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
