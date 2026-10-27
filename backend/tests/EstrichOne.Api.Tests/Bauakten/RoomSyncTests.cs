using System.Net.Http.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;

namespace EstrichOne.Api.Tests.Bauakten;

/// <summary>Räume und Flächen offline in der App erfassen (room.create/update/delete) und im Änderungsfeed sehen.</summary>
public sealed class RoomSyncTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ApiFactory _factory = null!;
    private HttpClient _office = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres);
        _office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
    }

    public async ValueTask DisposeAsync()
    {
        _office.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<Guid> BauakteAsync(string title = "EFH Schneider")
    {
        var id = Guid.CreateVersion7();
        (await _office.PushAsync(SyncClient.Op("bauakte.create", id, new { title }))).Results.Single().Status.ShouldBe(SyncOperationStatus.Applied);
        return id;
    }

    [Fact]
    public async Task OfflineCreatedRoom_IsSaved_VisibleInTheOffice_AndInTheFeed()
    {
        var bauakte = await BauakteAsync();
        var room = Guid.CreateVersion7();
        var payload = new
        {
            bauakteId = bauakte, name = "  Bad OG ", floor = "OG", areaSquareMeters = 8.456m, thicknessMillimeters = 65, insulationMillimeters = 40,
            underfloorHeating = true, status = "laid", notes = "  Fliesen folgen ",
        };

        var created = (await _office.PushAsync(SyncClient.Op("room.create", room, payload))).Results.Single();

        created.Status.ShouldBe(SyncOperationStatus.Applied);
        created.Entity.Text("name").ShouldBe("Bad OG");
        created.Entity.Text("status").ShouldBe("laid");
        created.Entity.Text("notes").ShouldBe("Fliesen folgen");
        created.Entity!["areaSquareMeters"]!.GetValue<decimal>().ShouldBe(8.46m);
        created.Entity.Text("bauakteId").ShouldBe(bauakte.ToString());

        var overview = await _office.GetFromJsonAsync<RoomOverview>($"/api/v1/bauakten/{bauakte}/rooms", TestJson.Options, Ct);
        overview!.Rooms.Single().Name.ShouldBe("Bad OG");
        overview.TotalAreaSquareMeters.ShouldBe(8.46m);
        overview.HeatedAreaSquareMeters.ShouldBe(8.46m);

        var (changes, _) = await _office.PullUntilAsync(null, all => all.Any(c => c.Id == room));
        var change = changes.Last(c => c.Id == room);
        change.EntityType.ShouldBe("room");
        change.Deleted.ShouldBeFalse();
        change.Data.Text("floor").ShouldBe("OG");

        // Antwort verloren, App sendet erneut mit neuer opId: dieselbe Anlage wird bestätigt.
        var again = (await _office.PushAsync(SyncClient.Op("room.create", room, payload))).Results.Single();
        again.Status.ShouldBe(SyncOperationStatus.Applied);
        again.Entity.Text("name").ShouldBe("Bad OG");
    }

    [Fact]
    public async Task Update_MergesFieldWise_KeepsNewerOfficeValues_AndDelete_ReachesTheFeed()
    {
        var bauakte = await BauakteAsync();
        var room = Guid.CreateVersion7();
        var created = (await _office.PushAsync(SyncClient.Op("room.create", room, new { bauakteId = bauakte, name = "Flur", areaSquareMeters = 10 })))
            .Results.Single();
        var area = created.Entity!["areaSquareMeters"]!.DeepClone();

        var updated = (await _office.PushAsync(SyncClient.Op("room.update", room, new
        {
            changes = new JsonObject { ["areaSquareMeters"] = 12.5m, ["status"] = "ready", ["underfloorHeating"] = true },
            @base = new JsonObject { ["areaSquareMeters"] = area.DeepClone(), ["status"] = "planned", ["underfloorHeating"] = false },
        }))).Results.Single();
        updated.Status.ShouldBe(SyncOperationStatus.Applied);
        updated.Entity!["areaSquareMeters"]!.GetValue<decimal>().ShouldBe(12.5m);
        updated.Entity.Text("status").ShouldBe("ready");
        updated.Entity!["underfloorHeating"]!.GetValue<bool>().ShouldBeTrue();
        updated.Entity.Text("name").ShouldBe("Flur");

        // Gerät kennt noch die alte Fläche: Fläche bleibt wie im Büro, der Name wird übernommen.
        var stale = (await _office.PushAsync(SyncClient.Op("room.update", room, new
        {
            changes = new JsonObject { ["areaSquareMeters"] = 9m, ["name"] = "Flur EG" },
            @base = new JsonObject { ["areaSquareMeters"] = area.DeepClone(), ["name"] = "Flur" },
        }))).Results.Single();
        stale.Status.ShouldBe(SyncOperationStatus.Conflict);
        stale.Code.ShouldBe("FIELD_CONFLICT");
        stale.Entity!["areaSquareMeters"]!.GetValue<decimal>().ShouldBe(12.5m);
        stale.Entity.Text("name").ShouldBe("Flur EG");

        (await _office.PushAsync(SyncClient.Op("room.delete", room, new { }))).Results.Single().Status.ShouldBe(SyncOperationStatus.Applied);
        var (changes, _) = await _office.PullUntilAsync(null, all => all.Any(c => c.Id == room && c.Deleted));
        changes.Last(c => c.Id == room).Deleted.ShouldBeTrue();
        var overview = await _office.GetFromJsonAsync<RoomOverview>($"/api/v1/bauakten/{bauakte}/rooms", TestJson.Options, Ct);
        overview!.Rooms.ShouldBeEmpty();
    }

    [Fact]
    public async Task InvalidValues_AreRejected()
    {
        var bauakte = await BauakteAsync();

        var results = (await _office.PushAsync(
            SyncClient.Op("room.create", Guid.CreateVersion7(), new { bauakteId = bauakte, name = "  " }),
            SyncClient.Op("room.create", Guid.CreateVersion7(), new { bauakteId = bauakte, name = "Bad", areaSquareMeters = -1 }),
            SyncClient.Op("room.create", Guid.CreateVersion7(), new { bauakteId = bauakte, name = "Bad", thicknessMillimeters = 5 }),
            SyncClient.Op("room.create", Guid.CreateVersion7(), new { bauakteId = bauakte, name = "Bad", status = "unbekannt" }),
            SyncClient.Op("room.create", Guid.CreateVersion7(), new { bauakteId = bauakte, name = "Bad", screedTypeId = Guid.CreateVersion7() })))
            .Results;

        results.ShouldAllBe(r => r.Status == SyncOperationStatus.Rejected && r.Code == "VALIDATION_FAILED");
    }

    [Fact]
    public async Task WithoutEditRight_OrForAnInvisibleBauakte_IsRejected()
    {
        var bauakte = await BauakteAsync();
        using var it = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        using var site = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);

        var denied = (await it.PushAsync(SyncClient.Op("room.create", Guid.CreateVersion7(), new { bauakteId = bauakte, name = "Bad" }))).Results.Single();
        denied.Code.ShouldBe("PERMISSION_DENIED");

        // Die Bauakte des Büros ist der Baustelle nicht zugewiesen und damit nicht sichtbar.
        var hidden = (await site.PushAsync(SyncClient.Op("room.create", Guid.CreateVersion7(), new { bauakteId = bauakte, name = "Bad" }))).Results.Single();
        hidden.Code.ShouldBe("NOT_FOUND");
    }
}
