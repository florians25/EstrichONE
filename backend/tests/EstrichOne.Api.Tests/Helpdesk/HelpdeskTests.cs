using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Devices;
using EstrichOne.Api.Modules.Helpdesk;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Helpdesk;

/// <summary>Helpdesk (docs/reengineering/14) einschließlich AK-1 bis AK-3.</summary>
public sealed class HelpdeskTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static object NewTicket(Guid id, string title = "Fotos werden nicht übertragen", object? diagnostics = null) => new
    {
        id, title, description = "seit heute morgen", category = "sync", source = "ios", reporterBlocked = true, diagnostics,
    };

    private static async Task<TicketDetail> CreateAsync(HttpClient client, object body)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/tickets", body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TicketDetail>(TestJson.Options, Ct))!;
    }

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, object body) =>
        client.PatchAsJsonAsync($"/api/v1/tickets/{id}", body, Ct);

    [Fact]
    public async Task Create_IsIdempotent_NumberedAndListedForReporter()
    {
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var id = Guid.CreateVersion7();

        using var first = await reporter.PostAsJsonAsync("/api/v1/tickets", NewTicket(id), Ct);
        using var repeat = await reporter.PostAsJsonAsync("/api/v1/tickets", NewTicket(id), Ct);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        repeat.StatusCode.ShouldBe(HttpStatusCode.OK);
        var ticket = (await first.Content.ReadFromJsonAsync<TicketDetail>(TestJson.Options, Ct))!;
        ticket.Summary.Number.ShouldMatch(@"^T-\d{4}-0001$");
        ticket.Summary.Status.ShouldBe(TicketStatus.New);
        ticket.Summary.Priority.ShouldBe(TicketPriority.Normal, "Die Priorität setzt die Bearbeitung, nicht der Melder.");
        ticket.Summary.ReporterBlocked.ShouldBeTrue();
        var mine = await reporter.GetFromJsonAsync<List<TicketSummary>>("/api/v1/tickets/mine", TestJson.Options, Ct);
        mine.ShouldHaveSingleItem().Id.ShouldBe(id);
    }

    [Fact]
    public async Task Create_FromWebForm_TakesPriority_ForNormalUsers()
    {
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var ticket = await CreateAsync(reporter, new
        {
            id = Guid.CreateVersion7(), title = "Drucker im Büro druckt nicht", description = "Papierstau-Meldung",
            category = "officeIt", source = "web", reporterBlocked = false, priority = "high",
        });

        ticket.Summary.Priority.ShouldBe(TicketPriority.High, "Im Webformular wählt der Melder die Priorität (Runde 3b, F2).");
        var mine = await reporter.GetFromJsonAsync<List<TicketSummary>>("/api/v1/tickets/mine", TestJson.Options, Ct);
        mine.ShouldHaveSingleItem().Priority.ShouldBe(TicketPriority.High);
    }

    [Fact]
    public async Task InternalNotes_AreNeverVisibleToTheReporter_AK2()
    {
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var agent = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        using var stranger = await _factory.CreateClientAsAsync("kollege", StandardRoles.SiteWorkerId);
        var ticket = await CreateAsync(reporter, NewTicket(Guid.CreateVersion7()));

        (await agent.PostAsJsonAsync($"/api/v1/tickets/{ticket.Summary.Id}/comments", new { text = "Vermutlich Token abgelaufen", isInternal = true }, Ct))
            .EnsureSuccessStatusCode();
        (await agent.PostAsJsonAsync($"/api/v1/tickets/{ticket.Summary.Id}/comments", new { text = "Bitte App einmal neu starten.", isInternal = false }, Ct))
            .EnsureSuccessStatusCode();

        var reporterView = await reporter.GetFromJsonAsync<TicketDetail>($"/api/v1/tickets/{ticket.Summary.Id}", TestJson.Options, Ct);
        var agentView = await agent.GetFromJsonAsync<TicketDetail>($"/api/v1/tickets/{ticket.Summary.Id}", TestJson.Options, Ct);
        using var reporterInternal = await reporter.PostAsJsonAsync($"/api/v1/tickets/{ticket.Summary.Id}/comments", new { text = "x", isInternal = true }, Ct);
        using var strangerView = await stranger.GetAsync(new Uri($"/api/v1/tickets/{ticket.Summary.Id}", UriKind.Relative), Ct);
        using var strangerList = await stranger.GetAsync(new Uri("/api/v1/tickets", UriKind.Relative), Ct);

        reporterView!.Comments.Select(c => c.Text).ShouldBe(["Bitte App einmal neu starten."]);
        agentView!.Comments.Count.ShouldBe(2);
        reporterInternal.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        strangerView.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        strangerList.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StatusFlow_ResolutionRequired_ReopenAndAutoClose()
    {
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var agent = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        var id = (await CreateAsync(reporter, NewTicket(Guid.CreateVersion7()))).Summary.Id;
        var listed = (await agent.GetFromJsonAsync<List<TicketSummary>>("/api/v1/tickets", TestJson.Options, Ct))!.Single().Id;
        listed.ShouldBe(id);
        (await agent.GetFromJsonAsync<List<TicketSummary>>("/api/v1/tickets?filter=unassigned&category=sync", TestJson.Options, Ct))!
            .ShouldHaveSingleItem();
        (await agent.GetFromJsonAsync<List<TicketSummary>>("/api/v1/tickets?filter=all&category=officeIt", TestJson.Options, Ct))!
            .ShouldBeEmpty();

        using var withoutResolution = await PatchAsync(agent, id, new { status = "resolved" });
        withoutResolution.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await PatchAsync(agent, id, new { status = "waitingForReporter", priority = "high" })).EnsureSuccessStatusCode();
        (await reporter.PostAsJsonAsync($"/api/v1/tickets/{id}/comments", new { text = "Fahrzeug 3, iPhone 15", isInternal = false }, Ct))
            .EnsureSuccessStatusCode();
        var afterAnswer = await agent.GetFromJsonAsync<TicketDetail>($"/api/v1/tickets/{id}", TestJson.Options, Ct);
        afterAnswer!.Summary.Status.ShouldBe(TicketStatus.InProgress, "Die Antwort auf eine Rückfrage holt das Ticket zurück.");

        (await PatchAsync(agent, id, new { status = "resolved", resolution = "App neu angemeldet, Upload läuft." })).EnsureSuccessStatusCode();
        (await reporter.PostAsync(new Uri($"/api/v1/tickets/{id}/reopen", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();
        (await PatchAsync(agent, id, new { status = "resolved" })).EnsureSuccessStatusCode();

        _factory.Clock.Advance(Ticket.AutoCloseAfter + TimeSpan.FromHours(1));
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<TicketService>().CloseResolvedAsync(Ct);
        }

        // Eine Woche später ist das Zugriffstoken längst abgelaufen: neu anmelden.
        using var later = _factory.CreateClient();
        later.WithToken((await later.LoginOkAsync("baustelle")).AccessToken);
        var closed = await later.GetFromJsonAsync<TicketDetail>($"/api/v1/tickets/{id}", TestJson.Options, Ct);
        closed!.Summary.Status.ShouldBe(TicketStatus.Closed);
        closed.Resolution.ShouldBe("App neu angemeldet, Upload läuft.");
        closed.Events.Select(e => e.Kind).ShouldContain("priority");
        closed.Events.Count(e => e.Kind == "status").ShouldBeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public async Task Diagnostics_OnlyAllowedFields_AK3()
    {
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var valid = new Dictionary<string, object>
        {
            ["appVersion"] = "1.0.4", ["build"] = "112", ["platform"] = "iOS", ["osVersion"] = "26.1", ["deviceModel"] = "iPhone16,2",
            ["syncPendingOperations"] = 14, ["errorCodes"] = new[] { new { code = "SYNC_UPLOAD_FAILED", at = "2026-10-19T07:12:03Z" } },
            ["log"] = new[] { "2026-10-19T07:12:03Z SYNC_PUSH ops=3", "2026-10-19T07:12:04Z UPLOAD_FAILED id=7d1e status=503" },
        };
        var ticket = await CreateAsync(reporter, NewTicket(Guid.CreateVersion7(), diagnostics: valid));
        ticket.Diagnostics!["errorCodes"]![0]!["code"]!.GetValue<string>().ShouldBe("SYNC_UPLOAD_FAILED");

        foreach (var invalid in new object[]
        {
            new Dictionary<string, object> { ["accessToken"] = "eo1_secret" },
            new Dictionary<string, object> { ["customerName"] = "Familie Müller" },
            new Dictionary<string, object> { ["log"] = new[] { "Notiz: \"Schlüssel beim Nachbarn\"" } },
            new Dictionary<string, object> { ["errorCodes"] = new[] { new { code = "kein code", at = "x" } } },
        })
        {
            using var response = await reporter.PostAsJsonAsync("/api/v1/tickets", NewTicket(Guid.CreateVersion7(), diagnostics: invalid), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await response.ReadProblemAsync()).Code.ShouldBe("VALIDATION_FAILED");
        }
    }

    [Fact]
    public async Task DeviceTicket_WithoutSignIn_OnlyFromRegisteredUnlockedDevicesWithValidSignature()
    {
        await _factory.CreateUserAsync("baustelle", roleIds: StandardRoles.SiteWorkerId);
        using var phone = _factory.CreateClient();
        phone.WithToken((await phone.LoginOkAsync("baustelle", clientKind: "ios")).AccessToken);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var register = await phone.PostAsJsonAsync("/api/v1/devices/register", new
        {
            name = "Fahrzeug 3", platform = "ios", kind = "personal", publicKey = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
        }, Ct);
        register.EnsureSuccessStatusCode();
        var device = (await register.Content.ReadFromJsonAsync<DeviceResponse>(TestJson.Options, Ct))!;
        using var anonymous = _factory.CreateClient();

        Task<HttpResponseMessage> SendAsync(ECDsa signer, string timestamp, Guid ticketId)
        {
            var message = TicketEndpoints.DeviceProofMessage(device.Id, timestamp, ticketId, "Anmeldung klappt nicht", "Passwort wird abgelehnt");
            return anonymous.PostAsJsonAsync("/api/v1/tickets/device", new
            {
                deviceId = device.Id, timestamp, reporterName = "Max",
                signature = Convert.ToBase64String(signer.SignData(Encoding.UTF8.GetBytes(message), HashAlgorithmName.SHA256)),
                ticket = new { id = ticketId, title = "Anmeldung klappt nicht", description = "Passwort wird abgelehnt", category = "signIn", source = "ios" },
            }, Ct);
        }

        var now = _factory.Clock.GetUtcNow().ToString("O");
        using var valid = await SendAsync(key, now, Guid.CreateVersion7());
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var wrongKey = await SendAsync(otherKey, now, Guid.CreateVersion7());
        using var stale = await SendAsync(key, _factory.Clock.GetUtcNow().AddMinutes(-30).ToString("O"), Guid.CreateVersion7());

        valid.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await valid.Content.ReadFromJsonAsync<DeviceTicketResponse>(TestJson.Options, Ct))!.Number.ShouldStartWith("T-");
        wrongKey.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        stale.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var it = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        (await it.PostAsync(new Uri($"/api/v1/devices/{device.Id}/lock", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();
        using var locked = await SendAsync(key, now, Guid.CreateVersion7());
        locked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var list = await it.GetFromJsonAsync<List<TicketSummary>>("/api/v1/tickets", TestJson.Options, Ct);
        list.ShouldHaveSingleItem().ReporterDisplayName.ShouldBe("Max (laut Eingabe)");
    }

    [Fact]
    public async Task OfflineTicketWithPhoto_ArrivesExactlyOnce_AndAgentSeesTheAttachment_AK1()
    {
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var agent = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        using var colleague = await _factory.CreateClientAsAsync("kollege", StandardRoles.SiteWorkerId);
        var file = Guid.CreateVersion7();
        var ticketId = Guid.CreateVersion7();
        var announce = SyncClient.Op("file.announce", file, new
        {
            kind = "photo", originalName = "bildschirm.png", contentType = "image/png", sizeBytes = 68, sha256 = new string('b', 64),
        });
        var create = SyncClient.Op("ticket.create", ticketId, new
        {
            title = "Upload hängt", category = "sync", source = "ios", attachmentFileIds = new[] { file },
        });

        var first = await reporter.PushAsync(announce, create);
        var again = await reporter.PushAsync(create);

        first.Results.ShouldAllBe(r => r.Status == SyncOperationStatus.Applied);
        again.Results.ShouldHaveSingleItem().Status.ShouldBe(SyncOperationStatus.Duplicate);
        var tickets = await agent.GetFromJsonAsync<List<TicketSummary>>("/api/v1/tickets", TestJson.Options, Ct);
        tickets!.Count(t => t.Id == ticketId).ShouldBe(1);
        var detail = await agent.GetFromJsonAsync<TicketDetail>($"/api/v1/tickets/{ticketId}", TestJson.Options, Ct);
        detail!.AttachmentFileIds.ShouldBe([file]);
        (await agent.GetAsync(new Uri($"/api/v1/files/{file}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await colleague.GetAsync(new Uri($"/api/v1/files/{file}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OfflineTicket_TakesPriorityFromTheApp()
    {
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var agent = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        var urgent = Guid.CreateVersion7();
        var plain = Guid.CreateVersion7();

        var result = await reporter.PushAsync(
            SyncClient.Op("ticket.create", urgent, new { title = "Tablet defekt", category = "deviceDefect", source = "ios", priority = "high" }),
            SyncClient.Op("ticket.create", plain, new { title = "Frage zur App", category = "other", source = "ios" }));

        result.Results.ShouldAllBe(r => r.Status == SyncOperationStatus.Applied);
        var tickets = await agent.GetFromJsonAsync<List<TicketSummary>>("/api/v1/tickets?filter=all", TestJson.Options, Ct);
        tickets!.Single(t => t.Id == urgent).Priority.ShouldBe(TicketPriority.High);
        tickets!.Single(t => t.Id == plain).Priority.ShouldBe(TicketPriority.Normal);
    }
}
