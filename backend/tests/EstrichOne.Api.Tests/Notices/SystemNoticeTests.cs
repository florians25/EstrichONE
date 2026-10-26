using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Notices;
using EstrichOne.Api.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace EstrichOne.Api.Tests.Notices;

public sealed class NoticeStoreTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "estrichone-notices", Guid.NewGuid().ToString("N"), "notices.json");
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(Path.GetDirectoryName(_file)))
        {
            Directory.Delete(Path.GetDirectoryName(_file)!, recursive: true);
        }
    }

    private NoticeStore Store() =>
        new(Options.Create(new NoticeSettings { StoragePath = _file }), _clock, NullLogger<NoticeStore>.Instance);

    [Fact]
    public void ForClients_FiltertNachZeitraum_UndKuendigtWartungAn()
    {
        var now = _clock.GetUtcNow();
        var store = Store();
        store.Replace(new SystemNoticesDocument(
        [
            new SystemAnnouncement("a", "Jetzt", "läuft", "info", now.AddHours(-1), now.AddHours(1)),
            new SystemAnnouncement("b", "Später", "kommt", "warning", now.AddDays(1), null),
            new SystemAnnouncement("c", "Vorbei", "war", "critical", null, now.AddMinutes(-1)),
        ], new MaintenanceWindow("Update", now.AddHours(2), now.AddHours(3))));

        var response = store.ForClients();
        response.Announcements.Select(a => a.Id).ShouldBe(["a"]);
        response.Maintenance!.Active.ShouldBeFalse();
        store.ActiveMaintenance().ShouldBeNull();

        _clock.Advance(TimeSpan.FromHours(2.5));
        store.ForClients().Maintenance!.Active.ShouldBeTrue();
        store.ActiveMaintenance().ShouldNotBeNull();

        _clock.Advance(TimeSpan.FromHours(1));
        store.ForClients().Maintenance.ShouldBeNull();
    }

    [Fact]
    public void Replace_BleibtNachNeustartErhalten()
    {
        Store().Replace(new SystemNoticesDocument([new SystemAnnouncement("x", "T", "", "info", null, null)], null));
        Store().Current.Announcements.Single().Id.ShouldBe("x");
    }

    [Fact]
    public void Validate_WeistUngueltigeHinweiseAb()
    {
        var now = DateTimeOffset.UtcNow;
        var errors = NoticesModule.Validate(new SystemNoticesDocument(
            [new SystemAnnouncement("", new string('x', 200), "t", "rot", now, now.AddHours(-1))],
            new MaintenanceWindow(null, now, now.AddMinutes(-5))));
        errors.Keys.ShouldBe(["announcements[0]", "maintenance"], ignoreOrder: true);
        errors["announcements[0]"].Length.ShouldBe(4);
    }
}

public sealed class SystemNoticeEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Token = "konsolen-schluessel-0123456789";
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres,
        new Dictionary<string, string> { ["EstrichOne:Notices:ManagementToken"] = Token });

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<HttpStatusCode> PutAsync(object document, string? token)
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/system/notices") { Content = JsonContent.Create(document) };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await client.SendAsync(request, Ct);
        return response.StatusCode;
    }

    [Fact]
    public async Task Setzen_NurMitInstanzSchluessel_LesenOhneAnmeldung()
    {
        var document = new
        {
            announcements = new[] { new { id = "wartung-ankuendigung", title = "Update am Freitag", text = "Kurze Unterbrechung.", severity = "warning" } },
            maintenance = (object?)null,
        };
        (await PutAsync(document, null)).ShouldBe(HttpStatusCode.Unauthorized);
        (await PutAsync(document, "falsch")).ShouldBe(HttpStatusCode.Unauthorized);
        (await PutAsync(new { announcements = new[] { new { id = "x", title = "", text = "", severity = "bunt" } } }, Token)).ShouldBe(HttpStatusCode.BadRequest);
        (await PutAsync(document, Token)).ShouldBe(HttpStatusCode.NoContent);

        using var anonymous = _factory.CreateClient();
        var notices = await anonymous.GetFromJsonAsync<SystemNoticesResponse>("/api/v1/system/notices", TestJson.Options, Ct);
        notices!.Announcements.Single().Title.ShouldBe("Update am Freitag");
        notices.Maintenance.ShouldBeNull();
    }

    [Fact]
    public async Task Wartung_SperrtBenutzer_AdministratorenArbeitenWeiter()
    {
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        (await worker.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var now = _factory.Clock.GetUtcNow();
        (await PutAsync(new { announcements = Array.Empty<object>(), maintenance = new { message = "Datenbank-Update", startsAt = now.AddMinutes(-1), endsAt = now.AddMinutes(4), adminsMayWork = true } }, Token))
            .ShouldBe(HttpStatusCode.NoContent);

        using var blocked = await worker.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct);
        blocked.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await blocked.ReadProblemAsync()).Code.ShouldBe("MAINTENANCE");
        (await admin.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var anonymous = _factory.CreateClient();
        (await anonymous.GetAsync(new Uri("/api/v1/server-info", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var notices = await anonymous.GetFromJsonAsync<SystemNoticesResponse>("/api/v1/system/notices", TestJson.Options, Ct);
        notices!.Maintenance!.Active.ShouldBeTrue();

        _factory.Clock.Advance(TimeSpan.FromMinutes(5));
        (await worker.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InstanzAdmins_SetzenEigeneHinweise_KonsoleBleibtGetrennt_ZielgruppeNachRolle()
    {
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        (await PutAsync(new { announcements = new[] { new { id = "konsole", title = "Betreiber", text = "Update", severity = "info" } } }, Token))
            .ShouldBe(HttpStatusCode.NoContent);

        // Ohne settings.manage: verboten.
        using var denied = await worker.PutAsJsonAsync("/api/v1/system/notices/admin/announcements/a1", new { title = "X", text = "Y", severity = "info" }, Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var all = await admin.PutAsJsonAsync("/api/v1/system/notices/admin/announcements/a1", new { title = "Betriebsfeier", text = "Freitag 14 Uhr", severity = "info" }, Ct);
        all.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var office = await admin.PutAsJsonAsync("/api/v1/system/notices/admin/announcements/a2",
            new { title = "Nur Büro", text = "Monatsabschluss", severity = "warning", roleIds = new[] { StandardRoles.OfficeId } }, Ct);
        office.StatusCode.ShouldBe(HttpStatusCode.OK);

        var seen = await worker.GetFromJsonAsync<SystemNoticesResponse>("/api/v1/system/notices", TestJson.Options, Ct);
        seen!.Announcements.Select(a => (a.Title, a.Source)).ShouldBe([("Betreiber", "console"), ("Betriebsfeier", "instance")], ignoreOrder: true);

        var overview = await admin.GetFromJsonAsync<AdminNoticesResponse>("/api/v1/system/notices/admin", TestJson.Options, Ct);
        overview!.Instance.Announcements.Count.ShouldBe(2);
        overview.Console.Announcements.ShouldHaveSingleItem().Id.ShouldBe("konsole");

        // Wartung der Instanz sperrt Benutzer, Administratoren arbeiten weiter.
        var now = _factory.Clock.GetUtcNow();
        (await admin.PutAsJsonAsync("/api/v1/system/notices/admin/maintenance", new { message = "Umzug", startsAt = now.AddMinutes(-1), endsAt = now.AddHours(1), adminsMayWork = true }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await worker.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await admin.DeleteAsync(new Uri("/api/v1/system/notices/admin/maintenance", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await worker.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OhneSchluessel_IstSetzenAbgeschaltet()
    {
        await using var factory = await ApiFactory.CreateMigratedAsync(postgres);
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/system/notices")
        {
            Content = JsonContent.Create(new { announcements = Array.Empty<object>() }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "irgendwas");
        (await client.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
