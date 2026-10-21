using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Planning;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Planning;

/// <summary>Personalverwaltung (C7): Feiertage, Urlaubsantrag → Genehmigung → Abwesenheit, Konflikte, Krankmeldung (Datenschutz), Fristen, Urlaubsplan.</summary>
public sealed class HrTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        using var response = await client.SendAsync(new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Ct))!;
    }

    [Fact]
    public void Holidays_EasterBasedAndPerState()
    {
        GermanHolidays.EasterSunday(2026).ShouldBe(new DateOnly(2026, 4, 5));
        GermanHolidays.EasterSunday(2027).ShouldBe(new DateOnly(2027, 3, 28));
        var nw = GermanHolidays.For(2026, "NW");
        nw.ShouldContain(h => h.Name == "Fronleichnam" && h.Date == new DateOnly(2026, 6, 4));
        nw.ShouldContain(h => h.Name == "Allerheiligen");
        nw.ShouldNotContain(h => h.Name == "Reformationstag");
        GermanHolidays.For(2026, "SN").ShouldContain(h => h.Name == "Buß- und Bettag" && h.Date == new DateOnly(2026, 11, 18));
        GermanHolidays.For(2026, "BY").Count.ShouldBe(13);
    }

    [Fact]
    public async Task VacationRequest_ApprovedBecomesAbsence_WithCrewConflictAndRemaining_RejectAndCancel()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var anna = await _factory.CreateUserAsync("anna", roleIds: StandardRoles.SiteWorkerId);
        var lukas = await _factory.CreateUserAsync("lukas", roleIds: StandardRoles.SiteWorkerId);
        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        await SendAsync<CrewResponse>(office, HttpMethod.Put, $"/api/v1/planning/crews/{Guid.CreateVersion7()}", new
        {
            name = "Kolonne Nord", leaderUserId = lukas.Id, memberUserIds = new[] { anna.Id },
        });
        await SendAsync<EmployeeDetail>(admin, HttpMethod.Put, $"/api/v1/staff/employees/{Guid.CreateVersion7()}", new
        {
            firstName = "Anna", lastName = "Kraus", userId = anna.Id, vacationDaysPerYear = 28m, vacationCarryOver = 2m,
        });

        var year = DateOnly.FromDateTime(_factory.Clock.GetUtcNow().UtcDateTime).Year + 1;
        // Lukas ist schon im Urlaub (genehmigt), Anna beantragt dieselbe Woche mit Fronleichnam (Do) → 4 Arbeitstage.
        var corpusChristi = GermanHolidays.For(year, "NW").Single(h => h.Name == "Fronleichnam").Date;
        var monday = corpusChristi.AddDays(-3);
        using var worker = _factory.CreateClient();
        worker.WithToken((await worker.LoginOkAsync("lukas")).AccessToken);
        var lukasRequest = await SendAsync<AbsenceRequestView>(worker, HttpMethod.Put, $"/api/v1/hr/absence-requests/{Guid.CreateVersion7()}", new
        {
            from = monday, to = monday.AddDays(4), kind = "vacation", note = "Familienurlaub",
        });
        lukasRequest.Status.ShouldBe(AbsenceRequestStatus.Pending);
        (await SendAsync<AbsenceRequestView>(office, HttpMethod.Post, $"/api/v1/hr/absence-requests/{lukasRequest.Id}/decision", new { approve = true }))
            .Status.ShouldBe(AbsenceRequestStatus.Approved);

        using var annaClient = _factory.CreateClient();
        annaClient.WithToken((await annaClient.LoginOkAsync("anna")).AccessToken);
        var request = await SendAsync<AbsenceRequestView>(annaClient, HttpMethod.Put, $"/api/v1/hr/absence-requests/{Guid.CreateVersion7()}", new
        {
            from = monday, to = monday.AddDays(4), kind = "vacation",
        });
        request.WorkingDays.ShouldBe(4m, "Fronleichnam (NRW) zählt nicht.");
        request.Warnings.ShouldContain(w => w.StartsWith("Kolonne Nord: 2 von 2", StringComparison.Ordinal));
        request.CanDecide.ShouldBeFalse();

        // Eigener Antrag nicht selbst genehmigen; Monteure dürfen gar nicht entscheiden.
        using var self = await annaClient.PostAsJsonAsync($"/api/v1/hr/absence-requests/{request.Id}/decision", new { approve = true }, Ct);
        self.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var pending = (await office.GetFromJsonAsync<List<AbsenceRequestView>>("/api/v1/hr/absence-requests?scope=pending", TestJson.Options, Ct))!;
        pending.ShouldHaveSingleItem().CanDecide.ShouldBeTrue();
        (await office.GetFromJsonAsync<HrSummary>("/api/v1/hr/summary", TestJson.Options, Ct))!.PendingApprovals.ShouldBe(1);
        var approved = await SendAsync<AbsenceRequestView>(office, HttpMethod.Post, $"/api/v1/hr/absence-requests/{request.Id}/decision", new
        {
            approve = true, comment = "Gute Erholung!",
        });
        approved.DecisionComment.ShouldBe("Gute Erholung!");

        // Planung kennt die Abwesenheit, der Urlaubsplan zeigt sie.
        var range = await office.GetFromJsonAsync<PlanningRange>($"/api/v1/planning/range?from={monday:yyyy-MM-dd}&to={monday.AddDays(6):yyyy-MM-dd}", TestJson.Options, Ct);
        range!.Absences.Count(a => a.Kind == AbsenceKind.Vacation).ShouldBe(2);
        range.Holidays.ShouldNotBeNull().ShouldContain(h => h.Name == "Fronleichnam");
        var plan = await office.GetFromJsonAsync<VacationPlan>($"/api/v1/hr/vacation-plan?from={year}-01-01&to={year}-12-31", TestJson.Options, Ct);
        plan!.Rows.Single(r => r.UserId == anna.Id).Entries.ShouldHaveSingleItem().Status.ShouldBe("approved");
        plan.State.ShouldBe("NW");

        var mine = await annaClient.GetFromJsonAsync<MyVacation>("/api/v1/hr/me", TestJson.Options, Ct);
        mine!.Vacation.ShouldNotBeNull().Entitlement.ShouldBe(28m);
        mine.Requests.ShouldHaveSingleItem().Status.ShouldBe(AbsenceRequestStatus.Approved);

        // Zurückziehen gibt den Urlaub wieder frei; Ablehnen mit Kommentar.
        (await SendAsync<AbsenceRequestView>(annaClient, HttpMethod.Post, $"/api/v1/hr/absence-requests/{request.Id}/cancel"))
            .Status.ShouldBe(AbsenceRequestStatus.Cancelled);
        range = await office.GetFromJsonAsync<PlanningRange>($"/api/v1/planning/range?from={monday:yyyy-MM-dd}&to={monday.AddDays(6):yyyy-MM-dd}", TestJson.Options, Ct);
        range!.Absences.ShouldHaveSingleItem();
        var second = await SendAsync<AbsenceRequestView>(annaClient, HttpMethod.Put, $"/api/v1/hr/absence-requests/{Guid.CreateVersion7()}", new
        {
            from = monday.AddDays(14), to = monday.AddDays(15), kind = "specialLeave", note = "Umzug",
        });
        (await SendAsync<AbsenceRequestView>(office, HttpMethod.Post, $"/api/v1/hr/absence-requests/{second.Id}/decision", new { approve = false, comment = "Bitte verschieben" }))
            .Status.ShouldBe(AbsenceRequestStatus.Rejected);
        using var overlap = await annaClient.PutAsJsonAsync($"/api/v1/hr/absence-requests/{Guid.CreateVersion7()}", new { from = monday.AddDays(20), to = monday.AddDays(22) }, Ct);
        overlap.EnsureSuccessStatusCode();
        using var twice = await annaClient.PutAsJsonAsync($"/api/v1/hr/absence-requests/{Guid.CreateVersion7()}", new { from = monday.AddDays(21), to = monday.AddDays(23) }, Ct);
        twice.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        using var sickRequest = await annaClient.PutAsJsonAsync($"/api/v1/hr/absence-requests/{Guid.CreateVersion7()}", new { from = monday.AddDays(40), to = monday.AddDays(41), kind = "sick" }, Ct);
        sickRequest.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        using var pdf = await office.GetAsync(new Uri($"/api/v1/hr/vacation-plan.pdf?year={year}&month={monday.Month}", UriKind.Relative), Ct);
        pdf.StatusCode.ShouldBe(HttpStatusCode.OK);
        Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync(Ct), 0, 5).ShouldBe("%PDF-");
        using var pdfYear = await office.GetAsync(new Uri($"/api/v1/hr/vacation-plan.pdf?year={year}", UriKind.Relative), Ct);
        pdfYear.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe($"Urlaubsplan {year}.pdf");
    }

    [Fact]
    public async Task SickNote_WithCertificate_OnlyVisibleToHr_PlanningShowsAbsent_RemindersAndSettings()
    {
        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var anna = await _factory.CreateUserAsync("anna", roleIds: StandardRoles.SiteWorkerId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_factory.Clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")).DateTime);
        await SendAsync<EmployeeDetail>(admin, HttpMethod.Put, $"/api/v1/staff/employees/{Guid.CreateVersion7()}", new
        {
            firstName = "Anna", lastName = "Kraus", userId = anna.Id, entryDate = today.AddYears(-5).AddDays(10), probationEnd = today.AddDays(10),
            birthDate = today.AddYears(-30).AddDays(3),
        });

        var content = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\nAU");
        var file = Guid.CreateVersion7();
        (await admin.PostAsJsonAsync("/api/v1/files", new
        {
            id = file, kind = "document", originalName = "AU.pdf", contentType = "application/pdf", sizeBytes = (long)content.Length,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
        }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Created);
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new("application/octet-stream");
        (await admin.PutAsync(new Uri($"/api/v1/files/{file}/content", UriKind.Relative), body, Ct)).EnsureSuccessStatusCode();

        var note = await SendAsync<SickNoteView>(admin, HttpMethod.Put, $"/api/v1/hr/sick-notes/{Guid.CreateVersion7()}", new
        {
            userId = anna.Id, from = today, to = today.AddDays(2), note = "Grippe", certificateFileId = file,
        });
        note.CertificateFileId.ShouldBe(file);
        (await admin.GetFromJsonAsync<List<SickNoteView>>("/api/v1/hr/sick-notes", TestJson.Options, Ct))!.ShouldHaveSingleItem();

        // Büro (hr.read, kein hr.manage) sieht „abwesend“, keine Diagnose, keine Krankmeldungsliste, keine AU.
        var range = await office.GetFromJsonAsync<PlanningRange>($"/api/v1/planning/range?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}", TestJson.Options, Ct);
        var absence = range!.Absences.ShouldHaveSingleItem();
        absence.Kind.ShouldBe(AbsenceKind.Other);
        absence.Note.ShouldBeNull();
        (await office.GetAsync(new Uri("/api/v1/hr/sick-notes", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await office.GetAsync(new Uri($"/api/v1/files/{file}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var second = await _factory.CreateClientAsAsync("personal", StandardRoles.AdministratorId);
        (await second.GetAsync(new Uri($"/api/v1/files/{file}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var reminders = (await office.GetFromJsonAsync<List<HrReminder>>("/api/v1/hr/reminders?days=30", TestJson.Options, Ct))!;
        reminders.ShouldContain(r => r.Kind == "probation");
        reminders.ShouldContain(r => r.Kind == "birthday" && r.Text.Contains("30 Jahre", StringComparison.Ordinal));
        reminders.ShouldContain(r => r.Kind == "anniversary" && r.Text.StartsWith("5 Jahre", StringComparison.Ordinal));

        var settings = await SendAsync<HrSettingsResponse>(admin, HttpMethod.Put, "/api/v1/hr/settings", new { state = "BY", maxCrewAbsencePercent = 30, remindBirthdays = false });
        settings.StateName.ShouldBe("Bayern");
        reminders = (await office.GetFromJsonAsync<List<HrReminder>>("/api/v1/hr/reminders?days=30", TestJson.Options, Ct))!;
        reminders.ShouldNotContain(r => r.Kind == "birthday");
        var holidays = (await office.GetFromJsonAsync<List<Holiday>>("/api/v1/hr/holidays?year=2026", TestJson.Options, Ct))!;
        holidays.ShouldContain(h => h.Name == "Mariä Himmelfahrt");
        using var invalid = await admin.PutAsJsonAsync("/api/v1/hr/settings", new { state = "XX" }, Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
