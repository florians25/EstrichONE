using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Helpdesk;
using EstrichOne.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Helpdesk;

/// <summary>
/// Arbeitsplatz der IT: automatische Zuweisung, SLA-Ziele, Ansichten und Suche, Sammelbearbeitung, Zusammenführen,
/// Verknüpfen, Zeiten, Wissensdatenbank, Bewertung und Rechte.
/// </summary>
public sealed class ServiceDeskTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Montag, 13. Januar 2031, 08:00 Uhr in Berlin – mitten in den Geschäftszeiten.</summary>
    private static readonly DateTimeOffset Monday = new(2031, 1, 13, 7, 0, 0, TimeSpan.Zero);

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres);
        _factory.Clock.SetUtcNow(Monday);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Ct))!;
    }

    private static async Task<TicketDetail> CreateAsync(HttpClient client, string title, string category = "officeIt", string? description = null,
        Guid? subcategoryId = null) =>
        await ReadAsync<TicketDetail>(await client.PostAsJsonAsync("/api/v1/tickets", new
        {
            id = Guid.CreateVersion7(), title, description, category, source = "web", reporterBlocked = false, subcategoryId,
        }, Ct));

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, object body) => client.PatchAsJsonAsync($"/api/v1/tickets/{id}", body, Ct);

    /// <summary>Nach einem Zeitsprung ist das Zugriffstoken abgelaufen: neu anmelden.</summary>
    private async Task<HttpClient> LoginAsync(string userName)
    {
        var client = _factory.CreateClient();
        return client.WithToken((await client.LoginOkAsync(userName)).AccessToken);
    }

    private async Task<Guid> UserIdAsync(string userName)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        return db.Set<EstrichOne.Api.Modules.Identity.Exports.User>().Single(u => u.UserName == userName).Id;
    }

    [Fact]
    public async Task NewTicket_RuleAssignsGroupAndLeastBusyAgent_SetsDueDates_AndNumbersSequentially()
    {
        using var it1 = await _factory.CreateClientAsAsync("it.anna", StandardRoles.ItAdministratorId);
        using var it2 = await _factory.CreateClientAsAsync("it.ben", StandardRoles.ItAdministratorId);
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        Guid anna = await UserIdAsync("it.anna"), ben = await UserIdAsync("it.ben");

        var group = await ReadAsync<HelpdeskGroupDto>(await it1.PostAsJsonAsync("/api/v1/helpdesk/groups",
            new { name = "IT", description = "Geräte, Apps, Zugänge", memberUserIds = new[] { anna, ben } }, Ct));
        var printers = await ReadAsync<TicketSubcategoryDto>(await it1.PostAsJsonAsync("/api/v1/helpdesk/subcategories",
            new { category = "officeIt", name = "Drucker" }, Ct));
        await ReadAsync<HelpdeskRuleDto>(await it1.PostAsJsonAsync("/api/v1/helpdesk/rules", new
        {
            name = "Büro-IT an die IT", category = "officeIt", groupId = group.Id, strategy = "leastBusy", priority = "high", sortOrder = 1,
        }, Ct));

        // Anna hat schon ein offenes Ticket – das nächste geht an Ben.
        var first = await CreateAsync(reporter, "Telefon im Büro ohne Freizeichen");
        first.Summary.AssigneeUserId.ShouldBe(anna);
        var second = await CreateAsync(reporter, "Drucker zieht kein Papier ein", subcategoryId: printers.Id);
        second.Summary.AssigneeUserId.ShouldBe(ben);
        second.Summary.GroupName.ShouldBe("IT");
        second.Summary.SubcategoryName.ShouldBe("Drucker");
        second.Summary.Priority.ShouldBe(TicketPriority.High);
        // Hoch: Reaktion 4 h, Lösung 16 h Geschäftszeit ab Montag 08:00 → Montag 12:00 und Dienstag 14:00 (Berlin).
        second.Summary.ResponseDueAt.ShouldBe(Monday.AddHours(4));
        second.Summary.ResolutionDueAt.ShouldBe(Monday.AddDays(1).AddHours(6));
        second.Summary.SlaState.ShouldBe("ok");

        var third = await CreateAsync(reporter, "Kein Zugriff auf Laufwerk", category: "access");
        third.Summary.AssigneeUserId.ShouldBeNull("Keine Regel für Zugang/Rechte.");
        new[] { first, second, third }.Select(t => t.Summary.Number[^4..]).ShouldBe(["0001", "0002", "0003"]);

        var agentView = await ReadAsync<TicketDetail>(await it1.GetAsync(new Uri($"/api/v1/tickets/{second.Summary.Id}", UriKind.Relative), Ct));
        agentView.Timeline!.Select(i => i.Kind).ShouldContain("rule");
        var reporterView = await ReadAsync<TicketDetail>(await reporter.GetAsync(new Uri($"/api/v1/tickets/{second.Summary.Id}", UriKind.Relative), Ct));
        reporterView.Timeline!.Select(i => i.Kind).ShouldNotContain("rule", "Interne Regeln sieht der Melder nicht.");
        reporterView.Timeline!.Select(i => i.Kind).ShouldNotContain("group");

        // Falsche Unterkategorie wird abgelehnt.
        using var wrong = await reporter.PostAsJsonAsync("/api/v1/tickets", new
        {
            id = Guid.CreateVersion7(), title = "x", category = "sync", source = "web", subcategoryId = printers.Id,
        }, Ct);
        wrong.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Sla_FirstResponse_PauseWhileWaiting_OverdueView_AndNotification()
    {
        using var agent = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var ticket = (await CreateAsync(reporter, "Tablet startet nicht", category: "deviceDefect")).Summary;
        // Normal: Reaktion bis Montag 16:00, Lösung nach 40 h Geschäftszeit am Freitag 08:00 (Berlin).
        ticket.ResponseDueAt.ShouldBe(Monday.AddHours(8));

        _factory.Clock.Advance(TimeSpan.FromHours(9));
        using var it = await LoginAsync("it");
        var overdue = await ReadAsync<List<TicketSummary>>(await it.GetAsync(new Uri("/api/v1/tickets?filter=overdue", UriKind.Relative), Ct));
        overdue.ShouldHaveSingleItem().SlaState.ShouldBe("overdue");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            // Die Wartung im Hintergrund läuft mit der Testuhr mit und kann schon gemeldet haben.
            (await scope.ServiceProvider.GetRequiredService<TicketService>().CheckSlaAsync(Ct)).ShouldBeLessThanOrEqualTo(1);
            (await scope.ServiceProvider.GetRequiredService<TicketService>().CheckSlaAsync(Ct)).ShouldBe(0, "Nur einmal melden.");
        }

        var notices = string.Empty;
        for (var i = 0; i < 50 && !notices.Contains("Reaktionsziel überschritten", StringComparison.Ordinal); i++)
        {
            await Task.Delay(100, Ct);
            notices = await it.GetStringAsync(new Uri("/api/v1/notifications", UriKind.Relative), Ct);
        }

        notices.ShouldContain("Reaktionsziel überschritten");

        (await it.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/comments", new { text = "Bitte einmal 30 Sekunden die Ein-Taste halten.", isInternal = false }, Ct))
            .EnsureSuccessStatusCode();
        var answered = await ReadAsync<TicketDetail>(await PatchAsync(it, ticket.Id, new { status = "waitingForReporter" }));
        answered.Summary.FirstResponseAt.ShouldNotBeNull();
        answered.Summary.SlaState.ShouldBe("paused");
        var resolutionBefore = answered.Summary.ResolutionDueAt!.Value;
        (await it.GetFromJsonAsync<List<TicketSummary>>("/api/v1/tickets?filter=waiting", TestJson.Options, Ct))!.ShouldHaveSingleItem();
        (await it.GetFromJsonAsync<List<TicketSummary>>("/api/v1/tickets?filter=overdue", TestJson.Options, Ct))!.ShouldBeEmpty("Rückfrage hält die Uhr an.");

        // Rückfrage von Montag 17:00 bis Mittwoch 08:00 (Berlin) = 11 h Geschäftszeit: Lösungsziel verschiebt sich.
        _factory.Clock.SetUtcNow(Monday.AddDays(2));
        using var later = await LoginAsync("baustelle");
        var resumed = await ReadAsync<TicketDetail>(await later.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/comments",
            new { text = "Hat nicht geholfen.", isInternal = false }, Ct));
        resumed.Summary.Status.ShouldBe(TicketStatus.InProgress);
        resumed.Summary.ResolutionDueAt.ShouldBe(resolutionBefore.AddDays(3).AddHours(1),
            "Freitag 08:00 + 11 h Geschäftszeit = Montag 09:00.");
    }

    [Fact]
    public async Task Bulk_AssignsAndPrioritizes_ReportsFailuresPerTicket_SearchFindsNumberAndReplies()
    {
        using var agent = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var me = await UserIdAsync("it");
        var a = (await CreateAsync(reporter, "Outlook fragt ständig nach dem Passwort", category: "signIn")).Summary;
        var b = (await CreateAsync(reporter, "Drucker 2. OG offline")).Summary;
        (await reporter.PostAsJsonAsync($"/api/v1/tickets/{b.Id}/comments", new { text = "Seriennummer XK-4711 steht auf dem Gerät", isInternal = false }, Ct))
            .EnsureSuccessStatusCode();

        var result = await ReadAsync<BulkTicketResponse>(await agent.PostAsJsonAsync("/api/v1/tickets/bulk",
            new { ids = new[] { a.Id, b.Id, Guid.CreateVersion7() }, assigneeUserId = me, priority = "high" }, Ct));
        result.Updated.ShouldBe(2);
        result.Failed.ShouldHaveSingleItem().Message.ShouldBe("Ticket nicht gefunden.");
        var mine = await ReadAsync<List<TicketSummary>>(await agent.GetAsync(new Uri("/api/v1/tickets?filter=mine", UriKind.Relative), Ct));
        mine.Select(t => t.Priority).ShouldAllBe(p => p == TicketPriority.High);
        mine.Count.ShouldBe(2);

        // Lösen ohne Lösungstext schlägt je Ticket fehl, die anderen bleiben unberührt.
        var resolve = await ReadAsync<BulkTicketResponse>(await agent.PostAsJsonAsync("/api/v1/tickets/bulk",
            new { ids = new[] { a.Id, b.Id }, status = "resolved" }, Ct));
        resolve.Updated.ShouldBe(0);
        resolve.Failed.Count.ShouldBe(2);

        Task<List<TicketSummary>> SearchAsync(string q) =>
            agent.GetFromJsonAsync<List<TicketSummary>>($"/api/v1/tickets?filter=all&q={Uri.EscapeDataString(q)}", TestJson.Options, Ct)!;
        (await SearchAsync(b.Number)).ShouldHaveSingleItem().Id.ShouldBe(b.Id);
        (await SearchAsync(a.Number[^4..])).Select(t => t.Id).ShouldContain(a.Id);
        (await SearchAsync("xk-4711")).ShouldHaveSingleItem().Id.ShouldBe(b.Id);
        (await SearchAsync("outlook passwort")).ShouldHaveSingleItem().Id.ShouldBe(a.Id);
        (await SearchAsync("100%")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Merge_ClosesDuplicate_ReporterFollowsTarget_LinksAndNotes()
    {
        using var agent = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        using var first = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var second = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        using var stranger = await _factory.CreateClientAsAsync("kollege", StandardRoles.SiteWorkerId);
        var target = (await CreateAsync(first, "WLAN im Lager fällt aus", category: "other")).Summary;
        var duplicate = (await CreateAsync(second, "Kein Internet im Lager", category: "other", description: "seit 8 Uhr")).Summary;

        var merged = await ReadAsync<TicketDetail>(await agent.PostAsJsonAsync($"/api/v1/tickets/{duplicate.Id}/merge", new { targetTicketId = target.Id }, Ct));
        merged.Summary.Id.ShouldBe(target.Id);
        merged.Links!.ShouldHaveSingleItem().Kind.ShouldBe("duplicate");
        merged.Comments.ShouldContain(c => c.IsInternal && c.Text.Contains("seit 8 Uhr", StringComparison.Ordinal));
        merged.Timeline!.Select(i => i.Kind).ShouldContain("mergedFrom");

        var source = await ReadAsync<TicketDetail>(await second.GetAsync(new Uri($"/api/v1/tickets/{duplicate.Id}", UriKind.Relative), Ct));
        source.Summary.Status.ShouldBe(TicketStatus.Closed);
        source.MergedIntoNumber.ShouldBe(target.Number);
        source.Resolution!.ShouldContain(target.Number);

        // Der Melder des Duplikats verfolgt das Ziel: sichtbar, in „Meine“ und mit Antworten der IT.
        var followed = await ReadAsync<TicketDetail>(await second.GetAsync(new Uri($"/api/v1/tickets/{target.Id}", UriKind.Relative), Ct));
        followed.Comments.ShouldNotContain(c => c.IsInternal);
        (await second.GetFromJsonAsync<List<TicketSummary>>("/api/v1/tickets/mine", TestJson.Options, Ct))!.Select(t => t.Id).ShouldContain(target.Id);
        (await stranger.GetAsync(new Uri($"/api/v1/tickets/{target.Id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var again = await agent.PostAsJsonAsync($"/api/v1/tickets/{duplicate.Id}/merge", new { targetTicketId = target.Id }, Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var self = await agent.PostAsJsonAsync($"/api/v1/tickets/{target.Id}/merge", new { targetTicketId = target.Id }, Ct);
        self.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Verknüpfen und lösen; Zeit erfassen.
        var other = (await CreateAsync(first, "Kamera am Tor zeigt kein Bild", category: "other")).Summary;
        var linked = await ReadAsync<TicketDetail>(await agent.PostAsJsonAsync($"/api/v1/tickets/{target.Id}/links", new { otherTicketId = other.Id }, Ct));
        linked.Links!.Select(l => l.Number).ShouldContain(other.Number);
        (await agent.DeleteAsync(new Uri($"/api/v1/tickets/{target.Id}/links/{other.Id}", UriKind.Relative), Ct)).EnsureSuccessStatusCode();
        await ReadAsync<TicketTimeEntryDto>(await agent.PostAsJsonAsync($"/api/v1/tickets/{target.Id}/time", new { minutes = 45, note = "Access Point getauscht" }, Ct));
        var withTime = await ReadAsync<TicketDetail>(await agent.GetAsync(new Uri($"/api/v1/tickets/{target.Id}", UriKind.Relative), Ct));
        withTime.TotalMinutes.ShouldBe(45);
        withTime.Links!.Select(l => l.Number).ShouldNotContain(other.Number);
        var reporterView = await ReadAsync<TicketDetail>(await first.GetAsync(new Uri($"/api/v1/tickets/{target.Id}", UriKind.Relative), Ct));
        reporterView.TotalMinutes.ShouldBeNull("Zeiten sieht nur die Bearbeitung.");
        reporterView.Links!.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rating_OnlyReporter_AfterResolution_ShowsInStats()
    {
        using var agent = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var stranger = await _factory.CreateClientAsAsync("kollege", StandardRoles.SiteWorkerId);
        var ticket = (await CreateAsync(reporter, "Bildschirm flackert", category: "deviceDefect")).Summary;

        using var early = await reporter.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/rating", new { rating = 5 }, Ct);
        early.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        agent.Dispose();
        reporter.Dispose();
        stranger.Dispose();
        _factory.Clock.Advance(TimeSpan.FromHours(2));
        using var agentNow = await LoginAsync("it");
        using var reporterNow = await LoginAsync("baustelle");
        using var strangerNow = await LoginAsync("kollege");
        await RateAsync(agentNow, reporterNow, strangerNow, ticket);
    }

    private static async Task RateAsync(HttpClient agent, HttpClient reporter, HttpClient stranger, TicketSummary ticket)
    {
        var resolved = await ReadAsync<TicketDetail>(await PatchAsync(agent, ticket.Id, new { status = "resolved", resolution = "Kabel getauscht." }));
        resolved.CanRate.ShouldBeFalse("Die Bearbeitung bewertet nicht.");
        var view = await ReadAsync<TicketDetail>(await reporter.GetAsync(new Uri($"/api/v1/tickets/{ticket.Id}", UriKind.Relative), Ct));
        view.CanRate.ShouldBeTrue();

        (await stranger.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/rating", new { rating = 1 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reporter.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/rating", new { rating = 9 }, Ct)).StatusCode
            .ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
        var rated = await ReadAsync<TicketDetail>(await reporter.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/rating",
            new { rating = 4, comment = "Schnell erledigt" }, Ct));
        rated.Rating.ShouldBe(4);
        rated.Timeline!.ShouldContain(i => i.Kind == "rating" && i.Summary == "Bewertung: 4 von 5 Sternen");

        var stats = await ReadAsync<HelpdeskStats>(await agent.GetAsync(new Uri("/api/v1/tickets/stats", UriKind.Relative), Ct));
        stats.SatisfactionAverage.ShouldBe(4);
        stats.ResolvedThisWeek.ShouldBe(1);
        stats.SlaMetPercent.ShouldBe(100);
        stats.AvgFirstResponseMinutes.ShouldBe(120, "Die Lösung zählt als erste Reaktion.");
        stats.Trend.Count.ShouldBe(14);
    }

    [Fact]
    public async Task OnBehalf_AgentRecordsCallForReporter_ReporterSeesAndIsNotified_OnlyAgentsMay()
    {
        using var agent = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        Guid me = await UserIdAsync("it"), reporterId = await UserIdAsync("baustelle");

        // Melder suchen: Name, Benutzername, E-Mail oder Telefon.
        var found = await ReadAsync<List<HelpdeskReporterDto>>(await agent.GetAsync(new Uri("/api/v1/helpdesk/reporters?q=baust", UriKind.Relative), Ct));
        found.ShouldContain(r => r.Id == reporterId);
        (await reporter.GetAsync(new Uri("/api/v1/helpdesk/reporters", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var id = Guid.CreateVersion7();
        object Body(string channel) => new
        {
            id, title = "Tablet zeigt keine Aufträge", description = "Anruf von der Baustelle Lindenstraße", category = "sync", channel, source = "web",
            reporterUserId = reporterId, priority = "high", assigneeUserId = me,
        };
        (await agent.PostAsJsonAsync("/api/v1/tickets/on-behalf", Body("selfService"), Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await reporter.PostAsJsonAsync("/api/v1/tickets/on-behalf", Body("phone"), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await agent.PostAsJsonAsync("/api/v1/tickets/on-behalf", new { id = Guid.CreateVersion7(), title = "Ohne Melder", category = "other", channel = "email",
            source = "web" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        using var createdResponse = await agent.PostAsJsonAsync("/api/v1/tickets/on-behalf", Body("phone"), Ct);
        createdResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await ReadAsync<TicketDetail>(createdResponse);
        created.Summary.ReporterUserId.ShouldBe(reporterId);
        created.Summary.Channel.ShouldBe(TicketChannel.Phone);
        created.Summary.CreatedByUserId.ShouldBe(me);
        created.Summary.AssigneeUserId.ShouldBe(me);
        created.Summary.Status.ShouldBe(TicketStatus.InProgress, "Wer selbst übernimmt, arbeitet gleich daran.");
        created.Summary.Priority.ShouldBe(TicketPriority.High);
        created.Timeline![0].Summary.ShouldContain("aufgenommen (Telefon)");

        // Wiederholung nach Verbindungsabbruch: kein zweites Ticket.
        (await agent.PostAsJsonAsync("/api/v1/tickets/on-behalf", Body("phone"), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Der Melder sieht das Ticket als eigenes und erhält einen Hinweis.
        var mine = await ReadAsync<List<TicketSummary>>(await reporter.GetAsync(new Uri("/api/v1/tickets/mine", UriKind.Relative), Ct));
        mine.ShouldHaveSingleItem().Id.ShouldBe(id);
        var notices = string.Empty;
        for (var i = 0; i < 50 && !notices.Contains("für Sie angelegt", StringComparison.Ordinal); i++)
        {
            await Task.Delay(100, Ct);
            notices = await reporter.GetStringAsync(new Uri("/api/v1/notifications", UriKind.Relative), Ct);
        }

        notices.ShouldContain($"Ticket {created.Summary.Number} für Sie angelegt");

        // Person ohne Zugang: nur Name; E-Mail-Eingang.
        var walkIn = await ReadAsync<TicketDetail>(await agent.PostAsJsonAsync("/api/v1/tickets/on-behalf", new
        {
            id = Guid.CreateVersion7(), title = "Drucker im Lager", category = "officeIt", channel = "inPerson", source = "web", reporterName = "Lagerhilfe Max",
        }, Ct));
        walkIn.Summary.ReporterUserId.ShouldBeNull();
        walkIn.Summary.ReporterDisplayName.ShouldBe("Lagerhilfe Max (laut Eingabe)");
        walkIn.Summary.Channel.ShouldBe(TicketChannel.InPerson);
        walkIn.Summary.Status.ShouldBe(TicketStatus.New);
    }

    [Fact]
    public async Task Page_ReturnsStableSlicesWithTotal()
    {
        using var agent = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        for (var i = 1; i <= 7; i++)
        {
            await CreateAsync(reporter, $"Meldung {i}");
            _factory.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        async Task<TicketPage> PageAsync(int offset) =>
            await ReadAsync<TicketPage>(await agent.GetAsync(new Uri($"/api/v1/tickets/page?filter=all&sort=created&offset={offset}&limit=3", UriKind.Relative), Ct));
        var first = await PageAsync(0);
        var second = await PageAsync(3);
        var third = await PageAsync(6);
        first.Total.ShouldBe(7);
        first.Items.Count.ShouldBe(3);
        third.Items.ShouldHaveSingleItem();
        first.Items.Concat(second.Items).Concat(third.Items).Select(t => t.Id).Distinct().Count().ShouldBe(7);
        first.Items[0].Title.ShouldBe("Meldung 7");
        (await PageAsync(30)).Items.ShouldBeEmpty();

        // Spaltensortierung wie im Web (auf- und absteigend) über alle Seiten hinweg.
        var byTitle = await ReadAsync<TicketPage>(await agent.GetAsync(new Uri("/api/v1/tickets/page?filter=all&sort=title&desc=true&limit=2", UriKind.Relative), Ct));
        byTitle.Items.Select(t => t.Title).ShouldBe(["Meldung 7", "Meldung 6"]);
        var byNumber = await ReadAsync<TicketPage>(await agent.GetAsync(new Uri("/api/v1/tickets/page?filter=all&sort=number&desc=false&limit=1", UriKind.Relative), Ct));
        byNumber.Items.ShouldHaveSingleItem().Title.ShouldBe("Meldung 1");
    }

    [Fact]
    public async Task Holidays_StateFromSettings_PreviewAndBusinessTimeStats()
    {
        using var agent = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        var settings = await ReadAsync<HelpdeskSettings>(await agent.GetAsync(new Uri("/api/v1/helpdesk/settings", UriKind.Relative), Ct));
        (await agent.PutAsJsonAsync("/api/v1/helpdesk/settings", settings with { State = "ZZ" }, TestJson.Options, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.UnprocessableEntity);
        var saved = await ReadAsync<HelpdeskSettings>(await agent.PutAsJsonAsync("/api/v1/helpdesk/settings",
            settings with { State = "BY", Holidays = ["2031-12-24"] }, TestJson.Options, Ct));
        saved.State.ShouldBe("BY");

        var year = await ReadAsync<HelpdeskHolidays>(await agent.GetAsync(new Uri("/api/v1/helpdesk/holidays?year=2031", UriKind.Relative), Ct));
        year.StateName.ShouldBe("Bayern");
        year.States.Count.ShouldBe(16);
        year.Days.ShouldContain(new HelpdeskHoliday(new DateOnly(2031, 6, 12), "Fronleichnam", true));
        year.Days.ShouldContain(new HelpdeskHoliday(new DateOnly(2031, 12, 24), "Zusatztag", false));
        var preview = await ReadAsync<HelpdeskHolidays>(await agent.GetAsync(new Uri("/api/v1/helpdesk/holidays?year=2031&state=HH", UriKind.Relative), Ct));
        preview.Days.ShouldNotContain(d => d.Name == "Fronleichnam");
        preview.Days.ShouldContain(d => d.Name == "Reformationstag");

        // Montag 08:00 gemeldet, Freitag 08:00 gelöst: 4 Tage Uhrzeit, aber 40 h Geschäftszeit (Mo 9 h, Di–Do je 10 h, Fr 1 h).
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var ticket = (await CreateAsync(reporter, "VPN bricht ab", category: "officeIt")).Summary;
        _factory.Clock.SetUtcNow(Monday.AddDays(4));
        using var it = await LoginAsync("it");
        await ReadAsync<TicketDetail>(await PatchAsync(it, ticket.Id, new { status = "resolved", resolution = "Treiber aktualisiert." }));
        var stats = await ReadAsync<HelpdeskStats>(await it.GetAsync(new Uri("/api/v1/tickets/stats", UriKind.Relative), Ct));
        stats.TimeBasis.ShouldBe("business");
        stats.AvgResolutionHours.ShouldBe(40);
        stats.AvgFirstResponseMinutes.ShouldBe(40 * 60);
    }

    [Fact]
    public async Task KnowledgeBase_PublishedForEveryone_DraftsForAgents_SuggestedAndLinked()
    {
        using var agent = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var published = await ReadAsync<KnowledgeArticleDto>(await agent.PostAsJsonAsync("/api/v1/helpdesk/articles", new
        {
            title = "Fotos werden nicht übertragen", body = "1. WLAN oder Mobilfunk prüfen\n2. Mehr › Synchronisation › Jetzt abgleichen", category = "sync",
            keywords = "upload, foto, abgleich", isPublished = true,
        }, Ct));
        var draft = await ReadAsync<KnowledgeArticleDto>(await agent.PostAsJsonAsync("/api/v1/helpdesk/articles",
            new { title = "Fotos per USB sichern", body = "Nur für die IT.", isPublished = false }, Ct));

        var suggested = await ReadAsync<List<KnowledgeArticleSummary>>(await reporter.GetAsync(
            new Uri("/api/v1/helpdesk/articles/suggest?q=Fotos%20werden%20nicht%20hochgeladen&category=sync", UriKind.Relative), Ct));
        suggested.ShouldHaveSingleItem().Id.ShouldBe(published.Id);
        (await reporter.GetAsync(new Uri($"/api/v1/helpdesk/articles/{draft.Id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reporter.GetFromJsonAsync<List<KnowledgeArticleSummary>>("/api/v1/helpdesk/articles", TestJson.Options, Ct))!.ShouldHaveSingleItem();
        (await agent.GetFromJsonAsync<List<KnowledgeArticleSummary>>("/api/v1/helpdesk/articles", TestJson.Options, Ct))!.Count.ShouldBe(2);
        (await reporter.PostAsJsonAsync("/api/v1/helpdesk/articles", new { title = "x", body = "y" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reporter.PostAsync(new Uri($"/api/v1/helpdesk/articles/{published.Id}/helpful", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();

        var ticket = (await CreateAsync(reporter, "Fotos hängen in der Warteschlange", category: "sync")).Summary;
        var context = await ReadAsync<TicketContext>(await agent.GetAsync(new Uri($"/api/v1/tickets/{ticket.Id}/context", UriKind.Relative), Ct));
        context.Reporter!.UserName.ShouldBe("baustelle");
        context.SuggestedArticles.Select(a => a.Id).ShouldContain(published.Id);
        await ReadAsync<TicketDetail>(await agent.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/articles", new { articleId = published.Id }, Ct));
        await ReadAsync<TicketDetail>(await agent.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/articles", new { articleId = draft.Id }, Ct));
        var reporterView = await ReadAsync<TicketDetail>(await reporter.GetAsync(new Uri($"/api/v1/tickets/{ticket.Id}", UriKind.Relative), Ct));
        reporterView.Articles!.ShouldHaveSingleItem().Title.ShouldBe("Fotos werden nicht übertragen");
        var read = await ReadAsync<KnowledgeArticleDto>(await reporter.GetAsync(new Uri($"/api/v1/helpdesk/articles/{published.Id}", UriKind.Relative), Ct));
        read.HelpfulCount.ShouldBe(1);
    }

    [Fact]
    public async Task Permissions_ConfigurationNeedsManage_CannedResponsesForAgents_MentionsOnlyAgents()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
            db.Add(Role.Create(Guid.Parse("01a00000-0000-7000-8000-0000000000a1"), "Nur Tickets", null, false, [Permissions.HelpdeskAgent]));
            await db.SaveChangesAsync(Ct);
        }

        using var agentOnly = await _factory.CreateClientAsAsync("support", Guid.Parse("01a00000-0000-7000-8000-0000000000a1"));
        using var manager = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);

        var settings = await ReadAsync<HelpdeskSettings>(await agentOnly.GetAsync(new Uri("/api/v1/helpdesk/settings", UriKind.Relative), Ct));
        settings.Targets!.Count.ShouldBe(4);
        (await agentOnly.PutAsJsonAsync("/api/v1/helpdesk/settings", settings with { RatingEnabled = false }, TestJson.Options, Ct)).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await agentOnly.PostAsJsonAsync("/api/v1/helpdesk/groups", new { name = "IT" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reporter.GetAsync(new Uri("/api/v1/helpdesk/settings", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reporter.GetAsync(new Uri("/api/v1/tickets/stats", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var saved = await ReadAsync<HelpdeskSettings>(await manager.PutAsJsonAsync("/api/v1/helpdesk/settings",
            settings with { Hours = new BusinessHours("08:00", "16:30", [1, 2, 3, 4]), TimeTrackingEnabled = false }, TestJson.Options, Ct));
        saved.Hours!.End.ShouldBe("16:30");
        (await manager.PutAsJsonAsync("/api/v1/helpdesk/settings", settings with { Hours = new BusinessHours("18:00", "08:00") }, TestJson.Options, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // Textbausteine: gemeinsame für alle Bearbeiter, persönliche nur für sich.
        await ReadAsync<CannedResponseDto>(await agentOnly.PostAsJsonAsync("/api/v1/helpdesk/responses",
            new { name = "Neustart", text = "Hallo {Melder}, bitte das Gerät einmal neu starten." }, Ct));
        await ReadAsync<CannedResponseDto>(await agentOnly.PostAsJsonAsync("/api/v1/helpdesk/responses",
            new { name = "Meine Signatur", text = "Viele Grüße", isPersonal = true }, Ct));
        (await manager.GetFromJsonAsync<List<CannedResponseDto>>("/api/v1/helpdesk/responses", TestJson.Options, Ct))!.Select(r => r.Name)
            .ShouldBe(["Neustart"]);

        // Erwähnen: nur Bearbeiter, nur durch die Bearbeitung.
        var ticket = (await CreateAsync(reporter, "Zeiterfassung lässt sich nicht öffnen", category: "appError")).Summary;
        (await manager.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/time", new { minutes = 10 }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var reporterId = await UserIdAsync("baustelle");
        var supportId = await UserIdAsync("support");
        var noted = await ReadAsync<TicketDetail>(await manager.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/comments",
            new { text = "@Support bitte übernehmen", isInternal = true, mentionUserIds = new[] { supportId, reporterId } }, Ct));
        noted.Timeline!.Last(i => i.Kind == "note").MentionUserIds.ShouldBe([supportId]);
        (await agentOnly.GetStringAsync(new Uri("/api/v1/notifications", UriKind.Relative), Ct)).ShouldContain("Erwähnung in Ticket");
        (await reporter.PostAsJsonAsync($"/api/v1/tickets/{ticket.Id}/comments", new { text = "x", isInternal = false, mentionUserIds = new[] { supportId } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
