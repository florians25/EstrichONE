using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Notifications;
using EstrichOne.Api.Modules.Planning;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Planning;

/// <summary>
/// Warnungen bei Krankmeldungen: Regeln (AU ab Kalendertag, Entgeltfortzahlung, BEM, Überschneidung, Häufung, angrenzende Tage),
/// betroffene Einsätze mit Mindestbesetzung, Datenschutz (Disposition nur „abwesend“), Benachrichtigung, Kenntnisnahme, eAU, Einstellungen.
/// </summary>
public sealed class AbsenceWarningTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 11, 18); // Mittwoch
    private static readonly Guid Anna = Guid.Parse("01a20000-0000-7000-8000-000000000001");
    private static readonly int[] ShortMonths = [3, 5, 8, 10];
    private static readonly Guid Ben = Guid.Parse("01a20000-0000-7000-8000-000000000002");

    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    // ---- Regeln (ohne Datenbank) ------------------------------------------------------------------------------------

    private static HrSettings Settings(Action<HrSettings>? change = null)
    {
        var s = new HrSettings();
        change?.Invoke(s);
        return s;
    }

    private static SickEntry Sick(DateOnly from, DateOnly to, Guid? caseId = null, bool certificate = false, Guid? user = null, bool child = false)
    {
        var id = Guid.CreateVersion7();
        return new SickEntry(id, user ?? Anna, from, to, caseId ?? id, certificate, !child);
    }

    private static AbsenceWarningInput Input(HrSettings settings, IReadOnlyList<SickEntry> sick, IReadOnlyList<VacationEntry>? vacations = null,
        IReadOnlyList<PlannedAbsence>? absences = null, IReadOnlyList<PlannedAssignment>? assignments = null, DateOnly? today = null) =>
        new(today ?? Today, settings, sick, vacations ?? [], absences ?? [], assignments ?? [],
            GermanHolidays.Dates(new DateOnly(2025, 1, 1), new DateOnly(2027, 12, 31), "NW"),
            new Dictionary<Guid, string> { [Anna] = "Anna Kraus", [Ben] = "Ben Maier" }, new Dictionary<Guid, Guid>());

    private static List<AbsenceWarning> Of(string kind, List<AbsenceWarning> all) => all.Where(w => w.Kind == kind).ToList();

    [Fact]
    public void Certificate_DueFromConfiguredCalendarDay_FollowUpAndProof()
    {
        // Seit Sonntag 15.11. krank (heute 18.11. = 4. Kalendertag): Nachweis fällig.
        var first = Sick(new DateOnly(2026, 11, 15), new DateOnly(2026, 11, 20));
        var warning = Of(AbsenceWarningKinds.Certificate, AbsenceWarningRules.Compute(Input(Settings(), [first]))).ShouldHaveSingleItem();
        warning.Date.ShouldBe(new DateOnly(2026, 11, 18));
        warning.Title.ShouldContain("AU-Bescheinigung fehlt");
        warning.Text.ShouldContain("4. Kalendertag");
        warning.Severity.ShouldBe("warning");

        // Erst am 3. Kalendertag: noch nicht fällig; dreitägige Krankmeldung braucht keinen Nachweis.
        AbsenceWarningRules.Compute(Input(Settings(), [Sick(new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 20))]))
            .ShouldNotContain(w => w.Kind == AbsenceWarningKinds.Certificate);
        AbsenceWarningRules.Compute(Input(Settings(), [Sick(new DateOnly(2026, 11, 14), new DateOnly(2026, 11, 16))]))
            .ShouldNotContain(w => w.Kind == AbsenceWarningKinds.Certificate);

        // Einstellung „ab dem 1. Tag“: auch eintägige Krankmeldung.
        var strict = Settings(s => s.UpdateWarnings(true, true, 14, true, 1, true, true, true, 0, false, true, true, false));
        AbsenceWarningRules.Compute(Input(strict, [Sick(Today, Today)])).ShouldContain(w => w.Kind == AbsenceWarningKinds.Certificate);

        // Mit Nachweis keine Warnung; Folgebescheinigung ohne Nachweis eigene Warnung; lange überfällig = Fehler.
        var withProof = first with { HasCertificate = true };
        var followUp = Sick(new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 27), first.CaseId);
        AbsenceWarningRules.Compute(Input(Settings(), [withProof, followUp], today: new DateOnly(2026, 11, 22)))
            .Where(w => w.Kind == AbsenceWarningKinds.Certificate).ShouldHaveSingleItem().Title.ShouldContain("Folgebescheinigung");
        AbsenceWarningRules.Compute(Input(Settings(), [first], today: new DateOnly(2026, 11, 25))).Single(w => w.Kind == AbsenceWarningKinds.Certificate)
            .Severity.ShouldBe("error");
    }

    [Fact]
    public void ContinuedPay_NoticeOneWeekBefore_Cumulated_AndNotForChildSick()
    {
        // Krank seit 14.10.: 6 Wochen enden am 24.11. – Hinweis ab 17.11.
        var c = Sick(new DateOnly(2026, 10, 14), new DateOnly(2026, 11, 30), certificate: true);
        var w = Of(AbsenceWarningKinds.ContinuedPay, AbsenceWarningRules.Compute(Input(Settings(), [c]))).ShouldHaveSingleItem();
        w.Date.ShouldBe(new DateOnly(2026, 11, 24));
        w.Text.ShouldContain("24.11.2026");
        w.Text.ShouldContain("Nur ein Hinweis");
        w.Severity.ShouldBe("info");
        AbsenceWarningRules.Compute(Input(Settings(), [c], today: new DateOnly(2026, 11, 10))).ShouldNotContain(x => x.Kind == AbsenceWarningKinds.ContinuedPay);
        AbsenceWarningRules.Compute(Input(Settings(), [c], today: new DateOnly(2026, 11, 26))).Single(x => x.Kind == AbsenceWarningKinds.ContinuedPay)
            .Severity.ShouldBe("warning");

        // Kumuliert: 20 Tage im Sommer, neuer Fall seit 01.11. bis 30.11. → bei derselben Ursache Ende bereits am 22.11.
        var summer = Sick(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 20), certificate: true);
        var now = Sick(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), certificate: true);
        var cumulated = Of(AbsenceWarningKinds.ContinuedPay, AbsenceWarningRules.Compute(Input(Settings(), [summer, now]))).ShouldHaveSingleItem();
        cumulated.Date.ShouldBe(new DateOnly(2026, 11, 22));
        cumulated.Text.ShouldContain("20 Kalendertage");

        // Kind krank: keine Entgeltfortzahlung nach EFZG, kein BEM.
        AbsenceWarningRules.Compute(Input(Settings(), [Sick(new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 30), certificate: true, child: true)]))
            .ShouldNotContain(x => x.Kind == AbsenceWarningKinds.ContinuedPay || x.Kind == AbsenceWarningKinds.Bem);
    }

    [Fact]
    public void Bem_MoreThan42CalendarDaysIn12Months()
    {
        var a = Sick(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), certificate: true); // 28 Tage
        var b = Sick(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 14), certificate: true); // 14 Tage → 42
        AbsenceWarningRules.Compute(Input(Settings(), [a, b])).ShouldNotContain(w => w.Kind == AbsenceWarningKinds.Bem, "genau 42 Tage reichen nicht");
        var c = Sick(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 10), certificate: true);
        var bem = Of(AbsenceWarningKinds.Bem, AbsenceWarningRules.Compute(Input(Settings(), [a, b, c]))).ShouldHaveSingleItem();
        bem.Key.ShouldBe($"bem:{Anna}:{c.CaseId}");
        bem.Text.ShouldContain("43 Kalendertage");
        bem.Text.ShouldContain("§ 167 Abs. 2 SGB IX");
        // Älter als 12 Monate zählt nicht; abgeschaltet keine Warnung.
        AbsenceWarningRules.Compute(Input(Settings(), [a, b, c], today: new DateOnly(2027, 2, 15))).ShouldNotContain(w => w.Kind == AbsenceWarningKinds.Bem);
        AbsenceWarningRules.Compute(Input(Settings(s => s.UpdateWarnings(true, true, 14, true, 4, true, false, true, 0, false, true, true, false)), [a, b, c]))
            .ShouldNotContain(w => w.Kind == AbsenceWarningKinds.Bem);
    }

    [Fact]
    public void Overlap_FrequentShort_Adjacent_OnlyWhenSwitchedOn()
    {
        var sick = Sick(new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 17), certificate: true);
        var vacation = new VacationEntry(Guid.CreateVersion7(), Anna, new DateOnly(2026, 11, 17), new DateOnly(2026, 11, 20), false);
        var pending = new VacationEntry(Guid.CreateVersion7(), Anna, new DateOnly(2026, 11, 16), new DateOnly(2026, 11, 16), true);
        var overlaps = Of(AbsenceWarningKinds.VacationOverlap, AbsenceWarningRules.Compute(Input(Settings(), [sick], [vacation, pending])));
        overlaps.Count.ShouldBe(2);
        overlaps.ShouldContain(w => w.Severity == "info" && w.Text.Contains("§ 9 BUrlG", StringComparison.Ordinal));
        overlaps.ShouldContain(w => w.Severity == "warning" && w.Link == "/personal/urlaub?reiter=antraege");

        // Häufung und angrenzende Tage sind standardmäßig aus.
        var shorts = ShortMonths.Select(m => Sick(new DateOnly(2026, m, 2), new DateOnly(2026, m, 3), certificate: true)).Append(sick).ToList();
        var defaults = AbsenceWarningRules.Compute(Input(Settings(), shorts, [vacation]));
        defaults.ShouldNotContain(w => w.Kind == AbsenceWarningKinds.FrequentShort || w.Kind == AbsenceWarningKinds.Adjacent);

        var on = Settings(s => s.UpdateWarnings(true, true, 14, true, 4, true, true, true, 5, true, true, true, false));
        var all = AbsenceWarningRules.Compute(Input(on, shorts, [vacation]));
        Of(AbsenceWarningKinds.FrequentShort, all).ShouldHaveSingleItem().Text.ShouldContain("5 Krankmeldungen");
        // Montag 16.11. nach dem Wochenende, endet direkt vor dem Urlaub.
        var adjacent = Of(AbsenceWarningKinds.Adjacent, all).ShouldHaveSingleItem();
        adjacent.Text.ShouldContain("beginnt direkt nach Wochenende bzw. Feiertag");
        adjacent.Severity.ShouldBe("info");

        // Hauptschalter aus: nichts.
        AbsenceWarningRules.Compute(Input(Settings(s => s.UpdateWarnings(false, true, 14, true, 4, true, true, true, 5, true, true, true, false)), shorts, [vacation]))
            .ShouldBeEmpty();
    }

    [Fact]
    public void PlanningImpact_UnderstaffedCrew_AndHalfDay()
    {
        var crew = Guid.CreateVersion7();
        var day = Today.AddDays(1);
        var absence = new PlannedAbsence(Guid.CreateVersion7(), Anna, Today, Today.AddDays(2), "abwesend", false, false);
        var assignment = new PlannedAssignment(Guid.CreateVersion7(), day, Guid.CreateVersion7(), "Halle Nord", "Estrich einbringen", crew, "Kolonne Nord", 2, [Anna, Ben]);
        var later = new PlannedAssignment(Guid.CreateVersion7(), Today.AddDays(30), Guid.CreateVersion7(), "Später", "Estrich", crew, "Kolonne Nord", 2, [Anna, Ben]);
        var input = Input(Settings(), [], absences: [absence], assignments: [assignment, later]);
        var w = AbsenceWarningRules.ImpactOf(absence, input).ShouldNotBeNull();
        w.Severity.ShouldBe("error");
        w.Title.ShouldBe($"Anna Kraus abwesend {HrEndpoints.Range(Today, Today.AddDays(2))}");
        w.Text.ShouldContain("unterbesetzt: Kolonne Nord");
        w.Link.ShouldBe($"/planung?ansicht=tag&datum={day:yyyy-MM-dd}&tag={day:yyyy-MM-dd}"); // „tag“ für die Apps
        w.Assignments.ShouldHaveSingleItem().Text.ShouldContain("1 von mindestens 2");

        // Halber Tag: die Person zählt noch zur Besetzung.
        var half = absence with { From = day, To = day, HalfDayStart = true };
        var halfInput = Input(Settings(), [], absences: [half], assignments: [assignment]);
        var halfWarning = AbsenceWarningRules.ImpactOf(half, halfInput).ShouldNotBeNull();
        halfWarning.Severity.ShouldBe("warning");
        halfWarning.Assignments.Single().Text.ShouldContain("(halber Tag)");

        // Neuer Schlüssel, sobald sich die betroffenen Einsätze ändern.
        AbsenceWarningRules.Hash([assignment.Id]).ShouldNotBe(AbsenceWarningRules.Hash([assignment.Id, later.Id]));
    }

    // ---- API, Datenschutz, Benachrichtigung --------------------------------------------------------------------------

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string url, object? body = null)
    {
        using var response = await client.SendAsync(new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Ct))!;
    }

    private DateOnly Now => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_factory.Clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")).DateTime);

    [Fact]
    public async Task SickNote_WarnsPlanningWithoutReason_HrWithDetails_AckAndSettings()
    {
        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        using var office = await _factory.CreateClientAsAsync("dispo", StandardRoles.OfficeId);
        var anna = await _factory.CreateUserAsync("anna", roleIds: StandardRoles.SiteWorkerId);
        var carl = await _factory.CreateUserAsync("carl", roleIds: StandardRoles.SiteWorkerId);
        var crewId = Guid.CreateVersion7();
        await SendAsync<CrewResponse>(admin, HttpMethod.Put, $"/api/v1/planning/crews/{crewId}", new
        {
            name = "Kolonne Süd", memberUserIds = new[] { anna.Id, carl.Id }, minimumStaff = 2,
        });
        using (var created = await admin.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title = "Halle Süd", city = "Landshut" }, Ct))
        {
            var bauakte = (await created.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;
            await SendAsync<AssignmentView>(office, HttpMethod.Put, $"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
            {
                bauakteId = bauakte.Id, day = Now.AddDays(1), activity = "Estrich einbringen", crewId,
            });
        }

        // Seit drei Tagen krank (heute 4. Kalendertag), ohne AU.
        var sickId = Guid.CreateVersion7();
        var note = await SendAsync<SickNoteView>(admin, HttpMethod.Put, $"/api/v1/hr/sick-notes/{sickId}", new
        {
            userId = anna.Id, from = Now.AddDays(-3), to = Now.AddDays(3), note = "telefonisch gemeldet",
        });
        note.CertificateStatus.ShouldBe("missing");
        note.CertificateDueOn.ShouldBe(Now);

        // Disposition: nur „abwesend“ mit betroffenem Einsatz, keine Gesundheitsdaten.
        var forOffice = await SendAsync<AbsenceWarningList>(office, HttpMethod.Get, "/api/v1/hr/absence-warnings");
        forOffice.HealthDetails.ShouldBeFalse();
        var impact = forOffice.Items.ShouldHaveSingleItem();
        impact.Kind.ShouldBe(AbsenceWarningKinds.PlanningImpact);
        impact.Title.ShouldContain("abwesend");
        impact.Severity.ShouldBe("error");
        impact.Assignments.ShouldHaveSingleItem().Understaffed.ShouldBeTrue();
        var officeJson = await (await office.GetAsync(new Uri("/api/v1/hr/absence-warnings", UriKind.Relative), Ct)).Content.ReadAsStringAsync(Ct);
        officeJson.ShouldNotContain("krank", Case.Insensitive);
        officeJson.ShouldNotContain("AU", Case.Sensitive);
        officeJson.ShouldNotContain("telefonisch");

        // Personal: zusätzlich AU fehlt.
        var forHr = await SendAsync<AbsenceWarningList>(admin, HttpMethod.Get, "/api/v1/hr/absence-warnings");
        forHr.HealthDetails.ShouldBeTrue();
        forHr.Items.ShouldContain(w => w.Kind == AbsenceWarningKinds.Certificate && w.AbsenceId == sickId);
        forHr.Items.ShouldContain(w => w.Kind == AbsenceWarningKinds.PlanningImpact);

        // Sofortige Benachrichtigung der Disposition – neutral (kein „krank“), mit Link zur Planung; die Kranke selbst nicht.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
            var officeId = await db.Set<User>().Where(u => u.UserName == "dispo").Select(u => u.Id).SingleAsync(Ct);
            var n = await db.Set<Notification>().AsNoTracking().SingleAsync(x => x.UserId == officeId && x.Title == "Abwesenheit betrifft Einsätze", Ct);
            n.Body.ShouldNotBeNull().ShouldNotContain("krank", Case.Insensitive);
            n.Link.ShouldNotBeNull().ShouldStartWith("/planung?ansicht=tag&datum=");
            (await db.Set<Notification>().AnyAsync(x => x.UserId == anna.Id && x.Title == "Abwesenheit betrifft Einsätze", Ct)).ShouldBeFalse();
        }

        // Baustelle (nur Planung lesen): keine Warnungen; Disposition ohne hr.manage darf AU-Warnungen nicht zur Kenntnis nehmen.
        using var carlClient = _factory.CreateClient();
        carlClient.WithToken((await carlClient.LoginOkAsync("carl")).AccessToken);
        (await carlClient.GetAsync(new Uri("/api/v1/hr/absence-warnings", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using (var denied = await office.PostAsJsonAsync("/api/v1/hr/absence-warnings/acknowledge", new { key = $"au:{sickId}" }, Ct))
        {
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using (var deniedSettings = await office.GetAsync(new Uri("/api/v1/hr/settings/warnings", UriKind.Relative), Ct))
        {
            deniedSettings.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        // eAU abgerufen → AU-Warnung entfällt.
        (await admin.PostAsJsonAsync($"/api/v1/hr/sick-notes/{sickId}/certificate-retrieved", new { retrievedOn = Now }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync<AbsenceWarningList>(admin, HttpMethod.Get, "/api/v1/hr/absence-warnings")).Items.ShouldNotContain(w => w.Kind == AbsenceWarningKinds.Certificate);
        var notes = await SendAsync<List<SickNoteView>>(admin, HttpMethod.Get, $"/api/v1/hr/sick-notes?year={Now.Year}");
        var saved = notes.Single(n => n.Id == sickId);
        saved.CertificateStatus.ShouldBe("retrieved");
        saved.CertificateRetrievedOn.ShouldBe(Now);

        // Bearbeiten behält die eAU-Markierung.
        await SendAsync<SickNoteView>(admin, HttpMethod.Put, $"/api/v1/hr/sick-notes/{sickId}", new { userId = anna.Id, from = Now.AddDays(-3), to = Now.AddDays(4) });
        (await SendAsync<List<SickNoteView>>(admin, HttpMethod.Get, $"/api/v1/hr/sick-notes?year={Now.Year}")).Single(n => n.Id == sickId)
            .CertificateRetrievedOn.ShouldBe(Now);

        // Disposition nimmt die Planungswarnung zur Kenntnis → für alle ausgeblendet.
        var current = (await SendAsync<AbsenceWarningList>(office, HttpMethod.Get, "/api/v1/hr/absence-warnings")).Items.ShouldHaveSingleItem();
        (await office.PostAsJsonAsync("/api/v1/hr/absence-warnings/acknowledge", new { key = current.Key }, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await SendAsync<AbsenceWarningList>(office, HttpMethod.Get, "/api/v1/hr/absence-warnings")).Items.ShouldBeEmpty();
        (await SendAsync<AbsenceWarningList>(admin, HttpMethod.Get, "/api/v1/hr/absence-warnings")).Acknowledged.ShouldBe(1);

        // Einstellungen: Grenzen prüfen, alles abschaltbar.
        using (var invalid = await admin.PutAsJsonAsync("/api/v1/hr/settings/warnings", new { certificateDueDay = 0 }, Ct))
        {
            invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        var defaults = await SendAsync<AbsenceWarningSettings>(admin, HttpMethod.Get, "/api/v1/hr/settings/warnings");
        defaults.CertificateDueDay.ShouldBe(4);
        defaults.FrequentShortThreshold.ShouldBe(0);
        defaults.Adjacent.ShouldBeFalse();
        var off = await SendAsync<AbsenceWarningSettings>(admin, HttpMethod.Put, "/api/v1/hr/settings/warnings", defaults with { Enabled = false });
        off.Enabled.ShouldBeFalse();
        var none = await SendAsync<AbsenceWarningList>(admin, HttpMethod.Get, "/api/v1/hr/absence-warnings");
        none.Enabled.ShouldBeFalse();
        none.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Scanner_NotifiesHrOncePerWarning_WithoutHealthDetails()
    {
        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        var hrColleague = await _factory.CreateUserAsync("personal", roleIds: StandardRoles.AdministratorId);
        var anna = await _factory.CreateUserAsync("anna", roleIds: StandardRoles.SiteWorkerId);
        await SendAsync<SickNoteView>(admin, HttpMethod.Put, $"/api/v1/hr/sick-notes/{Guid.CreateVersion7()}", new
        {
            userId = anna.Id, from = Now.AddDays(-5), to = Now.AddDays(2),
        });

        // Neue Krankmeldung ohne betroffene Einsätze: Kollegin in der Personalverwaltung erhält „Neue Abwesenheitsmeldung“.
        // Die Prüfung meldet nur zwischen 7 und 20 Uhr: notfalls auf den nächsten Morgen vorstellen.
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        var local = TimeZoneInfo.ConvertTime(_factory.Clock.GetUtcNow(), berlin);
        if (local.Hour is < 7 or >= 20)
        {
            var morning = (local.Hour >= 20 ? local.Date.AddDays(1) : local.Date).AddHours(9);
            _factory.Clock.SetUtcNow(new DateTimeOffset(morning, berlin.GetUtcOffset(morning)));
        }

        var scanner = _factory.Services.GetRequiredService<AbsenceWarningScanner>();
        (await scanner.RunOnceAsync(Ct)).ShouldBeGreaterThan(0);
        await scanner.RunOnceAsync(Ct);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var mine = await db.Set<Notification>().AsNoTracking().Where(n => n.UserId == hrColleague.Id).ToListAsync(Ct);
        mine.ShouldContain(n => n.Title == "Neue Abwesenheitsmeldung");
        var hint = mine.Where(n => n.Title == "Personalhinweis").ShouldHaveSingleItem("je Warnung nur einmal");
        hint.Body.ShouldNotBeNull().ShouldNotContain("AU");
        hint.Body.ShouldNotContain("krank", Case.Insensitive);
        (await db.Set<Notification>().AnyAsync(n => n.UserId == anna.Id, Ct)).ShouldBeFalse();
    }
}
