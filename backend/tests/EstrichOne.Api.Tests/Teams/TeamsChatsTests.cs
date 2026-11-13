using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Teams;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Teams;

/// <summary>
/// „Teams-Chat anlegen“ aus der Bauakte (Teilnehmer: Bauakten-Team, Kolonne, Gruppe, Personen) und Teams-Chat je Kolonne gegen den
/// nachgebildeten Graph-Server: Mindestzahl drei Konten, kein doppeltes Anlegen nach Zeitüberschreitung, Mitglieder folgen der
/// Kolonne, Löschen mit Recht <c>teams.delete</c>, fehlende Berechtigung lesbar.
/// </summary>
public sealed class TeamsChatsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "11111111-2222-4333-8444-555555555555";
    private const string Client = "66666666-7777-4888-8999-000000000000";
    private FakeGraphServer _graph = null!;
    private ApiFactory _factory = null!;
    private HttpClient _admin = null!;
    private Guid _adminId;

    private static readonly string[] TeamOnly = ["team"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _graph = await FakeGraphServer.StartAsync();
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
        {
            ["EstrichOne:Teams:GraphBaseUrl"] = _graph.Url, ["EstrichOne:Teams:LoginBaseUrl"] = _graph.Url,
            ["EstrichOne:Teams:MaxRetryDelay"] = "00:00:01", ["EstrichOne:Teams:MaxAttempts"] = "3",
            ["EstrichOne:Teams:InteractiveAttemptTimeout"] = "00:00:02", ["EstrichOne:Teams:InteractiveDeadline"] = "00:00:08",
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

    private async Task ConfigureAsync(bool graph = true)
    {
        using var response = await _admin.PutAsJsonAsync("/api/v1/settings/integrations/teams", graph
            ? new { chatMode = "both", tenantId = (string?)Tenant, clientId = (string?)Client, authKind = "secret", clientSecret = (string?)FakeGraphServer.Secret }
            : new { chatMode = "both", tenantId = (string?)null, clientId = (string?)null, authKind = "secret", clientSecret = (string?)null }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        if (graph)
        {
            using var match = await _admin.PostAsync(new Uri("/api/v1/settings/integrations/teams/users/match", UriKind.Relative), null, Ct);
            match.StatusCode.ShouldBe(HttpStatusCode.OK, await match.Content.ReadAsStringAsync(Ct));
        }
    }

    private async Task<Guid> UserAsync(string userName, string? aadId)
    {
        var user = await _factory.CreateUserAsync(userName, roleIds: StandardRoles.SiteWorkerId);
        if (aadId is not null)
        {
            _graph.Users[$"{userName}@example.org"] = (aadId, userName);
        }

        return user.Id;
    }

    private async Task<Guid> BauakteAsync(string title, params Guid[] members)
    {
        var id = Guid.CreateVersion7();
        using var created = await _admin.PostAsJsonAsync("/api/v1/bauakten", new { id, title }, Ct);
        created.EnsureSuccessStatusCode();
        if (members.Length > 0)
        {
            using var set = await _admin.PutAsJsonAsync($"/api/v1/bauakten/{id}/members", new { userIds = members }, Ct);
            set.EnsureSuccessStatusCode();
        }

        return id;
    }

    private async Task<Guid> CrewAsync(string name, params Guid[] members)
    {
        var id = Guid.CreateVersion7();
        using var response = await _admin.PutAsJsonAsync($"/api/v1/planning/crews/{id}", new { name, memberUserIds = members, isActive = true }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return id;
    }

    [Fact]
    public async Task BauakteChat_WithCrewAndPerson_CreatorIncluded_MembersFollowCrew()
    {
        var anna = await UserAsync("anna", "aad-anna");
        var bernd = await UserAsync("bernd", "aad-bernd");
        var carla = await UserAsync("carla", "aad-carla");
        var ohne = await UserAsync("ohnekonto", null);
        await ConfigureAsync();
        var crew = await CrewAsync("Nord", bernd, ohne);
        var bauakte = await BauakteAsync("EFH Teilnehmer", anna);

        // Auswahl für den Dialog: Bauakten-Team, Kolonne, Gruppe (Rolle), Personen – mit Microsoft-Konto bzw. Grund.
        var participants = (await _admin.GetFromJsonAsync<TeamsParticipantsResponse>($"/api/v1/bauakten/{bauakte}/teams/participants", TestJson.Options, Ct))!;
        participants.ViaGraph.ShouldBeTrue();
        participants.MinMembers.ShouldBe(3);
        participants.SelfUserId.ShouldBe(_adminId);
        participants.Options[0].Key.ShouldBe("team");
        participants.Options[0].UserIds.ShouldContain(anna);
        var crewOption = participants.Options.Single(o => o.Key == $"crew:{crew}");
        crewOption.Name.ShouldBe("Kolonne Nord");
        crewOption.UserIds.OrderBy(u => u).ShouldBe(new[] { bernd, ohne }.OrderBy(u => u));
        participants.Options.ShouldContain(o => o.Kind == "role" && o.UserIds.Contains(anna));
        participants.People.Single(p => p.UserId == ohne).Upn.ShouldBeNull();
        participants.Topic.ShouldContain("EFH Teilnehmer");

        using var response = await _admin.PostAsJsonAsync($"/api/v1/bauakten/{bauakte}/teams",
            new { kind = "groupChat", topic = "B-1 · EFH Teilnehmer", participants = new[] { "team", $"crew:{crew}" } }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        var link = (await response.Content.ReadFromJsonAsync<TeamsLinkResponse>(TestJson.Options, Ct))!;
        link.DisplayName.ShouldBe("B-1 · EFH Teilnehmer");
        link.UnmappedMembers.ShouldBe(1);
        var chat = _graph.Members[link.ChatId!];
        chat.Keys.OrderBy(k => k).ShouldBe(["aad-admin", "aad-anna", "aad-bernd"]); // anlegende Person immer dabei
        JsonNode.Parse(_graph.CallsTo("POST", "/v1.0/chats").Single().Body)!["topic"]!.GetValue<string>().ShouldBe("B-1 · EFH Teilnehmer");

        // Kolonne ändert sich → Bauakten-Chat folgt (im Hintergrund).
        await CrewUpdateAsync(crew, "Nord", carla);
        await WaitForAsync(() => chat.ContainsKey("aad-carla") && !chat.ContainsKey("aad-bernd"));
        chat.ContainsKey("aad-admin").ShouldBeTrue();

        var seen = (await _admin.GetFromJsonAsync<BauakteTeamsResponse>($"/api/v1/bauakten/{bauakte}/teams", TestJson.Options, Ct))!;
        seen.Link!.ChatId.ShouldBe(link.ChatId);
    }

    private async Task CrewUpdateAsync(Guid id, string name, params Guid[] members)
    {
        using var response = await _admin.PutAsJsonAsync($"/api/v1/planning/crews/{id}", new { name, memberUserIds = members, isActive = true }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task SlowCreate_NotRetried_NoDuplicateChat_ClearHint()
    {
        var anna = await UserAsync("anna", "aad-anna");
        var bernd = await UserAsync("bernd", "aad-bernd");
        await ConfigureAsync();
        var bauakte = await BauakteAsync("Langsam", anna, bernd);
        _graph.Slow["/v1.0/chats"] = TimeSpan.FromSeconds(30);

        using var response = await _admin.PostAsJsonAsync($"/api/v1/bauakten/{bauakte}/teams", new { kind = "groupChat", participants = TeamOnly }, Ct);
        _graph.Slow.Clear();
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        var text = await response.Content.ReadAsStringAsync(Ct);
        text.ShouldContain("TEAMS_TIMEOUT");
        text.ShouldContain("Per Link verknüpfen");
        _graph.CallsTo("POST", "/v1.0/chats").Count.ShouldBe(1); // kein zweiter Versuch → kein doppelter Chat
    }

    [Fact]
    public async Task CrewChat_CreateSyncDelete_PermissionsAndErrorsReadable()
    {
        var anna = await UserAsync("anna", "aad-anna");
        var bernd = await UserAsync("bernd", "aad-bernd");
        var carla = await UserAsync("carla", "aad-carla");
        await ConfigureAsync();
        var small = await CrewAsync("Klein", anna);
        var crew = await CrewAsync("Süd", anna, bernd);

        // Admin + anna = 2 Konten: kein Gruppenchat möglich, verständlich.
        using (var tooFew = await _admin.PostAsync(new Uri($"/api/v1/teams/crews/{small}/chat", UriKind.Relative), null, Ct))
        {
            tooFew.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
            (await tooFew.Content.ReadAsStringAsync(Ct)).ShouldContain("mindestens 3 Personen");
        }

        // Fehlende Berechtigung Chat.Create: lesbar mit Namen, nie 502.
        _graph.Forbidden.Add("/v1.0/chats");
        using (var forbidden = await _admin.PostAsync(new Uri($"/api/v1/teams/crews/{crew}/chat", UriKind.Relative), null, Ct))
        {
            forbidden.StatusCode.ShouldBe(HttpStatusCode.FailedDependency);
            (await forbidden.Content.ReadAsStringAsync(Ct)).ShouldContain("Chat.Create");
        }

        _graph.Forbidden.Clear();
        using var created = await _admin.PostAsync(new Uri($"/api/v1/teams/crews/{crew}/chat", UriKind.Relative), null, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(Ct));
        var chat = (await created.Content.ReadFromJsonAsync<TeamsCrewChatResponse>(TestJson.Options, Ct))!;
        chat.DisplayName.ShouldBe("Kolonne Süd");
        chat.AppUrl.ShouldStartWith("msteams:/l/chat/");
        var chatId = Uri.UnescapeDataString(chat.WebUrl.Split('/')[5]);
        _graph.Members[chatId].Keys.OrderBy(k => k).ShouldBe(["aad-admin", "aad-anna", "aad-bernd"]);

        using (var twice = await _admin.PostAsync(new Uri($"/api/v1/teams/crews/{crew}/chat", UriKind.Relative), null, Ct))
        {
            twice.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        var list = (await _admin.GetFromJsonAsync<TeamsCrewChatsResponse>("/api/v1/teams/crews", TestJson.Options, Ct))!;
        list.ViaGraph.ShouldBeTrue();
        list.CanCreate.ShouldBeTrue();
        list.CanDelete.ShouldBeTrue();
        list.Chats.Single().CrewId.ShouldBe(crew);

        // Apps: eigene Kolonnen-Chats („In Teams öffnen“).
        var annaLogin = _factory.CreateClient();
        using (var annaClient = annaLogin.WithToken((await annaLogin.LoginOkAsync("anna")).AccessToken))
        {
            var mine = (await annaClient.GetFromJsonAsync<List<TeamsMyCrewChatResponse>>("/api/v1/teams/crews/mine", TestJson.Options, Ct))!;
            mine.Single().CrewName.ShouldBe("Süd");
            mine.Single().AppUrl.ShouldStartWith("msteams:/l/chat/");
        }

        (await _admin.GetFromJsonAsync<List<TeamsMyCrewChatResponse>>("/api/v1/teams/crews/mine", TestJson.Options, Ct))!.ShouldBeEmpty();

        // Kolonne geändert → Chat folgt (Hintergrund): Carla rein, Bernd raus.
        await CrewUpdateAsync(crew, "Süd", anna, carla);
        await WaitForAsync(() => _graph.Members[chatId].ContainsKey("aad-carla") && !_graph.Members[chatId].ContainsKey("aad-bernd"));

        // Ohne Recht teams.delete: verboten.
        await _factory.CreateUserAsync("dispo", roleIds: StandardRoles.SiteWorkerId);
        var login = _factory.CreateClient();
        using var client = login.WithToken((await login.LoginOkAsync("dispo")).AccessToken);
        using (var denied = await client.PostAsync(new Uri($"/api/v1/teams/crews/{crew}/chat/delete", UriKind.Relative), null, Ct))
        {
            denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        using var deleted = await _admin.PostAsync(new Uri($"/api/v1/teams/crews/{crew}/chat/delete", UriKind.Relative), null, Ct);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync(Ct));
        _graph.Deleted.ShouldContain(chatId);
        (await _admin.GetFromJsonAsync<TeamsCrewChatsResponse>("/api/v1/teams/crews", TestJson.Options, Ct))!.Chats.ShouldBeEmpty();
    }

    [Fact]
    public async Task WithoutAppRegistration_ParticipantsForDeepLink_CrewCreateRefused()
    {
        await ConfigureAsync(graph: false);
        var anna = await UserAsync("anna", "aad-anna");
        var crew = await CrewAsync("Ost", anna);
        var bauakte = await BauakteAsync("Ohne Graph", anna);
        var participants = (await _admin.GetFromJsonAsync<TeamsParticipantsResponse>($"/api/v1/bauakten/{bauakte}/teams/participants", TestJson.Options, Ct))!;
        participants.ViaGraph.ShouldBeFalse();
        participants.People.Single(p => p.UserId == anna).Upn.ShouldBe("anna@example.org"); // E-Mail-Adresse als Konto (Deep Link)

        using var refused = await _admin.PostAsync(new Uri($"/api/v1/teams/crews/{crew}/chat", UriKind.Relative), null, Ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.Content.ReadAsStringAsync(Ct)).ShouldContain("TEAMS_NOT_CONFIGURED");
        var list = (await _admin.GetFromJsonAsync<TeamsCrewChatsResponse>("/api/v1/teams/crews", TestJson.Options, Ct))!;
        list.Available.ShouldBeTrue();
        list.ViaGraph.ShouldBeFalse();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(100, Ct);
        }

        condition().ShouldBeTrue();
    }
}
