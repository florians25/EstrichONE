using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Notifications;
using EstrichOne.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EstrichOne.Api.Tests.Notifications;

/// <summary>Web-Push-Verschlüsselung gegen die Testvektoren aus RFC 8291 (Anhang A).</summary>
public sealed class PushCryptoTests
{
    [Fact]
    public void EncryptWebPush_MatchesRfc8291Example()
    {
        var asPublic = PushCrypto.FromBase64Url("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8");
        var sender = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = PushCrypto.FromBase64Url("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw"),
            Q = new ECPoint { X = asPublic[1..33], Y = asPublic[33..65] },
        };

        var message = PushCrypto.EncryptWebPush(Encoding.UTF8.GetBytes("When I grow up, I want to be a watermelon"),
            PushCrypto.FromBase64Url("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4"),
            PushCrypto.FromBase64Url("BTBZMqHH6r4Tts7J_aSIgg"), PushCrypto.FromBase64Url("DGv6ra1nlYgDCS1FRnbzlw"), sender);

        PushCrypto.Base64Url(message).ShouldBe(
            "DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN");
    }

    [Fact]
    public void VapidAndApnsTokens_AreVerifiableEs256Jwts()
    {
        var (publicKey, privateKey) = PushCrypto.CreateVapidKeys();
        var header = PushCrypto.VapidAuthorization(new Uri("https://fcm.googleapis.com/fcm/send/abc"), publicKey, privateKey, "mailto:it@example.org",
            DateTimeOffset.UtcNow);
        var jwt = header["vapid t=".Length..header.IndexOf(',', StringComparison.Ordinal)];
        var parts = jwt.Split('.');
        var pub = PushCrypto.FromBase64Url(publicKey);
        using var key = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = pub[1..33], Y = pub[33..65] } });

        key.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), PushCrypto.FromBase64Url(parts[2]), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation).ShouldBeTrue();
        Encoding.UTF8.GetString(PushCrypto.FromBase64Url(parts[1])).ShouldContain("\"aud\":\"https://fcm.googleapis.com\"");

        using var apple = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = apple.ExportPkcs8PrivateKeyPem();
        PushCrypto.IsValidP8(pem).ShouldBeTrue();
        PushCrypto.IsValidP8("-----BEGIN PRIVATE KEY-----\nabc\n-----END PRIVATE KEY-----").ShouldBeFalse();
        PushCrypto.ApnsToken(pem, "ABC123DEFG", "TEAM123456", DateTimeOffset.UtcNow).Split('.').Length.ShouldBe(3);
    }
}

/// <summary>Postfach, Versand durch das Büro mit Zustellstatus, Geräte-Registrierung, Ereignisse aus anderen Modulen.</summary>
public sealed class NotificationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private async Task<(HttpClient Client, Guid Id)> AsAsync(string userName, Guid role)
    {
        var user = await _factory.CreateUserAsync(userName, roleIds: role);
        var client = _factory.CreateClient();
        return (client.WithToken((await client.LoginOkAsync(userName)).AccessToken), user.Id);
    }

    private async Task DispatchAsync()
    {
        var dispatcher = _factory.Services.GetServices<IHostedService>().OfType<NotificationDispatcher>().Single();
        while (await dispatcher.DispatchBatchAsync(Ct) > 0)
        {
        }
    }

    [Fact]
    public async Task Office_SendsToRoleAndPerson_RecipientsSeeInbox_HistoryShowsDeliveryStatus()
    {
        var (office, officeId) = await AsAsync("buero", StandardRoles.OfficeId);
        var (worker, workerId) = await AsAsync("baustelle", StandardRoles.SiteWorkerId);
        var (it, _) = await AsAsync("it", StandardRoles.ItAdministratorId);

        using var device = await worker.PostAsJsonAsync("/api/v1/notifications/devices",
            new { platform = "ios", token = new string('a', 64), deviceName = "iPhone Lukas", appVersion = "0.1.0", sandbox = true }, Ct);
        device.StatusCode.ShouldBe(HttpStatusCode.OK, await device.Content.ReadAsStringAsync(Ct));

        using var send = await office.PostAsJsonAsync("/api/v1/notifications/send", new
        {
            title = "Morgen Treffpunkt 6:30 Lager", body = "Bitte Messgeräte mitnehmen.", link = "/planung/wer-faehrt-wohin", urgency = "high",
            roleIds = new[] { StandardRoles.SiteWorkerId }, userIds = new[] { officeId },
        }, Ct);
        send.StatusCode.ShouldBe(HttpStatusCode.OK, await send.Content.ReadAsStringAsync(Ct));
        var broadcast = (await send.Content.ReadFromJsonAsync<BroadcastResponse>(TestJson.Options, Ct))!;
        broadcast.Recipients.ShouldBe(2);
        broadcast.Audience.ShouldBe("1 Person, Rolle Baustelle");

        await DispatchAsync();
        var inbox = (await worker.GetFromJsonAsync<NotificationInbox>("/api/v1/notifications", TestJson.Options, Ct))!;
        inbox.UnreadCount.ShouldBe(1);
        inbox.Items.Single().Title.ShouldBe("Morgen Treffpunkt 6:30 Lager");
        inbox.Items.Single().SenderName.ShouldBe("Max Huber");
        (await worker.PostAsync(new Uri($"/api/v1/notifications/{inbox.Items[0].Id}/read", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();
        (await worker.GetFromJsonAsync<UnreadCountResponse>("/api/v1/notifications/unread-count", TestJson.Options, Ct))!.Count.ShouldBe(0);

        // Fremde Einträge sind nicht erreichbar.
        using var foreign = await office.PostAsync(new Uri($"/api/v1/notifications/{inbox.Items[0].Id}/read", UriKind.Relative), null, Ct);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var detail = (await office.GetFromJsonAsync<BroadcastDetail>($"/api/v1/notifications/sent/{broadcast.Id}", TestJson.Options, Ct))!;
        detail.Broadcast.Read.ShouldBe(1);
        detail.Broadcast.Pending.ShouldBe(0);
        detail.Recipients.Single(r => r.UserId == workerId).PushState.ShouldBe(PushState.InAppOnly); // APNs nicht eingerichtet
        detail.Recipients.Single(r => r.UserId == officeId).PushState.ShouldBe(PushState.NoDevice);
        (await office.GetFromJsonAsync<List<BroadcastResponse>>("/api/v1/notifications/sent", TestJson.Options, Ct))!.ShouldHaveSingleItem();

        var audiences = (await office.GetFromJsonAsync<NotificationAudiences>("/api/v1/notifications/audiences", TestJson.Options, Ct))!;
        audiences.Roles.Single(r => r.Name == "Baustelle").Members.ShouldBe(1);
        audiences.ActiveUsers.ShouldBe(3);

        using var forbidden = await worker.PostAsJsonAsync("/api/v1/notifications/send", new { title = "x", all = true }, Ct);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var itSend = await it.PostAsJsonAsync("/api/v1/notifications/send", new { title = "x", all = true }, Ct);
        itSend.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var badLink = await office.PostAsJsonAsync("/api/v1/notifications/send", new { title = "x", all = true, link = "https://evil.example" }, Ct);
        badLink.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Devices_ValidateTokens_MoveBetweenUsers_AndUnregisterByToken()
    {
        var (anna, _) = await AsAsync("anna", StandardRoles.SiteWorkerId);
        var (max, _) = await AsAsync("max", StandardRoles.SiteWorkerId);
        var key = Convert.ToBase64String(new byte[65].Select((_, i) => i == 0 ? (byte)4 : (byte)i).ToArray());

        using var http = await anna.PostAsJsonAsync("/api/v1/notifications/devices",
            new { platform = "web", token = "http://127.0.0.1:8080/intern", p256dh = key, auth = "AAAAAAAAAAAAAAAAAAAAAA" }, Ct);
        using var foreignHost = await anna.PostAsJsonAsync("/api/v1/notifications/devices",
            new { platform = "web", token = "https://intranet.firma.local/push", p256dh = key, auth = "AAAAAAAAAAAAAAAAAAAAAA" }, Ct);
        using var badIos = await anna.PostAsJsonAsync("/api/v1/notifications/devices", new { platform = "ios", token = "kein-token" }, Ct);
        using var web = await anna.PostAsJsonAsync("/api/v1/notifications/devices",
            new { platform = "web", token = "https://fcm.googleapis.com/fcm/send/abc123", p256dh = key.TrimEnd('='), auth = "AAAAAAAAAAAAAAAAAAAAAA", deviceName = "Chrome" }, Ct);
        var token = new string('b', 64);
        (await anna.PostAsJsonAsync("/api/v1/notifications/devices", new { platform = "ios", token }, Ct)).EnsureSuccessStatusCode();
        (await max.PostAsJsonAsync("/api/v1/notifications/devices", new { platform = "ios", token = token.ToUpperInvariant() }, Ct)).EnsureSuccessStatusCode();

        http.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        foreignHost.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        badIos.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        web.StatusCode.ShouldBe(HttpStatusCode.OK, await web.Content.ReadAsStringAsync(Ct));
        (await anna.GetFromJsonAsync<List<PushDeviceResponse>>("/api/v1/notifications/devices", TestJson.Options, Ct))!.Single().Platform.ShouldBe(PushPlatform.Web);
        (await max.GetFromJsonAsync<List<PushDeviceResponse>>("/api/v1/notifications/devices", TestJson.Options, Ct))!.ShouldHaveSingleItem();

        (await max.DeleteAsync(new Uri($"/api/v1/notifications/devices?token={token}", UriKind.Relative), Ct)).EnsureSuccessStatusCode();
        (await max.GetFromJsonAsync<List<PushDeviceResponse>>("/api/v1/notifications/devices", TestJson.Options, Ct))!.ShouldBeEmpty();

        var config = (await anna.GetFromJsonAsync<PushClientConfig>("/api/v1/notifications/push-config", TestJson.Options, Ct))!;
        config.WebPushEnabled.ShouldBeTrue();
        PushCrypto.FromBase64Url(config.VapidPublicKey!).Length.ShouldBe(65);
        config.ApnsEnabled.ShouldBeFalse();
    }

    [Fact]
    public async Task PushSettings_StoreApnsKeyEncrypted_AndNeverReturnIt()
    {
        var admin = _factory.CreateClient();
        await _factory.CreateUserAsync("admin", roleIds: StandardRoles.AdministratorId);
        admin.WithToken((await admin.LoginOkAsync("admin")).AccessToken);
        using var apple = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = apple.ExportPkcs8PrivateKeyPem();

        using var invalid = await admin.PutAsJsonAsync("/api/v1/notifications/settings", new { apnsEnabled = true, apnsKey = "kein schlüssel" }, Ct);
        using var saved = await admin.PutAsJsonAsync("/api/v1/notifications/settings", new
        {
            webPushEnabled = true, vapidSubject = "mailto:it@example.org", apnsEnabled = true, apnsKeyId = "abc123defg", apnsTeamId = "team123456",
            apnsBundleId = "de.estrichone.app", apnsProduction = false, apnsKey = pem, showContent = false,
        }, Ct);

        invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var text = await saved.Content.ReadAsStringAsync(Ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK, text);
        text.ShouldNotContain("PRIVATE KEY");
        var settings = (await saved.Content.ReadFromJsonAsync<PushSettingsResponse>(TestJson.Options, Ct))!;
        settings.ApnsReady.ShouldBeTrue();
        settings.ApnsKeyId.ShouldBe("ABC123DEFG");
        settings.ApnsKeyStored.ShouldBeTrue();
        settings.ShowContent.ShouldBeFalse();
        (await admin.GetFromJsonAsync<PushClientConfig>("/api/v1/notifications/push-config", TestJson.Options, Ct))!.ApnsEnabled.ShouldBeTrue();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOne.Api.Infrastructure.Persistence.EstrichOneDbContext>();
        var row = db.Set<PushSettingsRow>().Single();
        row.ProtectedApnsKey.ShouldNotBeNull();
        row.ProtectedApnsKey.ShouldNotContain("PRIVATE");
        row.ProtectedVapidPrivateKey.ShouldNotBeNull();
    }

    [Fact]
    public async Task TestNotification_LandsInOwnInbox_AndReadAllClearsCount()
    {
        var (anna, _) = await AsAsync("anna", StandardRoles.SiteWorkerId);
        (await anna.PostAsync(new Uri("/api/v1/notifications/test", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();
        (await anna.PostAsync(new Uri("/api/v1/notifications/test", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();
        (await anna.GetFromJsonAsync<UnreadCountResponse>("/api/v1/notifications/unread-count", TestJson.Options, Ct))!.Count.ShouldBe(2);
        (await anna.PostAsync(new Uri("/api/v1/notifications/read-all", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();
        var inbox = (await anna.GetFromJsonAsync<NotificationInbox>("/api/v1/notifications?unread=true", TestJson.Options, Ct))!;
        inbox.Items.ShouldBeEmpty();
        inbox.UnreadCount.ShouldBe(0);
    }

    [Fact]
    public async Task AssigningBauakteMember_NotifiesNewMember_ButNotTheActor()
    {
        var (office, officeId) = await AsAsync("buero", StandardRoles.OfficeId);
        var (worker, workerId) = await AsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var created = await office.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title = "Neubau Halle Ost", city = "Landshut" }, Ct);
        created.EnsureSuccessStatusCode();
        var bauakte = (await created.Content.ReadFromJsonAsync<EstrichOne.Api.Modules.Bauakten.BauakteDetail>(TestJson.Options, Ct))!;

        using var members = await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/members", new { userIds = new[] { workerId, officeId } }, Ct);
        members.StatusCode.ShouldBe(HttpStatusCode.OK, await members.Content.ReadAsStringAsync(Ct));

        var inbox = (await worker.GetFromJsonAsync<NotificationInbox>("/api/v1/notifications", TestJson.Options, Ct))!;
        var item = inbox.Items.ShouldHaveSingleItem();
        item.Title.ShouldBe("Neue Bauakte zugewiesen");
        item.Link.ShouldBe($"/bauakten/{bauakte.Id}");
        item.ObjectId.ShouldBe(bauakte.Id);
        (await office.GetFromJsonAsync<NotificationInbox>("/api/v1/notifications", TestJson.Options, Ct))!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Sending_IsRateLimitedPerUser()
    {
        var (office, officeId) = await AsAsync("buero", StandardRoles.OfficeId);
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 21; i++)
        {
            using var response = await office.PostAsJsonAsync("/api/v1/notifications/send", new { title = $"Nachricht {i}", userIds = new[] { officeId } }, Ct);
            statuses.Add(response.StatusCode);
        }

        statuses.Take(20).ShouldAllBe(s => s == HttpStatusCode.OK);
        statuses[20].ShouldBe(HttpStatusCode.TooManyRequests);
    }
}
