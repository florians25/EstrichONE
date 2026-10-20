using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Sync;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;

namespace EstrichOne.Api.Tests.Bauakten;

public sealed class BauakteMapTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Map_ListsOnlyVisibleSitesWithLocation_AndHidesFinishedUnlessAll()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var located = Guid.CreateVersion7();
        var noLocation = Guid.CreateVersion7();
        var completed = Guid.CreateVersion7();
        var foreign = Guid.CreateVersion7();
        Applied(await office.PushAsync(
            SyncClient.Op("bauakte.create", located, new { title = "EFH Landshut", city = "Landshut", latitude = 48.537, longitude = 12.152 }),
            SyncClient.Op("bauakte.create", noLocation, new { title = "Ohne Standort" }),
            SyncClient.Op("bauakte.create", completed, new { title = "Fertig", latitude = 48.4, longitude = 12.0 })));
        Applied(await worker.PushAsync(SyncClient.Op("bauakte.create", foreign, new { title = "Eigene Baustelle", latitude = 48.1, longitude = 11.5 })));
        await SetStatusAsync(office, completed, "completed");

        var points = await office.GetFromJsonAsync<List<BauakteMapPoint>>("/api/v1/bauakten/map", TestJson.Options, TestContext.Current.CancellationToken);
        var all = await office.GetFromJsonAsync<List<BauakteMapPoint>>("/api/v1/bauakten/map?all=true", TestJson.Options, TestContext.Current.CancellationToken);
        var workerPoints = await worker.GetFromJsonAsync<List<BauakteMapPoint>>("/api/v1/bauakten/map", TestJson.Options, TestContext.Current.CancellationToken);

        points!.Select(p => p.Id).ShouldBe([foreign, located], ignoreOrder: true);
        points!.Single(p => p.Id == located).ShouldSatisfyAllConditions(
            p => p.Latitude.ShouldBe(48.537), p => p.Longitude.ShouldBe(12.152), p => p.City.ShouldBe("Landshut"));
        all!.Select(p => p.Id).ShouldContain(completed);
        // Baustellen-Mitarbeiter sehen nur ihre eigenen Bauakten – auch auf der Karte.
        workerPoints!.Select(p => p.Id).ShouldBe([foreign]);
    }

    [Fact]
    public async Task List_IncludesLocationAndResponsible()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var id = Guid.CreateVersion7();
        Applied(await office.PushAsync(SyncClient.Op("bauakte.create", id, new { title = "Halle", latitude = 48.5, longitude = 12.1 })));

        var page = await office.GetFromJsonAsync<BauaktePage>("/api/v1/bauakten", TestJson.Options, TestContext.Current.CancellationToken);

        var item = page!.Items.Single(i => i.Id == id);
        item.Latitude.ShouldBe(48.5);
        item.Longitude.ShouldBe(12.1);
    }

    private static async Task SetStatusAsync(HttpClient client, Guid id, string status)
    {
        var detail = await client.GetFromJsonAsync<BauakteDetail>($"/api/v1/bauakten/{id}", TestJson.Options, TestContext.Current.CancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/bauakten/{id}")
        {
            Content = JsonContent.Create(new
            {
                title = detail!.Title, status, street = detail.Street, postalCode = detail.PostalCode, city = detail.City,
                latitude = detail.Latitude, longitude = detail.Longitude, description = detail.Description,
                customerId = detail.CustomerId, responsibleUserId = detail.ResponsibleUserId,
            }),
        };
        request.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{detail.Version}\""));
        (await client.SendAsync(request, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
    }

    private static void Applied(PushResponse response) =>
        response.Results.ShouldAllBe(r => r.Status == SyncOperationStatus.Applied);
}
