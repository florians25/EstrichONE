using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Audit;
using EstrichOne.Api.Modules.Chat;
using EstrichOne.Api.Modules.Teams;
using EstrichOne.Api.Modules.Teams.Exports;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Teams;

/// <summary>
/// Microsoft-Teams-Anbindung gegen einen nachgebildeten Graph-Server: verschlüsselte Geheimnisse, Verbindungstest,
/// Kontenzuordnung, Kanal/Gruppenchat je Bauakte mit Drosselung (429), Mitgliederabgleich, Ereigniskarten (Webhook und
/// Dienstkonto), Chat-Modus „nur Teams“ (Archiv), Server ohne Internet und Betrieb ohne Einrichtung.
/// </summary>
public sealed class TeamsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "11111111-2222-4333-8444-555555555555";
    private const string Client = "66666666-7777-4888-8999-000000000000";
    private FakeGraphServer _graph = null!;
    private ApiFactory _factory = null!;
    private HttpClient _admin = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _graph = await FakeGraphServer.StartAsync();
        _factory = await ApiFactory.CreateMigratedAsync(postgres, Settings(_graph.Url, _graph.Url));
        _admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _factory.DisposeAsync();
        await _graph.DisposeAsync();
    }

    private static Dictionary<string, string> Settings(string graph, string login) => new()
    {
        ["EstrichOne:Teams:GraphBaseUrl"] = graph, ["EstrichOne:Teams:LoginBaseUrl"] = login, ["EstrichOne:Teams:AllowHttpWebhooks"] = "true",
        ["EstrichOne:Teams:MaxRetryDelay"] = "00:00:02", ["EstrichOne:Teams:MaxAttempts"] = "3", ["EstrichOne:PublicUrl"] = "https://estrichone.test",
    };

    private static async Task<TeamsSettingsResponse> SaveAsync(HttpClient client, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await client.PutAsJsonAsync("/api/v1/settings/integrations/teams", body, Ct);
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(Ct));
        return expected == HttpStatusCode.OK ? (await response.Content.ReadFromJsonAsync<TeamsSettingsResponse>(TestJson.Options, Ct))! : null!;
    }

    private Task<TeamsSettingsResponse> ConfigureAsync(string chatMode = "both", string postVia = "off", string? webhookUrl = null,
        string defaultLinkKind = "channel") =>
        SaveAsync(_admin, new
        {
            chatMode, tenantId = Tenant, clientId = Client, authKind = "secret", clientSecret = FakeGraphServer.Secret, siteTeam = FakeGraphServer.TeamId,
            defaultLinkKind, syncMembers = true, postVia, webhookUrl, postDefects = true, postAcceptance = true, postSchedule = true,
        });

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

    private async Task<TeamsLinkResponse> LinkAsync(Guid bauakteId, object body)
    {
        using var response = await _admin.PostAsJsonAsync($"/api/v1/bauakten/{bauakteId}/teams", body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<TeamsLinkResponse>(TestJson.Options, Ct))!;
    }

    private async Task<List<string>> AuditActionsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<AuditEvent>().Select(e => e.Action).ToListAsync(Ct);
    }

    [Fact]
    public async Task WithoutConfiguration_EverythingWorksAsBefore()
    {
        var (worker, workerId) = await UserAsync("baustelle", StandardRoles.SiteWorkerId);
        var status = (await worker.GetFromJsonAsync<TeamsStatusResponse>("/api/v1/teams/status", TestJson.Options, Ct))!;
        status.ChatMode.ShouldBe(TeamsChatMode.EstrichOne);
        status.Available.ShouldBeFalse();
        status.ChatReadOnly.ShouldBeFalse();
        status.CanManage.ShouldBeFalse();

        var bauakte = await BauakteAsync("EFH ohne Teams", workerId);
        var teams = (await _admin.GetFromJsonAsync<BauakteTeamsResponse>($"/api/v1/bauakten/{bauakte}/teams", TestJson.Options, Ct))!;
        teams.Link.ShouldBeNull();
        teams.CanCreate.ShouldBeFalse();

        // Anlegen ohne Einrichtung: klare Meldung statt Fehler.
        using var create = await _admin.PostAsJsonAsync($"/api/v1/bauakten/{bauakte}/teams", new { kind = "channel" }, Ct);
        create.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await create.Content.ReadAsStringAsync(Ct)).ShouldContain("TEAMS_NOT_CONFIGURED");

        // Mitarbeiter ohne settings.integrations: keine Einstellungen.
        using var forbidden = await worker.GetAsync(new Uri("/api/v1/settings/integrations/teams", UriKind.Relative), Ct);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        _graph.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Settings_SecretEncrypted_NeverReturned_ConnectionTestPasses()
    {
        var saved = await ConfigureAsync();
        saved.HasClientSecret.ShouldBeTrue();
        saved.GraphConfigured.ShouldBeTrue();
        saved.SiteTeamName.ShouldBe("Baustellen");
        saved.Permissions.ShouldContain(p => p.Name == "User.Read.All" && p.Required);

        using (var raw = await _admin.GetAsync(new Uri("/api/v1/settings/integrations/teams", UriKind.Relative), Ct))
        {
            (await raw.Content.ReadAsStringAsync(Ct)).ShouldNotContain(FakeGraphServer.Secret);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<TeamsSettingsRow>().SingleAsync(Ct);
            row.ProtectedClientSecret.ShouldNotBeNull();
            row.ProtectedClientSecret.ShouldNotContain(FakeGraphServer.Secret);
        }

        // Speichern ohne Schlüssel behält ihn.
        var again = await SaveAsync(_admin, new { chatMode = "both", tenantId = Tenant, clientId = Client, authKind = "secret" });
        again.HasClientSecret.ShouldBeTrue();

        using var test = await _admin.PostAsync(new Uri("/api/v1/settings/integrations/teams/test", UriKind.Relative), null, Ct);
        var result = (await test.Content.ReadFromJsonAsync<TeamsTestResponse>(TestJson.Options, Ct))!;
        result.Ok.ShouldBeTrue(string.Join(" | ", result.Steps.Select(s => s.Detail)));
        result.Steps.Count.ShouldBe(3);
        _graph.CallsTo("POST", $"/{Tenant}/oauth2/v2.0/token").ShouldNotBeEmpty();
        // Token wird zwischengespeichert: nicht für jeden Graph-Aufruf neu angemeldet.
        _graph.CallsTo("POST", $"/{Tenant}/oauth2/v2.0/token").Count.ShouldBeLessThan(_graph.CallsTo("GET", "/v1.0/").Count + 1);

        (await AuditActionsAsync()).ShouldContain("teams.settings_saved");
        (await AuditActionsAsync()).ShouldContain("teams.connection_tested");
    }

    [Fact]
    public async Task WrongSecret_AndInvalidInput_GiveClearReasons()
    {
        await SaveAsync(_admin, new { chatMode = "both", tenantId = "kein tenant", clientId = Client }, HttpStatusCode.UnprocessableEntity);
        await SaveAsync(_admin, new { chatMode = "both", tenantId = Tenant, clientId = "abc" }, HttpStatusCode.UnprocessableEntity);
        await SaveAsync(_admin, new { chatMode = "both", webhookUrl = "https://boese.example.com/hook" }, HttpStatusCode.UnprocessableEntity);
        await SaveAsync(_admin, new { chatMode = "both", tenantId = Tenant, clientId = Client, authKind = "secret", clientSecret = "falsch" });

        using var test = await _admin.PostAsync(new Uri("/api/v1/settings/integrations/teams/test", UriKind.Relative), null, Ct);
        var result = (await test.Content.ReadFromJsonAsync<TeamsTestResponse>(TestJson.Options, Ct))!;
        result.Ok.ShouldBeFalse();
        result.Steps.Single().Detail.ShouldContain("Clientschlüssel ist ungültig");
        var settings = (await _admin.GetFromJsonAsync<TeamsSettingsResponse>("/api/v1/settings/integrations/teams", TestJson.Options, Ct))!;
        settings.LastTestOk.ShouldBe(false);
    }

    [Fact]
    public async Task Certificate_SignsClientAssertion_ThumbprintShown()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=EstrichONE Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var pem = certificate.ExportCertificatePem() + "\n" + rsa.ExportPkcs8PrivateKeyPem();

        var saved = await SaveAsync(_admin, new { chatMode = "both", tenantId = Tenant, clientId = Client, authKind = "certificate", certificatePem = pem });
        saved.CertificateThumbprint.ShouldBe(certificate.Thumbprint);
        saved.GraphConfigured.ShouldBeTrue();

        using var test = await _admin.PostAsync(new Uri("/api/v1/settings/integrations/teams/test", UriKind.Relative), null, Ct);
        (await test.Content.ReadFromJsonAsync<TeamsTestResponse>(TestJson.Options, Ct))!.Ok.ShouldBeTrue();
        var token = _graph.CallsTo("POST", $"/{Tenant}/oauth2/v2.0/token").Last();
        token.Body.ShouldContain("client_assertion=");
        token.Body.ShouldNotContain("client_secret");
        token.Body.ShouldNotContain("PRIVATE");
    }

    [Fact]
    public async Task UserMapping_AutomaticByEmail_ManualCorrectionKept()
    {
        await ConfigureAsync();
        var (_, anna) = await UserAsync("anna", StandardRoles.SiteWorkerId, "aad-anna");
        var (_, bernd) = await UserAsync("bernd", StandardRoles.SiteWorkerId);
        _graph.Users["b.mueller@firma.de"] = ("aad-bernd", "Bernd Müller");

        using var match = await _admin.PostAsync(new Uri("/api/v1/settings/integrations/teams/users/match", UriKind.Relative), null, Ct);
        match.StatusCode.ShouldBe(HttpStatusCode.OK, await match.Content.ReadAsStringAsync(Ct));
        var users = (await _admin.GetFromJsonAsync<List<TeamsUserMapping>>("/api/v1/settings/integrations/teams/users", TestJson.Options, Ct))!;
        users.Single(u => u.UserId == anna).AadUserId.ShouldBe("aad-anna");
        users.Single(u => u.UserId == bernd).AadUserId.ShouldBeNull();
        users.Single(u => u.UserId == bernd).Error.ShouldNotBeNull();

        using var manual = await _admin.PutAsJsonAsync($"/api/v1/settings/integrations/teams/users/{bernd}", new { account = "b.mueller@firma.de" }, Ct);
        manual.StatusCode.ShouldBe(HttpStatusCode.OK, await manual.Content.ReadAsStringAsync(Ct));
        (await manual.Content.ReadFromJsonAsync<TeamsUserMapping>(TestJson.Options, Ct))!.Manual.ShouldBeTrue();

        using var unknown = await _admin.PutAsJsonAsync($"/api/v1/settings/integrations/teams/users/{bernd}", new { account = "gibt.es.nicht@firma.de" }, Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // Erneuter Abgleich ändert die Zuordnung von Hand nicht.
        using var again = await _admin.PostAsync(new Uri("/api/v1/settings/integrations/teams/users/match", UriKind.Relative), null, Ct);
        users = (await _admin.GetFromJsonAsync<List<TeamsUserMapping>>("/api/v1/settings/integrations/teams/users", TestJson.Options, Ct))!;
        users.Single(u => u.UserId == bernd).AadUserId.ShouldBe("aad-bernd");
        (await AuditActionsAsync()).ShouldContain("teams.user_mapped");
    }

    [Fact]
    public async Task Channel_CreatedDespiteThrottling_TeamMembersAdded_DeepLinks()
    {
        await ConfigureAsync();
        var (worker, workerId) = await UserAsync("anna", StandardRoles.SiteWorkerId, "aad-anna");
        using (await _admin.PostAsync(new Uri("/api/v1/settings/integrations/teams/users/match", UriKind.Relative), null, Ct))
        {
        }

        var bauakte = await BauakteAsync("EFH Müller: Heizestrich #1/2", workerId);
        _graph.Throttle[$"/v1.0/teams/{FakeGraphServer.TeamId}/channels"] = 1;

        var link = await LinkAsync(bauakte, new { kind = "channel" });
        link.Kind.ShouldBe(TeamsLinkKind.Channel);
        link.Created.ShouldBeTrue();
        link.WebUrl.ShouldStartWith("https://teams.microsoft.com/l/channel/");
        link.AppUrl.ShouldStartWith("msteams:/l/channel/");
        link.DisplayName.ShouldNotBeNull();
        link.DisplayName.ShouldNotContain(":");
        link.DisplayName.ShouldNotContain("#");
        link.DisplayName.ShouldNotContain("/");
        // 429 mit Retry-After: ein zweiter Versuch, dann angelegt.
        _graph.CallsTo("POST", $"/v1.0/teams/{FakeGraphServer.TeamId}/channels").Count.ShouldBe(2);
        _graph.Members[FakeGraphServer.TeamId].Keys.ShouldContain("aad-anna");

        // Team-Mitglied sieht die Verknüpfung (Deep Link für Web und Apps).
        var seen = (await worker.GetFromJsonAsync<BauakteTeamsResponse>($"/api/v1/bauakten/{bauakte}/teams", TestJson.Options, Ct))!;
        seen.Link!.WebUrl.ShouldBe(link.WebUrl);
        seen.Link.AppUrl.ShouldBe(link.AppUrl);

        using var twice = await _admin.PostAsJsonAsync($"/api/v1/bauakten/{bauakte}/teams", new { kind = "channel" }, Ct);
        twice.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var duplicate = await BauakteAsync("Doppelt", workerId);
        using var conflict = await _admin.PostAsJsonAsync($"/api/v1/bauakten/{duplicate}/teams", new { kind = "channel" }, Ct);
        conflict.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await conflict.Content.ReadAsStringAsync(Ct)).ShouldContain("per Link verknüpfen");
    }

    [Fact]
    public async Task GroupChat_MembersFollowTeam_OnlyOwnAdditionsRemoved()
    {
        await ConfigureAsync(defaultLinkKind: "groupChat");
        var (_, anna) = await UserAsync("anna", StandardRoles.SiteWorkerId, "aad-anna");
        var (_, bernd) = await UserAsync("bernd", StandardRoles.SiteWorkerId, "aad-bernd");
        var (_, carla) = await UserAsync("carla", StandardRoles.SiteWorkerId, "aad-carla");
        using (await _admin.PostAsync(new Uri("/api/v1/settings/integrations/teams/users/match", UriKind.Relative), null, Ct))
        {
        }

        var lonely = await BauakteAsync("Nur eine Person", anna);
        using (var tooFew = await _admin.PostAsJsonAsync($"/api/v1/bauakten/{lonely}/teams", new { kind = "groupChat" }, Ct))
        {
            tooFew.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        var bauakte = await BauakteAsync("MFH Gruppenchat", anna, bernd);
        var link = await LinkAsync(bauakte, new { kind = "groupChat" });
        link.Kind.ShouldBe(TeamsLinkKind.GroupChat);
        link.AppUrl.ShouldStartWith("msteams:/l/chat/");
        var chat = _graph.Members[link.ChatId!];
        chat.Keys.OrderBy(k => k).ShouldBe(["aad-anna", "aad-bernd"]);
        chat["aad-fremd"] = "m-aad-fremd"; // in Teams von Hand hinzugefügt

        // Team ändern: Bernd raus, Carla rein → Abgleich (im Hintergrund nach der Teamänderung).
        using (var set = await _admin.PutAsJsonAsync($"/api/v1/bauakten/{bauakte}/members", new { userIds = new[] { anna, carla } }, Ct))
        {
            set.EnsureSuccessStatusCode();
        }

        await WaitForAsync(() => chat.ContainsKey("aad-carla") && !chat.ContainsKey("aad-bernd"));
        chat.ContainsKey("aad-fremd").ShouldBeTrue(); // nie fremde Mitglieder entfernen

        // Fehlende Berechtigung: sichtbar an der Verknüpfung, kein Absturz.
        _graph.Forbidden.Add($"/v1.0/chats/{link.ChatId}/members");
        using var sync = await _admin.PostAsync(new Uri($"/api/v1/bauakten/{bauakte}/teams/sync", UriKind.Relative), null, Ct);
        sync.StatusCode.ShouldBe(HttpStatusCode.OK);
        var synced = (await sync.Content.ReadFromJsonAsync<TeamsLinkResponse>(TestJson.Options, Ct))!;
        synced.LastSyncError.ShouldNotBeNull();
        synced.LastSyncError.ShouldContain("ChatMember.ReadWrite.All");
    }

    [Fact]
    public async Task ExistingConversation_LinkedByUrl_UnlinkKeepsItInTeams()
    {
        var (_, anna) = await UserAsync("anna", StandardRoles.SiteWorkerId);
        var bauakte = await BauakteAsync("Altbau Link", anna);

        // Ohne App-Registrierung: Verknüpfen per Link geht trotzdem (nur „In Teams öffnen“).
        await SaveAsync(_admin, new { chatMode = "teams" });
        using (var bad = await _admin.PostAsJsonAsync($"/api/v1/bauakten/{bauakte}/teams", new { url = "https://example.com/l/channel/x" }, Ct))
        {
            bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        }

        var url = $"https://teams.microsoft.com/l/channel/19%3aabc123%40thread.tacv2/Altbau%20Link?groupId={FakeGraphServer.TeamId}&tenantId={Tenant}";
        var link = await LinkAsync(bauakte, new { url });
        link.Created.ShouldBeFalse();
        link.ChannelId.ShouldBe("19:abc123@thread.tacv2");
        link.TeamId.ShouldBe(FakeGraphServer.TeamId);
        link.DisplayName.ShouldBe("Altbau Link");
        link.SyncMembers.ShouldBeFalse();

        using var unlink = await _admin.DeleteAsync(new Uri($"/api/v1/bauakten/{bauakte}/teams", UriKind.Relative), Ct);
        unlink.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        _graph.Calls.Where(c => c.Method == "DELETE").ShouldBeEmpty();
        (await AuditActionsAsync()).ShouldContain("teams.bauakte_unlinked");
    }

    [Fact]
    public async Task Events_PostedAsAdaptiveCard_ViaWebhook_AndViaSenderAccount()
    {
        var hook = $"{_graph.Url}/webhook/baustellen";
        await ConfigureAsync(postVia: "webhook", webhookUrl: hook);
        var (_, anna) = await UserAsync("anna", StandardRoles.SiteWorkerId, "aad-anna");
        var bauakte = await BauakteAsync("EFH Karte", anna);

        using (var test = await _admin.PostAsJsonAsync("/api/v1/settings/integrations/teams/webhook/test", new { }, Ct))
        {
            (await test.Content.ReadFromJsonAsync<TeamsTestResponse>(TestJson.Options, Ct))!.Ok.ShouldBeTrue();
        }

        using (var defect = await _admin.PostAsJsonAsync("/api/v1/work-items", new { id = Guid.CreateVersion7(), bauakteId = bauakte, kind = "defect", title = "Riss im Flur" }, Ct))
        {
            defect.EnsureSuccessStatusCode();
        }

        await WaitForAsync(() => _graph.CallsTo("POST", "/webhook/").Count >= 2);
        var card = JsonNode.Parse(_graph.CallsTo("POST", "/webhook/").Last().Body)!;
        card["attachments"]![0]!["contentType"]!.GetValue<string>().ShouldBe("application/vnd.microsoft.card.adaptive");
        var text = card.ToJsonString();
        text.ShouldContain("Neuer Mangel");
        text.ShouldContain("Riss im Flur");
        text.ShouldContain("https://estrichone.test/bauakten/");

        // Über das Dienstkonto (Gerätecode) in den verknüpften Gruppenchat.
        await ConfigureAsync(postVia: "graph");
        using (var start = await _admin.PostAsync(new Uri("/api/v1/settings/integrations/teams/sender/start", UriKind.Relative), null, Ct))
        {
            (await start.Content.ReadFromJsonAsync<TeamsSenderStart>(TestJson.Options, Ct))!.UserCode.ShouldBe("ABCD-EFGH");
        }

        using (var pending = await _admin.PostAsync(new Uri("/api/v1/settings/integrations/teams/sender/poll", UriKind.Relative), null, Ct))
        {
            (await pending.Content.ReadFromJsonAsync<TeamsSenderState>(TestJson.Options, Ct))!.Pending.ShouldBeTrue();
        }

        using (var done = await _admin.PostAsync(new Uri("/api/v1/settings/integrations/teams/sender/poll", UriKind.Relative), null, Ct))
        {
            var state = (await done.Content.ReadFromJsonAsync<TeamsSenderState>(TestJson.Options, Ct))!;
            state.Connected.ShouldBeTrue();
            state.SenderName.ShouldBe("estrichone-bot@firma.de");
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<TeamsSettingsRow>().SingleAsync(Ct);
            row.ProtectedSenderToken.ShouldNotBeNull();
            row.ProtectedSenderToken.ShouldNotContain("refresh-1");
        }

        var (_, bernd) = await UserAsync("bernd", StandardRoles.SiteWorkerId, "aad-bernd");
        using (await _admin.PostAsync(new Uri("/api/v1/settings/integrations/teams/users/match", UriKind.Relative), null, Ct))
        {
        }

        var chatAkte = await BauakteAsync("EFH Chatkarte", anna, bernd);
        var link = await LinkAsync(chatAkte, new { kind = "groupChat" });
        _graph.Members[link.ChatId!].Keys.ShouldContain("bot-id"); // Dienstkonto ist Mitglied, damit es senden darf

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITeamsIntegration>().Publish(
                new TeamsEvent(chatAkte, TeamsEventKind.AcceptanceSigned, "Ergebnis: abgenommen"));
        }

        await WaitForAsync(() => _graph.CallsTo("POST", $"/v1.0/chats/{link.ChatId}/messages").Count == 1);
        var message = _graph.CallsTo("POST", $"/v1.0/chats/{link.ChatId}/messages").Single();
        message.Authorization.ShouldBe("Bearer user-token");
        message.Body.ShouldContain("Abnahme unterschrieben");
    }

    [Fact]
    public async Task TeamsMode_ChatBecomesReadOnlyArchive_ExportStillWorks()
    {
        var (office, officeId) = await UserAsync("buero", StandardRoles.OfficeId);
        var (worker, workerId) = await UserAsync("baustelle", StandardRoles.SiteWorkerId);
        using (var enable = await office.PutAsJsonAsync("/api/v1/chat/admin/settings", new { enabled = true, retentionDays = 90 }, Ct))
        {
            enable.EnsureSuccessStatusCode();
        }

        using var start = await worker.PostAsJsonAsync("/api/v1/chat/conversations/direct", new { userId = officeId }, Ct);
        var conversation = (await start.Content.ReadFromJsonAsync<ChatConversationSummary>(TestJson.Options, Ct))!;
        using (var sent = await worker.PostAsJsonAsync($"/api/v1/chat/conversations/{conversation.Id}/messages", new { text = "Vor dem Umzug" }, Ct))
        {
            sent.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        await SaveAsync(_admin, new { chatMode = "teams" });
        var status = (await worker.GetFromJsonAsync<ChatStatusResponse>("/api/v1/chat/status", TestJson.Options, Ct))!;
        status.ReadOnly.ShouldBeTrue();
        status.Mode.ShouldBe(TeamsChatMode.Teams);
        (await worker.GetFromJsonAsync<TeamsStatusResponse>("/api/v1/teams/status", TestJson.Options, Ct))!.ChatReadOnly.ShouldBeTrue();

        using (var blocked = await worker.PostAsJsonAsync($"/api/v1/chat/conversations/{conversation.Id}/messages", new { text = "Nach dem Umzug" }, Ct))
        {
            blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await blocked.Content.ReadAsStringAsync(Ct)).ShouldContain("CHAT_READ_ONLY");
        }

        using (var group = await office.PostAsJsonAsync("/api/v1/chat/conversations", new { title = "Neu", memberIds = new[] { workerId } }, Ct))
        {
            group.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        // Lesen, gelesen markieren und exportieren bleiben möglich; nichts wird gelöscht.
        var page = (await office.GetFromJsonAsync<ChatMessagePage>($"/api/v1/chat/conversations/{conversation.Id}/messages", TestJson.Options, Ct))!;
        page.Items.Single().Text.ShouldBe("Vor dem Umzug");
        using (var read = await office.PostAsync(new Uri($"/api/v1/chat/conversations/{conversation.Id}/read", UriKind.Relative), null, Ct))
        {
            read.IsSuccessStatusCode.ShouldBeTrue(await read.Content.ReadAsStringAsync(Ct));
        }

        var export = (await worker.GetFromJsonAsync<ChatExport>("/api/v1/chat/export", TestJson.Options, Ct))!;
        export.Messages.ShouldContain(m => m.Text == "Vor dem Umzug");
        (await AuditActionsAsync()).ShouldContain("teams.chat_mode_changed");

        // Zurück auf „beides“: Schreiben geht wieder.
        await SaveAsync(_admin, new { chatMode = "both" });
        using var after = await worker.PostAsJsonAsync($"/api/v1/chat/conversations/{conversation.Id}/messages", new { text = "Wieder da" }, Ct);
        after.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ServerWithoutInternet_ClearMessage()
    {
        // Geschlossener Port statt Microsoft: wie ein Server ohne Internet bzw. mit Firewall.
        await using var offline = await ApiFactory.CreateMigratedAsync(postgres, Settings("http://127.0.0.1:1", "http://127.0.0.1:1"));
        using var admin = await offline.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        using (var save = await admin.PutAsJsonAsync("/api/v1/settings/integrations/teams",
            new { chatMode = "both", tenantId = Tenant, clientId = Client, authKind = "secret", clientSecret = "x" }, Ct))
        {
            save.EnsureSuccessStatusCode();
        }

        using var test = await admin.PostAsync(new Uri("/api/v1/settings/integrations/teams/test", UriKind.Relative), null, Ct);
        var result = (await test.Content.ReadFromJsonAsync<TeamsTestResponse>(TestJson.Options, Ct))!;
        result.Ok.ShouldBeFalse();
        result.Steps.Single().Detail.ShouldContain("Internetverbindung");

        using var match = await admin.PostAsync(new Uri("/api/v1/settings/integrations/teams/users/match", UriKind.Relative), null, Ct);
        match.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        (await match.Content.ReadAsStringAsync(Ct)).ShouldContain("TEAMS_OFFLINE");
    }

    [Theory]
    [InlineData("https://teams.microsoft.com/l/channel/19%3aabc%40thread.tacv2/Baustelle%201?groupId=g1&tenantId=t1", TeamsLinkKind.Channel, "19:abc@thread.tacv2", "g1")]
    [InlineData("msteams:/l/channel/19%3aabc%40thread.tacv2/X?groupId=g1&tenantId=t1", TeamsLinkKind.Channel, "19:abc@thread.tacv2", "g1")]
    [InlineData("https://teams.cloud.microsoft/l/chat/19:xyz@thread.v2/conversations?context=%7B%7D", TeamsLinkKind.GroupChat, "19:xyz@thread.v2", null)]
    public void ParseLink_RecognisesChannelsAndChats(string url, TeamsLinkKind kind, string id, string? team)
    {
        var target = TeamsService.ParseLink(url)!;
        target.Kind.ShouldBe(kind);
        (target.ChannelId ?? target.ChatId).ShouldBe(id);
        target.TeamId.ShouldBe(team);
        target.WebUrl.ShouldStartWith("https://teams.microsoft.com/l/");
        TeamsService.AppUrl(target.WebUrl).ShouldStartWith("msteams:/l/");
    }

    [Theory]
    [InlineData("https://example.com/l/channel/x")]
    [InlineData("https://teams.microsoft.com/l/chat/0/0?users=a@b.de")]
    [InlineData("kein link")]
    public void ParseLink_RejectsOtherLinks(string url) => TeamsService.ParseLink(url)?.Kind.ShouldBeNull();

    [Fact]
    public void ChannelName_FollowsTeamsRules()
    {
        TeamsService.ChannelName("2026-0042", "EFH Müller: Heizestrich #1/2 & Keller?").ShouldBe("2026-0042 EFH Müller Heizestrich 1 2 Keller");
        TeamsService.ChannelName(null, new string('x', 80)).Length.ShouldBe(50);
        TeamsService.ChannelName(null, "...").ShouldBe("Bauakte");
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
