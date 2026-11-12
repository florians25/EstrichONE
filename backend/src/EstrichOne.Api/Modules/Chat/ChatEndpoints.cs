using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Notifications.Exports;
using EstrichOne.Api.Modules.Teams.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Chat;

/// <summary>Was Clients zum Anzeigen brauchen (für alle Angemeldeten, auch bei abgeschaltetem Modul).</summary>
/// <param name="PrivacyAccepted">Datenschutzhinweis bestätigt (sonst beim ersten Öffnen zeigen, <c>PUT /chat/me/settings</c>).</param>
/// <param name="Blocked">Von der Verwaltung gesperrt: lesen ja, schreiben nein.</param>
/// <param name="Realtime">Echtzeitkanal: <c>sse</c> = <c>GET /chat/events</c>; Polling bleibt als Rückfall.</param>
/// <param name="Mode">Chat-Modus (Einstellungen › Integrationen › Microsoft Teams).</param>
/// <param name="ReadOnly">Modus „Microsoft Teams“: der Chat ist ein Archiv – lesen und exportieren, nicht schreiben.</param>
public sealed record ChatStatusResponse(bool Enabled, bool CanUse, bool CanAdmin, int UnreadCount, int UnreadConversations, int RetentionDays, bool ShowPreview,
    bool PrivacyAccepted = false, bool Blocked = false, int EditWindowMinutes = ChatSettingsRow.DefaultEditWindowMinutes, bool AttachmentsEnabled = true,
    long MaxAttachmentBytes = ChatLimits.MaxAttachmentBytes, bool ReadReceipts = true, bool PersonalShowPreview = true, string Realtime = "sse",
    int PrivacyNoticeVersion = ChatLimits.PrivacyNoticeVersion, TeamsChatMode Mode = TeamsChatMode.EstrichOne, bool ReadOnly = false);

/// <summary>Grenzen für Clients.</summary>
public static class ChatLimits
{
    public const long MaxAttachmentBytes = 25L * 1024 * 1024;
    public const int MaxAttachmentsPerMessage = 10;

    /// <summary>Fassung des Datenschutzhinweises (steigt bei inhaltlicher Änderung).</summary>
    public const int PrivacyNoticeVersion = 1;
}

public sealed record ChatLocation([property: Range(-90, 90)] double Latitude, [property: Range(-180, 180)] double Longitude,
    [property: MaxLength(300)] string? Label = null);

public sealed record ChatAttachmentResponse(
    Guid Id, ChatAttachmentKind Kind, string FileName, string ContentType, long SizeBytes, int? Width, int? Height, bool HasThumbnail,
    DateTimeOffset CreatedAt, Guid? MessageId, Guid UploadedByUserId);

/// <summary>Zitat der beantworteten Nachricht (gekürzt).</summary>
public sealed record ChatReplyPreview(Guid Id, Guid SenderUserId, string SenderName, string? Text, bool Deleted, ChatAttachmentKind? Attachment);

public sealed record ChatReactionGroup(string Emoji, int Count, bool Mine, IReadOnlyList<string> Names);

public sealed record ChatPerson(Guid Id, string Name);

public sealed record ChatLastMessage(Guid Id, Guid SenderUserId, string SenderName, string? Text, DateTimeOffset CreatedAt, bool Mine, bool Deleted,
    ChatAttachmentKind? Attachment = null, bool Location = false);

/// <param name="Title">Gruppenname bzw. bei Einzelchats der Name der anderen Person.</param>
public sealed record ChatConversationSummary(
    Guid Id, ChatConversationKind Kind, string Title, Guid? CrewId, IReadOnlyList<ChatPerson> Members, ChatLastMessage? LastMessage,
    DateTimeOffset LastActivityAt, int UnreadCount, bool Muted, Guid? PartnerUserId, string? Description = null, Guid? BauakteId = null,
    bool AnnouncementOnly = false, DateTimeOffset? ImageUpdatedAt = null, DateTimeOffset? MutedUntil = null, bool IsAdmin = false, bool CanPost = true,
    int UnreadMentions = 0);

/// <param name="ReadUpTo"><c>null</c> auch, wenn die Person Lesebestätigungen abgeschaltet hat.</param>
/// <param name="FromCrew">Automatisch Mitglied (Kolonne bzw. Team der Bauakte).</param>
public sealed record ChatMemberState(Guid UserId, string Name, DateTimeOffset? DeliveredUpTo, DateTimeOffset? ReadUpTo, bool FromCrew, bool IsAdmin = false);

public sealed record ChatConversationDetail(ChatConversationSummary Conversation, IReadOnlyList<ChatMemberState> Members, bool CanManage,
    IReadOnlyList<ChatMessageResponse>? Pinned = null, string? BauakteTitle = null);

/// <param name="Status">Nur eigene Nachrichten: <c>sent</c> (beim Server), <c>delivered</c> (bei allen abgeholt), <c>read</c> (von allen gelesen).</param>
/// <param name="ReadBy">Eigene Nachrichten: Anzahl Empfänger, die gelesen haben.</param>
public sealed record ChatMessageResponse(
    Guid Id, Guid ConversationId, Guid SenderUserId, string SenderName, string? Text, DateTimeOffset CreatedAt, bool Mine, bool Deleted,
    string? Status, int ReadBy, int Recipients, Guid? ClientMessageId, ChatReplyPreview? ReplyTo = null,
    IReadOnlyList<ChatAttachmentResponse>? Attachments = null, IReadOnlyList<ChatReactionGroup>? Reactions = null, IReadOnlyList<Guid>? MentionedUserIds = null,
    DateTimeOffset? EditedAt = null, bool Forwarded = false, DateTimeOffset? PinnedAt = null, ChatLocation? Location = null, bool CanEdit = false);

public sealed record ChatMessagePage(IReadOnlyList<ChatMessageResponse> Items, bool HasMore, IReadOnlyList<ChatMemberState> Members);

public sealed record StartDirectChatRequest([property: Required] Guid UserId);

/// <param name="AnnouncementOnly">Ankündigungsgruppe: nur Gruppenadmins schreiben.</param>
/// <param name="AdminIds">Weitere Gruppenadmins (die anlegende Person ist immer Admin).</param>
public sealed record CreateGroupChatRequest(
    [property: Required, MaxLength(ChatConversation.MaxTitle)] string Title, IReadOnlyList<Guid>? MemberIds = null, Guid? CrewId = null,
    [property: MaxLength(ChatConversation.MaxDescription)] string? Description = null, bool AnnouncementOnly = false, IReadOnlyList<Guid>? AdminIds = null);

/// <param name="AdminIds">Vollständige Liste der Gruppenadmins (nur Mitglieder; mindestens einer bleibt).</param>
public sealed record UpdateGroupChatRequest([property: MaxLength(ChatConversation.MaxTitle)] string? Title = null, IReadOnlyList<Guid>? MemberIds = null,
    [property: MaxLength(ChatConversation.MaxDescription)] string? Description = null, IReadOnlyList<Guid>? AdminIds = null, bool? AnnouncementOnly = null);

/// <param name="ClientMessageId">ID aus der Offline-Warteschlange der App: wiederholtes Senden erzeugt keine Doppelung.</param>
/// <param name="AttachmentIds">Vorher mit <c>POST …/attachments</c> hochgeladene Anhänge (höchstens 10).</param>
/// <param name="MentionIds">@Erwähnte Mitglieder (erhalten auch bei Stummschaltung einen Hinweis).</param>
/// <param name="AttachmentFileIds">Gleichbedeutend mit <paramref name="AttachmentIds"/> (Schreibweise der App).</param>
/// <param name="MentionUserIds">Gleichbedeutend mit <paramref name="MentionIds"/>.</param>
public sealed record SendChatMessageRequest([property: MaxLength(ChatMessage.MaxText)] string? Text = null, Guid? ClientMessageId = null,
    Guid? ReplyToId = null, IReadOnlyList<Guid>? AttachmentIds = null, ChatLocation? Location = null, IReadOnlyList<Guid>? MentionIds = null,
    IReadOnlyList<Guid>? AttachmentFileIds = null, IReadOnlyList<Guid>? MentionUserIds = null);

/// <param name="Minutes">Dauer der Stummschaltung (z. B. 60, 480, 10080); ohne Angabe: immer.</param>
/// <param name="Until">Alternativ: stumm bis zu diesem Zeitpunkt.</param>
public sealed record MuteChatRequest(bool Muted, [property: Range(1, 525_600)] int? Minutes = null, DateTimeOffset? Until = null);

public sealed record ChatSettingsResponse(
    bool Enabled, int RetentionDays, bool ShowPreview, bool WorkersCanChatEachOther, DateTimeOffset? LastCleanupAt, int Conversations, int Messages, long Version,
    int EditWindowMinutes = ChatSettingsRow.DefaultEditWindowMinutes, bool AttachmentsEnabled = true, int Attachments = 0, long AttachmentBytes = 0);

public sealed record SaveChatSettingsRequest(
    bool Enabled, [property: Range(7, 730)] int RetentionDays = ChatSettingsRow.DefaultRetentionDays, bool ShowPreview = false,
    bool WorkersCanChatEachOther = true, long? Version = null, [property: Range(0, 1440)] int EditWindowMinutes = ChatSettingsRow.DefaultEditWindowMinutes,
    bool AttachmentsEnabled = true);

public sealed record ChatExportMessage(Guid Id, Guid ConversationId, string Conversation, string SenderName, bool Mine, DateTimeOffset CreatedAt, string? Text,
    IReadOnlyList<string>? Attachments = null);

/// <summary>Auskunft nach Art. 15/20 DSGVO: Unterhaltungen der Person und alle Nachrichten darin, die sie gesendet oder empfangen hat.</summary>
public sealed record ChatExport(Guid UserId, string UserName, DateTimeOffset ExportedAt, int RetentionDays, IReadOnlyList<ChatExportMessage> Messages);

public sealed record ChatErasureResponse(int DeletedMessages, int RemovedMemberships);

/// <summary>Mini-Chat Büro ↔ Mitarbeiter (nur Text). Siehe docs/chat.md.</summary>
internal static partial class ChatEndpoints
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static void Map(IEndpointRouteBuilder api)
    {
        var root = api.MapGroup("/chat").WithTags("Chat");
        root.MapGet("/status", StatusAsync).WithName("GetChatStatus").WithSummary("Modul aktiv? Rechte, ungelesene Nachrichten (Badge)");

        var chat = root.MapGroup(string.Empty).RequirePermission(Permissions.ChatUse);
        chat.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var settings = await http.RequestServices.GetRequiredService<ChatService>().SettingsAsync(http.RequestAborted);
            if (!settings.Enabled)
            {
                return Disabled();
            }

            // Modus „Microsoft Teams“: Verläufe bleiben lesbar und exportierbar, Schreiben geht nur noch in Teams.
            if (!HttpMethods.IsGet(http.Request.Method) && !ArchiveAllowed.Contains(http.GetEndpoint()?.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName ?? string.Empty)
                && await http.RequestServices.GetRequiredService<ITeamsIntegration>().ChatModeAsync(http.RequestAborted) == TeamsChatMode.Teams)
            {
                return ReadOnly();
            }

            return await next(context);
        });
        chat.MapGet("/contacts", ContactsAsync).WithName("ListChatContacts").WithSummary("Personen, mit denen ein Einzelchat möglich ist");
        chat.MapGet("/conversations", ConversationsAsync).WithName("ListChatConversations")
            .WithSummary("Eigene Unterhaltungen, zuletzt aktive zuerst (markiert alles Abgeholte als zugestellt)");
        chat.MapPost("/conversations/direct", StartDirectAsync).WithName("StartDirectChat").WithSummary("Einzelchat öffnen (vorhandenen oder neuen)")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        chat.MapPost("/conversations", CreateGroupAsync).WithName("CreateGroupChat").WithSummary("Gruppe anlegen (frei oder für eine Kolonne)")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.ChatAdmin);
        chat.MapGet("/conversations/{id:guid}", ConversationAsync).WithName("GetChatConversation").ProducesProblem(StatusCodes.Status404NotFound);
        chat.MapPut("/conversations/{id:guid}", UpdateGroupAsync).WithName("UpdateGroupChat").WithSummary("Gruppe umbenennen, Mitglieder ändern")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        chat.MapDelete("/conversations/{id:guid}", DeleteConversationAsync).WithName("DeleteChatConversation")
            .WithSummary("Unterhaltung mit allen Nachrichten löschen (chat.admin oder Gruppenadmin)").ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        chat.MapPost("/conversations/{id:guid}/mute", MuteAsync).WithName("MuteChatConversation").ProducesProblem(StatusCodes.Status404NotFound);
        chat.MapGet("/conversations/{id:guid}/messages", MessagesAsync).WithName("ListChatMessages")
            .WithSummary("Verlauf (aufsteigend); before= für ältere, after= für neue seit dem letzten Abruf").ProducesProblem(StatusCodes.Status404NotFound);
        chat.MapPost("/conversations/{id:guid}/messages", SendAsync).WithName("SendChatMessage")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        chat.MapPost("/conversations/{id:guid}/read", ReadAsync).WithName("MarkChatRead").ProducesProblem(StatusCodes.Status404NotFound);
        chat.MapDelete("/messages/{id:guid}", DeleteMessageAsync).WithName("DeleteChatMessage")
            .WithSummary("Eigene Nachricht löschen (chat.admin: jede)").ProducesProblem(StatusCodes.Status404NotFound);
        chat.MapGet("/export", OwnExportAsync).WithName("ExportOwnChatData").WithSummary("Eigene Chatdaten als JSON (Auskunft, Datenübertragbarkeit)");
        chat.MapGet("/crews", CrewsAsync).WithName("ListChatCrews").WithSummary("Kolonnen für Gruppen").RequirePermission(Permissions.ChatAdmin);
        MapFeatures(chat);

        // Verwaltung: auch bei abgeschaltetem Modul (Einschalten, Betroffenenrechte für vorhandene Daten).
        var admin = root.MapGroup("/admin").RequirePermission(Permissions.ChatAdmin);
        admin.MapGet("/settings", SettingsAsync).WithName("GetChatSettings");
        admin.MapPut("/settings", SaveSettingsAsync).WithName("SaveChatSettings")
            .ProducesProblem(StatusCodes.Status412PreconditionFailed).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        admin.MapGet("/users/{userId:guid}/export", UserExportAsync).WithName("ExportUserChatData")
            .WithSummary("Chatdaten einer Person (Auskunftsersuchen)").ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapDelete("/users/{userId:guid}/messages", EraseUserAsync).WithName("EraseUserChatData")
            .WithSummary("Alle Nachrichten einer Person löschen und sie aus Unterhaltungen entfernen (Recht auf Löschung)")
            .ProducesProblem(StatusCodes.Status404NotFound);
        MapAdminFeatures(admin);
    }

    private static async Task<Ok<ChatStatusResponse>> StatusAsync(
        ChatService chat, EstrichOneDbContext db, IUserPermissions permissions, ICurrentUser user, ITeamsIntegration teams, CancellationToken cancellationToken)
    {
        var settings = await chat.SettingsAsync(cancellationToken);
        var mode = await teams.ChatModeAsync(cancellationToken);
        var granted = await permissions.GetAsync(cancellationToken);
        var canUse = granted.Contains(Permissions.ChatUse) || granted.Contains(Permissions.ChatAdmin);
        var (count, conversations) = settings.Enabled && canUse ? await UnreadAsync(db, user.UserId, cancellationToken) : (0, 0);
        var personal = await chat.UserSettingsAsync(user.UserId, cancellationToken);
        return TypedResults.Ok(new ChatStatusResponse(settings.Enabled, canUse && settings.Enabled, granted.Contains(Permissions.ChatAdmin), count, conversations,
            settings.RetentionDays, settings.ShowPreview, personal?.PrivacyAcceptedAt is not null, personal?.BlockedAt is not null, settings.EditWindowMinutes,
            settings.AttachmentsEnabled, ChatLimits.MaxAttachmentBytes, personal?.ReadReceipts ?? true, personal?.ShowPreview ?? true, Mode: mode,
            ReadOnly: mode == TeamsChatMode.Teams));
    }

    private static async Task<(int Messages, int Conversations)> UnreadAsync(EstrichOneDbContext db, Guid userId, CancellationToken cancellationToken)
    {
        var perConversation = await db.Set<ChatMember>().Where(m => m.UserId == userId)
            .Select(m => db.Set<ChatMessage>().Count(x => x.ConversationId == m.ConversationId && x.SenderUserId != userId && x.DeletedAt == null
                && (m.ReadUpTo == null || x.CreatedAt > m.ReadUpTo)))
            .ToListAsync(cancellationToken);
        return (perConversation.Sum(), perConversation.Count(c => c > 0));
    }

    private static async Task<Ok<List<ChatPerson>>> ContactsAsync(
        ChatService chat, IRoleAssignments roles, IUserPermissions permissions, ICurrentUser user, CancellationToken cancellationToken)
    {
        var users = await chat.ChatUsersAsync(cancellationToken);
        var settings = await chat.SettingsAsync(cancellationToken);
        if (!settings.WorkersCanChatEachOther && !await ChatService.IsAdminAsync(permissions, cancellationToken))
        {
            users = [.. await roles.GetUserIdsWithPermissionAsync(Permissions.ChatAdmin, cancellationToken)];
        }

        users.Remove(user.UserId);
        var names = await chat.NamesAsync(users, cancellationToken);
        return TypedResults.Ok(names.OrderBy(n => n.Value, StringComparer.CurrentCulture).Select(n => new ChatPerson(n.Key, n.Value)).ToList());
    }

    private static async Task<Ok<List<ChatConversationSummary>>> ConversationsAsync(
        ChatService chat, EstrichOneDbContext db, ICurrentUser user, TimeProvider clock, ITeamsIntegration teams, CancellationToken cancellationToken)
    {
        // Im Archiv (Modus „Microsoft Teams“) bleiben Gruppen, wie sie sind.
        if (await teams.ChatModeAsync(cancellationToken) != TeamsChatMode.Teams)
        {
            await chat.SyncCrewGroupsAsync(cancellationToken);
        }

        var mine = await db.Set<ChatMember>().Where(m => m.UserId == user.UserId).ToListAsync(cancellationToken);
        var ids = mine.Select(m => m.ConversationId).ToList();
        var conversations = await db.Set<ChatConversation>().AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken);
        var summaries = await SummariesAsync(chat, db, user.UserId, conversations, mine, cancellationToken);

        // Abgeholt = zugestellt (Häkchen beim Absender).
        var now = clock.GetUtcNow();
        foreach (var member in mine)
        {
            member.Delivered(now);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(summaries.OrderByDescending(s => s.LastActivityAt).ToList());
    }

    private static async Task<List<ChatConversationSummary>> SummariesAsync(
        ChatService chat, EstrichOneDbContext db, Guid userId, IReadOnlyList<ChatConversation> conversations, IReadOnlyList<ChatMember> mine,
        CancellationToken cancellationToken)
    {
        var ids = conversations.Select(c => c.Id).ToList();
        var members = await db.Set<ChatMember>().AsNoTracking().Where(m => ids.Contains(m.ConversationId)).ToListAsync(cancellationToken);
        var last = await db.Set<ChatMessage>().AsNoTracking().Where(m => ids.Contains(m.ConversationId))
            .GroupBy(m => m.ConversationId).Select(g => g.OrderByDescending(m => m.CreatedAt).First()).ToListAsync(cancellationToken);
        var unread = await db.Set<ChatMessage>().AsNoTracking()
            .Where(m => ids.Contains(m.ConversationId) && m.SenderUserId != userId && m.DeletedAt == null)
            .Join(db.Set<ChatMember>().Where(x => x.UserId == userId), m => m.ConversationId, x => x.ConversationId, (m, x) => new { m.ConversationId, m.CreatedAt, x.ReadUpTo })
            .Where(x => x.ReadUpTo == null || x.CreatedAt > x.ReadUpTo)
            .GroupBy(x => x.ConversationId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var mentions = await db.Set<ChatMessage>().AsNoTracking()
            .Where(m => ids.Contains(m.ConversationId) && m.DeletedAt == null && m.MentionedUserIds.Contains(userId))
            .Join(db.Set<ChatMember>().Where(x => x.UserId == userId), m => m.ConversationId, x => x.ConversationId, (m, x) => new { m.ConversationId, m.CreatedAt, x.ReadUpTo })
            .Where(x => x.ReadUpTo == null || x.CreatedAt > x.ReadUpTo)
            .GroupBy(x => x.ConversationId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        var names = await chat.NamesAsync(members.Select(m => m.UserId).Concat(last.Select(l => l.SenderUserId)), cancellationToken);
        var lastIds = last.Select(l => l.Id).ToList();
        var lastAttachments = await db.Set<ChatAttachment>().AsNoTracking().Where(a => a.MessageId != null && lastIds.Contains(a.MessageId.Value))
            .Select(a => new { a.MessageId, a.Kind }).ToListAsync(cancellationToken);
        var isChatAdmin = await chat.IsChatAdminAsync(cancellationToken);
        var blocked = (await chat.UserSettingsAsync(userId, cancellationToken))?.BlockedAt is not null;
        var now = DateTimeOffset.UtcNow;
        return conversations.Select(c =>
        {
            var people = members.Where(m => m.ConversationId == c.Id).Select(m => new ChatPerson(m.UserId, names.GetValueOrDefault(m.UserId) ?? "Ehemaliger Benutzer"))
                .OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList();
            var partner = c.Kind == ChatConversationKind.Direct ? people.FirstOrDefault(p => p.Id != userId) : null;
            var message = last.FirstOrDefault(l => l.ConversationId == c.Id);
            var preview = message is null ? null : new ChatLastMessage(message.Id, message.SenderUserId, names.GetValueOrDefault(message.SenderUserId) ?? "Unbekannt",
                chat.Unprotect(message.ProtectedText) is { } text ? ChatService.Preview(text, 140) : null, message.CreatedAt, message.SenderUserId == userId,
                message.DeletedAt is not null, lastAttachments.FirstOrDefault(a => a.MessageId == message.Id)?.Kind, message.ProtectedLocation is not null);
            var title = c.Kind == ChatConversationKind.Group ? c.Title ?? "Gruppe" : partner?.Name ?? "Einzelchat";
            var me = mine.FirstOrDefault(m => m.ConversationId == c.Id);
            var muted = me?.IsMuted(now) ?? false;
            var admin = me?.IsAdmin ?? false;
            return new ChatConversationSummary(c.Id, c.Kind, title, c.CrewId, people, preview, c.LastActivityAt, unread.GetValueOrDefault(c.Id),
                muted, partner?.Id, c.Description, c.BauakteId, c.AnnouncementOnly, c.ImageUpdatedAt, muted ? me!.MutedUntil : null, admin,
                !blocked && (!c.AnnouncementOnly || admin || isChatAdmin), mentions.GetValueOrDefault(c.Id));
        }).ToList();
    }

    private static async Task<Results<Ok<ChatConversationSummary>, ProblemHttpResult>> StartDirectAsync(
        StartDirectChatRequest request, ChatService chat, EstrichOneDbContext db, ICurrentUser user, IUserPermissions permissions, IRoleAssignments roles,
        IAuditLog audit, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (request.UserId == user.UserId)
        {
            return Validation("Ein Chat mit sich selbst ist nicht möglich.");
        }

        var users = await chat.ChatUsersAsync(cancellationToken);
        if (!users.Contains(request.UserId))
        {
            return Validation("Diese Person kann den Chat nicht nutzen (gesperrt oder ohne Recht „Chat nutzen“).");
        }

        var settings = await chat.SettingsAsync(cancellationToken);
        if (!settings.WorkersCanChatEachOther && !await ChatService.IsAdminAsync(permissions, cancellationToken)
            && !(await roles.GetUserIdsWithPermissionAsync(Permissions.ChatAdmin, cancellationToken)).Contains(request.UserId))
        {
            return Validation("Einzelchats sind nur mit dem Büro möglich.");
        }

        var key = ChatConversation.DirectKeyOf(user.UserId, request.UserId);
        var conversation = await db.Set<ChatConversation>().SingleOrDefaultAsync(c => c.DirectKey == key, cancellationToken);
        if (conversation is null)
        {
            var now = clock.GetUtcNow();
            conversation = ChatConversation.Direct(user.UserId, request.UserId, user.UserId, now);
            db.Add(conversation);
            db.Add(ChatMember.Create(conversation.Id, user.UserId, now));
            db.Add(ChatMember.Create(conversation.Id, request.UserId, now));
            audit.Record("chat.conversation_created", user.UserId, "chat_conversation", conversation.Id.ToString(),
                new Dictionary<string, string?> { ["kind"] = "direct" });
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Gleichzeitig von beiden Seiten geöffnet: den vorhandenen nehmen.
                db.ChangeTracker.Clear();
                conversation = await db.Set<ChatConversation>().SingleAsync(c => c.DirectKey == key, cancellationToken);
            }
        }

        return TypedResults.Ok(await SummaryAsync(chat, db, user.UserId, conversation, cancellationToken));
    }

    private static async Task<ChatConversationSummary> SummaryAsync(ChatService chat, EstrichOneDbContext db, Guid userId, ChatConversation conversation,
        CancellationToken cancellationToken)
    {
        var mine = await db.Set<ChatMember>().AsNoTracking().Where(m => m.UserId == userId && m.ConversationId == conversation.Id).ToListAsync(cancellationToken);
        return (await SummariesAsync(chat, db, userId, [conversation], mine, cancellationToken)).Single();
    }

    private static async Task<Results<Created<ChatConversationSummary>, ProblemHttpResult>> CreateGroupAsync(
        CreateGroupChatRequest request, ChatService chat, EstrichOneDbContext db, ICurrentUser user, IAuditLog audit, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var title = ChatConversation.Clip(request.Title, ChatConversation.MaxTitle);
        if (title is null)
        {
            return Validation("Bitte einen Namen für die Gruppe angeben.");
        }

        var users = await chat.ChatUsersAsync(cancellationToken);
        var wanted = (request.MemberIds ?? []).Where(users.Contains).Append(user.UserId).Distinct().ToList();
        if (request.CrewId is null && wanted.Count < 2)
        {
            return Validation("Eine Gruppe braucht mindestens eine weitere Person mit Chat-Recht.");
        }

        var now = clock.GetUtcNow();
        var conversation = ChatConversation.Group(title, request.CrewId, user.UserId, now, description: request.Description,
            announcementOnly: request.AnnouncementOnly);
        db.Add(conversation);
        var admins = (request.AdminIds ?? []).Append(user.UserId).ToHashSet();
        db.AddRange(wanted.Select(id => ChatMember.Create(conversation.Id, id, now, isAdmin: admins.Contains(id))));
        audit.Record("chat.conversation_created", user.UserId, "chat_conversation", conversation.Id.ToString(), new Dictionary<string, string?>
        {
            ["kind"] = "group", ["members"] = wanted.Count.ToString(Invariant), ["crew"] = request.CrewId?.ToString(),
        });
        await db.SaveChangesAsync(cancellationToken);
        if (request.CrewId is not null)
        {
            await chat.SyncCrewGroupsAsync(cancellationToken);
        }

        await chat.PublishAsync(conversation.Id, new ChatEvent("conversation", conversation.Id), cancellationToken);
        return TypedResults.Created($"/api/v1/chat/conversations/{conversation.Id}", await SummaryAsync(chat, db, user.UserId, conversation, cancellationToken));
    }

    private static async Task<(ChatConversation? Conversation, ChatMember? Member)> FindAsync(EstrichOneDbContext db, Guid id, Guid userId,
        CancellationToken cancellationToken)
    {
        var member = await db.Set<ChatMember>().SingleOrDefaultAsync(m => m.ConversationId == id && m.UserId == userId, cancellationToken);
        if (member is null)
        {
            return (null, null);
        }

        return (await db.Set<ChatConversation>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken), member);
    }

    private static async Task<List<ChatMemberState>> MemberStatesAsync(ChatService chat, EstrichOneDbContext db, Guid conversationId, CancellationToken cancellationToken)
    {
        var members = await db.Set<ChatMember>().AsNoTracking().Where(m => m.ConversationId == conversationId).ToListAsync(cancellationToken);
        var ids = members.Select(m => m.UserId).ToList();
        var names = await chat.NamesAsync(ids, cancellationToken);

        // Lesebestätigungen abgeschaltet: andere sehen nicht, wann diese Person gelesen hat.
        var hidden = await db.Set<ChatUserSettings>().AsNoTracking().Where(s => ids.Contains(s.UserId) && !s.ReadReceipts).Select(s => s.UserId)
            .ToListAsync(cancellationToken);
        return members.Select(m => new ChatMemberState(m.UserId, names.GetValueOrDefault(m.UserId) ?? "Ehemaliger Benutzer", m.DeliveredUpTo,
                hidden.Contains(m.UserId) && m.UserId != chat.UserId ? null : m.ReadUpTo, m.FromCrew, m.IsAdmin))
            .OrderByDescending(m => m.IsAdmin).ThenBy(m => m.Name, StringComparer.CurrentCulture).ToList();
    }

    private static async Task<Results<Ok<ChatConversationDetail>, ProblemHttpResult>> ConversationAsync(
        Guid id, ChatService chat, EstrichOneDbContext db, ICurrentUser user, Bauakten.Exports.IBauakteReportSource bauakten, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var (conversation, member) = await FindAsync(db, id, user.UserId, cancellationToken);
        if (conversation is null)
        {
            return NotFound();
        }

        return TypedResults.Ok(await DetailAsync(chat, db, user.UserId, conversation, member, bauakten, clock, cancellationToken));
    }

    private static async Task<ChatConversationDetail> DetailAsync(ChatService chat, EstrichOneDbContext db, Guid userId, ChatConversation conversation,
        ChatMember? member, Bauakten.Exports.IBauakteReportSource bauakten, TimeProvider clock, CancellationToken cancellationToken)
    {
        var states = await MemberStatesAsync(chat, db, conversation.Id, cancellationToken);
        var pinnedRows = await db.Set<ChatMessage>().AsNoTracking().Where(m => m.ConversationId == conversation.Id && m.PinnedAt != null)
            .OrderByDescending(m => m.PinnedAt).Take(20).ToListAsync(cancellationToken);
        var pinned = await RenderAsync(chat, db, userId, pinnedRows, states, clock, cancellationToken);
        var bauakte = conversation.BauakteId is { } bauakteId ? await bauakten.GetAsync(bauakteId, cancellationToken) : null;
        return new ChatConversationDetail(await SummaryAsync(chat, db, userId, conversation, cancellationToken), states,
            await chat.CanManageAsync(conversation, member, cancellationToken), pinned,
            bauakte is null ? null : $"{bauakte.Number} {bauakte.Title}".Trim());
    }

    private static async Task<Results<Ok<ChatConversationDetail>, ProblemHttpResult>> UpdateGroupAsync(
        Guid id, UpdateGroupChatRequest request, ChatService chat, EstrichOneDbContext db, ICurrentUser user, IAuditLog audit,
        Bauakten.Exports.IBauakteReportSource bauakten, TimeProvider clock, CancellationToken cancellationToken)
    {
        var conversation = await db.Set<ChatConversation>().SingleOrDefaultAsync(c => c.Id == id && c.Kind == ChatConversationKind.Group, cancellationToken);
        var self = await db.Set<ChatMember>().SingleOrDefaultAsync(m => m.ConversationId == id && m.UserId == user.UserId, cancellationToken);
        var isChatAdmin = await chat.IsChatAdminAsync(cancellationToken);
        if (conversation is null || (self is null && !isChatAdmin))
        {
            return NotFound();
        }

        if (!await chat.CanManageAsync(conversation, self, cancellationToken))
        {
            return Forbidden("Nur Gruppenadmins können die Gruppe ändern.");
        }

        if (request.Description is not null)
        {
            conversation.Describe(request.Description);
        }

        if (request.AnnouncementOnly is { } announcement)
        {
            conversation.SetAnnouncementOnly(announcement);
        }

        if (request.Title is not null)
        {
            if (ChatConversation.Clip(request.Title, ChatConversation.MaxTitle) is null)
            {
                return Validation("Bitte einen Namen für die Gruppe angeben.");
            }

            conversation.Rename(request.Title);
        }

        var removed = new List<Guid>();
        if (request.MemberIds is { } memberIds)
        {
            var users = await chat.ChatUsersAsync(cancellationToken);
            var wanted = memberIds.Where(users.Contains).ToHashSet();
            var current = await db.Set<ChatMember>().Where(m => m.ConversationId == id).ToListAsync(cancellationToken);
            var now = clock.GetUtcNow();
            removed.AddRange(current.Where(m => !m.FromCrew && !wanted.Contains(m.UserId)).Select(m => m.UserId));
            db.RemoveRange(current.Where(m => !m.FromCrew && !wanted.Contains(m.UserId)));
            foreach (var userId in wanted)
            {
                var existing = current.FirstOrDefault(m => m.UserId == userId);
                if (existing is null)
                {
                    db.Add(ChatMember.Create(id, userId, now, readUpTo: conversation.LastActivityAt));
                }
                else if (existing.FromCrew)
                {
                    existing.SetFromCrew(false); // Von Hand bestätigt: bleibt auch beim Wechsel der Kolonne.
                }
            }
        }

        audit.Record("chat.group_changed", user.UserId, "chat_conversation", id.ToString(), new Dictionary<string, string?>
        {
            ["members"] = request.MemberIds?.Count.ToString(Invariant), ["renamed"] = (request.Title is not null).ToString(),
            ["admins"] = request.AdminIds?.Count.ToString(Invariant), ["announcementOnly"] = request.AnnouncementOnly?.ToString(),
        });
        await db.SaveChangesAsync(cancellationToken);
        if (request.AdminIds is { } adminIds)
        {
            var members = await db.Set<ChatMember>().Where(m => m.ConversationId == id).ToListAsync(cancellationToken);
            var admins = adminIds.ToHashSet();
            if (!members.Any(m => admins.Contains(m.UserId)))
            {
                return Validation("Eine Gruppe braucht mindestens einen Admin.");
            }

            foreach (var member in members)
            {
                member.SetAdmin(admins.Contains(member.UserId));
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await chat.PublishAsync(id, new ChatEvent("conversation", id), cancellationToken, extra: removed);
        return TypedResults.Ok(await DetailAsync(chat, db, user.UserId, conversation, self, bauakten, clock, cancellationToken));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteConversationAsync(
        Guid id, ChatService chat, EstrichOneDbContext db, ICurrentUser user, IAuditLog audit, CancellationToken cancellationToken)
    {
        var conversation = await db.Set<ChatConversation>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        var self = await db.Set<ChatMember>().AsNoTracking().SingleOrDefaultAsync(m => m.ConversationId == id && m.UserId == user.UserId, cancellationToken);
        var isChatAdmin = await chat.IsChatAdminAsync(cancellationToken);
        if (conversation is null || (self is null && !isChatAdmin))
        {
            return NotFound();
        }

        if (!isChatAdmin && !(conversation.Kind == ChatConversationKind.Group && self?.IsAdmin == true))
        {
            return Forbidden("Nur Gruppenadmins und die Chat-Verwaltung können Unterhaltungen löschen.");
        }

        var memberIds = await db.Set<ChatMember>().AsNoTracking().Where(m => m.ConversationId == id).Select(m => m.UserId).ToListAsync(cancellationToken);
        await chat.DeleteAttachmentsAsync(db.Set<ChatAttachment>().Where(a => a.ConversationId == id), cancellationToken);
        if (conversation.ImageKey is { } image)
        {
            chat.Blobs.Delete([image]);
        }

        var messages = await db.Set<ChatMessage>().Where(m => m.ConversationId == id).ExecuteDeleteAsync(cancellationToken);
        db.Remove(conversation);
        audit.Record("chat.conversation_deleted", user.UserId, "chat_conversation", id.ToString(),
            new Dictionary<string, string?> { ["messages"] = messages.ToString(Invariant), ["kind"] = conversation.Kind.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        chat.Hub.Publish(memberIds, new ChatEvent("conversation.removed", id));
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> MuteAsync(
        Guid id, MuteChatRequest request, EstrichOneDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken cancellationToken)
    {
        var (conversation, member) = await FindAsync(db, id, user.UserId, cancellationToken);
        if (conversation is null || member is null)
        {
            return NotFound();
        }

        member.Mute(request.Muted, request.Minutes is { } minutes ? clock.GetUtcNow().AddMinutes(minutes) : request.Until);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ChatMessagePage>, ProblemHttpResult>> MessagesAsync(
        Guid id, ChatService chat, EstrichOneDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken cancellationToken,
        [FromQuery] DateTimeOffset? before = null, [FromQuery] DateTimeOffset? after = null, [FromQuery] int? limit = null)
    {
        var (conversation, member) = await FindAsync(db, id, user.UserId, cancellationToken);
        if (conversation is null || member is null)
        {
            return NotFound();
        }

        var take = Math.Clamp(limit ?? 50, 1, 200);
        var query = db.Set<ChatMessage>().AsNoTracking().Where(m => m.ConversationId == id);
        List<ChatMessage> rows;
        bool hasMore;
        if (after is { } a)
        {
            rows = await query.Where(m => m.CreatedAt > a).OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).Take(take + 1).ToListAsync(cancellationToken);
            hasMore = rows.Count > take;
            rows = rows.Take(take).ToList();
        }
        else
        {
            if (before is { } b)
            {
                query = query.Where(m => m.CreatedAt < b);
            }

            rows = await query.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(take + 1).ToListAsync(cancellationToken);
            hasMore = rows.Count > take;
            rows = rows.Take(take).OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).ToList();
        }

        if (rows.Count > 0)
        {
            member.Delivered(rows[^1].CreatedAt);
            await db.SaveChangesAsync(cancellationToken);
        }

        var states = await MemberStatesAsync(chat, db, id, cancellationToken);
        return TypedResults.Ok(new ChatMessagePage(await RenderAsync(chat, db, user.UserId, rows, states, clock, cancellationToken), hasMore, states));
    }

    private sealed record RenderContext(
        IReadOnlyDictionary<Guid, string> Names, ILookup<Guid, ChatAttachment> Attachments, ILookup<Guid, ChatReaction> Reactions,
        IReadOnlyDictionary<Guid, ChatReplyPreview> Replies, DateTimeOffset EditableAfter);

    /// <summary>Nachrichten samt Anhängen, Reaktionen und Zitaten in wenigen Abfragen aufbereiten.</summary>
    private static async Task<List<ChatMessageResponse>> RenderAsync(ChatService chat, EstrichOneDbContext db, Guid userId, List<ChatMessage> rows,
        IReadOnlyList<ChatMemberState> states, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ids = rows.Select(r => r.Id).ToList();
        var replyIds = rows.Where(r => r.ReplyToMessageId != null).Select(r => r.ReplyToMessageId!.Value).Distinct().ToList();
        var attachments = await db.Set<ChatAttachment>().AsNoTracking().Where(a => a.MessageId != null && ids.Contains(a.MessageId.Value))
            .OrderBy(a => a.CreatedAt).ToListAsync(cancellationToken);
        var reactions = await db.Set<ChatReaction>().AsNoTracking().Where(r => ids.Contains(r.MessageId)).OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        var replies = replyIds.Count == 0 ? [] : await db.Set<ChatMessage>().AsNoTracking().Where(m => replyIds.Contains(m.Id)).ToListAsync(cancellationToken);
        var replyKinds = replyIds.Count == 0 ? [] : await db.Set<ChatAttachment>().AsNoTracking()
            .Where(a => a.MessageId != null && replyIds.Contains(a.MessageId.Value)).Select(a => new { MessageId = a.MessageId!.Value, a.Kind })
            .ToListAsync(cancellationToken);
        var names = states.ToDictionary(s => s.UserId, s => s.Name);
        var missing = rows.Select(r => r.SenderUserId).Concat(reactions.Select(r => r.UserId)).Concat(replies.Select(r => r.SenderUserId))
            .Where(s => !names.ContainsKey(s)).Distinct().ToList();
        foreach (var (key, value) in await chat.NamesAsync(missing, cancellationToken))
        {
            names[key] = value;
        }

        var replyPreviews = replies.ToDictionary(r => r.Id, r => new ChatReplyPreview(r.Id, r.SenderUserId,
            names.GetValueOrDefault(r.SenderUserId) ?? "Ehemaliger Benutzer",
            chat.Unprotect(r.ProtectedText) is { } text ? ChatService.Preview(text, 160) : null, r.DeletedAt is not null,
            replyKinds.FirstOrDefault(k => k.MessageId == r.Id)?.Kind));
        var settings = await chat.SettingsAsync(cancellationToken);
        var context = new RenderContext(names, attachments.ToLookup(a => a.MessageId!.Value), reactions.ToLookup(r => r.MessageId), replyPreviews,
            settings.EditWindowMinutes <= 0 ? DateTimeOffset.MaxValue : clock.GetUtcNow().AddMinutes(-settings.EditWindowMinutes));
        return rows.Select(r => ToResponse(chat, r, userId, states, context)).ToList();
    }

    private static ChatAttachmentResponse ToAttachment(ChatService chat, ChatAttachment a) =>
        new(a.Id, a.Kind, chat.Unprotect(a.ProtectedName) ?? "Anhang", a.ContentType, a.SizeBytes, a.Width, a.Height, a.HasThumbnail, a.CreatedAt,
            a.MessageId, a.UploadedByUserId);

    private static ChatMessageResponse ToResponse(ChatService chat, ChatMessage m, Guid userId, IReadOnlyList<ChatMemberState> states, RenderContext context)
    {
        var names = context.Names;
        var mine = m.SenderUserId == userId;
        var others = states.Where(s => s.UserId != m.SenderUserId).ToList();
        var read = others.Count(s => s.ReadUpTo >= m.CreatedAt);
        var delivered = others.Count(s => s.DeliveredUpTo >= m.CreatedAt);
        string? status = null;
        if (mine && m.DeletedAt is null)
        {
            status = others.Count > 0 && read == others.Count ? "read" : others.Count > 0 && delivered == others.Count ? "delivered" : "sent";
        }

        var deleted = m.DeletedAt is not null;
        var reactions = deleted ? [] : context.Reactions[m.Id].GroupBy(r => r.Emoji)
            .Select(g => new ChatReactionGroup(g.Key, g.Count(), g.Any(r => r.UserId == userId), g.Select(r => names.GetValueOrDefault(r.UserId) ?? "?").ToList()))
            .OrderByDescending(g => g.Count).ToList();
        return new ChatMessageResponse(m.Id, m.ConversationId, m.SenderUserId, names.GetValueOrDefault(m.SenderUserId) ?? "Ehemaliger Benutzer",
            chat.Unprotect(m.ProtectedText), m.CreatedAt, mine, deleted, status, mine ? read : 0, mine ? others.Count : 0, mine ? m.ClientMessageId : null,
            m.ReplyToMessageId is { } reply && !deleted ? context.Replies.GetValueOrDefault(reply) : null,
            deleted ? [] : context.Attachments[m.Id].Select(a => ToAttachment(chat, a)).ToList(), reactions, m.MentionedUserIds,
            m.EditedAt, m.Forwarded, m.PinnedAt, deleted ? null : chat.UnprotectLocation(m.ProtectedLocation),
            mine && !deleted && m.ProtectedText is not null && m.CreatedAt >= context.EditableAfter);
    }

    private static async Task<Results<Created<ChatMessageResponse>, Ok<ChatMessageResponse>, ProblemHttpResult>> SendAsync(
        Guid id, SendChatMessageRequest request, ChatService chat, EstrichOneDbContext db, ICurrentUser user, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var (conversation, member) = await FindAsync(db, id, user.UserId, cancellationToken);
        if (conversation is null || member is null)
        {
            return NotFound();
        }

        if (request.ClientMessageId is { } clientId)
        {
            var existing = await db.Set<ChatMessage>().AsNoTracking()
                .SingleOrDefaultAsync(m => m.SenderUserId == user.UserId && m.ClientMessageId == clientId, cancellationToken);
            if (existing is not null)
            {
                var states0 = await MemberStatesAsync(chat, db, existing.ConversationId, cancellationToken);
                return TypedResults.Ok((await RenderAsync(chat, db, user.UserId, [existing], states0, clock, cancellationToken)).Single());
            }
        }

        var result = await SendCoreAsync(chat, db, clock, conversation, member, request.Text, request.ReplyToId,
            [.. request.AttachmentIds ?? [], .. request.AttachmentFileIds ?? []], request.Location,
            [.. request.MentionIds ?? [], .. request.MentionUserIds ?? []], request.ClientMessageId, forwarded: false, cancellationToken);
        if (result.Problem is { } problem)
        {
            return problem;
        }

        var states = await MemberStatesAsync(chat, db, id, cancellationToken);
        return TypedResults.Created($"/api/v1/chat/conversations/{id}/messages",
            (await RenderAsync(chat, db, user.UserId, [result.Message!], states, clock, cancellationToken)).Single());
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> ReadAsync(
        Guid id, ChatService chat, EstrichOneDbContext db, ICurrentUser user, CancellationToken cancellationToken)
    {
        var (conversation, member) = await FindAsync(db, id, user.UserId, cancellationToken);
        if (conversation is null || member is null)
        {
            return NotFound();
        }

        await chat.MarkReadAsync(member, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteMessageAsync(
        Guid id, ChatService chat, EstrichOneDbContext db, ICurrentUser user, IUserPermissions permissions, IAuditLog audit, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var message = await db.Set<ChatMessage>().SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (message is null || !await db.Set<ChatMember>().AnyAsync(m => m.ConversationId == message.ConversationId && m.UserId == user.UserId, cancellationToken))
        {
            return NotFound();
        }

        var admin = message.SenderUserId != user.UserId;
        if (admin && !await ChatService.IsAdminAsync(permissions, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Keine Berechtigung",
                detail: "Nur eigene Nachrichten lassen sich löschen.", extensions: new Dictionary<string, object?> { ["code"] = "PERMISSION_DENIED" });
        }

        message.Delete(clock.GetUtcNow());
        await chat.DeleteAttachmentsAsync(db.Set<ChatAttachment>().Where(a => a.MessageId == id), cancellationToken);
        await db.Set<ChatReaction>().Where(r => r.MessageId == id).ExecuteDeleteAsync(cancellationToken);
        if (admin)
        {
            audit.Record("chat.message_deleted", user.UserId, "chat_message", id.ToString(),
                new Dictionary<string, string?> { ["conversation"] = message.ConversationId.ToString(), ["sender"] = message.SenderUserId.ToString() });
        }

        await db.SaveChangesAsync(cancellationToken);
        await chat.PublishAsync(message.ConversationId, new ChatEvent("message.deleted", message.ConversationId, id), cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<ChatExport> ExportAsync(ChatService chat, EstrichOneDbContext db, Guid userId, string userName, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var conversationIds = await db.Set<ChatMember>().AsNoTracking().Where(m => m.UserId == userId).Select(m => m.ConversationId).ToListAsync(cancellationToken);
        var messages = await db.Set<ChatMessage>().AsNoTracking()
            .Where(m => m.SenderUserId == userId || conversationIds.Contains(m.ConversationId)).OrderBy(m => m.CreatedAt).ToListAsync(cancellationToken);
        var allIds = messages.Select(m => m.ConversationId).Concat(conversationIds).Distinct().ToList();
        var conversations = await db.Set<ChatConversation>().AsNoTracking().Where(c => allIds.Contains(c.Id)).ToListAsync(cancellationToken);
        var members = await db.Set<ChatMember>().AsNoTracking().Where(m => allIds.Contains(m.ConversationId)).ToListAsync(cancellationToken);
        var names = await chat.NamesAsync(messages.Select(m => m.SenderUserId).Concat(members.Select(m => m.UserId)), cancellationToken);
        string Title(Guid id)
        {
            var c = conversations.FirstOrDefault(x => x.Id == id);
            if (c is null)
            {
                return "Unterhaltung";
            }

            return c.Kind == ChatConversationKind.Group ? c.Title ?? "Gruppe"
                : "Einzelchat mit " + string.Join(", ", members.Where(m => m.ConversationId == id && m.UserId != userId).Select(m => names.GetValueOrDefault(m.UserId) ?? "?"));
        }

        var settings = await chat.SettingsAsync(cancellationToken);
        var messageIds = messages.Select(m => m.Id).ToList();
        var files = (await db.Set<ChatAttachment>().AsNoTracking().Where(a => a.MessageId != null && messageIds.Contains(a.MessageId.Value))
            .ToListAsync(cancellationToken)).ToLookup(a => a.MessageId!.Value);
        return new ChatExport(userId, userName, clock.GetUtcNow(), settings.RetentionDays, messages.Select(m => new ChatExportMessage(m.Id, m.ConversationId,
            Title(m.ConversationId), names.GetValueOrDefault(m.SenderUserId) ?? "Ehemaliger Benutzer", m.SenderUserId == userId, m.CreatedAt,
            chat.Unprotect(m.ProtectedText) ?? (chat.UnprotectLocation(m.ProtectedLocation) is { } location
                ? $"Standort {location.Latitude.ToString(Invariant)}, {location.Longitude.ToString(Invariant)} {location.Label}".Trim() : null),
            files[m.Id].Select(a => $"{a.Id:N}_{chat.Unprotect(a.ProtectedName) ?? "anhang"}").ToList())).ToList());
    }

    private static async Task<IResult> OwnExportAsync(ChatService chat, EstrichOneDbContext db, ICurrentUser user, IAuditLog audit, TimeProvider clock,
        HttpContext http, CancellationToken cancellationToken)
    {
        var name = (await chat.NamesAsync([user.UserId], cancellationToken)).GetValueOrDefault(user.UserId) ?? "Benutzer";
        var export = await ExportAsync(chat, db, user.UserId, name, clock, cancellationToken);
        audit.Record("chat.data_exported", user.UserId, "user", user.UserId.ToString(),
            new Dictionary<string, string?> { ["messages"] = export.Messages.Count.ToString(Invariant), ["by"] = "self" });
        await db.SaveChangesAsync(cancellationToken);
        http.Response.Headers.ContentDisposition = $"attachment; filename=\"chat-export-{clock.GetUtcNow():yyyy-MM-dd}.json\"";
        return TypedResults.Ok(export);
    }

    private static async Task<Results<Ok<ChatExport>, ProblemHttpResult>> UserExportAsync(
        Guid userId, ChatService chat, EstrichOneDbContext db, ICurrentUser user, IAuditLog audit, TimeProvider clock, CancellationToken cancellationToken)
    {
        var name = (await chat.NamesAsync([userId], cancellationToken)).GetValueOrDefault(userId);
        if (name is null)
        {
            return NotFound();
        }

        var export = await ExportAsync(chat, db, userId, name, clock, cancellationToken);
        audit.Record("chat.data_exported", user.UserId, "user", userId.ToString(),
            new Dictionary<string, string?> { ["messages"] = export.Messages.Count.ToString(Invariant), ["by"] = "admin" });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(export);
    }

    private static async Task<Results<Ok<ChatErasureResponse>, ProblemHttpResult>> EraseUserAsync(
        Guid userId, ChatService chat, EstrichOneDbContext db, ICurrentUser user, IAuditLog audit, CancellationToken cancellationToken)
    {
        if ((await chat.NamesAsync([userId], cancellationToken)).Count == 0)
        {
            return NotFound();
        }

        var direct = await db.Set<ChatMember>().Where(m => m.UserId == userId)
            .Join(db.Set<ChatConversation>().Where(c => c.Kind == ChatConversationKind.Direct), m => m.ConversationId, c => c.Id, (m, c) => c.Id)
            .ToListAsync(cancellationToken);
        await chat.DeleteAttachmentsAsync(db.Set<ChatAttachment>().Where(a => a.UploadedByUserId == userId || direct.Contains(a.ConversationId)), cancellationToken);
        await db.Set<ChatReaction>().Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<ChatUserSettings>().Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        var messages = await db.Set<ChatMessage>().Where(m => m.SenderUserId == userId).ExecuteDeleteAsync(cancellationToken);
        await db.Set<ChatMessage>().Where(m => direct.Contains(m.ConversationId)).ExecuteDeleteAsync(cancellationToken);
        await db.Set<ChatConversation>().Where(c => direct.Contains(c.Id)).ExecuteDeleteAsync(cancellationToken);
        var memberships = await db.Set<ChatMember>().Where(m => m.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        audit.Record("chat.data_erased", user.UserId, "user", userId.ToString(), new Dictionary<string, string?>
        {
            ["messages"] = messages.ToString(Invariant), ["memberships"] = (memberships + direct.Count).ToString(Invariant),
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new ChatErasureResponse(messages, memberships + direct.Count));
    }

    private static async Task<Ok<IReadOnlyList<AudienceOption>>> CrewsAsync(IEnumerable<INotificationAudience> audiences, CancellationToken cancellationToken)
    {
        var list = new List<AudienceOption>();
        foreach (var audience in audiences)
        {
            list.AddRange(await audience.CrewsAsync(cancellationToken));
        }

        return TypedResults.Ok<IReadOnlyList<AudienceOption>>(list);
    }

    private static async Task<Ok<ChatSettingsResponse>> SettingsAsync(EstrichOneDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok(await SettingsResponseAsync(db, cancellationToken));

    private static async Task<ChatSettingsResponse> SettingsResponseAsync(EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var row = await db.Set<ChatSettingsRow>().AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new ChatSettingsRow();
        return new ChatSettingsResponse(row.Enabled, row.RetentionDays, row.ShowPreview, row.WorkersCanChatEachOther, row.LastCleanupAt,
            await db.Set<ChatConversation>().CountAsync(cancellationToken), await db.Set<ChatMessage>().CountAsync(cancellationToken), row.Version,
            row.EditWindowMinutes, row.AttachmentsEnabled, await db.Set<ChatAttachment>().CountAsync(cancellationToken),
            await db.Set<ChatAttachment>().SumAsync(a => (long?)a.SizeBytes, cancellationToken) ?? 0);
    }

    private static async Task<Results<Ok<ChatSettingsResponse>, ProblemHttpResult>> SaveSettingsAsync(
        SaveChatSettingsRequest request, EstrichOneDbContext db, ICurrentUser user, IAuditLog audit, CancellationToken cancellationToken)
    {
        var row = await db.Set<ChatSettingsRow>().SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            row = new ChatSettingsRow();
            db.Add(row);
        }
        else if (request.Version is { } version && version != row.Version)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Zwischenzeitlich geändert",
                detail: "Die Chat-Einstellungen wurden inzwischen geändert. Bitte neu laden.", extensions: new Dictionary<string, object?> { ["code"] = "VERSION_CONFLICT" });
        }

        row.Enabled = request.Enabled;
        row.RetentionDays = Math.Clamp(request.RetentionDays, 7, 730);
        row.ShowPreview = request.ShowPreview;
        row.WorkersCanChatEachOther = request.WorkersCanChatEachOther;
        row.EditWindowMinutes = Math.Clamp(request.EditWindowMinutes, 0, 1440);
        row.AttachmentsEnabled = request.AttachmentsEnabled;
        audit.Record("chat.settings_saved", user.UserId, "settings", "chat", new Dictionary<string, string?>
        {
            ["enabled"] = row.Enabled.ToString(), ["retentionDays"] = row.RetentionDays.ToString(Invariant), ["showPreview"] = row.ShowPreview.ToString(),
            ["workersCanChatEachOther"] = row.WorkersCanChatEachOther.ToString(), ["editWindowMinutes"] = row.EditWindowMinutes.ToString(Invariant),
            ["attachmentsEnabled"] = row.AttachmentsEnabled.ToString(),
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await SettingsResponseAsync(db, cancellationToken));
    }

    /// <summary>Im Archiv (Modus „Microsoft Teams“) erlaubt: gelesen markieren, stumm schalten, persönliche Einstellungen, Anhang in Bauakte übernehmen.</summary>
    private static readonly HashSet<string> ArchiveAllowed = new(StringComparer.Ordinal)
    {
        "MarkChatRead", "MuteChatConversation", "AcceptChatPrivacyNotice", "SaveMyChatSettings", "CopyChatAttachmentToBauakte",
    };

    private static ProblemHttpResult ReadOnly() => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Chat-Archiv",
        detail: "Unterhaltungen finden jetzt in Microsoft Teams statt. Der Chat in EstrichONE ist nur noch zum Lesen und Exportieren da.",
        extensions: new Dictionary<string, object?> { ["code"] = "CHAT_READ_ONLY" });

    private static ProblemHttpResult Disabled() => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Modul deaktiviert",
        detail: "Der Chat ist deaktiviert (Einstellungen › Module).", extensions: new Dictionary<string, object?> { ["code"] = "MODULE_DISABLED" });

    private static ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Unterhaltung nicht gefunden",
        extensions: new Dictionary<string, object?> { ["code"] = "NOT_FOUND" });

    private static ProblemHttpResult Forbidden(string detail, string code = "PERMISSION_DENIED") => TypedResults.Problem(
        statusCode: StatusCodes.Status403Forbidden, title: "Keine Berechtigung", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    private static ProblemHttpResult Validation(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Ungültige Angaben", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
}
