using System.Net;
using System.Net.Http.Json;
using System.Text;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Notifications;
using EstrichOne.Api.Modules.Planning;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Planning;

/// <summary>
/// Abwesenheitsverwaltung: Feiertage aller Bundesländer, Arbeitstage mit halben Tagen, Urlaubskonto (anteilig, Übertrag mit Verfall,
/// Krankheit im Urlaub), Arten, Genehmigungsfluss mit Vertretung und Änderungsantrag, Kolonnenführer als Genehmiger, Mindestbesetzung,
/// Betriebsferien, Jahresübersicht (CSV/PDF), Rechte und Datenschutz.
/// </summary>
public sealed class AbsenceManagementTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_factory.Clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")).DateTime);

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        using var response = await client.SendAsync(new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Ct))!;
    }

    private static Dictionary<Guid, AbsenceTypeRule> Rules => StandardAbsenceTypes.Create().ToDictionary(t => t.Id, AbsenceTypeRule.From);

    private static VacationInput Input(int year, DateOnly today, IReadOnlyList<AbsenceSpan> absences, VacationAccount? account = null, DateOnly? entry = null,
        DateOnly? exit = null, decimal annual = 30, IReadOnlyList<AbsenceSpan>? pending = null, string state = "NW") =>
        new(year, today, annual, VacationCalculator.DefaultWorkDays, entry, exit, account, null, absences, pending ?? [],
            GermanHolidays.Dates(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), state), new DateOnly(year, 3, 31), true, Rules, true);

    [Theory]
    [InlineData("BW", 12)]
    [InlineData("BY", 13)]
    [InlineData("BE", 10)]
    [InlineData("BB", 12)]
    [InlineData("HB", 10)]
    [InlineData("HH", 10)]
    [InlineData("HE", 10)]
    [InlineData("MV", 11)]
    [InlineData("NI", 10)]
    [InlineData("NW", 11)]
    [InlineData("RP", 11)]
    [InlineData("SL", 12)]
    [InlineData("SN", 11)]
    [InlineData("ST", 11)]
    [InlineData("SH", 10)]
    [InlineData("TH", 11)]
    public void Holidays_AllStates2026(string state, int count)
    {
        var list = GermanHolidays.For(2026, state);
        list.Count.ShouldBe(count, string.Join(", ", list.Select(h => h.Name)));
        // Ostern-abhängig: Karfreitag 03.04., Ostermontag 06.04., Himmelfahrt 14.05., Pfingstmontag 25.05.2026.
        list.ShouldContain(h => h.Name == "Karfreitag" && h.Date == new DateOnly(2026, 4, 3));
        list.ShouldContain(h => h.Name == "Ostermontag" && h.Date == new DateOnly(2026, 4, 6));
        list.ShouldContain(h => h.Name == "Christi Himmelfahrt" && h.Date == new DateOnly(2026, 5, 14));
        list.ShouldContain(h => h.Name == "Pfingstmontag" && h.Date == new DateOnly(2026, 5, 25));
    }

    [Fact]
    public void Easter_KnownYears()
    {
        GermanHolidays.EasterSunday(2024).ShouldBe(new DateOnly(2024, 3, 31));
        GermanHolidays.EasterSunday(2025).ShouldBe(new DateOnly(2025, 4, 20));
        GermanHolidays.EasterSunday(2030).ShouldBe(new DateOnly(2030, 4, 21));
        GermanHolidays.For(2027, "BY").ShouldContain(h => h.Name == "Fronleichnam" && h.Date == new DateOnly(2027, 5, 27));
        GermanHolidays.For(2026, "BB").ShouldContain(h => h.Name == "Pfingstsonntag" && h.Date == new DateOnly(2026, 5, 24));
        GermanHolidays.For(2026, "TH").ShouldContain(h => h.Name == "Weltkindertag");
        GermanHolidays.For(2022, "MV").ShouldNotContain(h => h.Name == "Internationaler Frauentag");
    }

    [Fact]
    public void WorkingDays_WeekendsHolidaysAndHalfDays()
    {
        var nw = GermanHolidays.Dates(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "NW");
        var by = GermanHolidays.Dates(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "BY");
        // 30.03.–10.04.2026: zwei Wochen, Karfreitag und Ostermontag frei → 8 Arbeitstage.
        VacationCalculator.WorkingDays(VacationCalculator.DefaultWorkDays, new DateOnly(2026, 3, 30), new DateOnly(2026, 4, 10), false, false, nw).ShouldBe(8m);
        // Halber erster und letzter Tag.
        VacationCalculator.WorkingDays(VacationCalculator.DefaultWorkDays, new DateOnly(2026, 3, 30), new DateOnly(2026, 4, 10), true, true, nw).ShouldBe(7m);
        // Ein halber Tag.
        VacationCalculator.WorkingDays(VacationCalculator.DefaultWorkDays, new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10), true, false, nw).ShouldBe(0.5m);
        // Mariä Himmelfahrt (Sa 15.08.2026) zählt ohnehin nicht; Allerheiligen ist 2026 ein Sonntag.
        VacationCalculator.WorkingDays(VacationCalculator.DefaultWorkDays, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 16), false, false, by).ShouldBe(5m);
        // Vier-Tage-Woche (Mo–Do).
        const int fourDays = (1 << 1) | (1 << 2) | (1 << 3) | (1 << 4);
        VacationCalculator.WorkingDays(fourDays, new DateOnly(2026, 6, 8), new DateOnly(2026, 6, 14), false, false, nw).ShouldBe(4m);
    }

    [Fact]
    public void Entitlement_ProratedByFullMonths()
    {
        VacationCalculator.Prorated(30, null, null, 2026).ShouldBe(30m);
        VacationCalculator.Prorated(30, new DateOnly(2026, 3, 1), null, 2026).ShouldBe(25m, "März bis Dezember = 10 Monate");
        VacationCalculator.Prorated(30, new DateOnly(2026, 3, 15), null, 2026).ShouldBe(23m, "April bis Dezember = 9 Monate = 22,5 → aufgerundet");
        VacationCalculator.Prorated(28, null, new DateOnly(2026, 7, 31), 2026).ShouldBe(16.5m, "7 Monate = 16,33 → halber Tag");
        VacationCalculator.Prorated(30, new DateOnly(2025, 5, 1), new DateOnly(2026, 6, 30), 2026).ShouldBe(15m);
        VacationCalculator.Prorated(30, new DateOnly(2027, 1, 1), null, 2026).ShouldBe(0m);
    }

    [Fact]
    public void Account_CarryOverExpires_SicknessDuringVacation_HalfDaysAndRequests()
    {
        var vacation = StandardAbsenceTypes.Vacation;
        var account = VacationAccount.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), 2026);
        account.Update(null, 5, null, 1, "Ausgleich");
        // Februar: 2 Tage Urlaub (verbrauchen den Übertrag zuerst), Juni: Mo–Fr Urlaub mit Krankheit am Mittwoch, Juli: halber Tag.
        List<AbsenceSpan> absences =
        [
            new(new DateOnly(2026, 2, 9), new DateOnly(2026, 2, 10), vacation),
            new(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 19), vacation),
            new(new DateOnly(2026, 6, 17), new DateOnly(2026, 6, 17), StandardAbsenceTypes.Sick),
            new(new DateOnly(2026, 7, 3), new DateOnly(2026, 7, 3), vacation, HalfDayStart: true),
        ];
        var after = VacationCalculator.Calculate(Input(2026, new DateOnly(2026, 8, 1), absences, account));
        after.Entitlement.ShouldBe(30m);
        after.CarryOver.ShouldBe(5m);
        after.CarryOverExpired.ShouldBe(3m, "5 Übertrag − 2 bis 31.03. genommen");
        after.Taken.ShouldBe(6.5m, "2 + 4 (Mittwoch krank) + 0,5");
        after.SickDays.ShouldBe(1);
        after.Adjustment.ShouldBe(1m);
        after.Remaining.ShouldBe(30m + 5 + 1 - 3 - 6.5m);
        after.ByType.ShouldNotBeNull().ShouldContain(t => t.TypeId == StandardAbsenceTypes.Sick && t.Days == 1);

        // Vor dem Verfall: Warnung „verfällt“, noch nichts verfallen; geplanter Urlaub bis 31.03. zählt mit.
        var before = VacationCalculator.Calculate(Input(2026, new DateOnly(2026, 2, 1), [.. absences, new(new DateOnly(2026, 3, 30), new DateOnly(2026, 3, 30), vacation)],
            account, pending: [new(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9), vacation)]));
        before.CarryOverExpired.ShouldBe(0m);
        before.CarryOverExpiring.ShouldBe(2m);
        before.CarryOverExpiresOn.ShouldBe(new DateOnly(2026, 3, 31));
        before.Taken.ShouldBe(0m);
        before.Planned.ShouldBe(7.5m);
        before.Requested.ShouldBe(5m);
        before.RemainingAfterRequests.ShouldBe(before.Remaining - 5);

        // Verlängertes Verfallsdatum im Konto.
        account.Update(null, 5, new DateOnly(2026, 9, 30), 0, null);
        VacationCalculator.Calculate(Input(2026, new DateOnly(2026, 8, 1), absences, account)).CarryOverExpiring.ShouldBe(0m, "Februar 2 + Juni 4 + Juli 0,5 ≥ 5");

        // Unbezahlter Urlaub und Überstundenabbau zählen nicht vom Urlaubskonto; Betriebsferien schon.
        var other = VacationCalculator.Calculate(Input(2026, new DateOnly(2026, 1, 2),
        [
            new(new DateOnly(2026, 5, 4), new DateOnly(2026, 5, 5), StandardAbsenceTypes.Unpaid),
            new(new DateOnly(2026, 5, 6), new DateOnly(2026, 5, 6), StandardAbsenceTypes.Overtime),
            new(new DateOnly(2026, 12, 28), new DateOnly(2026, 12, 31), StandardAbsenceTypes.Closure),
        ]));
        other.Planned.ShouldBe(4m);
        other.ByType!.Single(t => t.TypeId == StandardAbsenceTypes.Unpaid).Days.ShouldBe(2m);

        // Eintritt im Jahr: anteilig, Tage vor dem Eintritt zählen nicht.
        VacationCalculator.Calculate(Input(2026, new DateOnly(2026, 1, 2), [], entry: new DateOnly(2026, 7, 1))).Entitlement.ShouldBe(15m);
    }

    [Fact]
    public async Task Types_HalfDays_Preview_AutoApproval_ChangeRequest_Substitute_Notifications()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        var anna = await _factory.CreateUserAsync("anna", roleIds: StandardRoles.SiteWorkerId);
        var ben = await _factory.CreateUserAsync("ben", roleIds: StandardRoles.SiteWorkerId);
        var employee = await SendAsync<EmployeeDetail>(admin, HttpMethod.Put, $"/api/v1/staff/employees/{Guid.CreateVersion7()}", new
        {
            firstName = "Anna", lastName = "Kraus", userId = anna.Id, vacationDaysPerYear = 30m, entryDate = $"{Today.Year - 3}-01-01",
        });
        using var annaClient = _factory.CreateClient();
        annaClient.WithToken((await annaClient.LoginOkAsync("anna")).AccessToken);

        var types = await SendAsync<List<AbsenceTypeView>>(annaClient, HttpMethod.Get, "/api/v1/hr/absence-types");
        types.Select(t => t.Key).ShouldBe(["vacation", "sick", "child_sick", "school", "training", "special_leave", "overtime", "unpaid", "closure", "other"]);
        types.Single(t => t.Key == "child_sick").IsSickness.ShouldBeTrue();
        var mine = await SendAsync<MyVacation>(annaClient, HttpMethod.Get, "/api/v1/hr/me");
        mine.Types.ShouldNotBeNull().ShouldNotContain(t => t.IsSickness || t.Key == "closure");

        var year = Today.Year + 1;
        var monday = new DateOnly(year, 6, 7);
        while (monday.DayOfWeek != DayOfWeek.Monday)
        {
            monday = monday.AddDays(1);
        }

        // Vorschau: Mo halb bis Mi = 2,5 Tage, Vertretung Ben.
        var preview = await SendAsync<AbsencePreview>(annaClient, HttpMethod.Post, "/api/v1/hr/absence-requests/preview", new
        {
            from = monday, to = monday.AddDays(2), typeId = StandardAbsenceTypes.Vacation, halfDayStart = true, substituteUserId = ben.Id,
        });
        preview.WorkingDays.ShouldBe(2.5m);
        preview.RequiresApproval.ShouldBeTrue();
        preview.RemainingAfter.ShouldNotBeNull();

        var request = await SendAsync<AbsenceRequestView>(annaClient, HttpMethod.Put, $"/api/v1/hr/absence-requests/{Guid.CreateVersion7()}", new
        {
            from = monday, to = monday.AddDays(2), typeId = StandardAbsenceTypes.Vacation, halfDayStart = true, substituteUserId = ben.Id, note = "Familie",
        });
        request.Status.ShouldBe(AbsenceRequestStatus.Pending);
        request.WorkingDays.ShouldBe(2.5m);
        request.TypeName.ShouldBe("Urlaub");
        request.SubstituteName.ShouldNotBeNull();

        // Büro wird benachrichtigt: Titel = Ereignis, Text = Objekt · Angaben.
        var inbox = await SendAsync<NotificationInbox>(office, HttpMethod.Get, "/api/v1/notifications/");
        var notice = inbox.Items.ShouldHaveSingleItem();
        notice.Title.ShouldBe("Abwesenheitsantrag eingegangen");
        notice.Body.ShouldNotBeNull().ShouldContain("Urlaub · ");
        notice.Body.ShouldEndWith("2,5 Tage");

        // Offenen Antrag ändern (gleiche ID): jetzt ganze Tage.
        var changed = await SendAsync<AbsenceRequestView>(annaClient, HttpMethod.Put, $"/api/v1/hr/absence-requests/{request.Id}", new
        {
            from = monday, to = monday.AddDays(2), typeId = StandardAbsenceTypes.Vacation, substituteUserId = ben.Id, note = "Familie",
        });
        changed.WorkingDays.ShouldBe(3m);

        // Ablehnung nur mit Begründung.
        using (var noReason = await office.PostAsJsonAsync($"/api/v1/hr/absence-requests/{request.Id}/decision", new { approve = false }, Ct))
        {
            noReason.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        var approved = await SendAsync<AbsenceRequestView>(office, HttpMethod.Post, $"/api/v1/hr/absence-requests/{request.Id}/decision", new { approve = true });
        approved.Status.ShouldBe(AbsenceRequestStatus.Approved);
        var benInbox = await SendAsync<NotificationInbox>(_factory.CreateClient().WithToken((await _factory.CreateClient().LoginOkAsync("ben")).AccessToken), HttpMethod.Get,
            "/api/v1/notifications/");
        benInbox.Items.ShouldContain(n => n.Title == "Vertretung eingetragen");

        // Änderungsantrag: Di–Do statt Mo–Mi; der alte Urlaub bleibt bis zur Genehmigung bestehen.
        mine = await SendAsync<MyVacation>(annaClient, HttpMethod.Get, $"/api/v1/hr/me?year={year}");
        mine.Requests.Single(r => r.Id == request.Id).CanChange.ShouldBeTrue();
        var change = await SendAsync<AbsenceRequestView>(annaClient, HttpMethod.Put, $"/api/v1/hr/absence-requests/{Guid.CreateVersion7()}", new
        {
            from = monday.AddDays(1), to = monday.AddDays(3), typeId = StandardAbsenceTypes.Vacation, replacesRequestId = request.Id,
        });
        change.ReplacesRequestId.ShouldBe(request.Id);
        change.Warnings.ShouldNotContain("Im Zeitraum ist bereits eine Abwesenheit eingetragen.", "Der ersetzte Urlaub zählt nicht als Überschneidung.");
        using (var second = await annaClient.PutAsJsonAsync($"/api/v1/hr/absence-requests/{Guid.CreateVersion7()}", new
        {
            from = monday.AddDays(2), to = monday.AddDays(4), typeId = StandardAbsenceTypes.Vacation, replacesRequestId = request.Id,
        }, Ct))
        {
            second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        (await SendAsync<AbsenceRequestView>(office, HttpMethod.Post, $"/api/v1/hr/absence-requests/{change.Id}/decision", new { approve = true }))
            .Status.ShouldBe(AbsenceRequestStatus.Approved);
        mine = await SendAsync<MyVacation>(annaClient, HttpMethod.Get, $"/api/v1/hr/me?year={year}");
        mine.Requests.Single(r => r.Id == request.Id).Status.ShouldBe(AbsenceRequestStatus.Cancelled);
        mine.Requests.Single(r => r.Id == request.Id).DecisionComment.ShouldBe("Durch Änderungsantrag ersetzt.");
        var absence = mine.Absences.ShouldNotBeNull().ShouldHaveSingleItem();
        absence.From.ShouldBe(monday.AddDays(1));
        absence.WorkingDays.ShouldBe(3m);
        mine.Vacation.ShouldNotBeNull().Planned.ShouldBe(3m);

        // Berufsschule: nicht genehmigungspflichtig, gilt sofort; Mitarbeiter darf keine Krankmeldung beantragen.
        var school = await SendAsync<AbsenceRequestView>(annaClient, HttpMethod.Put, $"/api/v1/hr/absence-requests/{Guid.CreateVersion7()}", new
        {
            from = monday.AddDays(14), to = monday.AddDays(14), typeId = StandardAbsenceTypes.School,
        });
        school.Status.ShouldBe(AbsenceRequestStatus.Approved);
        using (var sick = await annaClient.PutAsJsonAsync($"/api/v1/hr/absence-requests/{Guid.CreateVersion7()}", new
        {
            from = monday.AddDays(20), to = monday.AddDays(20), typeId = StandardAbsenceTypes.ChildSick,
        }, Ct))
        {
            sick.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        using (var closure = await annaClient.PutAsJsonAsync($"/api/v1/hr/absence-requests/{Guid.CreateVersion7()}", new
        {
            from = monday.AddDays(20), to = monday.AddDays(20), typeId = StandardAbsenceTypes.Closure,
        }, Ct))
        {
            closure.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, "Betriebsferien trägt das Büro ein.");
        }

        // Stornierung durch den Mitarbeiter (noch nicht begonnen): Genehmiger erfahren es.
        (await SendAsync<AbsenceRequestView>(annaClient, HttpMethod.Post, $"/api/v1/hr/absence-requests/{change.Id}/cancel")).Status.ShouldBe(AbsenceRequestStatus.Cancelled);
        inbox = await SendAsync<NotificationInbox>(office, HttpMethod.Get, "/api/v1/notifications/");
        inbox.Items.ShouldContain(n => n.Title == "Genehmigte Abwesenheit storniert");
        (await SendAsync<MyVacation>(annaClient, HttpMethod.Get, $"/api/v1/hr/me?year={year}")).Absences.ShouldNotBeNull().ShouldHaveSingleItem().TypeId
            .ShouldBe(StandardAbsenceTypes.School);
        employee.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task CrewLeaderApproval_MinimumStaff_Rights_AndIdor()
    {
        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        var lead = await _factory.CreateUserAsync("lead", roleIds: StandardRoles.SiteWorkerId);
        var anna = await _factory.CreateUserAsync("anna", roleIds: StandardRoles.SiteWorkerId);
        var carl = await _factory.CreateUserAsync("carl", roleIds: StandardRoles.SiteWorkerId);
        var crew = await SendAsync<CrewResponse>(admin, HttpMethod.Put, $"/api/v1/planning/crews/{Guid.CreateVersion7()}", new
        {
            name = "Kolonne Süd", leaderUserId = lead.Id, memberUserIds = new[] { anna.Id, carl.Id }, minimumStaff = 3,
        });
        crew.MinimumStaff.ShouldBe(3);
        // Ältere Clients ohne Angabe ändern die Mindestbesetzung nicht.
        (await SendAsync<CrewResponse>(admin, HttpMethod.Put, $"/api/v1/planning/crews/{crew.Id}", new
        {
            name = "Kolonne Süd", leaderUserId = lead.Id, memberUserIds = new[] { anna.Id, carl.Id },
        })).MinimumStaff.ShouldBe(3);

        using var annaClient = _factory.CreateClient();
        annaClient.WithToken((await annaClient.LoginOkAsync("anna")).AccessToken);
        using var leadClient = _factory.CreateClient();
        leadClient.WithToken((await leadClient.LoginOkAsync("lead")).AccessToken);
        using var carlClient = _factory.CreateClient();
        carlClient.WithToken((await carlClient.LoginOkAsync("carl")).AccessToken);

        var day = new DateOnly(Today.Year + 1, 9, 1);
        while (day.DayOfWeek != DayOfWeek.Tuesday)
        {
            day = day.AddDays(1);
        }

        var request = await SendAsync<AbsenceRequestView>(annaClient, HttpMethod.Put, $"/api/v1/hr/absence-requests/{Guid.CreateVersion7()}", new
        {
            from = day, to = day, typeId = StandardAbsenceTypes.Overtime,
        });
        var pending = await SendAsync<List<AbsenceRequestView>>(admin, HttpMethod.Get, "/api/v1/hr/absence-requests?scope=pending");
        pending.ShouldHaveSingleItem().Warnings.ShouldContain(w => w.Contains("Mindestbesetzung 3", StringComparison.Ordinal));

        // Ohne Einstellung darf der Kolonnenführer nicht entscheiden und sieht den Antrag nicht.
        using (var denied = await leadClient.PostAsJsonAsync($"/api/v1/hr/absence-requests/{request.Id}/decision", new { approve = true }, Ct))
        {
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await SendAsync<List<AbsenceRequestView>>(leadClient, HttpMethod.Get, "/api/v1/hr/absence-requests?scope=pending")).ShouldBeEmpty();
        await SendAsync<HrSettingsResponse>(admin, HttpMethod.Put, "/api/v1/hr/settings", new { state = "BY", crewLeadersApprove = true, carryOverExpiryMonth = 4, carryOverExpiryDay = 30 });
        var settings = await SendAsync<HrSettingsResponse>(admin, HttpMethod.Get, "/api/v1/hr/settings");
        settings.CrewLeadersApprove.ShouldBeTrue();
        settings.CarryOverExpiryMonth.ShouldBe(4);
        settings.MaxCrewAbsencePercent.ShouldBe(50);
        var leadPending = await SendAsync<List<AbsenceRequestView>>(leadClient, HttpMethod.Get, "/api/v1/hr/absence-requests?scope=pending");
        leadPending.ShouldHaveSingleItem().CanDecide.ShouldBeTrue();
        (await SendAsync<HrSummary>(leadClient, HttpMethod.Get, "/api/v1/hr/summary")).PendingApprovals.ShouldBe(1);
        (await SendAsync<AbsenceRequestView>(leadClient, HttpMethod.Post, $"/api/v1/hr/absence-requests/{request.Id}/decision", new { approve = true, comment = "In Ordnung" }))
            .Status.ShouldBe(AbsenceRequestStatus.Approved);

        // Kolonnenkollege darf weder entscheiden noch fremde Anträge stornieren (404, kein Hinweis auf Existenz).
        var other = await SendAsync<AbsenceRequestView>(annaClient, HttpMethod.Put, $"/api/v1/hr/absence-requests/{Guid.CreateVersion7()}", new
        {
            from = day.AddDays(7), to = day.AddDays(7), typeId = StandardAbsenceTypes.Vacation,
        });
        using (var foreignCancel = await carlClient.PostAsync(new Uri($"/api/v1/hr/absence-requests/{other.Id}/cancel", UriKind.Relative), null, Ct))
        {
            foreignCancel.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        using (var foreignDecide = await carlClient.PostAsJsonAsync($"/api/v1/hr/absence-requests/{other.Id}/decision", new { approve = true }, Ct))
        {
            foreignDecide.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var forOther = await carlClient.PutAsJsonAsync($"/api/v1/hr/absence-requests/{Guid.CreateVersion7()}", new
        {
            userId = anna.Id, from = day.AddDays(14), to = day.AddDays(14),
        }, Ct))
        {
            forOther.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        (await SendAsync<List<AbsenceRequestView>>(carlClient, HttpMethod.Get, "/api/v1/hr/absence-requests?scope=all")).ShouldBeEmpty();
        foreach (var (method, url) in new[]
        {
            (HttpMethod.Get, "/api/v1/hr/vacation-accounts"), (HttpMethod.Get, "/api/v1/hr/vacation-accounts.csv"), (HttpMethod.Get, "/api/v1/hr/vacation-plan?from=2026-01-01&to=2026-01-31"),
        })
        {
            using var response = await carlClient.SendAsync(new HttpRequestMessage(method, url), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, url);
        }

        using (var typeChange = await carlClient.PutAsJsonAsync($"/api/v1/hr/absence-types/{StandardAbsenceTypes.Vacation}", new { name = "Ferien", code = "F", color = "#00FF00" }, Ct))
        {
            typeChange.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Closures_Accounts_CarryOver_Export_AndSicknessPrivacy()
    {
        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var anna = await _factory.CreateUserAsync("anna", roleIds: StandardRoles.SiteWorkerId);
        var year = Today.Year + 1;
        var employee = await SendAsync<EmployeeDetail>(admin, HttpMethod.Put, $"/api/v1/staff/employees/{Guid.CreateVersion7()}", new
        {
            firstName = "Anna", lastName = "Kraus", userId = anna.Id, vacationDaysPerYear = 24m, entryDate = $"{year}-04-01", personnelNumber = "P-9",
        });

        // Betriebsferien 28.–31.12. (Montag bis Donnerstag je nach Jahr), für alle eingetragen und vom Urlaub abgezogen.
        var closure = await SendAsync<ClosureView>(admin, HttpMethod.Put, $"/api/v1/hr/closures/{Guid.CreateVersion7()}", new
        {
            name = "Betriebsferien Jahreswechsel", from = new DateOnly(year, 12, 28), to = new DateOnly(year, 12, 31),
        });
        closure.Employees.ShouldBeGreaterThanOrEqualTo(3);
        var closureDays = VacationCalculator.WorkingDays(VacationCalculator.DefaultWorkDays, closure.From, closure.To, false, false,
            GermanHolidays.Dates(closure.From, closure.To, "NW"));
        closure.WorkingDays.ShouldBe(closureDays);
        (await SendAsync<List<ClosureView>>(office, HttpMethod.Get, $"/api/v1/hr/closures?year={year}")).ShouldHaveSingleItem();

        var accounts = await SendAsync<List<VacationAccountRow>>(office, HttpMethod.Get, $"/api/v1/hr/vacation-accounts?year={year}");
        var row = accounts.Single(a => a.EmployeeId == employee.Id);
        row.Vacation.Entitlement.ShouldBe(18m, "April bis Dezember = 9/12 von 24");
        row.Vacation.Planned.ShouldBe(closureDays);
        row.Vacation.ByType.ShouldNotBeNull().ShouldContain(t => t.TypeId == StandardAbsenceTypes.Closure);

        // Korrektur im Konto (abweichender Anspruch, Übertrag, Korrektur), Verlauf im Protokoll.
        var saved = await SendAsync<VacationAccountRow>(admin, HttpMethod.Put, $"/api/v1/hr/vacation-accounts/{employee.Id}", new
        {
            year, entitlement = 20m, carryOver = 3m, carryOverExpiresOn = new DateOnly(year, 6, 30), adjustment = -1m, note = "Vereinbarung",
        });
        saved.Vacation.Entitlement.ShouldBe(20m);
        saved.Vacation.CarryOver.ShouldBe(3m);
        saved.Vacation.CarryOverExpiresOn.ShouldBe(new DateOnly(year, 6, 30));
        saved.Account.ShouldNotBeNull().Note.ShouldBe("Vereinbarung");
        saved.Vacation.Remaining.ShouldBe(20m + 3 - 1 - closureDays);

        // Folgejahr: Rest wird automatisch übertragen (ohne Übertrag aus dem Vorjahr, der Übertrag verfällt am 30.06.).
        var next = (await SendAsync<List<VacationAccountRow>>(office, HttpMethod.Get, $"/api/v1/hr/vacation-accounts?year={year + 1}")).Single(a => a.EmployeeId == employee.Id);
        next.Vacation.CarryOver.ShouldBe(saved.Vacation.Remaining - 3, "Der Übertrag von 3 Tagen ist zum 30.06. verfallen.");
        next.Vacation.Entitlement.ShouldBe(24m);

        // Export CSV (Excel-tauglich) und PDF.
        using var csv = await office.GetAsync(new Uri($"/api/v1/hr/vacation-accounts.csv?year={year}", UriKind.Relative), Ct);
        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        var text = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync(Ct));
        text.ShouldContain("Personalnummer;Mitarbeiter;Kolonne");
        text.ShouldContain("P-9;Anna Kraus;");
        using var pdf = await office.GetAsync(new Uri($"/api/v1/hr/vacation-accounts.pdf?year={year}", UriKind.Relative), Ct);
        Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync(Ct), 0, 5).ShouldBe("%PDF-");

        // Kind krank: Personalverwaltung sieht die Art, Büro (ohne hr.manage) nur „Sonstige Abwesenheit“.
        var sickDay = new DateOnly(year, 5, 5);
        await SendAsync<SickNoteView>(admin, HttpMethod.Put, $"/api/v1/hr/sick-notes/{Guid.CreateVersion7()}", new
        {
            userId = anna.Id, from = sickDay, to = sickDay, typeId = StandardAbsenceTypes.ChildSick,
        });
        using (var wrong = await admin.PutAsJsonAsync($"/api/v1/hr/sick-notes/{Guid.CreateVersion7()}", new
        {
            userId = anna.Id, from = sickDay, to = sickDay, typeId = StandardAbsenceTypes.Vacation,
        }, Ct))
        {
            wrong.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        var plan = await SendAsync<VacationPlan>(office, HttpMethod.Get, $"/api/v1/hr/vacation-plan?from={sickDay:yyyy-MM-dd}&to={sickDay:yyyy-MM-dd}");
        var entry = plan.Rows.Single(r => r.UserId == anna.Id).Entries.ShouldHaveSingleItem();
        entry.TypeId.ShouldBe(StandardAbsenceTypes.Other);
        entry.Kind.ShouldBe(AbsenceKind.Other);
        var hrPlan = await SendAsync<VacationPlan>(admin, HttpMethod.Get, $"/api/v1/hr/vacation-plan?from={sickDay:yyyy-MM-dd}&to={sickDay:yyyy-MM-dd}");
        hrPlan.Rows.Single(r => r.UserId == anna.Id).Entries.ShouldHaveSingleItem().TypeId.ShouldBe(StandardAbsenceTypes.ChildSick);
        (await SendAsync<List<VacationAccountRow>>(office, HttpMethod.Get, $"/api/v1/hr/vacation-accounts?year={year}")).Single(a => a.EmployeeId == employee.Id)
            .Vacation.ByType.ShouldNotBeNull().ShouldNotContain(t => t.TypeId == StandardAbsenceTypes.ChildSick);

        // Eigene Art anlegen, Standardart umbenennen; Krankheitsarten nie beantragbar.
        var custom = await SendAsync<AbsenceTypeView>(admin, HttpMethod.Put, $"/api/v1/hr/absence-types/{Guid.CreateVersion7()}", new
        {
            name = "Arzttermin", code = "AT", color = "#3949ab", kind = "other", requiresApproval = false,
        });
        custom.Key.ShouldStartWith("custom_");
        custom.Color.ShouldBe("#3949AB");
        using (var invalid = await admin.PutAsJsonAsync($"/api/v1/hr/absence-types/{Guid.CreateVersion7()}", new
        {
            name = "Krank selbst", code = "KS", color = "#FF0000", kind = "sick", requestable = true,
        }, Ct))
        {
            invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        // Betriebsferien löschen entfernt die Einträge wieder.
        (await admin.DeleteAsync(new Uri($"/api/v1/hr/closures/{closure.Id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync<List<VacationAccountRow>>(office, HttpMethod.Get, $"/api/v1/hr/vacation-accounts?year={year}")).Single(a => a.EmployeeId == employee.Id)
            .Vacation.Planned.ShouldBe(0m);
    }
}
