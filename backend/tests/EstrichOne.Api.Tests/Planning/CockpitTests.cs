using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Planning;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Planning;

/// <summary>Disponenten-Cockpit (W6) und Schnelldisposition (I3): Batch, Konfliktprüfung, Tagesbesetzung, Reihenfolge, Freigabe.</summary>
public sealed class CockpitTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Day = "2026-11-04";
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string url, object body)
    {
        using var response = await client.SendAsync(new HttpRequestMessage(method, url) { Content = JsonContent.Create(body) }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Ct))!;
    }

    private static async Task<Guid> BauakteAsync(HttpClient client, string title)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title, city = "Landshut", street = "Am Hang 3" }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!.Id;
    }

    private async Task<User> UserAsync(string userName, string displayName)
    {
        var user = await _factory.CreateUserAsync(userName, roleIds: StandardRoles.SiteWorkerId);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var tracked = await db.Set<User>().SingleAsync(u => u.Id == user.Id, Ct);
        tracked.Rename(displayName);
        await db.SaveChangesAsync(Ct);
        return tracked;
    }

    private static object Job(Guid bauakte, Guid? crewId, string activity, string start, string? end = null, string day = Day) =>
        new { bauakteId = bauakte, day, startTime = start, endTime = end, activity, crewId };

    [Fact]
    public async Task Cockpit_PlansInBatch_ChecksConflicts_SwapsRoster_OrdersStops_AndMarksChangesAfterRelease()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var lukas = await UserAsync("lukas", "Lukas Berger");
        var anna = await UserAsync("anna", "Anna Kraus");
        var max = await UserAsync("max", "Max Huber");
        var nord = Guid.CreateVersion7();
        var sued = Guid.CreateVersion7();
        await SendAsync<CrewResponse>(office, HttpMethod.Put, $"/api/v1/planning/crews/{nord}", new { name = "Kolonne Nord", leaderUserId = lukas.Id, memberUserIds = new[] { anna.Id } });
        await SendAsync<CrewResponse>(office, HttpMethod.Put, $"/api/v1/planning/crews/{sued}", new { name = "Kolonne Süd", leaderUserId = max.Id, sortOrder = 1 });
        var truck = Guid.CreateVersion7();
        await SendAsync<ResourceResponse>(office, HttpMethod.Put, $"/api/v1/planning/resources/{truck}", new { kind = "vehicle", name = "Sprinter 1", crewId = nord });
        var halle = await BauakteAsync(office, "Halle Nord");
        var efh = await BauakteAsync(office, "EFH Müller");

        // Konfliktprüfung ohne Speichern: Anna hat Urlaub.
        await SendAsync<AbsenceView>(office, HttpMethod.Put, $"/api/v1/planning/absences/{Guid.CreateVersion7()}", new { userId = anna.Id, from = Day, to = Day, kind = "vacation" });
        var check = await SendAsync<PlanCheckResult>(office, HttpMethod.Post, "/api/v1/planning/check", Job(halle, nord, "Estrich einbringen EG", "07:00:00"));
        check.Warnings.ShouldContain("Anna Kraus ist im Urlaub.");
        (await office.GetFromJsonAsync<PlanningRange>($"/api/v1/planning/range?from={Day}&to={Day}", TestJson.Options, Ct))!.Assignments.ShouldBeEmpty();

        // Zwei Baustellen für Kolonne Nord in einem Schritt (Ziehen aus dem Vorrat, Kopieren).
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var batch = await SendAsync<PlanBatchResult>(office, HttpMethod.Post, "/api/v1/planning/batch", new
        {
            operations = new object[]
            {
                new { op = "upsert", id = first, assignment = Job(halle, nord, "Estrich einbringen EG", "07:00:00", "11:00:00") },
                new { op = "upsert", id = second, assignment = Job(efh, nord, "Randdämmung", "12:00:00", "15:00:00") },
            },
        });
        batch.Assignments.Count.ShouldBe(2);
        batch.DryRun.ShouldBeFalse();

        var board = await office.GetFromJsonAsync<CockpitBoard>($"/api/v1/planning/cockpit?from={Day}&to=2026-11-06", TestJson.Options, Ct);
        board!.Days.Count.ShouldBe(3);
        board.CanPlan.ShouldBeTrue();
        var cell = board.Crews.Single(c => c.Id == nord).Cells.Single(c => c.Day == new DateOnly(2026, 11, 4));
        cell.AssignmentIds.ShouldBe([first, second]);
        cell.PlannedMinutes.ShouldBe(7 * 60);
        cell.CapacityMinutes.ShouldBe(CockpitService.DayMinutes);
        cell.AvailableMembers.ShouldBe(1);
        cell.Vehicles.ShouldHaveSingleItem().Name.ShouldBe("Sprinter 1");
        cell.Warnings.ShouldContain("Anna Kraus: Urlaub.");

        // Fahrtreihenfolge umdrehen, EFH zuerst um 06:30.
        var plan = await SendAsync<DayPlan>(office, HttpMethod.Put, $"/api/v1/planning/crews/{nord}/days/{Day}/order",
            new { assignmentIds = new[] { second, first }, startTimes = new[] { "06:30:00", null } });
        var stops = plan.Crews.Single(c => c.Id == nord).Stops;
        stops.Select(s => s.AssignmentId).ShouldBe([second, first]);
        stops[0].StartTime.ShouldBe(new TimeOnly(6, 30));
        stops[0].EndTime.ShouldBe(new TimeOnly(9, 30));
        stops[0].NavigationUrl.ShouldNotBeNull().ShouldContain("openstreetmap.org");

        // Tagesbesetzung: Anna (Urlaub) gegen Max tauschen – Anna wandert im Gegenzug zur Kolonne Süd.
        plan = await SendAsync<DayPlan>(office, HttpMethod.Post, $"/api/v1/planning/crews/{nord}/days/{Day}/swap", new { fromUserId = anna.Id, toUserId = max.Id });
        var nordDay = plan.Crews.Single(c => c.Id == nord);
        nordDay.RosterChanged.ShouldBeTrue();
        nordDay.Members.Select(m => m.Name).ShouldBe(["Lukas Berger", "Max Huber"]);
        plan.Crews.Single(c => c.Id == sued).Members.Select(m => m.Name).ShouldBe(["Anna Kraus"]);
        nordDay.Stops.SelectMany(s => s.People).Select(p => p.Name).Distinct().ShouldBe(["Lukas Berger", "Max Huber"], ignoreOrder: true);
        nordDay.Warnings.ShouldNotContain("Anna Kraus: Urlaub.");

        // Freigabe: Stand festhalten; danach geänderte, neue und entfernte Einsätze markieren.
        var releases = await SendAsync<List<PlanReleaseInfo>>(office, HttpMethod.Post, "/api/v1/planning/releases", new { from = Day, to = Day, note = "Plan KW 45" });
        releases.ShouldHaveSingleItem().ChangedCount.ShouldBe(0);
        await SendAsync<PlanBatchResult>(office, HttpMethod.Post, "/api/v1/planning/batch", new
        {
            operations = new object[]
            {
                new { op = "move", id = first, setTime = true, startTime = "10:00:00", endTime = "14:00:00" },
                new { op = "delete", id = second },
            },
        });
        plan = (await office.GetFromJsonAsync<DayPlan>($"/api/v1/planning/day/{Day}", TestJson.Options, Ct))!;
        plan.Release.ShouldNotBeNull().ChangedCount.ShouldBe(2);
        plan.Release.Removed.ShouldHaveSingleItem().Title.ShouldBe("EFH Müller");
        plan.Crews.Single(c => c.Id == nord).Stops.ShouldHaveSingleItem().Change.ShouldBe("changed");

        // Probelauf ändert nichts.
        var dry = await SendAsync<PlanBatchResult>(office, HttpMethod.Post, "/api/v1/planning/batch", new
        {
            dryRun = true, operations = new object[] { new { op = "move", id = first, day = "2026-11-05" } },
        });
        dry.DryRun.ShouldBeTrue();
        dry.Assignments.ShouldHaveSingleItem().Day.ShouldBe(new DateOnly(2026, 11, 5));
        (await office.GetFromJsonAsync<PlanningRange>($"/api/v1/planning/range?from={Day}&to={Day}", TestJson.Options, Ct))!.Assignments.ShouldHaveSingleItem();

        // Kolonnen tauschen und nächsten freien Tag suchen.
        plan = await SendAsync<DayPlan>(office, HttpMethod.Post, $"/api/v1/planning/days/{Day}/swap-crews", new { crewA = nord, crewB = sued });
        plan.Crews.Single(c => c.Id == sued).Stops.ShouldHaveSingleItem().AssignmentId.ShouldBe(first);
        var moved = await SendAsync<AssignmentView>(office, HttpMethod.Post, $"/api/v1/planning/assignments/{first}/next-free-day", new { });
        moved.Day.ShouldBe(new DateOnly(2026, 11, 5));

        // Alles oder nichts: ein ungültiger Schritt nimmt den ganzen Stapel zurück.
        using var invalid = await office.PostAsJsonAsync("/api/v1/planning/batch", new
        {
            operations = new object[]
            {
                new { op = "delete", id = first },
                new { op = "move", id = Guid.CreateVersion7(), day = Day },
            },
        }, Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await office.GetFromJsonAsync<PlanningRange>("/api/v1/planning/range?from=2026-11-05&to=2026-11-05", TestJson.Options, Ct))!.Assignments.ShouldHaveSingleItem();

        // Mitarbeiter (App): sieht den Tagesplan, darf aber nicht disponieren.
        using var worker = _factory.CreateClient();
        worker.WithToken((await worker.LoginOkAsync("max")).AccessToken);
        var mine = (await worker.GetFromJsonAsync<DayPlan>($"/api/v1/planning/day/{Day}", TestJson.Options, Ct))!;
        mine.CanPlan.ShouldBeFalse();
        mine.MyCrewIds.ShouldContain(nord);
        using var forbidden = await worker.PostAsJsonAsync("/api/v1/planning/batch", new { operations = new object[] { new { op = "delete", id = first } } }, Ct);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var reset = await office.DeleteAsync(new Uri($"/api/v1/planning/crews/{nord}/days/{Day}/roster", UriKind.Relative), Ct);
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using var withdraw = await office.DeleteAsync(new Uri($"/api/v1/planning/releases/{Day}", UriKind.Relative), Ct);
        withdraw.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
