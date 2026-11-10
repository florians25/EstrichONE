using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;

namespace EstrichOne.Api.Tests.Bauakten;

/// <summary>„Erstbegehung nicht erforderlich“: setzen, aufheben, Rechte, Hinweis, Verlauf und Synchronisation.</summary>
public sealed class InitialInspectionWaiverTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static async Task<BauakteDetail> CreateAsync(HttpClient client, string title)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;
    }

    private static async Task<List<BauakteHint>> HintsAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<List<BauakteHint>>($"/api/v1/bauakten/{id}/hints", TestJson.Options, Ct))!;

    [Fact]
    public async Task Waive_HidesMissingHint_RecordsWhoAndWhen_AndCanBeUndone()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = await CreateAsync(office, "Garage Becker");
        (await HintsAsync(office, bauakte.Id)).ShouldContain(h => h.Kind == "initial_inspection_missing");
        _factory.Clock.Advance(TimeSpan.FromMinutes(1));

        using var waived = await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/initial-inspection-waiver",
            new { waived = true, reason = "  Bestandsgebäude, Untergrund bekannt " }, Ct);
        waived.EnsureSuccessStatusCode();
        var detail = (await waived.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;

        detail.InitialInspectionWaived.ShouldBeTrue();
        detail.InitialInspectionWaivedReason.ShouldBe("Bestandsgebäude, Untergrund bekannt");
        detail.InitialInspectionWaivedByName.ShouldBe("Max Huber");
        detail.InitialInspectionWaivedByUserId.ShouldNotBeNull();
        detail.InitialInspectionWaivedAt.ShouldBe(_factory.Clock.GetUtcNow());
        detail.Version.ShouldBeGreaterThan(bauakte.Version);
        (await HintsAsync(office, bauakte.Id)).ShouldNotContain(h => h.Kind == "initial_inspection_missing");

        var history = await office.GetFromJsonAsync<List<BauakteHistoryEntry>>($"/api/v1/bauakten/{bauakte.Id}/history", TestJson.Options, Ct);
        history![0].Text.ShouldBe("Max Huber hat vermerkt, dass keine Erstbegehung erforderlich ist (Begründung: Bestandsgebäude, Untergrund bekannt).");

        _factory.Clock.Advance(TimeSpan.FromMinutes(1));
        using var undone = await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/initial-inspection-waiver", new { waived = false }, Ct);
        undone.EnsureSuccessStatusCode();
        var reset = (await undone.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;

        reset.InitialInspectionWaived.ShouldBeFalse();
        reset.InitialInspectionWaivedReason.ShouldBeNull();
        reset.InitialInspectionWaivedByName.ShouldBeNull();
        reset.InitialInspectionWaivedAt.ShouldBeNull();
        (await HintsAsync(office, bauakte.Id)).ShouldContain(h => h.Kind == "initial_inspection_missing");
        history = await office.GetFromJsonAsync<List<BauakteHistoryEntry>>($"/api/v1/bauakten/{bauakte.Id}/history", TestJson.Options, Ct);
        history![0].Text.ShouldBe("Max Huber hat den Vermerk „Keine Erstbegehung erforderlich“ aufgehoben.");
    }

    [Fact]
    public async Task Waive_WithoutReason_IsAllowed_TooLongReasonIsRejected()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = await CreateAsync(office, "Carport");

        using var plain = await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/initial-inspection-waiver", new { waived = true }, Ct);
        using var tooLong = await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/initial-inspection-waiver",
            new { waived = true, reason = new string('x', 501) }, Ct);

        plain.EnsureSuccessStatusCode();
        var detail = (await plain.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;
        detail.InitialInspectionWaived.ShouldBeTrue();
        detail.InitialInspectionWaivedReason.ShouldBeNull();
        tooLong.StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task WithoutEditPermission_WaiverIsDenied_OverApiAndSync()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        using var it = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        var bauakte = await CreateAsync(office, "Lager");

        using var response = await it.PutAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/initial-inspection-waiver", new { waived = true }, Ct);
        var sync = await it.PushAsync(SyncClient.Op("bauakte.update", bauakte.Id, new
        {
            changes = new { initialInspectionWaived = true }, @base = new { initialInspectionWaived = false },
        }));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        sync.Results.Single().Code.ShouldBe("PERMISSION_DENIED");
        (await office.GetFromJsonAsync<BauakteDetail>($"/api/v1/bauakten/{bauakte.Id}", TestJson.Options, Ct))!.InitialInspectionWaived.ShouldBeFalse();
    }

    [Fact]
    public async Task Sync_SetsAndClearsWaiver_FieldByField()
    {
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var id = Guid.CreateVersion7();
        (await worker.PushAsync(SyncClient.Op("bauakte.create", id, new { title = "Anbau" }))).Results.Single().Status.ShouldBe(SyncOperationStatus.Applied);

        var set = (await worker.PushAsync(SyncClient.Op("bauakte.update", id, new
        {
            changes = new { initialInspectionWaived = true, initialInspectionWaivedReason = "Nur Ausbesserung" },
            // Stand des Geräts von vor dem Feld: null gilt als „nicht vermerkt“.
            @base = new { initialInspectionWaived = (bool?)null, initialInspectionWaivedReason = (string?)null },
        }))).Results.Single();

        set.Status.ShouldBe(SyncOperationStatus.Applied);
        set.Entity!["initialInspectionWaived"]!.GetValue<bool>().ShouldBeTrue();
        set.Entity["initialInspectionWaivedReason"]!.GetValue<string>().ShouldBe("Nur Ausbesserung");
        set.Entity["initialInspectionWaivedByName"]!.GetValue<string>().ShouldBe("Max Huber");
        (await HintsAsync(worker, id)).ShouldNotContain(h => h.Kind == "initial_inspection_missing");

        // Zweites Gerät mit altem Stand ändert nur die Begründung: Konflikt, weil sie inzwischen gesetzt wurde.
        var stale = (await worker.PushAsync(SyncClient.Op("bauakte.update", id, new
        {
            changes = new { initialInspectionWaivedReason = "Anders" }, @base = new { initialInspectionWaivedReason = (string?)null },
        }))).Results.Single();
        stale.Status.ShouldBe(SyncOperationStatus.Conflict);

        var clear = (await worker.PushAsync(SyncClient.Op("bauakte.update", id, new
        {
            changes = new { initialInspectionWaived = false }, @base = new { initialInspectionWaived = true },
        }))).Results.Single();

        clear.Status.ShouldBe(SyncOperationStatus.Applied);
        clear.Entity!["initialInspectionWaived"]!.GetValue<bool>().ShouldBeFalse();
        (await HintsAsync(worker, id)).ShouldContain(h => h.Kind == "initial_inspection_missing");
    }

    [Fact]
    public async Task Sync_CreateWithWaiver_OfflineCreatedBauakte()
    {
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var id = Guid.CreateVersion7();

        var created = (await worker.PushAsync(SyncClient.Op("bauakte.create", id, new
        {
            title = "Offline", initialInspectionWaived = true, initialInspectionWaivedReason = "Kleinfläche",
        }))).Results.Single();

        created.Status.ShouldBe(SyncOperationStatus.Applied);
        var detail = (await worker.GetFromJsonAsync<BauakteDetail>($"/api/v1/bauakten/{id}", TestJson.Options, Ct))!;
        detail.InitialInspectionWaived.ShouldBeTrue();
        detail.InitialInspectionWaivedReason.ShouldBe("Kleinfläche");
        (await HintsAsync(worker, id)).ShouldNotContain(h => h.Kind == "initial_inspection_missing");
    }

    [Fact]
    public async Task FullUpdate_KeepsWaiver()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = await CreateAsync(office, "Keller");
        using var waived = await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/initial-inspection-waiver", new { waived = true }, Ct);
        var detail = (await waived.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/bauakten/{bauakte.Id}")
        {
            Content = JsonContent.Create(new { title = "Keller neu", status = "planned" }),
        };
        request.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue($"\"{detail.Version}\""));
        using var updated = await office.SendAsync(request, Ct);

        updated.EnsureSuccessStatusCode();
        (await updated.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!.InitialInspectionWaived.ShouldBeTrue();
    }
}
