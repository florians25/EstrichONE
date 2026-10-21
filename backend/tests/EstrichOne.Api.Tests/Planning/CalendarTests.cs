using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Planning;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Planning;

/// <summary>Terminkalender (C4): Kalender, Sichtbarkeit, Serien mit Ausnahmen, Verschieben, Teilnehmer, Konflikte im Kolonnenplan.</summary>
public sealed class CalendarTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly string[] MondayThursday = ["monday", "thursday"];

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

    private static Task<List<CalendarOccurrence>> EventsAsync(HttpClient client, string from, string to) =>
        client.GetFromJsonAsync<List<CalendarOccurrence>>($"/api/v1/calendar/events?from={from}&to={to}", TestJson.Options, Ct)!;

    [Fact]
    public async Task Calendars_SharedSeededAndPersonalCreatedOnFirstUse_PersonalIsPrivate()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        using var worker = await _factory.CreateClientAsAsync("lukas", StandardRoles.SiteWorkerId);

        var calendars = (await office.GetFromJsonAsync<List<CalendarResponse>>("/api/v1/calendar/calendars", TestJson.Options, Ct))!;
        calendars.Where(c => c.Kind == CalendarKind.Shared).Select(c => c.Name).ShouldBe(["Firmenkalender", "Kundentermine"]);
        calendars.ShouldContain(c => c.Kind == CalendarKind.Personal && c.Name == "Mein Kalender" && c.CanEdit);
        calendars.Where(c => c.Kind == CalendarKind.Shared).ShouldAllBe(c => c.CanEdit);
        var personal = calendars.Single(c => c.Kind == CalendarKind.Personal);

        var workerCalendars = (await worker.GetFromJsonAsync<List<CalendarResponse>>("/api/v1/calendar/calendars", TestJson.Options, Ct))!;
        workerCalendars.ShouldNotContain(c => c.Id == personal.Id);
        workerCalendars.Where(c => c.Kind == CalendarKind.Shared).ShouldAllBe(c => !c.CanEdit);

        // Ein Monteur legt keinen gemeinsamen Kalender an, aber Termine im eigenen.
        using var shared = await worker.PutAsJsonAsync($"/api/v1/calendar/calendars/{Guid.CreateVersion7()}", new { name = "Baustelle", color = "#AA0808" }, Ct);
        shared.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var own = workerCalendars.Single(c => c.Kind == CalendarKind.Personal);
        await SendAsync<CalendarEventDetail>(worker, HttpMethod.Put, $"/api/v1/calendar/events/{Guid.CreateVersion7()}", new
        {
            calendarId = own.Id, title = "Zahnarzt", kind = "private", allDay = false, start = "2026-11-04T15:00:00", end = "2026-11-04T16:00:00",
        });
        using var foreign = await worker.PutAsJsonAsync($"/api/v1/calendar/events/{Guid.CreateVersion7()}", new
        {
            calendarId = calendars[0].Id, title = "Firmenfeier", kind = "meeting", allDay = true, start = "2026-11-04T00:00:00", end = "2026-11-05T00:00:00",
        }, Ct);
        foreign.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await EventsAsync(office, "2026-11-01", "2026-11-30")).ShouldNotContain(o => o.Title == "Zahnarzt");
        (await EventsAsync(worker, "2026-11-01", "2026-11-30")).ShouldHaveSingleItem().Title.ShouldBe("Zahnarzt");
    }

    [Fact]
    public async Task WeeklySeries_ExpandsWithExceptions_MoveOneOccurrence_DeleteFollowing()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var calendars = (await office.GetFromJsonAsync<List<CalendarResponse>>("/api/v1/calendar/calendars", TestJson.Options, Ct))!;
        var id = Guid.CreateVersion7();
        var series = await SendAsync<CalendarEventDetail>(office, HttpMethod.Put, $"/api/v1/calendar/events/{id}", new
        {
            calendarId = calendars[0].Id, title = "Baubesprechung", kind = "meeting", allDay = false,
            start = "2026-11-02T07:30:00", end = "2026-11-02T08:00:00",
            recurrence = new { frequency = "weekly", interval = 1, weekdays = MondayThursday, count = 6 },
        });
        series.Recurrence.Weekdays!.ShouldBe([DayOfWeek.Monday, DayOfWeek.Thursday]);

        var all = await EventsAsync(office, "2026-11-01", "2026-12-31");
        all.Select(o => o.Start.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture)).ShouldBe(["11-02 07:30", "11-05 07:30", "11-09 07:30", "11-12 07:30", "11-16 07:30", "11-19 07:30"]);
        all.ShouldAllBe(o => o.IsRecurring && o.CanEdit && o.CalendarName == "Firmenkalender");

        // Nur den Donnerstag 12.11. auf Freitag 9 Uhr ziehen und eine Stunde lang machen.
        var moved = await SendAsync<CalendarEventDetail>(office, HttpMethod.Post, $"/api/v1/calendar/events/{id}/move", new
        {
            start = "2026-11-13T09:00:00", end = "2026-11-13T10:00:00", allDay = false, occurrenceDate = "2026-11-12", scope = "occurrence",
        });
        moved.SeriesId.ShouldBe(id);
        moved.Recurrence.Frequency.ShouldBe(RecurrenceFrequency.None);
        all = await EventsAsync(office, "2026-11-01", "2026-12-31");
        all.Count.ShouldBe(6);
        all.ShouldContain(o => o.Start == new DateTime(2026, 11, 13, 9, 0, 0) && o.End == new DateTime(2026, 11, 13, 10, 0, 0) && !o.IsRecurring);
        all.ShouldNotContain(o => o.Start.Day == 12);

        // Ab 16.11. alle folgenden löschen.
        using var delete = await office.DeleteAsync(new Uri($"/api/v1/calendar/events/{id}?occurrence=2026-11-16&scope=following", UriKind.Relative), Ct);
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await EventsAsync(office, "2026-11-01", "2026-12-31")).Select(o => o.Start.Day).ShouldBe([2, 5, 9, 13]);

        // Ganze Serie eine Stunde später ziehen (am Vorkommen vom 05.11.).
        await SendAsync<CalendarEventDetail>(office, HttpMethod.Post, $"/api/v1/calendar/events/{id}/move", new
        {
            start = "2026-11-05T08:30:00", end = "2026-11-05T09:00:00", allDay = false, occurrenceDate = "2026-11-05", scope = "series",
        });
        (await EventsAsync(office, "2026-11-01", "2026-11-10")).Where(o => o.IsRecurring).Select(o => o.Start.ToString("dd HH:mm", CultureInfo.InvariantCulture)).ShouldBe(["02 08:30", "05 08:30", "09 08:30"]);
    }

    [Fact]
    public async Task MonthlyAndAllDay_AndValidation()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var calendars = (await office.GetFromJsonAsync<List<CalendarResponse>>("/api/v1/calendar/calendars", TestJson.Options, Ct))!;
        await SendAsync<CalendarEventDetail>(office, HttpMethod.Put, $"/api/v1/calendar/events/{Guid.CreateVersion7()}", new
        {
            calendarId = calendars[0].Id, title = "Monatsabschluss", kind = "other", allDay = true, start = "2026-01-31T00:00:00", end = "2026-02-01T00:00:00",
            recurrence = new { frequency = "monthly", until = "2026-06-30" },
        });
        var occurrences = await EventsAsync(office, "2026-01-01", "2026-12-31");
        occurrences.Select(o => o.Start.Month).ShouldBe([1, 3, 5], "Monate ohne 31. Tag fallen aus.");
        occurrences.ShouldAllBe(o => o.AllDay && o.End - o.Start == TimeSpan.FromDays(1));

        using var backwards = await office.PutAsJsonAsync($"/api/v1/calendar/events/{Guid.CreateVersion7()}", new
        {
            calendarId = calendars[0].Id, title = "Falsch", kind = "other", allDay = false, start = "2026-11-02T10:00:00", end = "2026-11-02T09:00:00",
        }, Ct);
        backwards.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var range = await office.GetAsync(new Uri("/api/v1/calendar/events?from=2026-01-01&to=2027-12-31", UriKind.Relative), Ct);
        range.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Participants_SeeEvent_AndPlanningWarnsAboutAppointmentConflict()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var lukas = await _factory.CreateUserAsync("lukas", roleIds: StandardRoles.SiteWorkerId);
        var crewId = Guid.CreateVersion7();
        await SendAsync<CrewResponse>(office, HttpMethod.Put, $"/api/v1/planning/crews/{crewId}", new { name = "Kolonne Nord", leaderUserId = lukas.Id });
        using var created = await office.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title = "Halle Nord", city = "Landshut" }, Ct);
        var bauakte = (await created.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!.Id;

        var personal = (await office.GetFromJsonAsync<List<CalendarResponse>>("/api/v1/calendar/calendars", TestJson.Options, Ct))!
            .Single(c => c.Kind == CalendarKind.Personal);
        await SendAsync<CalendarEventDetail>(office, HttpMethod.Put, $"/api/v1/calendar/events/{Guid.CreateVersion7()}", new
        {
            calendarId = personal.Id, title = "Staplerschulung", kind = "training", allDay = false, start = "2026-11-04T08:00:00", end = "2026-11-04T12:00:00",
            crewIds = new[] { crewId }, bauakteId = bauakte, location = "Lager",
        });

        using var worker = _factory.CreateClient();
        worker.WithToken((await worker.LoginOkAsync("lukas")).AccessToken);
        var seen = (await EventsAsync(worker, "2026-11-04", "2026-11-04")).ShouldHaveSingleItem();
        seen.Title.ShouldBe("Staplerschulung");
        seen.CanEdit.ShouldBeFalse();
        seen.People.ShouldHaveSingleItem().Id.ShouldBe(lukas.Id);
        seen.BauakteTitle.ShouldBeNull("Die Bauakte ist für den Monteur nicht sichtbar.");

        // Nachmittags-Einsatz überschneidet sich nicht, vormittags schon.
        var afternoon = await SendAsync<AssignmentView>(office, HttpMethod.Put, $"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
        {
            bauakteId = bauakte, day = "2026-11-04", startTime = "13:00:00", endTime = "16:00:00", activity = "Estrich einbringen", crewId,
        });
        afternoon.Warnings.ShouldBeEmpty();
        var morning = await SendAsync<AssignmentView>(office, HttpMethod.Post, $"/api/v1/planning/assignments/{afternoon.Id}/move", new
        {
            day = "2026-11-04", startTime = "07:00:00", endTime = "09:00:00",
        });
        morning.Warnings.ShouldContain("Max Huber hat zur selben Zeit den Termin „Staplerschulung“.");

        var range = await office.GetFromJsonAsync<PlanningRange>("/api/v1/planning/range?from=2026-11-02&to=2026-11-08", TestJson.Options, Ct);
        range!.Appointments.ShouldNotBeNull().ShouldHaveSingleItem().Crews.ShouldHaveSingleItem().Name.ShouldBe("Kolonne Nord");
    }
}
