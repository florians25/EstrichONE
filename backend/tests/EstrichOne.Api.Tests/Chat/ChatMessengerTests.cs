using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Chat;
using EstrichOne.Api.Modules.Documentation;
using EstrichOne.Api.Modules.Notifications;
using EstrichOne.Api.Modules.Notifications.Exports;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NetVips;

namespace EstrichOne.Api.Tests.Chat;

/// <summary>Messenger-Funktionen (Runde 10): Anhänge verschlüsselt, Antworten, Reaktionen, Bearbeiten, Anheften, Weiterleiten, Erwähnungen, Echtzeit.</summary>
public sealed class ChatMessengerTests(PostgresFixture postgres) : IAsyncLifetime
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

    private static async Task EnableAsync(HttpClient admin, bool showPreview = true)
    {
        using var response = await admin.PutAsJsonAsync("/api/v1/chat/admin/settings", new { enabled = true, retentionDays = 90, showPreview }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<ChatConversationSummary> DirectAsync(HttpClient from, Guid to)
    {
        using var start = await from.PostAsJsonAsync("/api/v1/chat/conversations/direct", new { userId = to }, Ct);
        return (await start.Content.ReadFromJsonAsync<ChatConversationSummary>(TestJson.Options, Ct))!;
    }

    private static async Task<ChatMessageResponse> SendAsync(HttpClient client, Guid conversationId, object body)
    {
        using var send = await client.PostAsJsonAsync($"/api/v1/chat/conversations/{conversationId}/messages", body, Ct);
        send.StatusCode.ShouldBe(HttpStatusCode.Created, await send.Content.ReadAsStringAsync(Ct));
        return (await send.Content.ReadFromJsonAsync<ChatMessageResponse>(TestJson.Options, Ct))!;
    }

    private static byte[] Jpeg(int width, int height)
    {
        using var image = (Image.Black(width, height, bands: 3) + new[] { 160.0, 90.0, 40.0 }).Cast(Enums.BandFormat.Uchar);
        return image.JpegsaveBuffer(q: 95);
    }

    private static async Task<ChatAttachmentResponse> UploadAsync(HttpClient client, Guid conversationId, byte[] content, string name, string type)
    {
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue(type);
        using var upload = await client.PostAsync(new Uri($"/api/v1/chat/conversations/{conversationId}/attachments?fileName={name}", UriKind.Relative), body, Ct);
        upload.StatusCode.ShouldBe(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync(Ct));
        return (await upload.Content.ReadFromJsonAsync<ChatAttachmentResponse>(TestJson.Options, Ct))!;
    }

    [Fact]
    public async Task Attachments_CompressedAndEncrypted_SentWithMessage_CopiedIntoBauakte_DeletedForAll()
    {
        var (office, officeId) = await AsAsync("buero", StandardRoles.OfficeId);
        var (worker, _) = await AsAsync("baustelle", StandardRoles.SiteWorkerId);
        await EnableAsync(office);
        var conversation = await DirectAsync(worker, officeId);

        var jpeg = Jpeg(5000, 3000);
        var photo = await UploadAsync(worker, conversation.Id, jpeg, "IMG_0042.jpg", "image/jpeg");
        photo.Kind.ShouldBe(ChatAttachmentKind.Photo);
        photo.FileName.ShouldBe("IMG_0042.jpg");
        photo.HasThumbnail.ShouldBeTrue();
        photo.SizeBytes.ShouldBeLessThan(jpeg.Length); // verdichtet (lange Kante begrenzt)
        Math.Max(photo.Width!.Value, photo.Height!.Value).ShouldBeLessThan(5000);

        // Verschlüsselt im Dateispeicher: weder JPEG-Kopf noch Dateiname im Klartext.
        var stored = Directory.GetFiles(Path.Combine(_factory.FilesRoot, "chat"), "*.bin", SearchOption.AllDirectories).Single();
        var raw = await File.ReadAllBytesAsync(stored, Ct);
        raw.Take(3).ShouldNotBe(new byte[] { 0xFF, 0xD8, 0xFF });

        // Fremdtyp abgelehnt.
        using var bad = new ByteArrayContent("MZ ausführbar"u8.ToArray());
        using var rejected = await worker.PostAsync(new Uri($"/api/v1/chat/conversations/{conversation.Id}/attachments?fileName=x.exe", UriKind.Relative), bad, Ct);
        rejected.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // Vor dem Senden sieht nur die hochladende Person den Anhang.
        using var early = await office.GetAsync(new Uri($"/api/v1/chat/attachments/{photo.Id}/content", UriKind.Relative), Ct);
        early.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var message = await SendAsync(worker, conversation.Id, new { text = "Halle 2 fertig", attachmentIds = new[] { photo.Id } });
        message.Attachments!.Single().Id.ShouldBe(photo.Id);

        var content = await office.GetByteArrayAsync(new Uri($"/api/v1/chat/attachments/{photo.Id}/content", UriKind.Relative), Ct);
        content.Take(3).ShouldBe(new byte[] { 0xFF, 0xD8, 0xFF });
        using var thumb = await office.GetAsync(new Uri($"/api/v1/chat/attachments/{photo.Id}/content?thumbnail=true", UriKind.Relative), Ct);
        thumb.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");

        var media = (await office.GetFromJsonAsync<List<ChatAttachmentResponse>>($"/api/v1/chat/conversations/{conversation.Id}/attachments?kind=photo",
            TestJson.Options, Ct))!;
        media.Single().Id.ShouldBe(photo.Id);

        // Push-Hinweis nennt den Anhang (Vorschau erlaubt).
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
            (await db.Set<Notification>().SingleAsync(n => n.UserId == officeId, Ct)).Body.ShouldBe("📷 Foto: Halle 2 fertig");
        }

        // In die Bauakte übernehmen (als Foto mit Vorschauvarianten).
        using var created = await worker.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title = "EFH Chat" }, Ct);
        var bauakte = (await created.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;
        using var copy = await worker.PostAsJsonAsync($"/api/v1/chat/attachments/{photo.Id}/bauakte", new { bauakteId = bauakte.Id, caption = "Aus dem Chat" }, Ct);
        copy.StatusCode.ShouldBe(HttpStatusCode.OK, await copy.Content.ReadAsStringAsync(Ct));
        var copied = (await copy.Content.ReadFromJsonAsync<ChatAttachmentToBauakteResponse>(TestJson.Options, Ct))!;
        using var fileResponse = await worker.GetAsync(new Uri($"/api/v1/files/{copied.FileId}", UriKind.Relative), Ct);
        fileResponse.StatusCode.ShouldBe(HttpStatusCode.OK, await fileResponse.Content.ReadAsStringAsync(Ct));
        var file = (await fileResponse.Content.ReadFromJsonAsync<FileResponse>(TestJson.Options, Ct))!;
        file.Kind.ShouldBe(FileKind.Photo);
        file.BauakteId.ShouldBe(bauakte.Id);

        // Löschen für alle entfernt auch den Anhang samt Datei.
        using var deleteForAll = await worker.DeleteAsync(new Uri($"/api/v1/chat/messages/{message.Id}", UriKind.Relative), Ct);
        deleteForAll.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleteForAll.Content.ReadAsStringAsync(Ct));
        File.Exists(stored).ShouldBeFalse();
        using var gone = await office.GetAsync(new Uri($"/api/v1/chat/attachments/{photo.Id}/content", UriKind.Relative), Ct);
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReplyReactEditPinForward_SearchFindsDecryptedText()
    {
        var (office, officeId) = await AsAsync("buero", StandardRoles.OfficeId);
        var (worker, workerId) = await AsAsync("baustelle", StandardRoles.SiteWorkerId);
        await EnableAsync(office);
        var conversation = await DirectAsync(worker, officeId);
        var first = await SendAsync(worker, conversation.Id, new { text = "Wann kommt der Estrichpumpe?" });
        var reply = await SendAsync(office, conversation.Id, new { text = "Morgen 7 Uhr", replyToId = first.Id });
        reply.ReplyTo!.Id.ShouldBe(first.Id);
        reply.ReplyTo.Text.ShouldBe("Wann kommt der Estrichpumpe?");
        reply.CanEdit.ShouldBeTrue();

        using var react = await worker.PutAsJsonAsync($"/api/v1/chat/messages/{reply.Id}/reaction", new { emoji = "👍" }, Ct);
        react.StatusCode.ShouldBe(HttpStatusCode.OK, await react.Content.ReadAsStringAsync(Ct));
        var reacted = (await react.Content.ReadFromJsonAsync<ChatMessageResponse>(TestJson.Options, Ct))!;
        reacted.Reactions!.Single().Emoji.ShouldBe("👍");
        reacted.Reactions!.Single().Mine.ShouldBeTrue();
        using var letters = await worker.PutAsJsonAsync($"/api/v1/chat/messages/{reply.Id}/reaction", new { emoji = "ok" }, Ct);
        letters.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        // Bearbeiten nur eigene, innerhalb des Zeitfensters.
        using var foreign = await worker.PutAsJsonAsync($"/api/v1/chat/messages/{reply.Id}", new { text = "Übermorgen" }, Ct);
        foreign.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var edit = await office.PutAsJsonAsync($"/api/v1/chat/messages/{reply.Id}", new { text = "Morgen 6:30 Uhr" }, Ct);
        edit.StatusCode.ShouldBe(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync(Ct));
        var edited = (await edit.Content.ReadFromJsonAsync<ChatMessageResponse>(TestJson.Options, Ct))!;
        edited.Text.ShouldBe("Morgen 6:30 Uhr");
        edited.EditedAt.ShouldNotBeNull();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
            await db.Database.ExecuteSqlAsync($"UPDATE chat_messages SET created_at = now() - interval '20 minutes' WHERE id = {reply.Id}", Ct);
        }

        using var late = await office.PutAsJsonAsync($"/api/v1/chat/messages/{reply.Id}", new { text = "zu spät" }, Ct);
        late.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await worker.PostAsJsonAsync($"/api/v1/chat/messages/{reply.Id}/pin", new { pinned = true }, Ct)).EnsureSuccessStatusCode();
        var detail = (await office.GetFromJsonAsync<ChatConversationDetail>($"/api/v1/chat/conversations/{conversation.Id}", TestJson.Options, Ct))!;
        detail.Pinned!.Single().Id.ShouldBe(reply.Id);

        // Weiterleiten in eine Gruppe.
        using var groupResponse = await office.PostAsJsonAsync("/api/v1/chat/conversations", new { title = "Disposition", memberIds = new[] { workerId } }, Ct);
        var group = (await groupResponse.Content.ReadFromJsonAsync<ChatConversationSummary>(TestJson.Options, Ct))!;
        using var forward = await office.PostAsJsonAsync($"/api/v1/chat/messages/{first.Id}/forward", new { conversationIds = new[] { group.Id } }, Ct);
        forward.StatusCode.ShouldBe(HttpStatusCode.OK, await forward.Content.ReadAsStringAsync(Ct));
        var forwarded = (await forward.Content.ReadFromJsonAsync<ForwardChatMessageResponse>(TestJson.Options, Ct))!.Messages.Single();
        forwarded.Forwarded.ShouldBeTrue();
        forwarded.ConversationId.ShouldBe(group.Id);
        forwarded.Text.ShouldBe("Wann kommt der Estrichpumpe?");

        var search = (await worker.GetFromJsonAsync<ChatSearchResponse>("/api/v1/chat/search?q=estrichpumpe", TestJson.Options, Ct))!;
        search.Items.Count.ShouldBe(2);
        search.Items.ShouldContain(h => h.ConversationTitle == "Disposition");
        var (stranger, _) = await AsAsync("fremd", StandardRoles.SiteWorkerId);
        (await stranger.GetFromJsonAsync<ChatSearchResponse>("/api/v1/chat/search?q=estrichpumpe", TestJson.Options, Ct))!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Notifications_PerMessage_ThreadedReplacingInbox_MentionBreaksMute_AnnouncementOnlyAdmins()
    {
        var (office, officeId) = await AsAsync("buero", StandardRoles.OfficeId);
        var (worker, workerId) = await AsAsync("baustelle", StandardRoles.SiteWorkerId);
        var (second, secondId) = await AsAsync("zweite", StandardRoles.SiteWorkerId);
        await EnableAsync(office, showPreview: false);

        using var create = await office.PostAsJsonAsync("/api/v1/chat/conversations",
            new { title = "Alle Mitarbeiter", memberIds = new[] { workerId, secondId }, announcementOnly = true, description = "Ankündigungen aus dem Büro" }, Ct);
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync(Ct));
        var group = (await create.Content.ReadFromJsonAsync<ChatConversationSummary>(TestJson.Options, Ct))!;
        group.IsAdmin.ShouldBeTrue();
        group.AnnouncementOnly.ShouldBeTrue();

        // Mitarbeiter dürfen in der Ankündigungsgruppe nicht schreiben.
        var workerView = (await worker.GetFromJsonAsync<List<ChatConversationSummary>>("/api/v1/chat/conversations", TestJson.Options, Ct))!.Single();
        workerView.CanPost.ShouldBeFalse();
        using var denied = await worker.PostAsJsonAsync($"/api/v1/chat/conversations/{group.Id}/messages", new { text = "Hallo" }, Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await denied.Content.ReadAsStringAsync(Ct)).ShouldContain("ANNOUNCEMENT_ONLY");

        // Stumm für 8 Stunden; @Erwähnung kommt trotzdem an.
        (await second.PostAsJsonAsync($"/api/v1/chat/conversations/{group.Id}/mute", new { muted = true, minutes = 480 }, Ct)).EnsureSuccessStatusCode();
        var secondView = (await second.GetFromJsonAsync<List<ChatConversationSummary>>("/api/v1/chat/conversations", TestJson.Options, Ct))!.Single();
        secondView.Muted.ShouldBeTrue();
        secondView.MutedUntil.ShouldNotBeNull();

        await SendAsync(office, group.Id, new { text = "Freitag Betriebsversammlung" });
        await SendAsync(office, group.Id, new { text = "@Zweite bitte Schlüssel mitbringen", mentionIds = new[] { secondId } });

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var workerRows = await db.Set<Notification>().AsNoTracking().Where(n => n.UserId == workerId).ToListAsync(Ct);
        workerRows.Count.ShouldBeInRange(1, 2); // je Nachricht ein Hinweis; schon versandte ersetzt der neueste
        workerRows.ShouldAllBe(n => n.Body == "Neue Nachricht im Chat");
        var secondRows = await db.Set<Notification>().AsNoTracking().Where(n => n.UserId == secondId).ToListAsync(Ct);
        secondRows.Single().Body.ShouldBe("Hat Sie erwähnt");
        secondRows.Single().Urgency.ShouldBe(NotificationUrgency.High);

        // Nach dem Versand ersetzt der nächste Hinweis die vorigen im Postfach.
        await db.Database.ExecuteSqlRawAsync("UPDATE notifications SET push_state = 'Sent'", Ct);
        await SendAsync(office, group.Id, new { text = "Dritte Nachricht" });
        (await db.Set<Notification>().CountAsync(n => n.UserId == workerId, Ct)).ShouldBe(1);

        // Push-Inhalt: thread-id je Unterhaltung, Kategorie für die Antwort-Aktion.
        var message = new PushMessage(Guid.CreateVersion7(), "Max Huber in Alle Mitarbeiter", null, $"/chat/{group.Id}", "chat", "normal", 1, "chat", group.Id);
        message.ThreadId.ShouldBe($"chat-{group.Id:N}");
        message.ActionCategory.ShouldBe("chat");
    }

    [Fact]
    public async Task GroupAdmins_ManageGroup_LeaveHandsOverAdmin_BlockedCannotWrite_ReadReceiptsOff()
    {
        var (office, _) = await AsAsync("buero", StandardRoles.OfficeId);
        var (worker, workerId) = await AsAsync("baustelle", StandardRoles.SiteWorkerId);
        var (second, secondId) = await AsAsync("zweite", StandardRoles.SiteWorkerId);
        await EnableAsync(office);
        using var create = await office.PostAsJsonAsync("/api/v1/chat/conversations",
            new { title = "Kolonne Süd", memberIds = new[] { workerId, secondId }, adminIds = new[] { workerId } }, Ct);
        var group = (await create.Content.ReadFromJsonAsync<ChatConversationSummary>(TestJson.Options, Ct))!;

        // Gruppenadmin (ohne chat.admin) darf umbenennen und beschreiben; normales Mitglied nicht.
        using var rename = await worker.PutAsJsonAsync($"/api/v1/chat/conversations/{group.Id}", new { title = "Kolonne Süd 2", description = "Bauabschnitt B" }, Ct);
        rename.StatusCode.ShouldBe(HttpStatusCode.OK, await rename.Content.ReadAsStringAsync(Ct));
        (await rename.Content.ReadFromJsonAsync<ChatConversationDetail>(TestJson.Options, Ct))!.Conversation.Description.ShouldBe("Bauabschnitt B");
        using var notAdmin = await second.PutAsJsonAsync($"/api/v1/chat/conversations/{group.Id}", new { title = "X" }, Ct);
        notAdmin.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Gruppenbild.
        using var imageBody = new ByteArrayContent(Jpeg(800, 600));
        imageBody.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        using var image = await worker.PutAsync(new Uri($"/api/v1/chat/conversations/{group.Id}/image", UriKind.Relative), imageBody, Ct);
        image.StatusCode.ShouldBe(HttpStatusCode.OK, await image.Content.ReadAsStringAsync(Ct));
        using var avatar = await second.GetAsync(new Uri($"/api/v1/chat/conversations/{group.Id}/image", UriKind.Relative), Ct);
        avatar.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");

        // Lesebestätigungen aus: andere sehen den Lesestand nicht.
        (await second.PutAsJsonAsync("/api/v1/chat/me/settings", new { readReceipts = false, privacyAccepted = true }, Ct)).EnsureSuccessStatusCode();
        var sent = await SendAsync(worker, group.Id, new { text = "Material ist da" });
        (await second.PostAsync(new Uri($"/api/v1/chat/conversations/{group.Id}/read", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();
        var page = (await worker.GetFromJsonAsync<ChatMessagePage>($"/api/v1/chat/conversations/{group.Id}/messages", TestJson.Options, Ct))!;
        page.Members.Single(m => m.UserId == secondId).ReadUpTo.ShouldBeNull();
        page.Items.Single(m => m.Id == sent.Id).ReadBy.ShouldBe(0);
        (await second.GetFromJsonAsync<ChatStatusResponse>("/api/v1/chat/status", TestJson.Options, Ct))!.PrivacyAccepted.ShouldBeTrue();

        // Sperre durch die Verwaltung.
        (await office.PutAsJsonAsync($"/api/v1/chat/admin/users/{secondId}/block", new { blocked = true }, Ct)).EnsureSuccessStatusCode();
        using var blocked = await second.PostAsJsonAsync($"/api/v1/chat/conversations/{group.Id}/messages", new { text = "Hallo" }, Ct);
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await blocked.Content.ReadAsStringAsync(Ct)).ShouldContain("CHAT_BLOCKED");

        // Büro (einziger weiterer Admin neben dem Mitarbeiter) verlässt – Admins bleiben.
        (await worker.PostAsync(new Uri($"/api/v1/chat/conversations/{group.Id}/leave", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();
        (await office.PostAsync(new Uri($"/api/v1/chat/conversations/{group.Id}/leave", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();
        var detail = (await second.GetFromJsonAsync<ChatConversationDetail>($"/api/v1/chat/conversations/{group.Id}", TestJson.Options, Ct))!;
        detail.Members.Single().IsAdmin.ShouldBeTrue();
    }

    [Fact]
    public async Task BauakteGroup_TeamJoinsAutomatically_LinkForBauakte()
    {
        var (office, _) = await AsAsync("buero", StandardRoles.OfficeId);
        var (worker, workerId) = await AsAsync("baustelle", StandardRoles.SiteWorkerId);
        await EnableAsync(office);
        using var created = await worker.PostAsJsonAsync("/api/v1/bauakten",
            new { id = Guid.CreateVersion7(), title = "EFH Berger", street = "Hauptstr. 3", postalCode = "84028", city = "Landshut" }, Ct);
        var bauakte = (await created.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;

        var before = (await worker.GetFromJsonAsync<ChatBauakteGroup>($"/api/v1/chat/bauakten/{bauakte.Id}/group", TestJson.Options, Ct))!;
        before.ConversationId.ShouldBeNull();
        before.CanCreate.ShouldBeTrue();

        using var create = await office.PostAsync(new Uri($"/api/v1/chat/bauakten/{bauakte.Id}/group", UriKind.Relative), null, Ct);
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync(Ct));
        var group = (await create.Content.ReadFromJsonAsync<ChatConversationSummary>(TestJson.Options, Ct))!;
        group.BauakteId.ShouldBe(bauakte.Id);
        group.Members.ShouldContain(m => m.Id == workerId);
        group.Description!.ShouldContain("Landshut");

        var link = (await worker.GetFromJsonAsync<ChatBauakteGroup>($"/api/v1/chat/bauakten/{bauakte.Id}/group", TestJson.Options, Ct))!;
        link.ConversationId.ShouldBe(group.Id);
        link.Member.ShouldBeTrue();
        var detail = (await worker.GetFromJsonAsync<ChatConversationDetail>($"/api/v1/chat/conversations/{group.Id}", TestJson.Options, Ct))!;
        detail.BauakteTitle.ShouldNotBeNull();

        // Automatische Mitglieder verlassen nicht (Stummschalten stattdessen).
        using var leave = await worker.PostAsync(new Uri($"/api/v1/chat/conversations/{group.Id}/leave", UriKind.Relative), null, Ct);
        leave.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Realtime_ServerSentEvents_DeliverMessageAndTypingEvents()
    {
        var (office, officeId) = await AsAsync("buero", StandardRoles.OfficeId);
        var (worker, _) = await AsAsync("baustelle", StandardRoles.SiteWorkerId);
        await EnableAsync(office);
        var conversation = await DirectAsync(worker, officeId);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/chat/events");
        using var stream = await office.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Ct);
        stream.StatusCode.ShouldBe(HttpStatusCode.OK);
        stream.Content.Headers.ContentType!.MediaType.ShouldBe("text/event-stream");
        stream.Headers.GetValues("X-Accel-Buffering").Single().ShouldBe("no");
        using var reader = new StreamReader(await stream.Content.ReadAsStreamAsync(Ct));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        async Task<string> NextEventAsync()
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(timeout.Token) ?? throw new InvalidOperationException("Strom beendet");
                if (line.StartsWith("event: ", StringComparison.Ordinal))
                {
                    var data = await reader.ReadLineAsync(timeout.Token);
                    return line[7..] + " " + data;
                }
            }
        }

        (await NextEventAsync()).ShouldStartWith("ready");
        (await worker.PostAsync(new Uri($"/api/v1/chat/conversations/{conversation.Id}/typing", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();
        (await NextEventAsync()).ShouldStartWith("typing");
        var sent = await SendAsync(worker, conversation.Id, new { text = "Bin gleich da" });
        var message = await NextEventAsync();
        message.ShouldStartWith("message");
        message.ShouldContain(sent.Id.ToString());
        message.ShouldNotContain("Bin gleich da"); // nur IDs im Strom
    }
}
