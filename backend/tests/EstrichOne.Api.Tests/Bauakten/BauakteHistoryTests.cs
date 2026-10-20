using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity;
using EstrichOne.Api.Modules.Sync;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;

namespace EstrichOne.Api.Tests.Bauakten;

public sealed class BauakteHistoryTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task History_ListsChangesNewestFirstWithReadableTexts()
    {
        using var client = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var bauakte = Guid.CreateVersion7();
        var task = Guid.CreateVersion7();

        Applied(await client.PushAsync(SyncClient.Op("bauakte.create", bauakte, new { title = "EFH Müller" })));
        _factory.Clock.Advance(TimeSpan.FromMinutes(1));
        Applied(await client.PushAsync(
            SyncClient.Op("note.create", Guid.CreateVersion7(), new { bauakteId = bauakte, text = "Zugang über Garage" }),
            SyncClient.Op("workitem.create", task, new { bauakteId = bauakte, kind = "defect", title = "Riss im Flur" })));
        _factory.Clock.Advance(TimeSpan.FromMinutes(1));
        Applied(await client.PushAsync(SyncClient.Op("workitem.update", task, new
        {
            changes = new { status = "done" }, @base = new { status = "open" },
        })));
        _factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var detail = await client.GetFromJsonAsync<BauakteDetail>($"/api/v1/bauakten/{bauakte}", TestJson.Options, TestContext.Current.CancellationToken);
        var change = new { title = detail!.Title, status = "inProgress", street = "Musterstr. 1", postalCode = detail.PostalCode,
            city = "Landshut", latitude = detail.Latitude, longitude = detail.Longitude, description = detail.Description,
            customerId = detail.CustomerId, responsibleUserId = detail.ResponsibleUserId };
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/bauakten/{bauakte}") { Content = JsonContent.Create(change) };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{detail.Version}\""));
        (await client.SendAsync(request, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        var history = await client.GetFromJsonAsync<List<BauakteHistoryEntry>>(
            $"/api/v1/bauakten/{bauakte}/history", TestJson.Options, TestContext.Current.CancellationToken);

        // Ereignisse desselben Zeitpunkts (eine Anfrage) haben keine festgelegte Reihenfolge.
        var texts = history!.Select(e => e.Text).ToList();
        texts[..2].ShouldBe([
            "Max Huber hat die Straße und den Ort geändert.",
            "Max Huber hat den Status von „Geplant“ auf „In Ausführung“ gesetzt.",
        ], ignoreOrder: true);
        texts[2].ShouldBe("Max Huber hat einen Mangel als behoben markiert.");
        texts[3..5].ShouldBe(["Max Huber hat einen Mangel erfasst.", "Max Huber hat eine Notiz geschrieben."], ignoreOrder: true);
        texts[5].ShouldBe("Max Huber hat die Bauakte angelegt.");
        texts.Count.ShouldBe(6);
        history!.ShouldAllBe(e => e.ActorName == "Max Huber");

        // Feldänderungen mit altem und neuem Wert.
        var updated = history!.Single(e => e.Action == "bauakte.updated");
        updated.Changes.ShouldNotBeNull();
        updated.Changes!.Single(c => c.Field == "street").ShouldBe(new BauakteFieldChange("street", "Straße", null, "Musterstr. 1"));
        updated.Changes!.Single(c => c.Field == "city").To.ShouldBe("Landshut");
        history!.Single(e => e.Action == "bauakte.status_changed").Changes!.ShouldHaveSingleItem()
            .ShouldBe(new BauakteFieldChange("status", "Status", "Geplant", "In Ausführung"));
    }

    [Fact]
    public async Task History_OfForeignBauakte_IsNotFound()
    {
        using var owner = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var colleague = await _factory.CreateClientAsAsync("kollege", StandardRoles.SiteWorkerId);
        var bauakte = Guid.CreateVersion7();
        Applied(await owner.PushAsync(SyncClient.Op("bauakte.create", bauakte, new { title = "Halle" })));

        using var response = await colleague.GetAsync(new Uri($"/api/v1/bauakten/{bauakte}/history", UriKind.Relative), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.ReadProblemAsync()).Code.ShouldBe("BAUAKTE_NOT_FOUND");
    }

    [Fact]
    public async Task Directory_ListsNamesForEverySignedInUser()
    {
        var office = await _factory.CreateUserAsync("buero", roleIds: StandardRoles.OfficeId);
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);

        var users = await worker.GetFromJsonAsync<List<DirectoryUser>>("/api/v1/directory/users", TestJson.Options, TestContext.Current.CancellationToken);
        using var anonymous = _factory.CreateClient();
        using var denied = await anonymous.GetAsync(new Uri("/api/v1/directory/users", UriKind.Relative), TestContext.Current.CancellationToken);

        users!.ShouldContain(u => u.Id == office.Id && u.DisplayName == "Max Huber" && u.IsActive);
        users!.Count.ShouldBe(2);
        denied.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static void Applied(PushResponse response) =>
        response.Results.ShouldAllBe(r => r.Status == SyncOperationStatus.Applied);
}
