using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Planning;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Planning;

/// <summary>Disposition wie Estrich Web: Terminarten mit Ende, Baustellen-Vorrat (frei/verplant) mit Fläche und Aufbau.</summary>
public sealed class BacklogTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_factory.Clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")).DateTime);

    private static async Task<BauakteDetail> BauakteAsync(HttpClient client, string title, string? status = null)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title, city = "Landshut", status }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;
    }

    [Fact]
    public async Task Backlog_ShowsPlannedSitesWithAreaAndScreed_AndWhetherTheyAreScheduled()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var me = await _factory.CreateUserAsync("lukas", roleIds: StandardRoles.SiteWorkerId);
        var free = await BauakteAsync(office, "EFH frei");
        var scheduled = await BauakteAsync(office, "Halle verplant");
        await BauakteAsync(office, "Nur Anfrage", "inquiry");
        var types = await office.GetFromJsonAsync<List<ScreedTypeResponse>>("/api/v1/screed-types", TestJson.Options, Ct);
        var roomId = Guid.CreateVersion7();
        (await office.PutAsJsonAsync($"/api/v1/bauakten/{free.Id}/rooms/{roomId}", new
        {
            id = roomId, name = "Wohnen", areaSquareMeters = 42.5m, screedTypeId = types![0].Id, underfloorHeating = true, status = "planned",
        }, Ct)).EnsureSuccessStatusCode();
        (await office.PutAsJsonAsync($"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
        {
            bauakteId = scheduled.Id, day = Today.AddDays(2), activity = "Erstbegehung", kind = "initialInspection", userIds = new[] { me.Id },
            startTime = "08:00:00", endTime = "09:30:00",
        }, Ct)).EnsureSuccessStatusCode();

        var backlog = (await office.GetFromJsonAsync<List<BacklogItem>>("/api/v1/planning/backlog", TestJson.Options, Ct))!;

        backlog!.Select(b => b.Title).ShouldBe(["EFH frei", "Halle verplant"], ignoreOrder: true);
        var item = backlog!.Single(b => b.BauakteId == free.Id);
        item.TotalAreaSquareMeters.ShouldBe(42.5m);
        item.ScreedTypes.ShouldHaveSingleItem();
        item.UpcomingAssignments.ShouldBe(0);
        var planned = backlog.Single(b => b.BauakteId == scheduled.Id);
        planned.UpcomingAssignments.ShouldBe(1);
        planned.NextAssignment.ShouldBe(Today.AddDays(2));
        (await office.GetFromJsonAsync<List<BacklogItem>>("/api/v1/planning/backlog?search=halle", TestJson.Options, Ct))!.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Assignment_KeepsKindAndEndTime_AndRejectsEndBeforeStart()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var me = await _factory.CreateUserAsync("lukas", roleIds: StandardRoles.SiteWorkerId);
        var bauakte = await BauakteAsync(office, "EFH");
        var id = Guid.CreateVersion7();

        using var saved = await office.PutAsJsonAsync($"/api/v1/planning/assignments/{id}", new
        {
            bauakteId = bauakte.Id, day = Today, activity = "CM-Messung Bad", kind = "cmMeasurement", userIds = new[] { me.Id },
            startTime = "10:00:00", endTime = "10:30:00",
        }, Ct);
        using var invalid = await office.PutAsJsonAsync($"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
        {
            bauakteId = bauakte.Id, day = Today, activity = "Falsch", userIds = new[] { me.Id }, startTime = "10:00:00", endTime = "09:00:00",
        }, Ct);

        var view = (await saved.Content.ReadFromJsonAsync<AssignmentView>(TestJson.Options, Ct))!;
        view.Kind.ShouldBe(AssignmentKind.CmMeasurement);
        view.EndTime.ShouldBe(new TimeOnly(10, 30));
        view.BauakteStatus.ShouldBe(BauakteStatus.Planned);
        invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
