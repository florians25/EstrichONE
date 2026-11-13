using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Audit;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Teams;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Teams;

/// <summary>
/// Teams-Lebenszyklus gegen den nachgebildeten Graph-Server: Kanal archivieren, wiederherstellen und löschen (Recht
/// <c>teams.delete</c>, Bestätigung mit Bauaktennummer), Gruppenchat löschen, fehlende Berechtigung lesbar, Abschluss-Aktion,
/// Einzel- und Gruppenchats per Deep Link bzw. Graph, Massenverknüpfung im Hintergrund und Umstellungshinweis.
/// </summary>
public sealed class TeamsLifecycleTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "11111111-2222-4333-8444-555555555555";
    private const string Client = "66666666-7777-4888-8999-000000000000";
    private FakeGraphServer _graph = null!;
    private ApiFactory _factory = null!;
    private HttpClient _admin = null!;
    private Guid _adminId;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _graph = await FakeGraphServer.StartAsync();
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
        {
            ["EstrichOne:Teams:GraphBaseUrl"] = _graph.Url, ["EstrichOne:Teams:LoginBaseUrl"] = _graph.Url, ["EstrichOne:Teams:AllowHttpWebhooks"] = "true",
            ["EstrichOne:Teams:MaxRetryDelay"] = "00:00:02", ["EstrichOne:Teams:MaxAttempts"] = "3", ["EstrichOne:PublicUrl"] = "https://estrichone.test",
        });
        _adminId = (await _factory.CreateUserAsync("admin", roleIds: StandardRoles.AdministratorId)).Id;
        var client = _factory.CreateClient();
        _admin = client.WithToken((await client.LoginOkAsync("admin")).AccessToken);
        _graph.Users["admin@example.org"] = ("aad-admin", "Admin");
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _factory.DisposeAsync();
        await _graph.DisposeAsync();
    }

    private async Task ConfigureAsync(string chatMode = "teams", string defaultLinkKind = "channel", string closeAction = "archive", bool chatsViaGraph = false,
        bool rolloutNotice = false, string postVia = "off", string? webhookUrl = null)
    {
        using var response = await _admin.PutAsJsonAsync("/api/v1/settings/integrations/teams", new
        {
            chatMode, tenantId = Tenant, clientId = Client, authKind = "secret", clientSecret = FakeGraphServer.Secret, siteTeam = FakeGraphServer.TeamId,
            defaultLinkKind, syncMembers = true, postVia, webhookUrl, closeAction, chatsViaGraph, rolloutNotice,
        }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private async Task<(HttpClient Client, Guid Id)> UserAsync(string userName, Guid role, string? aadId = null)
    {
        var user = await _factory.CreateUserAsync(userName, roleIds: role);
        if (aadId is not null)
        {
            _graph.Users[$"{userName}@example.org"] = (aadId, userName);
        }

        var client = _factory.CreateClient();
        return (client.WithToken((await client.LoginOkAsync(userName)).AccessToken), user.Id);
    }

    private async Task<(Guid Id, string Number)> BauakteAsync(string title, params Guid[] members)
    {
        var id = Guid.CreateVersion7();
        using var created = await _admin.PostAsJsonAsync("/api/v1/bauakten", new { id, title }, Ct);
        created.EnsureSuccessStatusCode();
        var number = (await created.Content.ReadFromJsonAsync<JsonObject>(TestJson.Options, Ct))!["number"]!.GetValue<string>();
        if (members.Length > 0)
        {
            using var set = await _admin.PutAsJsonAsync($"/api/v1/bauakten/{id}/members", new { userIds = members }, Ct);
            set.EnsureSuccessStatusCode();
        }

        return (id, number);
    }

    private async Task MatchAsync()
    {
        using var match = await _admin.PostAsync(new Uri("/api/v1/settings/integrations/teams/users/match", UriKind.Relative), null, Ct);
        match.StatusCode.ShouldBe(HttpStatusCode.OK, await match.Content.ReadAsStringAsync(Ct));
    }

    private async Task<TeamsLinkResponse> LinkAsync(Guid bauakteId, string kind)
    {
        using var response = await _admin.PostAsJsonAsync($"/api/v1/bauakten/{bauakteId}/teams", new { kind }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<TeamsLinkResponse>(TestJson.Options, Ct))!;
    }

    private async Task<List<string>> AuditActionsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<AuditEvent>().Select(e => e.Action).ToListAsync(Ct);
    }

    private async Task SetStatusAsync(Guid bauakteId, BauakteStatus status)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<Bauakte>().Where(b => b.Id == bauakteId)
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.Status, status), Ct);
    }

    private async Task SweepClosedAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<TeamsService>();
        await service.HandleClosedAsync(await service.SettingsAsync(Ct), Ct);
    }

    private async Task<TeamsBauakteLink?> StoredLinkAsync(Guid bauakteId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<TeamsBauakteLink>().AsNoTracking()
            .SingleOrDefaultAsync(l => l.BauakteId == bauakteId, Ct);
    }

    [Fact]
    public async Task Channel_Archive_Unarchive_Delete_OnlyWithPermission_AndConfirmation()
    {
        await ConfigureAsync();
        var (worker, workerId) = await UserAsync("monteur", StandardRoles.SiteWorkerId, "aad-monteur");
        await MatchAsync();
        var (bauakte, number) = await BauakteAsync("EFH Kanal", workerId);
        var link = await LinkAsync(bauakte, "channel");
        _graph.Channels[link.ChannelId!].ShouldBeFalse();

        // Ohne teams.delete: kein Archivieren oder Löschen.
        using (var denied = await worker.PostAsync(new Uri($"/api/v1/bauakten/{bauakte}/teams/archive", UriKind.Relative), null, Ct))
        {
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        var teams = (await worker.GetFromJsonAsync<BauakteTeamsResponse>($"/api/v1/bauakten/{bauakte}/teams", TestJson.Options, Ct))!;
        teams.CanDelete.ShouldBeFalse();
        teams.Confirmation.ShouldBe(number);
        var adminView = (await _admin.GetFromJsonAsync<BauakteTeamsResponse>($"/api/v1/bauakten/{bauakte}/teams", TestJson.Options, Ct))!;
        adminView.CanDelete.ShouldBeTrue();
        adminView.CloseAction.ShouldBe(TeamsCloseAction.Archive);

        using (var archived = await _admin.PostAsync(new Uri($"/api/v1/bauakten/{bauakte}/teams/archive", UriKind.Relative), null, Ct))
        {
            archived.StatusCode.ShouldBe(HttpStatusCode.OK, await archived.Content.ReadAsStringAsync(Ct));
            (await archived.Content.ReadFromJsonAsync<TeamsLinkResponse>(TestJson.Options, Ct))!.ArchivedAt.ShouldNotBeNull();
        }

        _graph.Channels[link.ChannelId!].ShouldBeTrue();
        using (var restored = await _admin.PostAsync(new Uri($"/api/v1/bauakten/{bauakte}/teams/unarchive", UriKind.Relative), null, Ct))
        {
            restored.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await restored.Content.ReadFromJsonAsync<TeamsLinkResponse>(TestJson.Options, Ct))!.ArchivedAt.ShouldBeNull();
        }

        _graph.Channels[link.ChannelId!].ShouldBeFalse();

        // Falsche Bestätigung: nichts gelöscht.
        using (var wrong = await _admin.PostAsJsonAsync($"/api/v1/bauakten/{bauakte}/teams/delete", new { confirmation = "falsch" }, Ct))
        {
            wrong.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            var text = await wrong.Content.ReadAsStringAsync(Ct);
            text.ShouldContain("TEAMS_CONFIRMATION_MISMATCH");
            text.ShouldContain(number);
        }

        _graph.Deleted.ShouldBeEmpty();
        using (var deleted = await _admin.PostAsJsonAsync($"/api/v1/bauakten/{bauakte}/teams/delete", new { confirmation = number.ToLowerInvariant() }, Ct))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync(Ct));
        }

        _graph.Deleted.ShouldContain(link.ChannelId!);
        _graph.CallsTo("DELETE", $"/v1.0/teams/{FakeGraphServer.TeamId}/channels/").Count.ShouldBe(1);
        (await StoredLinkAsync(bauakte)).ShouldBeNull();
        var actions = await AuditActionsAsync();
        actions.ShouldContain("teams.bauakte_archived_in_teams");
        actions.ShouldContain("teams.bauakte_unarchived_in_teams");
        actions.ShouldContain("teams.bauakte_deleted_in_teams");
    }

    [Fact]
    public async Task GroupChat_Delete_ArchiveNotPossible_MissingPermissionReadable()
    {
        await ConfigureAsync(defaultLinkKind: "groupChat");
        var (_, a) = await UserAsync("anna", StandardRoles.SiteWorkerId, "aad-anna");
        var (_, b) = await UserAsync("bernd", StandardRoles.SiteWorkerId, "aad-bernd");
        var (_, c) = await UserAsync("carla", StandardRoles.SiteWorkerId, "aad-carla");
        await MatchAsync();
        var (bauakte, number) = await BauakteAsync("MFH Chat", a, b, c);
        var link = await LinkAsync(bauakte, "groupChat");

        using (var archive = await _admin.PostAsync(new Uri($"/api/v1/bauakten/{bauakte}/teams/archive", UriKind.Relative), null, Ct))
        {
            archive.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            var text = await archive.Content.ReadAsStringAsync(Ct);
            text.ShouldContain("TEAMS_NOT_SUPPORTED");
            text.ShouldContain("Gruppenchats lassen sich in Microsoft Teams nicht archivieren");
        }

        // Fehlende Berechtigung: lesbar, mit Namen der Berechtigung, nie 502/504.
        _graph.Forbidden.Add("/v1.0/chats/19");
        using (var forbidden = await _admin.PostAsJsonAsync($"/api/v1/bauakten/{bauakte}/teams/delete", new { confirmation = number }, Ct))
        {
            forbidden.StatusCode.ShouldBe(HttpStatusCode.FailedDependency);
            var text = await forbidden.Content.ReadAsStringAsync(Ct);
            text.ShouldContain("TEAMS_FORBIDDEN");
            text.ShouldContain("Chat.ManageDeletion.All");
        }

        (await StoredLinkAsync(bauakte)).ShouldNotBeNull();
        _graph.Forbidden.Clear();
        using (var deleted = await _admin.PostAsJsonAsync($"/api/v1/bauakten/{bauakte}/teams/delete", new { confirmation = number }, Ct))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync(Ct));
        }

        _graph.Deleted.ShouldContain(link.ChatId!);
        (await StoredLinkAsync(bauakte)).ShouldBeNull();
    }

    [Fact]
    public async Task ConnectionTest_ShowsMissingOptionalPermissions()
    {
        _graph.AppRoles.TryRemove("Channel.Delete.All", out _);
        _graph.AppRoles.TryRemove("ChannelSettings.ReadWrite.All", out _);
        await ConfigureAsync();
        using var test = await _admin.PostAsync(new Uri("/api/v1/settings/integrations/teams/test", UriKind.Relative), null, Ct);
        test.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = (await test.Content.ReadFromJsonAsync<TeamsTestResponse>(TestJson.Options, Ct))!;
        result.Ok.ShouldBeTrue(); // optionale Berechtigungen fehlen: Hinweis, kein Fehler
        var step = result.Steps.Single(s => s.Name.StartsWith("Berechtigungen", StringComparison.Ordinal));
        step.Warning.ShouldBeTrue();
        step.Detail.ShouldContain("Channel.Delete.All – Kanäle lassen sich nicht löschen");
        step.Detail.ShouldContain("ChannelSettings.ReadWrite.All");
        step.Detail.ShouldNotContain("Chat.ManageDeletion.All");
    }

    [Fact]
    public async Task ClosingBauakte_ArchivesChannel_ReopenRestores_DeleteSettingDeletes()
    {
        await ConfigureAsync(closeAction: "archive");
        var (_, workerId) = await UserAsync("kolonne", StandardRoles.SiteWorkerId, "aad-kolonne");
        await MatchAsync();
        var (first, _) = await BauakteAsync("Abschluss archivieren", workerId);
        var link = await LinkAsync(first, "channel");

        await SweepClosedAsync();
        _graph.Channels[link.ChannelId!].ShouldBeFalse(); // offen: nichts

        await SetStatusAsync(first, BauakteStatus.Completed);
        await SweepClosedAsync();
        _graph.Channels[link.ChannelId!].ShouldBeTrue();
        var stored = (await StoredLinkAsync(first))!;
        stored.ArchivedAt.ShouldNotBeNull();
        stored.CloseHandledAt.ShouldNotBeNull();
        stored.CloseError.ShouldBeNull();

        // Erneuter Lauf: kein zweiter Aufruf.
        var archiveCalls = _graph.CallsTo("POST", "/v1.0/teams/").Count(c => c.Path.EndsWith("/archive", StringComparison.Ordinal));
        await SweepClosedAsync();
        _graph.CallsTo("POST", "/v1.0/teams/").Count(c => c.Path.EndsWith("/archive", StringComparison.Ordinal)).ShouldBe(archiveCalls);

        // Wieder geöffnet: Archivierung aufgehoben.
        await SetStatusAsync(first, BauakteStatus.InProgress);
        await SweepClosedAsync();
        _graph.Channels[link.ChannelId!].ShouldBeFalse();
        (await StoredLinkAsync(first))!.ArchivedAt.ShouldBeNull();

        // Einstellung „löschen“: Kanal weg, Verknüpfung weg.
        await ConfigureAsync(closeAction: "delete");
        var (second, _) = await BauakteAsync("Abschluss löschen", workerId);
        var secondLink = await LinkAsync(second, "channel");
        await SetStatusAsync(second, BauakteStatus.Cancelled);
        await SweepClosedAsync();
        _graph.Deleted.ShouldContain(secondLink.ChannelId!);
        (await StoredLinkAsync(second)).ShouldBeNull();
        (await AuditActionsAsync()).ShouldContain("teams.bauakte_deleted_in_teams");
    }

    [Fact]
    public async Task ChatLinks_DeepLinkByUpn_UnmappedExplained_OneOnOneViaGraph()
    {
        await ConfigureAsync(rolloutNotice: true);
        var (worker, workerId) = await UserAsync("clara", StandardRoles.SiteWorkerId, "aad-clara");
        var (_, dieterId) = await UserAsync("dieter", StandardRoles.SiteWorkerId, "aad-dieter");
        var (_, egonId) = await UserAsync("egon", StandardRoles.SiteWorkerId); // ohne Microsoft-Konto
        await MatchAsync();

        var status = (await worker.GetFromJsonAsync<TeamsStatusResponse>("/api/v1/teams/status", TestJson.Options, Ct))!;
        status.CanStartChats.ShouldBeTrue();
        status.CanDelete.ShouldBeFalse();
        status.RolloutNotice!.ShouldContain("Ab sofort chatten wir in Microsoft Teams");

        var people = (await worker.GetFromJsonAsync<List<TeamsPerson>>("/api/v1/teams/people", TestJson.Options, Ct))!;
        people.Single(p => p.UserId == dieterId).Upn.ShouldBe("dieter@example.org");
        var egon = people.Single(p => p.UserId == egonId);
        egon.Upn.ShouldBeNull();
        egon.Reason!.ShouldContain("Kein Microsoft-Konto");
        people.ShouldNotContain(p => p.Upn != null && p.Upn.Contains("egon"));

        // Einzelchat per Deep Link.
        using (var single = await worker.PostAsJsonAsync("/api/v1/teams/chats", new { userIds = new[] { dieterId, workerId } }, Ct))
        {
            single.StatusCode.ShouldBe(HttpStatusCode.OK, await single.Content.ReadAsStringAsync(Ct));
            var chat = (await single.Content.ReadFromJsonAsync<TeamsChatLinkResponse>(TestJson.Options, Ct))!;
            chat.WebUrl.ShouldBe("https://teams.microsoft.com/l/chat/0/0?users=dieter@example.org");
            chat.AppUrl.ShouldBe("msteams:/l/chat/0/0?users=dieter@example.org");
            chat.ViaGraph.ShouldBeFalse();
        }

        // Gruppenchat mit Thema; Person ohne Konto wird mit Grund gemeldet.
        using (var group = await worker.PostAsJsonAsync("/api/v1/teams/chats",
            new { userIds = new[] { dieterId, egonId, _adminId }, topic = "Bauakte 4711 EFH & Co", message = "Hallo" }, Ct))
        {
            var chat = (await group.Content.ReadFromJsonAsync<TeamsChatLinkResponse>(TestJson.Options, Ct))!;
            chat.WebUrl.ShouldStartWith("https://teams.microsoft.com/l/chat/0/0?users=");
            chat.WebUrl.ShouldContain("admin@example.org");
            chat.WebUrl.ShouldContain("dieter@example.org");
            chat.WebUrl.ShouldEndWith("&topicName=Bauakte%204711%20EFH%20%26%20Co&message=Hallo");
            chat.Included.Count.ShouldBe(2);
            chat.Missing.Single().UserId.ShouldBe(egonId);
        }

        // Nur Personen ohne Konto: verständliche Meldung.
        using (var none = await worker.PostAsJsonAsync("/api/v1/teams/chats", new { userIds = new[] { egonId } }, Ct))
        {
            none.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await none.Content.ReadAsStringAsync(Ct)).ShouldContain("TEAMS_NO_PARTICIPANTS");
        }

        // Bauakte: „Chat mit dem ganzen Team“ ohne den Aufrufer.
        var (bauakte, _) = await BauakteAsync("Teamchat", workerId, dieterId, egonId);
        var teams = (await worker.GetFromJsonAsync<BauakteTeamsResponse>($"/api/v1/bauakten/{bauakte}/teams", TestJson.Options, Ct))!;
        teams.TeamChat!.WebUrl.ShouldStartWith("https://teams.microsoft.com/l/chat/0/0?users=");
        teams.TeamChat.WebUrl.ShouldContain("dieter@example.org");
        teams.TeamChat.WebUrl.ShouldContain("&topicName=Bauakte%20");
        teams.TeamChat.WebUrl.ShouldNotContain("clara");
        teams.TeamChat.Missing.Single().UserId.ShouldBe(egonId);

        // Einzelchat über Microsoft Graph (stabiler Link).
        await ConfigureAsync(chatsViaGraph: true);
        using (var graph = await worker.PostAsJsonAsync("/api/v1/teams/chats", new { userIds = new[] { dieterId } }, Ct))
        {
            var chat = (await graph.Content.ReadFromJsonAsync<TeamsChatLinkResponse>(TestJson.Options, Ct))!;
            chat.ViaGraph.ShouldBeTrue();
            chat.WebUrl.ShouldStartWith("https://teams.microsoft.com/l/chat/19%3Aaad-clara_aad-dieter");
        }

        _graph.CallsTo("POST", "/v1.0/chats").ShouldHaveSingleItem().Body.ShouldContain("oneOnOne");
    }

    [Fact]
    public async Task LinkAll_RunsInBackground_ReportsProgress_SkipsLinkedAndClosed()
    {
        await ConfigureAsync();
        var (_, workerId) = await UserAsync("fritz", StandardRoles.SiteWorkerId, "aad-fritz");
        await MatchAsync();
        var (open1, _) = await BauakteAsync("Offen eins", workerId);
        var (open2, _) = await BauakteAsync("Offen zwei");
        var (linked, _) = await BauakteAsync("Schon verknüpft");
        var (closed, _) = await BauakteAsync("Abgeschlossen");
        await LinkAsync(linked, "channel");
        await SetStatusAsync(closed, BauakteStatus.Completed);

        var rollout = (await _admin.GetFromJsonAsync<TeamsRolloutResponse>("/api/v1/settings/integrations/teams/rollout", TestJson.Options, Ct))!;
        rollout.UnlinkedOpen.ShouldBe(2);
        rollout.LinkedOpen.ShouldBe(1);
        rollout.CanLinkAll.ShouldBeTrue();
        rollout.Job.ShouldBeNull();

        _graph.Slow["/v1.0/teams/" + FakeGraphServer.TeamId + "/channels"] = TimeSpan.FromMilliseconds(300);
        var started = Stopwatch.StartNew();
        using (var start = await _admin.PostAsJsonAsync("/api/v1/settings/integrations/teams/rollout/link-all", new { }, Ct))
        {
            start.StatusCode.ShouldBe(HttpStatusCode.Accepted, await start.Content.ReadAsStringAsync(Ct));
        }

        started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(5)); // sofort zurück, Arbeit im Hintergrund

        TeamsLinkJobResponse job;
        var deadline = Stopwatch.StartNew();
        do
        {
            await Task.Delay(200, Ct);
            job = (await _admin.GetFromJsonAsync<TeamsLinkJobResponse>("/api/v1/settings/integrations/teams/rollout/job", TestJson.Options, Ct))!;
        }
        while (job.State == "running" && deadline.Elapsed < TimeSpan.FromSeconds(30));

        job.State.ShouldBe("completed", string.Join("; ", job.Errors));
        job.Total.ShouldBe(2);
        job.Linked.ShouldBe(2);
        job.Processed.ShouldBe(2);
        (await StoredLinkAsync(open1)).ShouldNotBeNull();
        (await StoredLinkAsync(open2)).ShouldNotBeNull();
        (await StoredLinkAsync(closed)).ShouldBeNull();
        rollout = (await _admin.GetFromJsonAsync<TeamsRolloutResponse>("/api/v1/settings/integrations/teams/rollout", TestJson.Options, Ct))!;
        rollout.UnlinkedOpen.ShouldBe(0);
        (await AuditActionsAsync()).ShouldContain("teams.link_all_started");
    }

    [Fact]
    public async Task PostMessage_ViaWebhook_AndWithoutSetupReadable()
    {
        await ConfigureAsync();
        var (bauakte, _) = await BauakteAsync("Nachricht");
        await LinkAsync(bauakte, "channel");
        using (var off = await _admin.PostAsJsonAsync($"/api/v1/bauakten/{bauakte}/teams/message", new { text = "Hallo Team" }, Ct))
        {
            off.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await off.Content.ReadAsStringAsync(Ct)).ShouldContain("TEAMS_NO_POST");
        }

        await ConfigureAsync(postVia: "webhook", webhookUrl: $"{_graph.Url}/webhook/kanal");
        (await _admin.GetFromJsonAsync<BauakteTeamsResponse>($"/api/v1/bauakten/{bauakte}/teams", TestJson.Options, Ct))!.CanPost.ShouldBeTrue();
        using (var sent = await _admin.PostAsJsonAsync($"/api/v1/bauakten/{bauakte}/teams/message", new { text = "Estrich kommt morgen um 7" }, Ct))
        {
            sent.StatusCode.ShouldBe(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync(Ct));
        }

        _graph.CallsTo("POST", "/webhook/kanal").ShouldHaveSingleItem().Body.ShouldContain("Estrich kommt morgen um 7");
    }
}
