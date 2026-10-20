using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Planning;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Planning;

/// <summary>Einsatzplanung (AP-19): Kolonnen, Geräte, Abwesenheiten, Konflikthinweise, „Mein Einsatz“.</summary>
public sealed class PlanningTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static async Task<T> PutAsync<T>(HttpClient client, string url, object body)
    {
        using var response = await client.PutAsJsonAsync(url, body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Ct))!;
    }

    private static async Task<Guid> BauakteAsync(HttpClient client, string title)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title, city = "Landshut" }, Ct);
        return (await response.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!.Id;
    }

    [Fact]
    public async Task WeekPlan_WarnsAboutDoubleBookingAndAbsence_AndWorkerSeesOwnAssignments()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var lukas = await _factory.CreateUserAsync("lukas", roleIds: StandardRoles.SiteWorkerId);
        var anna = await _factory.CreateUserAsync("anna", roleIds: StandardRoles.SiteWorkerId);
        var crewId = Guid.CreateVersion7();
        var crew = await PutAsync<CrewResponse>(office, $"/api/v1/planning/crews/{crewId}", new
        {
            name = "Kolonne Huber", leaderUserId = lukas.Id, memberUserIds = new[] { anna.Id }, color = "#0062CC",
        });
        crew.MemberUserIds.ShouldBe([anna.Id, lukas.Id], ignoreOrder: true);
        var pump = Guid.CreateVersion7();
        await PutAsync<ResourceResponse>(office, $"/api/v1/planning/resources/{pump}", new { kind = "pump", name = "Mixokret 2", identifier = "LA-EO 12" });
        await PutAsync<AbsenceView>(office, $"/api/v1/planning/absences/{Guid.CreateVersion7()}", new
        {
            userId = anna.Id, from = "2026-11-03", to = "2026-11-04", kind = "vacation",
        });
        var halle = await BauakteAsync(office, "Halle Nord");
        var efh = await BauakteAsync(office, "EFH Müller");

        var first = await PutAsync<AssignmentView>(office, $"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
        {
            bauakteId = halle, day = "2026-11-02", startTime = "07:00:00", activity = "Estrich einbringen EG", crewId, resourceIds = new[] { pump },
        });
        first.Warnings.ShouldBeEmpty();
        first.People.Select(p => p.Id).ShouldBe([anna.Id, lukas.Id], ignoreOrder: true);
        first.Resources.ShouldHaveSingleItem().Name.ShouldBe("Mixokret 2");

        var second = await PutAsync<AssignmentView>(office, $"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
        {
            bauakteId = efh, day = "2026-11-02", activity = "Randdämmung stellen", userIds = new[] { lukas.Id }, resourceIds = new[] { pump },
        });
        second.Warnings.ShouldContain("Max Huber ist an diesem Tag mehrfach eingeplant.");
        second.Warnings.ShouldContain("Mixokret 2 ist an diesem Tag mehrfach eingeplant.");
        var absent = await PutAsync<AssignmentView>(office, $"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
        {
            bauakteId = efh, day = "2026-11-03", activity = "Estrich einbringen", crewId,
        });
        absent.Warnings.ShouldContain("Max Huber ist im Urlaub.");

        var week = await office.GetFromJsonAsync<PlanningRange>("/api/v1/planning/range?from=2026-11-02&to=2026-11-08", TestJson.Options, Ct);
        week!.Assignments.Count.ShouldBe(3);
        week.Absences.ShouldHaveSingleItem().Kind.ShouldBe(AbsenceKind.Vacation);

        using var worker = _factory.CreateClient();
        worker.WithToken((await worker.LoginOkAsync("anna")).AccessToken);
        var mine = await worker.GetFromJsonAsync<PlanningRange>("/api/v1/planning/mine?from=2026-11-02&to=2026-11-02", TestJson.Options, Ct);
        mine!.Assignments.ShouldHaveSingleItem().BauakteTitle.ShouldBe("Halle Nord");
        mine.Assignments[0].StartTime.ShouldBe(new TimeOnly(7, 0));
        mine.Assignments[0].BauakteAddress.ShouldBe("Landshut");

        using var planAsWorker = await worker.PutAsJsonAsync($"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
        {
            bauakteId = halle, day = "2026-11-05", activity = "x", userIds = new[] { anna.Id },
        }, Ct);
        planAsWorker.StatusCode.ShouldBe(HttpStatusCode.Forbidden, "Planen darf nur das Büro.");
    }

    [Fact]
    public async Task Validation_RangeAndAssignmentWithoutPeople()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = await BauakteAsync(office, "Garage");

        using var noPeople = await office.PutAsJsonAsync($"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
        {
            bauakteId = bauakte, day = "2026-11-02", activity = "Estrich",
        }, Ct);
        using var tooLong = await office.GetAsync(new Uri("/api/v1/planning/range?from=2026-01-01&to=2026-12-31", UriKind.Relative), Ct);
        using var backwards = await office.PutAsJsonAsync($"/api/v1/planning/absences/{Guid.CreateVersion7()}", new
        {
            userId = (await _factory.CreateUserAsync("x", roleIds: StandardRoles.SiteWorkerId)).Id, from = "2026-11-05", to = "2026-11-01", kind = "sick",
        }, Ct);

        noPeople.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        tooLong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        backwards.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
