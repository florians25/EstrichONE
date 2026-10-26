using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using EstrichOne.Api.Modules.Planning;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Planning;

/// <summary>„Wer fährt wohin“ (C3) und Teamplaner (C2): Einteilen per Ziehen, Kolonnenmitglied abziehen, Fahrzeuge, PDF, App-Sicht.</summary>
public sealed class DispatchTests(PostgresFixture postgres) : IAsyncLifetime
{
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

    private async Task<Guid> BauakteAsync(HttpClient client, string title, bool cancelled = false)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title, city = "Landshut", street = "Am Hang 3" }, Ct);
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!.Id;
        if (cancelled)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<Bauakte>().Where(b => b.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, BauakteStatus.Cancelled), Ct);
        }

        return id;
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

    [Fact]
    public async Task Dispatch_MovesPeopleAndVehicles_BetweenSites_AndWorkerSeesOwnSite()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var lukas = await UserAsync("lukas", "Lukas Berger");
        var anna = await UserAsync("anna", "Anna Kraus");
        var crewId = Guid.CreateVersion7();
        await SendAsync<CrewResponse>(office, HttpMethod.Put, $"/api/v1/planning/crews/{crewId}", new { name = "Kolonne Nord", leaderUserId = lukas.Id, memberUserIds = new[] { anna.Id } });
        var truck = Guid.CreateVersion7();
        await SendAsync<ResourceResponse>(office, HttpMethod.Put, $"/api/v1/planning/resources/{truck}", new { kind = "vehicle", name = "Sprinter 1", identifier = "LA-EO 1" });
        var halle = await BauakteAsync(office, "Halle Nord");
        var efh = await BauakteAsync(office, "EFH Müller");
        await SendAsync<AssignmentView>(office, HttpMethod.Put, $"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
        {
            bauakteId = halle, day = "2026-11-04", startTime = "07:00:00", activity = "Estrich einbringen EG", crewId, resourceIds = new[] { truck },
        });

        var board = await office.GetFromJsonAsync<DispatchBoard>("/api/v1/planning/dispatch/2026-11-04", TestJson.Options, Ct);
        var site = board!.Sites.ShouldHaveSingleItem();
        site.People.Select(p => p.Name).ShouldBe(["Anna Kraus", "Lukas Berger"]);
        site.Vehicles.ShouldHaveSingleItem().Name.ShouldBe("Sprinter 1");
        site.NavigationUrl.ShouldNotBeNull().ShouldContain("openstreetmap.org");
        board.People.Single(p => p.Id == anna.Id).SiteIds.ShouldBe([halle]);
        board.CanPlan.ShouldBeTrue();

        // Anna fährt heute zum EFH (noch kein Einsatz dort → wird angelegt), sie fehlt dann in der Kolonne auf der Halle.
        board = await SendAsync<DispatchBoard>(office, HttpMethod.Post, "/api/v1/planning/dispatch/2026-11-04/moves", new
        {
            target = "person", id = anna.Id, toBauakteId = efh, activity = "Randdämmung stellen",
        });
        board.Sites.Count.ShouldBe(2);
        board.Sites.Single(s => s.BauakteId == halle).People.Select(p => p.Name).ShouldBe(["Lukas Berger"]);
        var efhSite = board.Sites.Single(s => s.BauakteId == efh);
        efhSite.People.ShouldHaveSingleItem().Id.ShouldBe(anna.Id);
        efhSite.Jobs.ShouldHaveSingleItem().Activity.ShouldBe("Randdämmung stellen");

        // Sprinter mitnehmen.
        board = await SendAsync<DispatchBoard>(office, HttpMethod.Post, "/api/v1/planning/dispatch/2026-11-04/moves", new { target = "vehicle", id = truck, toBauakteId = efh });
        board.Sites.Single(s => s.BauakteId == efh).Vehicles.ShouldHaveSingleItem();
        board.Sites.Single(s => s.BauakteId == halle).Vehicles.ShouldBeEmpty();

        // Die Kolonnenplanung sieht Anna nicht mehr auf der Halle (Ausnahme), aber auf dem EFH.
        var range = await office.GetFromJsonAsync<PlanningRange>("/api/v1/planning/range?from=2026-11-04&to=2026-11-04", TestJson.Options, Ct);
        range!.Assignments.Single(a => a.BauakteId == halle).ExcludedUserIds.ShouldBe([anna.Id]);
        range.Assignments.SelectMany(a => a.Warnings).ShouldBeEmpty();

        // Zurück auf die Halle: wieder Teil der Kolonne.
        board = await SendAsync<DispatchBoard>(office, HttpMethod.Post, "/api/v1/planning/dispatch/2026-11-04/moves", new { target = "person", id = anna.Id, toBauakteId = halle });
        board.Sites.Single(s => s.BauakteId == halle).People.Count.ShouldBe(2);
        board.Sites.Single(s => s.BauakteId == efh).Warnings.ShouldContain("Noch niemand eingeteilt.");

        // App: Lukas sieht seine Baustelle.
        using var worker = _factory.CreateClient();
        worker.WithToken((await worker.LoginOkAsync("lukas")).AccessToken);
        var mine = await worker.GetFromJsonAsync<DispatchBoard>("/api/v1/planning/dispatch/2026-11-04", TestJson.Options, Ct);
        mine!.MySiteIds.ShouldBe([halle]);
        mine.CanPlan.ShouldBeFalse();
        using var forbidden = await worker.PostAsJsonAsync("/api/v1/planning/dispatch/2026-11-04/moves", new { target = "person", id = lukas.Id, toBauakteId = efh }, Ct);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var pdf = await office.GetAsync(new Uri("/api/v1/planning/dispatch/2026-11-04/print.pdf", UriKind.Relative), Ct);
        pdf.StatusCode.ShouldBe(HttpStatusCode.OK);
        pdf.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("Wer fährt wohin 2026-11-04.pdf");
        System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync(Ct), 0, 5).ShouldBe("%PDF-");

        using var heute = await office.GetAsync(new Uri("/api/v1/planning/dispatch/heute", UriKind.Relative), Ct);
        heute.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var invalid = await office.GetAsync(new Uri("/api/v1/planning/dispatch/gestern", UriKind.Relative), Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Dispatch_ByCrew_RelocatesWithVehicles_SeveralCrewsPerSite_WarnsWhenTwice_AndRemoves()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var lukas = await UserAsync("lukas", "Lukas Berger");
        var anna = await UserAsync("anna", "Anna Kraus");
        var tom = await UserAsync("tom", "Tom Weber");
        var nord = Guid.CreateVersion7();
        var sued = Guid.CreateVersion7();
        await SendAsync<CrewResponse>(office, HttpMethod.Put, $"/api/v1/planning/crews/{nord}", new { name = "Kolonne Nord", color = "#1565c0", leaderUserId = lukas.Id, memberUserIds = new[] { anna.Id } });
        await SendAsync<CrewResponse>(office, HttpMethod.Put, $"/api/v1/planning/crews/{sued}", new { name = "Kolonne Süd", leaderUserId = tom.Id, memberUserIds = Array.Empty<Guid>() });
        var truck = Guid.CreateVersion7();
        await SendAsync<ResourceResponse>(office, HttpMethod.Put, $"/api/v1/planning/resources/{truck}", new { kind = "vehicle", name = "Sprinter 1" });
        var halle = await BauakteAsync(office, "Halle Nord");
        var efh = await BauakteAsync(office, "EFH Müller");
        await SendAsync<AssignmentView>(office, HttpMethod.Put, $"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
        {
            bauakteId = halle, day = "2026-11-05", startTime = "07:00:00", activity = "Estrich einbringen EG", crewId = nord, resourceIds = new[] { truck },
        });
        const string moves = "/api/v1/planning/dispatch/2026-11-05/moves";

        var board = await office.GetFromJsonAsync<DispatchBoard>("/api/v1/planning/dispatch/2026-11-05", TestJson.Options, Ct);
        var crew = board!.Crews.Single(c => c.Id == nord);
        crew.LeaderName.ShouldBe("Lukas Berger");
        crew.MemberCount.ShouldBe(2);
        crew.Color.ShouldBe("#1565c0");
        crew.SiteIds.ShouldBe([halle]);
        board.Crews.Single(c => c.Id == sued).SiteIds.ShouldBeEmpty();
        board.Sites.ShouldHaveSingleItem().Crews.ShouldHaveSingleItem().Activity.ShouldBe("Estrich einbringen EG");

        // Kolonne Nord fährt zum EFH: Einsatz wandert mit Fahrzeug und Tätigkeit, neue Startzeit.
        board = await SendAsync<DispatchBoard>(office, HttpMethod.Post, moves, new { target = "crew", id = nord, toBauakteId = efh, startTime = "06:30:00" });
        var efhSite = board.Sites.ShouldHaveSingleItem();
        efhSite.BauakteId.ShouldBe(efh);
        efhSite.Crews.ShouldHaveSingleItem().StartTime.ShouldBe(new TimeOnly(6, 30));
        efhSite.Vehicles.ShouldHaveSingleItem().Id.ShouldBe(truck);
        efhSite.People.Count.ShouldBe(2);

        // Kolonne Süd ebenfalls zum EFH (mehrere Kolonnen je Baustelle), mit eigener Tätigkeit.
        board = await SendAsync<DispatchBoard>(office, HttpMethod.Post, moves, new { target = "crew", id = sued, toBauakteId = efh, activity = "Randdämmung stellen" });
        efhSite = board.Sites.ShouldHaveSingleItem();
        efhSite.Crews.Select(c => c.Name).ShouldBe(["Kolonne Nord", "Kolonne Süd"], ignoreOrder: true);
        efhSite.Crews.Single(c => c.Id == sued).Activity.ShouldBe("Randdämmung stellen");

        // Zusätzlich auf die Halle (keep): mehrere Baustellen am Tag; Überschneidung nur bei gleicher Zeit.
        board = await SendAsync<DispatchBoard>(office, HttpMethod.Post, moves, new { target = "crew", id = sued, toBauakteId = halle, keep = true });
        board.Crews.Single(c => c.Id == sued).SiteIds.Count.ShouldBe(2);
        board.Crews.Single(c => c.Id == sued).Warnings.ShouldNotBeNull().ShouldContain(w => w.StartsWith("Überschneidung"));
        board = await SendAsync<DispatchBoard>(office, HttpMethod.Post, moves, new { target = "crew", id = sued, toBauakteId = efh, keep = true, startTime = "07:00:00", endTime = "11:00:00" });
        board = await SendAsync<DispatchBoard>(office, HttpMethod.Post, moves, new { target = "crew", id = sued, toBauakteId = halle, keep = true, startTime = "12:00:00", endTime = "16:00:00" });
        var sued2 = board.Crews.Single(c => c.Id == sued);
        sued2.Warnings.ShouldNotBeNull().ShouldBeEmpty();
        sued2.SiteIds.ShouldBe([efh, halle]);
        board.Sites.Single(s => s.BauakteId == halle).Crews.Single(c => c.Id == sued).EndTime.ShouldBe(new TimeOnly(16, 0));
        var pool = await office.GetFromJsonAsync<List<DispatchPoolSite>>("/api/v1/planning/dispatch/2026-11-05/pool?search=Halle", TestJson.Options, Ct);
        pool!.ShouldHaveSingleItem().CrewIds.ShouldContain(sued);

        // Von der Halle wieder herausnehmen.
        board = await SendAsync<DispatchBoard>(office, HttpMethod.Post, moves, new { target = "crew", id = sued, toBauakteId = (Guid?)null, fromBauakteId = halle });
        board.Crews.Single(c => c.Id == sued).SiteIds.ShouldBe([efh]);
        board.Sites.ShouldHaveSingleItem();

        using var unknown = await office.PostAsJsonAsync(moves, new { target = "crew", id = Guid.CreateVersion7(), toBauakteId = efh }, Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var pdf = await office.GetAsync(new Uri("/api/v1/planning/dispatch/2026-11-05/print.pdf", UriKind.Relative), Ct);
        pdf.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TeamPlanner_MoveResizeSwapPerson_TimeAwareConflicts_CancelledBauakteRejected()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var lukas = await UserAsync("lukas", "Lukas Berger");
        var anna = await UserAsync("anna", "Anna Kraus");
        var halle = await BauakteAsync(office, "Halle Nord");
        var efh = await BauakteAsync(office, "EFH Müller");

        var first = await SendAsync<AssignmentView>(office, HttpMethod.Put, $"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
        {
            bauakteId = halle, day = "2026-11-04", startTime = "07:00:00", endTime = "10:00:00", activity = "Erstbegehung", kind = "initialInspection", userIds = new[] { lukas.Id },
        });
        var second = await SendAsync<AssignmentView>(office, HttpMethod.Put, $"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
        {
            bauakteId = efh, day = "2026-11-04", startTime = "11:00:00", endTime = "12:00:00", activity = "Aufmaß", kind = "survey", userIds = new[] { lukas.Id },
        });
        second.Warnings.ShouldBeEmpty("Zeiten überschneiden sich nicht.");

        // Dauer ziehen bis 11:30 → Überschneidung.
        var resized = await SendAsync<AssignmentView>(office, HttpMethod.Post, $"/api/v1/planning/assignments/{first.Id}/move", new
        {
            day = "2026-11-04", startTime = "07:00:00", endTime = "11:30:00",
        });
        resized.Warnings.ShouldContain("Lukas Berger ist an diesem Tag mehrfach eingeplant.");

        // In die Zeile von Anna ziehen: Person tauschen.
        var swapped = await SendAsync<AssignmentView>(office, HttpMethod.Post, $"/api/v1/planning/assignments/{first.Id}/move", new
        {
            day = "2026-11-05", startTime = "08:00:00", endTime = "09:00:00", fromUserId = lukas.Id, toUserId = anna.Id,
        });
        swapped.Day.ShouldBe(new DateOnly(2026, 11, 5));
        swapped.People.ShouldHaveSingleItem().Id.ShouldBe(anna.Id);
        swapped.Warnings.ShouldBeEmpty();

        using var backwards = await office.PostAsJsonAsync($"/api/v1/planning/assignments/{first.Id}/move", new { day = "2026-11-05", startTime = "09:00:00", endTime = "08:00:00" }, Ct);
        backwards.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var cancelled = await BauakteAsync(office, "Storniert", cancelled: true);
        using var plan = await office.PutAsJsonAsync($"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
        {
            bauakteId = cancelled, day = "2026-11-04", activity = "Estrich", userIds = new[] { anna.Id },
        }, Ct);
        plan.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var unknown = await office.PutAsJsonAsync($"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
        {
            bauakteId = Guid.CreateVersion7(), day = "2026-11-04", activity = "Phantasie", userIds = new[] { anna.Id },
        }, Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
