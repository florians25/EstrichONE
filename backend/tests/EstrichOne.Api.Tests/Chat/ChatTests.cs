using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Chat;
using EstrichOne.Api.Modules.Notifications;
using EstrichOne.Api.Modules.Notifications.Exports;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EstrichOne.Api.Tests.Chat;

/// <summary>Chat: Modulschalter, Einzel- und Gruppenchats, Verschlüsselung, Zustell-/Lesestand, Hinweise, Betroffenenrechte, Löschfrist.</summary>
public sealed class ChatTests(PostgresFixture postgres) : IAsyncLifetime
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

    private static async Task EnableAsync(HttpClient admin, bool showPreview = false)
    {
        using var response = await admin.PutAsJsonAsync("/api/v1/chat/admin/settings", new { enabled = true, retentionDays = 90, showPreview }, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task DisabledByDefault_EndpointsAnswerModuleDisabled_StatusTellsClients()
    {
        var (office, _) = await AsAsync("buero", StandardRoles.OfficeId);
        var status = (await office.GetFromJsonAsync<ChatStatusResponse>("/api/v1/chat/status", TestJson.Options, Ct))!;
        status.Enabled.ShouldBeFalse();
        status.CanUse.ShouldBeFalse();
        status.CanAdmin.ShouldBeTrue();

        using var list = await office.GetAsync(new Uri("/api/v1/chat/conversations", UriKind.Relative), Ct);
        list.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await list.Content.ReadAsStringAsync(Ct)).ShouldContain("MODULE_DISABLED");

        var (worker, _) = await AsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var settings = await worker.GetAsync(new Uri("/api/v1/chat/admin/settings", UriKind.Relative), Ct);
        settings.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DirectChat_EncryptedAtRest_DeliveredAndRead_NotificationWithoutContent()
    {
        var (office, officeId) = await AsAsync("buero", StandardRoles.OfficeId);
        var (worker, workerId) = await AsAsync("baustelle", StandardRoles.SiteWorkerId);
        await EnableAsync(office);

        var contacts = (await worker.GetFromJsonAsync<List<ChatPerson>>("/api/v1/chat/contacts", TestJson.Options, Ct))!;
        contacts.ShouldContain(p => p.Id == officeId);

        using var start = await worker.PostAsJsonAsync("/api/v1/chat/conversations/direct", new { userId = officeId }, Ct);
        start.StatusCode.ShouldBe(HttpStatusCode.OK, await start.Content.ReadAsStringAsync(Ct));
        var conversation = (await start.Content.ReadFromJsonAsync<ChatConversationSummary>(TestJson.Options, Ct))!;
        conversation.Kind.ShouldBe(ChatConversationKind.Direct);

        // Derselbe Einzelchat von der anderen Seite.
        using var again = await office.PostAsJsonAsync("/api/v1/chat/conversations/direct", new { userId = workerId }, Ct);
        (await again.Content.ReadFromJsonAsync<ChatConversationSummary>(TestJson.Options, Ct))!.Id.ShouldBe(conversation.Id);

        var clientId = Guid.CreateVersion7();
        using var send = await worker.PostAsJsonAsync($"/api/v1/chat/conversations/{conversation.Id}/messages",
            new { text = "Estrich in Halle 2 ist fertig, Heizestrich trocknet.", clientMessageId = clientId }, Ct);
        send.StatusCode.ShouldBe(HttpStatusCode.Created, await send.Content.ReadAsStringAsync(Ct));
        var sent = (await send.Content.ReadFromJsonAsync<ChatMessageResponse>(TestJson.Options, Ct))!;
        sent.Status.ShouldBe("sent");

        // Wiederholtes Senden aus der Offline-Warteschlange: keine Doppelung.
        using var repeat = await worker.PostAsJsonAsync($"/api/v1/chat/conversations/{conversation.Id}/messages",
            new { text = "Estrich in Halle 2 ist fertig, Heizestrich trocknet.", clientMessageId = clientId }, Ct);
        repeat.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await repeat.Content.ReadFromJsonAsync<ChatMessageResponse>(TestJson.Options, Ct))!.Id.ShouldBe(sent.Id);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
            var row = await db.Set<ChatMessage>().SingleAsync(Ct);
            row.ProtectedText.ShouldNotBeNull();
            row.ProtectedText.ShouldNotContain("Halle");
            var notification = await db.Set<Notification>().SingleAsync(n => n.UserId == officeId, Ct);
            notification.Category.ShouldBe(NotificationCategory.Chat);
            notification.Title.ShouldBe("Nachricht von Max Huber");
            (notification.Body ?? string.Empty).ShouldNotContain("Halle");
            notification.Link.ShouldBe($"/chat/{conversation.Id}");
        }

        var status = (await office.GetFromJsonAsync<ChatStatusResponse>("/api/v1/chat/status", TestJson.Options, Ct))!;
        status.UnreadCount.ShouldBe(1);
        var list = (await office.GetFromJsonAsync<List<ChatConversationSummary>>("/api/v1/chat/conversations", TestJson.Options, Ct))!;
        list.Single().UnreadCount.ShouldBe(1);
        list.Single().LastMessage!.Text.ShouldBe("Estrich in Halle 2 ist fertig, Heizestrich trocknet.");

        var mine = (await worker.GetFromJsonAsync<ChatMessagePage>($"/api/v1/chat/conversations/{conversation.Id}/messages", TestJson.Options, Ct))!;
        mine.Items.Single().Status.ShouldBe("delivered");

        (await office.PostAsync(new Uri($"/api/v1/chat/conversations/{conversation.Id}/read", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();
        mine = (await worker.GetFromJsonAsync<ChatMessagePage>($"/api/v1/chat/conversations/{conversation.Id}/messages", TestJson.Options, Ct))!;
        mine.Items.Single().Status.ShouldBe("read");
        (await office.GetFromJsonAsync<UnreadCountResponse>("/api/v1/notifications/unread-count", TestJson.Options, Ct))!.Count.ShouldBe(0);

        // Fremde sehen die Unterhaltung nicht.
        var (other, _) = await AsAsync("andere", StandardRoles.SiteWorkerId);
        using var foreign = await other.GetAsync(new Uri($"/api/v1/chat/conversations/{conversation.Id}/messages", UriKind.Relative), Ct);
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Eigene Nachricht löschen: Text weg, Platzhalter bleibt.
        (await worker.DeleteAsync(new Uri($"/api/v1/chat/messages/{sent.Id}", UriKind.Relative), Ct)).EnsureSuccessStatusCode();
        var after = (await office.GetFromJsonAsync<ChatMessagePage>($"/api/v1/chat/conversations/{conversation.Id}/messages", TestJson.Options, Ct))!;
        after.Items.Single().Deleted.ShouldBeTrue();
        after.Items.Single().Text.ShouldBeNull();
    }

    [Fact]
    public async Task Groups_OnlyChatAdmin_MembersSeeMessages_MutedGetNoNotification()
    {
        var (office, _) = await AsAsync("buero", StandardRoles.OfficeId);
        var (worker, workerId) = await AsAsync("baustelle", StandardRoles.SiteWorkerId);
        var (second, secondId) = await AsAsync("zweite", StandardRoles.SiteWorkerId);
        await EnableAsync(office);

        using var denied = await worker.PostAsJsonAsync("/api/v1/chat/conversations", new { title = "Kolonne 1", memberIds = new[] { secondId } }, Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var create = await office.PostAsJsonAsync("/api/v1/chat/conversations",
            new { title = "Kolonne Nord", memberIds = new[] { workerId, secondId } }, Ct);
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync(Ct));
        var group = (await create.Content.ReadFromJsonAsync<ChatConversationSummary>(TestJson.Options, Ct))!;
        group.Members.Count.ShouldBe(3);

        (await second.PostAsJsonAsync($"/api/v1/chat/conversations/{group.Id}/mute", new { muted = true }, Ct)).EnsureSuccessStatusCode();
        (await office.PostAsJsonAsync($"/api/v1/chat/conversations/{group.Id}/messages", new { text = "Morgen 6:30 am Lager." }, Ct)).EnsureSuccessStatusCode();

        var page = (await worker.GetFromJsonAsync<ChatMessagePage>($"/api/v1/chat/conversations/{group.Id}/messages", TestJson.Options, Ct))!;
        page.Items.Single().Text.ShouldBe("Morgen 6:30 am Lager.");
        page.Members.Count.ShouldBe(3);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        (await db.Set<Notification>().CountAsync(n => n.UserId == workerId && n.Category == NotificationCategory.Chat, Ct)).ShouldBe(1);
        (await db.Set<Notification>().CountAsync(n => n.UserId == secondId && n.Category == NotificationCategory.Chat, Ct)).ShouldBe(0);
        (await db.Set<Notification>().SingleAsync(n => n.UserId == workerId, Ct)).Title.ShouldBe("Max Huber in Kolonne Nord");
    }

    [Fact]
    public async Task DataSubjectRights_ExportAndErase_RetentionDeletesOldMessages()
    {
        var (office, officeId) = await AsAsync("buero", StandardRoles.OfficeId);
        var (worker, workerId) = await AsAsync("baustelle", StandardRoles.SiteWorkerId);
        await EnableAsync(office);
        using var start = await worker.PostAsJsonAsync("/api/v1/chat/conversations/direct", new { userId = officeId }, Ct);
        var conversation = (await start.Content.ReadFromJsonAsync<ChatConversationSummary>(TestJson.Options, Ct))!;
        (await worker.PostAsJsonAsync($"/api/v1/chat/conversations/{conversation.Id}/messages", new { text = "Bin krank, komme morgen nicht." }, Ct))
            .EnsureSuccessStatusCode();

        var export = (await worker.GetFromJsonAsync<ChatExport>("/api/v1/chat/export", TestJson.Options, Ct))!;
        export.Messages.Single().Text.ShouldBe("Bin krank, komme morgen nicht.");
        export.Messages.Single().Mine.ShouldBeTrue();
        var adminExport = (await office.GetFromJsonAsync<ChatExport>($"/api/v1/chat/admin/users/{workerId}/export", TestJson.Options, Ct))!;
        adminExport.Messages.Count.ShouldBe(1);

        using var erase = await office.DeleteAsync(new Uri($"/api/v1/chat/admin/users/{workerId}/messages", UriKind.Relative), Ct);
        erase.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await erase.Content.ReadFromJsonAsync<ChatErasureResponse>(TestJson.Options, Ct))!.DeletedMessages.ShouldBe(1);
        (await office.GetFromJsonAsync<List<ChatConversationSummary>>("/api/v1/chat/conversations", TestJson.Options, Ct))!.ShouldBeEmpty();

        // Löschfrist.
        using var start2 = await office.PostAsJsonAsync("/api/v1/chat/conversations/direct", new { userId = workerId }, Ct);
        var c2 = (await start2.Content.ReadFromJsonAsync<ChatConversationSummary>(TestJson.Options, Ct))!;
        (await office.PostAsJsonAsync($"/api/v1/chat/conversations/{c2.Id}/messages", new { text = "Alt" }, Ct)).EnsureSuccessStatusCode();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
            await db.Database.ExecuteSqlRawAsync("UPDATE chat_messages SET created_at = now() - interval '91 days'", Ct);
        }

        var retention = _factory.Services.GetServices<IHostedService>().OfType<ChatRetention>().Single();
        (await retention.CleanupAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Preferences_UnsubscribeEvent_MandatoryCannotBeTurnedOff()
    {
        var (office, officeId) = await AsAsync("buero", StandardRoles.OfficeId);
        var (worker, _) = await AsAsync("baustelle", StandardRoles.SiteWorkerId);
        await EnableAsync(office);

        var prefs = (await office.GetFromJsonAsync<NotificationPreferencesResponse>("/api/v1/notifications/preferences", TestJson.Options, Ct))!;
        prefs.Items.ShouldContain(i => i.Event == NotificationEvents.ChatMessage && i.Delivery == NotificationDelivery.Push);
        prefs.Items.ShouldNotContain(i => i.AdminOnly); // Büro ohne Backup-/Einstellungsrecht

        using var bad = await office.PutAsJsonAsync("/api/v1/notifications/preferences",
            new { items = new[] { new { @event = NotificationEvents.OfficeMessage, delivery = "off" } } }, Ct);
        bad.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        using var save = await office.PutAsJsonAsync("/api/v1/notifications/preferences",
            new { items = new[] { new { @event = NotificationEvents.ChatMessage, delivery = "off" } } }, Ct);
        save.StatusCode.ShouldBe(HttpStatusCode.OK, await save.Content.ReadAsStringAsync(Ct));

        using var start = await worker.PostAsJsonAsync("/api/v1/chat/conversations/direct", new { userId = officeId }, Ct);
        var conversation = (await start.Content.ReadFromJsonAsync<ChatConversationSummary>(TestJson.Options, Ct))!;
        (await worker.PostAsJsonAsync($"/api/v1/chat/conversations/{conversation.Id}/messages", new { text = "Hallo" }, Ct)).EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        (await db.Set<Notification>().CountAsync(n => n.UserId == officeId, Ct)).ShouldBe(0);

        // Nur Postfach: Eintrag ja, Push nein.
        using var inbox = await office.PutAsJsonAsync("/api/v1/notifications/preferences",
            new { items = new[] { new { @event = NotificationEvents.ChatMessage, delivery = "inbox" } } }, Ct);
        inbox.EnsureSuccessStatusCode();
        await db.Database.ExecuteSqlRawAsync("UPDATE chat_members SET read_up_to = now()", Ct);
        (await worker.PostAsJsonAsync($"/api/v1/chat/conversations/{conversation.Id}/messages", new { text = "Noch da?" }, Ct)).EnsureSuccessStatusCode();
        var row = await db.Set<Notification>().AsNoTracking().SingleAsync(n => n.UserId == officeId, Ct);
        row.PushState.ShouldBe(PushState.InAppOnly);
    }
}
