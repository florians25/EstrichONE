using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using System.Text.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Documentation.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Chat;

public sealed record EditChatMessageRequest([property: Required, MaxLength(ChatMessage.MaxText)] string Text, IReadOnlyList<Guid>? MentionIds = null);

/// <param name="Emoji">Ein Emoji; leer oder <c>null</c> entfernt die eigene Reaktion.</param>
public sealed record ChatReactionRequest([property: MaxLength(ChatReaction.MaxEmoji)] string? Emoji);

public sealed record ChatPinRequest(bool Pinned);

public sealed record ForwardChatMessageRequest([property: Required] IReadOnlyList<Guid> ConversationIds);

public sealed record ForwardChatMessageResponse(IReadOnlyList<ChatMessageResponse> Messages);

/// <param name="BauakteId">Ziel; ohne Angabe die Bauakte der Baustellen-Gruppe.</param>
public sealed record ChatAttachmentToBauakteRequest(Guid? BauakteId = null, [property: MaxLength(500)] string? Caption = null);

public sealed record ChatAttachmentToBauakteResponse(Guid FileId, Guid BauakteId);

public sealed record ChatSearchHit(Guid MessageId, Guid ConversationId, string ConversationTitle, string SenderName, string Snippet, DateTimeOffset CreatedAt);

public sealed record ChatSearchResponse(IReadOnlyList<ChatSearchHit> Items, int Searched, bool Truncated);

public sealed record ChatMySettings(bool ShowPreview, bool ReadReceipts, DateTimeOffset? PrivacyAcceptedAt, bool Blocked, bool PreviewAllowedByAdmin);

public sealed record SaveChatMySettingsRequest(bool? ShowPreview = null, bool? ReadReceipts = null, bool? PrivacyAccepted = null);

/// <param name="ConversationId">Vorhandene Baustellen-Gruppe; <c>null</c> = noch keine.</param>
/// <param name="Member">Die anfragende Person ist Mitglied der Gruppe.</param>
public sealed record ChatBauakteGroup(Guid BauakteId, Guid? ConversationId, string? Title, bool Member, int Members, bool CanCreate);

public sealed record ChatBlockRequest(bool Blocked);

public sealed record ChatPrivacyNoticeRequest(int Version = ChatLimits.PrivacyNoticeVersion);

/// <summary>Neue Chat-Funktionen (Runde 10): Echtzeit, Anhänge, Antworten, Reaktionen, Bearbeiten, Weiterleiten, Anheften, Suche, Baustellen-Gruppen.</summary>
internal static partial class ChatEndpoints
{
    private static readonly JsonSerializerOptions ExportJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static void MapFeatures(RouteGroupBuilder chat)
    {
        chat.MapGet("/events", EventsAsync).WithName("StreamChatEvents")
            .WithSummary("Echtzeit (Server-Sent Events): Ereignisse mit IDs, Herzschlag alle 20 s, Ende nach 15 min (Client verbindet neu)")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");
        chat.MapPost("/conversations/{id:guid}/typing", TypingAsync).WithName("ChatTyping").WithSummary("„… schreibt“ an die anderen Mitglieder")
            .ProducesProblem(StatusCodes.Status404NotFound);
        chat.MapGet("/messages/{id:guid}", MessageAsync).WithName("GetChatMessage").ProducesProblem(StatusCodes.Status404NotFound);
        chat.MapPut("/messages/{id:guid}", EditAsync).WithName("EditChatMessage").WithSummary("Eigene Nachricht bearbeiten (Zeitfenster, „bearbeitet“)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);
        chat.MapPut("/messages/{id:guid}/reaction", ReactAsync).WithName("SetChatReaction").WithSummary("Eigene Reaktion setzen oder entfernen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        chat.MapPost("/messages/{id:guid}/pin", PinAsync).WithName("PinChatMessage").WithSummary("Nachricht anheften bzw. lösen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status403Forbidden);
        chat.MapPost("/messages/{id:guid}/forward", ForwardAsync).WithName("ForwardChatMessage").WithSummary("Nachricht (mit Anhängen) weiterleiten")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        chat.MapPost("/conversations/{id:guid}/attachments", UploadAsync).WithName("UploadChatAttachment")
            .WithSummary("Foto oder PDF hochladen (Inhalt roh im Body, ?fileName=); wird verdichtet und verschlüsselt gespeichert")
            .Accepts<byte[]>("application/octet-stream", "image/jpeg", "image/png", "image/webp", "image/heic", "application/pdf")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge);
        chat.MapGet("/conversations/{id:guid}/attachments", MediaAsync).WithName("ListChatMedia").WithSummary("Medien und Dokumente der Unterhaltung")
            .ProducesProblem(StatusCodes.Status404NotFound);
        chat.MapGet("/attachments/{id:guid}/content", AttachmentContentAsync).WithName("DownloadChatAttachment")
            .WithSummary("Anhang (entschlüsselt); ?thumbnail=true für das Vorschaubild, ?download=true als Download").ProducesProblem(StatusCodes.Status404NotFound);
        chat.MapPost("/attachments/{id:guid}/bauakte", ToBauakteAsync).WithName("CopyChatAttachmentToBauakte")
            .WithSummary("Anhang als Foto/Dokument in die Bauakte übernehmen").RequirePermission(Permissions.FilesUpload)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        chat.MapPost("/conversations/{id:guid}/leave", LeaveAsync).WithName("LeaveChatGroup").WithSummary("Gruppe verlassen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        chat.MapPut("/conversations/{id:guid}/image", SetImageAsync).WithName("SetChatGroupImage").WithSummary("Gruppenbild setzen (Bild roh im Body)")
            .Accepts<byte[]>("image/jpeg", "image/png", "image/webp")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status403Forbidden);
        chat.MapDelete("/conversations/{id:guid}/image", RemoveImageAsync).WithName("RemoveChatGroupImage")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status403Forbidden);
        chat.MapGet("/conversations/{id:guid}/image", ImageAsync).WithName("GetChatGroupImage").ProducesProblem(StatusCodes.Status404NotFound);
        chat.MapGet("/search", SearchAsync).WithName("SearchChat").WithSummary("Suche in eigenen Unterhaltungen (Texte liegen verschlüsselt vor)");
        chat.MapPost("/privacy-notice", AcceptPrivacyAsync).WithName("AcceptChatPrivacyNotice")
            .WithSummary("Datenschutzhinweis bestätigt (Fassung aus /chat/status privacyNoticeVersion)");
        chat.MapGet("/me/settings", MySettingsAsync).WithName("GetMyChatSettings");
        chat.MapPut("/me/settings", SaveMySettingsAsync).WithName("SaveMyChatSettings")
            .WithSummary("Vorschau in eigenen Pushnachrichten, Lesebestätigungen, Datenschutzhinweis bestätigt");
        chat.MapGet("/bauakten/{bauakteId:guid}/group", BauakteGroupAsync).WithName("GetBauakteChatGroup")
            .WithSummary("Baustellen-Gruppe einer Bauakte (für den Link in der Bauakte)").ProducesProblem(StatusCodes.Status404NotFound);
        chat.MapPost("/bauakten/{bauakteId:guid}/group", CreateBauakteGroupAsync).WithName("CreateBauakteChatGroup")
            .WithSummary("Baustellen-Gruppe anlegen (Team und Zuständige(r) der Bauakte automatisch)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status403Forbidden);
        chat.MapGet("/export/archive", OwnArchiveAsync).WithName("ExportOwnChatArchive")
            .WithSummary("Eigene Chatdaten mit Anhängen als ZIP (Auskunft, Datenübertragbarkeit)").Produces(StatusCodes.Status200OK, contentType: "application/zip");
    }

    private static void MapAdminFeatures(RouteGroupBuilder admin)
    {
        admin.MapPut("/users/{userId:guid}/block", BlockAsync).WithName("BlockChatUser")
            .WithSummary("Moderation: Person sperren (lesen ja, schreiben nein) bzw. entsperren").ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapGet("/users/{userId:guid}/export/archive", UserArchiveAsync).WithName("ExportUserChatArchive")
            .WithSummary("Chatdaten einer Person mit Anhängen als ZIP").Produces(StatusCodes.Status200OK, contentType: "application/zip")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task EventsAsync(HttpContext http, ChatHub hub, ICurrentUser user, TimeProvider clock) =>
        await ChatEventStream.RunAsync(http, hub, user.UserId, clock);

    private static async Task<Results<NoContent, ProblemHttpResult>> TypingAsync(
        Guid id, ChatService chat, EstrichOneDbContext db, ICurrentUser user, CancellationToken cancellationToken)
    {
        var (conversation, _) = await FindAsync(db, id, user.UserId, cancellationToken);
        if (conversation is null)
        {
            return NotFound();
        }

        var name = (await chat.NamesAsync([user.UserId], cancellationToken)).GetValueOrDefault(user.UserId);
        await chat.PublishAsync(id, new ChatEvent("typing", id, UserId: user.UserId, UserName: name), cancellationToken, except: user.UserId);
        return TypedResults.NoContent();
    }

    private static async Task<(ChatMessage? Message, ChatConversation? Conversation, ChatMember? Member)> FindMessageAsync(
        EstrichOneDbContext db, Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var message = await db.Set<ChatMessage>().SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (message is null)
        {
            return (null, null, null);
        }

        var (conversation, member) = await FindAsync(db, message.ConversationId, userId, cancellationToken);
        return conversation is null ? (null, null, null) : (message, conversation, member);
    }

    private static async Task<ChatMessageResponse> RenderOneAsync(ChatService chat, EstrichOneDbContext db, Guid userId, ChatMessage message, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var states = await MemberStatesAsync(chat, db, message.ConversationId, cancellationToken);
        return (await RenderAsync(chat, db, userId, [message], states, clock, cancellationToken)).Single();
    }

    private static async Task<Results<Ok<ChatMessageResponse>, ProblemHttpResult>> MessageAsync(
        Guid id, ChatService chat, EstrichOneDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken cancellationToken)
    {
        var (message, _, _) = await FindMessageAsync(db, id, user.UserId, cancellationToken);
        return message is null ? NotFound() : TypedResults.Ok(await RenderOneAsync(chat, db, user.UserId, message, clock, cancellationToken));
    }

    /// <summary>Schreibrecht in dieser Unterhaltung (Sperre, Ankündigungsgruppe).</summary>
    private static async Task<ProblemHttpResult?> CanPostAsync(ChatService chat, ChatConversation conversation, ChatMember member, Guid userId,
        CancellationToken cancellationToken)
    {
        if ((await chat.UserSettingsAsync(userId, cancellationToken))?.BlockedAt is not null)
        {
            return Forbidden("Sie sind im Chat gesperrt und können keine Nachrichten senden. Bitte an die Verwaltung wenden.", "CHAT_BLOCKED");
        }

        if (conversation.AnnouncementOnly && !member.IsAdmin && !await chat.IsChatAdminAsync(cancellationToken))
        {
            return Forbidden("In dieser Ankündigungsgruppe schreiben nur Admins.", "ANNOUNCEMENT_ONLY");
        }

        return null;
    }

    private static string? AttachmentHint(List<ChatAttachment> attachments, bool location) =>
        attachments.Count == 0 ? location ? "📍 Standort" : null
        : attachments.All(a => a.Kind == ChatAttachmentKind.Photo) ? attachments.Count == 1 ? "📷 Foto" : $"📷 {attachments.Count} Fotos"
        : attachments.Count == 1 ? "📄 Dokument" : $"📎 {attachments.Count} Anhänge";

    private static async Task<(ChatMessage? Message, ProblemHttpResult? Problem)> SendCoreAsync(
        ChatService chat, EstrichOneDbContext db, TimeProvider clock, ChatConversation conversation, ChatMember member, string? rawText, Guid? replyToId,
        IReadOnlyList<Guid> attachmentIds, ChatLocation? location, IReadOnlyList<Guid> mentionIds, Guid? clientMessageId, bool forwarded,
        CancellationToken cancellationToken, IReadOnlyList<ChatAttachment>? copiedAttachments = null)
    {
        if (await CanPostAsync(chat, conversation, member, member.UserId, cancellationToken) is { } denied)
        {
            return (null, denied);
        }

        var text = rawText?.Trim();
        if (text?.Length > ChatMessage.MaxText)
        {
            return (null, Validation($"Höchstens {ChatMessage.MaxText} Zeichen."));
        }

        var wanted = attachmentIds.Distinct().ToList();
        if (wanted.Count > ChatLimits.MaxAttachmentsPerMessage)
        {
            return (null, Validation($"Höchstens {ChatLimits.MaxAttachmentsPerMessage} Anhänge je Nachricht."));
        }

        var attachments = wanted.Count == 0 ? [] : await db.Set<ChatAttachment>()
            .Where(a => wanted.Contains(a.Id) && a.ConversationId == conversation.Id && a.UploadedByUserId == member.UserId && a.MessageId == null)
            .ToListAsync(cancellationToken);
        if (attachments.Count != wanted.Count)
        {
            return (null, Validation("Ein Anhang fehlt oder wurde schon gesendet. Bitte erneut hochladen."));
        }

        attachments.AddRange(copiedAttachments ?? []);
        if (string.IsNullOrEmpty(text) && attachments.Count == 0 && location is null)
        {
            return (null, Validation("Die Nachricht ist leer."));
        }

        if (replyToId is { } reply && !await db.Set<ChatMessage>().AnyAsync(m => m.Id == reply && m.ConversationId == conversation.Id, cancellationToken))
        {
            return (null, Validation("Die zitierte Nachricht gibt es in dieser Unterhaltung nicht."));
        }

        var memberIds = await db.Set<ChatMember>().AsNoTracking().Where(m => m.ConversationId == conversation.Id).Select(m => m.UserId).ToListAsync(cancellationToken);
        var mentions = mentionIds.Where(id => id != member.UserId && memberIds.Contains(id)).ToList();
        var now = clock.GetUtcNow();
        var message = ChatMessage.Create(conversation.Id, member.UserId, string.IsNullOrEmpty(text) ? null : chat.Protect(text), clientMessageId, now, replyToId,
            forwarded, mentions, location is null ? null : chat.ProtectLocation(location.Latitude, location.Longitude, location.Label));
        db.Add(message);
        foreach (var attachment in attachments)
        {
            attachment.AttachTo(message.Id);
        }

        conversation.Touch(now);
        member.Read(now);
        await db.SaveChangesAsync(cancellationToken);
        await chat.NotifyAsync(conversation, message, text, AttachmentHint(attachments, location is not null), cancellationToken);
        await chat.PublishAsync(conversation.Id, new ChatEvent("message", conversation.Id, message.Id, member.UserId), cancellationToken);
        return (message, null);
    }

    private static async Task<Results<Ok<ChatMessageResponse>, ProblemHttpResult>> EditAsync(
        Guid id, EditChatMessageRequest request, ChatService chat, EstrichOneDbContext db, ICurrentUser user, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var (message, conversation, member) = await FindMessageAsync(db, id, user.UserId, cancellationToken);
        if (message is null || conversation is null || member is null)
        {
            return NotFound();
        }

        if (message.SenderUserId != user.UserId || message.DeletedAt is not null)
        {
            return Forbidden("Nur eigene Nachrichten lassen sich bearbeiten.");
        }

        if (await CanPostAsync(chat, conversation, member, user.UserId, cancellationToken) is { } denied)
        {
            return denied;
        }

        var settings = await chat.SettingsAsync(cancellationToken);
        if (settings.EditWindowMinutes <= 0 || clock.GetUtcNow() - message.CreatedAt > TimeSpan.FromMinutes(settings.EditWindowMinutes))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Zeitfenster abgelaufen",
                detail: $"Nachrichten lassen sich nur innerhalb von {settings.EditWindowMinutes} Minuten nach dem Senden bearbeiten.",
                extensions: new Dictionary<string, object?> { ["code"] = "EDIT_WINDOW_EXPIRED" });
        }

        var text = request.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return Validation("Die Nachricht ist leer. Zum Entfernen „Löschen für alle“ verwenden.");
        }

        var memberIds = await db.Set<ChatMember>().AsNoTracking().Where(m => m.ConversationId == conversation.Id).Select(m => m.UserId).ToListAsync(cancellationToken);
        message.Edit(chat.Protect(text), (request.MentionIds ?? []).Where(m => m != user.UserId && memberIds.Contains(m)), clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await chat.PublishAsync(conversation.Id, new ChatEvent("message.updated", conversation.Id, id), cancellationToken);
        return TypedResults.Ok(await RenderOneAsync(chat, db, user.UserId, message, clock, cancellationToken));
    }

    private static async Task<Results<Ok<ChatMessageResponse>, ProblemHttpResult>> ReactAsync(
        Guid id, ChatReactionRequest request, ChatService chat, EstrichOneDbContext db, ICurrentUser user, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var (message, conversation, _) = await FindMessageAsync(db, id, user.UserId, cancellationToken);
        if (message is null || conversation is null || message.DeletedAt is not null)
        {
            return NotFound();
        }

        if ((await chat.UserSettingsAsync(user.UserId, cancellationToken))?.BlockedAt is not null)
        {
            return Forbidden("Sie sind im Chat gesperrt.", "CHAT_BLOCKED");
        }

        var emoji = request.Emoji?.Trim();
        if (emoji is { Length: > ChatReaction.MaxEmoji } || (emoji is { Length: > 0 } && emoji.Any(char.IsLetterOrDigit)))
        {
            return Validation("Bitte ein Emoji wählen.");
        }

        var existing = await db.Set<ChatReaction>().SingleOrDefaultAsync(r => r.MessageId == id && r.UserId == user.UserId, cancellationToken);
        if (string.IsNullOrEmpty(emoji))
        {
            if (existing is not null)
            {
                db.Remove(existing);
            }
        }
        else if (existing is null)
        {
            db.Add(ChatReaction.Create(id, user.UserId, emoji, clock.GetUtcNow()));
        }
        else
        {
            existing.Change(emoji, clock.GetUtcNow());
        }

        await db.SaveChangesAsync(cancellationToken);
        await chat.PublishAsync(conversation.Id, new ChatEvent("reaction", conversation.Id, id, user.UserId), cancellationToken);
        return TypedResults.Ok(await RenderOneAsync(chat, db, user.UserId, message, clock, cancellationToken));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> PinAsync(
        Guid id, ChatPinRequest request, ChatService chat, EstrichOneDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken cancellationToken)
    {
        var (message, conversation, member) = await FindMessageAsync(db, id, user.UserId, cancellationToken);
        if (message is null || conversation is null || member is null || message.DeletedAt is not null)
        {
            return NotFound();
        }

        if (await CanPostAsync(chat, conversation, member, user.UserId, cancellationToken) is { } denied)
        {
            return denied;
        }

        message.Pin(request.Pinned ? user.UserId : null, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await chat.PublishAsync(conversation.Id, new ChatEvent("message.updated", conversation.Id, id), cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ForwardChatMessageResponse>, ProblemHttpResult>> ForwardAsync(
        Guid id, ForwardChatMessageRequest request, ChatService chat, EstrichOneDbContext db, ICurrentUser user, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var (message, _, _) = await FindMessageAsync(db, id, user.UserId, cancellationToken);
        if (message is null || message.DeletedAt is not null)
        {
            return NotFound();
        }

        var targets = request.ConversationIds.Distinct().Take(20).ToList();
        if (targets.Count == 0)
        {
            return Validation("Bitte mindestens eine Unterhaltung wählen.");
        }

        var text = chat.Unprotect(message.ProtectedText);
        var location = chat.UnprotectLocation(message.ProtectedLocation);
        var sources = await db.Set<ChatAttachment>().AsNoTracking().Where(a => a.MessageId == id).OrderBy(a => a.CreatedAt).ToListAsync(cancellationToken);
        var results = new List<ChatMessage>();
        foreach (var target in targets)
        {
            var (conversation, member) = await FindAsync(db, target, user.UserId, cancellationToken);
            if (conversation is null || member is null)
            {
                return NotFound();
            }

            var now = clock.GetUtcNow();
            var copies = new List<ChatAttachment>();
            foreach (var source in sources)
            {
                var copyId = Guid.CreateVersion7(now);
                var key = ChatBlobs.KeyFor(copyId, now);
                await chat.Blobs.CopyAsync(source.StorageKey, key, cancellationToken);
                var copy = ChatAttachment.Create(copyId, target, user.UserId, source.Kind, source.ProtectedName, source.ContentType, source.SizeBytes, source.Width,
                    source.Height, key, source.HasThumbnail, now);
                db.Add(copy);
                copies.Add(copy);
            }

            var (sent, problem) = await SendCoreAsync(chat, db, clock, conversation, member, text, null, [], location, [], null, forwarded: true,
                cancellationToken, copies);
            if (problem is not null)
            {
                chat.Blobs.Delete(copies.Select(c => c.StorageKey));
                return problem;
            }

            results.Add(sent!);
        }

        var rendered = new List<ChatMessageResponse>();
        foreach (var sent in results)
        {
            rendered.Add(await RenderOneAsync(chat, db, user.UserId, sent, clock, cancellationToken));
        }

        return TypedResults.Ok(new ForwardChatMessageResponse(rendered));
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, long max, CancellationToken cancellationToken)
    {
        if (request.ContentLength > max)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > max)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static ProblemHttpResult TooLarge() => TypedResults.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Datei zu groß",
        detail: $"Anhänge dürfen höchstens {ChatLimits.MaxAttachmentBytes / 1024 / 1024} MB groß sein.",
        extensions: new Dictionary<string, object?> { ["code"] = "FILE_TOO_LARGE" });

    private static async Task<Results<Created<ChatAttachmentResponse>, ProblemHttpResult>> UploadAsync(
        Guid id, HttpRequest http, ChatService chat, EstrichOneDbContext db, ICurrentUser user, IMediaPreparation media, TimeProvider clock,
        CancellationToken cancellationToken, [FromQuery] string? fileName = null)
    {
        var (conversation, member) = await FindAsync(db, id, user.UserId, cancellationToken);
        if (conversation is null || member is null)
        {
            return NotFound();
        }

        if (await CanPostAsync(chat, conversation, member, user.UserId, cancellationToken) is { } denied)
        {
            return denied;
        }

        if (!(await chat.SettingsAsync(cancellationToken)).AttachmentsEnabled)
        {
            return Validation("Anhänge sind im Chat ausgeschaltet (Einstellungen → Module → Chat).");
        }

        var content = await ReadBodyAsync(http, ChatLimits.MaxAttachmentBytes, cancellationToken);
        if (content is null)
        {
            return TooLarge();
        }

        if (content.Length == 0)
        {
            return Validation("Die Datei ist leer.");
        }

        var prepared = await media.PrepareAsync(content, cancellationToken);
        if (prepared is null)
        {
            return Validation("Nur Fotos (JPEG, PNG, WebP, HEIC) und PDF-Dokumente sind erlaubt.");
        }

        var now = clock.GetUtcNow();
        var attachmentId = Guid.CreateVersion7(now);
        var key = ChatBlobs.KeyFor(attachmentId, now);
        await chat.Blobs.WriteAsync(key, prepared.Content, thumbnail: false, cancellationToken);
        if (prepared.Thumbnail is { } thumbnail)
        {
            await chat.Blobs.WriteAsync(key, thumbnail, thumbnail: true, cancellationToken);
        }

        var name = SafeName(fileName, prepared);
        var attachment = ChatAttachment.Create(attachmentId, id, user.UserId, prepared.IsImage ? ChatAttachmentKind.Photo : ChatAttachmentKind.Document,
            chat.Protect(name), prepared.ContentType, prepared.Content.Length, prepared.Width, prepared.Height, key, prepared.Thumbnail is not null, now);
        db.Add(attachment);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/chat/attachments/{attachmentId}/content", ToAttachment(chat, attachment));
    }

    private static string SafeName(string? fileName, PreparedMedia prepared)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);
        name = string.Join("_", name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        var extension = prepared.ContentType switch
        {
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg",
        };
        if (name.Length == 0)
        {
            name = (prepared.IsImage ? "Foto" : "Dokument") + extension;
        }
        else if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) && !(extension == ".jpg" && name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)))
        {
            name = Path.GetFileNameWithoutExtension(name) + extension;
        }

        return name.Length > 200 ? name[^200..] : name;
    }

    private static async Task<Results<Ok<List<ChatAttachmentResponse>>, ProblemHttpResult>> MediaAsync(
        Guid id, ChatService chat, EstrichOneDbContext db, ICurrentUser user, CancellationToken cancellationToken,
        [FromQuery] string? kind = null, [FromQuery] int? limit = null)
    {
        var (conversation, _) = await FindAsync(db, id, user.UserId, cancellationToken);
        if (conversation is null)
        {
            return NotFound();
        }

        var query = db.Set<ChatAttachment>().AsNoTracking().Where(a => a.ConversationId == id && a.MessageId != null);
        if (Enum.TryParse<ChatAttachmentKind>(kind, ignoreCase: true, out var k))
        {
            query = query.Where(a => a.Kind == k);
        }

        var rows = await query.OrderByDescending(a => a.CreatedAt).Take(Math.Clamp(limit ?? 200, 1, 500)).ToListAsync(cancellationToken);
        return TypedResults.Ok(rows.Select(a => ToAttachment(chat, a)).ToList());
    }

    private static async Task<ChatAttachment?> ReadableAttachmentAsync(EstrichOneDbContext db, Guid id, Guid userId, CancellationToken cancellationToken)
    {
        var attachment = await db.Set<ChatAttachment>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (attachment is null || (attachment.MessageId is null && attachment.UploadedByUserId != userId))
        {
            return null;
        }

        return await db.Set<ChatMember>().AnyAsync(m => m.ConversationId == attachment.ConversationId && m.UserId == userId, cancellationToken) ? attachment : null;
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> AttachmentContentAsync(
        Guid id, ChatService chat, EstrichOneDbContext db, ICurrentUser user, HttpContext http, CancellationToken cancellationToken,
        [FromQuery] bool thumbnail = false, [FromQuery] bool download = false, [FromQuery] string? variant = null)
    {
        thumbnail |= variant is "thumb" or "thumbnail";
        var attachment = await ReadableAttachmentAsync(db, id, user.UserId, cancellationToken);
        if (attachment is null)
        {
            return NotFound();
        }

        var useThumbnail = thumbnail && attachment.HasThumbnail;
        var content = await chat.Blobs.ReadAsync(attachment.StorageKey, useThumbnail, cancellationToken);
        if (content is null)
        {
            return NotFound();
        }

        http.Response.Headers.CacheControl = "private, max-age=86400";
        var name = chat.Unprotect(attachment.ProtectedName) ?? "anhang";
        return TypedResults.File(content, useThumbnail ? "image/webp" : attachment.ContentType, download ? name : null);
    }

    private static async Task<Results<Ok<ChatAttachmentToBauakteResponse>, ProblemHttpResult>> ToBauakteAsync(
        Guid id, ChatAttachmentToBauakteRequest request, ChatService chat, EstrichOneDbContext db, ICurrentUser user, IBauakteAccess bauakten,
        IGeneratedDocuments documents, CancellationToken cancellationToken)
    {
        var attachment = await ReadableAttachmentAsync(db, id, user.UserId, cancellationToken);
        if (attachment is null)
        {
            return NotFound();
        }

        var conversation = await db.Set<ChatConversation>().AsNoTracking().SingleAsync(c => c.Id == attachment.ConversationId, cancellationToken);
        var target = request.BauakteId ?? conversation.BauakteId;
        if (target is not { } bauakteId)
        {
            return Validation("Bitte eine Bauakte wählen.");
        }

        if (!await bauakten.CanReadAsync(bauakteId, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Bauakte nicht gefunden",
                extensions: new Dictionary<string, object?> { ["code"] = "NOT_FOUND" });
        }

        var content = await chat.Blobs.ReadAsync(attachment.StorageKey, thumbnail: false, cancellationToken);
        if (content is null)
        {
            return NotFound();
        }

        var fileId = await documents.StoreAttachmentAsync(bauakteId, user.UserId, chat.Unprotect(attachment.ProtectedName) ?? "chat-anhang", attachment.ContentType,
            content, attachment.Kind == ChatAttachmentKind.Photo, request.Caption, cancellationToken);
        return TypedResults.Ok(new ChatAttachmentToBauakteResponse(fileId, bauakteId));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> LeaveAsync(
        Guid id, ChatService chat, EstrichOneDbContext db, ICurrentUser user, IAuditLog audit, CancellationToken cancellationToken)
    {
        var (conversation, member) = await FindAsync(db, id, user.UserId, cancellationToken);
        if (conversation is null || member is null)
        {
            return NotFound();
        }

        if (conversation.Kind != ChatConversationKind.Group)
        {
            return Validation("Einzelchats lassen sich nicht verlassen – stattdessen stumm schalten.");
        }

        if (member.FromCrew)
        {
            return Validation("Sie gehören über Kolonne bzw. Bauakte automatisch dazu. Stattdessen stumm schalten.");
        }

        db.Remove(member);
        if (member.IsAdmin)
        {
            var others = await db.Set<ChatMember>().Where(m => m.ConversationId == id && m.UserId != user.UserId).OrderBy(m => m.JoinedAt).ToListAsync(cancellationToken);
            if (others.Count > 0 && !others.Any(m => m.IsAdmin))
            {
                others[0].SetAdmin(true); // Wie gewohnt: ohne Admin übernimmt das älteste Mitglied.
            }
        }

        audit.Record("chat.group_left", user.UserId, "chat_conversation", id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        await chat.PublishAsync(id, new ChatEvent("conversation", id), cancellationToken, extra: [user.UserId]);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ChatConversationSummary>, ProblemHttpResult>> SetImageAsync(
        Guid id, HttpRequest http, ChatService chat, EstrichOneDbContext db, ICurrentUser user, IMediaPreparation media, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var (conversation, member) = await FindAsync(db, id, user.UserId, cancellationToken);
        if (conversation is null)
        {
            return NotFound();
        }

        if (!await chat.CanManageAsync(conversation, member, cancellationToken))
        {
            return Forbidden("Nur Gruppenadmins können das Gruppenbild ändern.");
        }

        var content = await ReadBodyAsync(http, 15L * 1024 * 1024, cancellationToken);
        if (content is null)
        {
            return TooLarge();
        }

        if ((await media.PrepareAsync(content, cancellationToken)) is not { IsImage: true } || media.Square(content, 256) is not { } square)
        {
            return Validation("Bitte ein Bild (JPEG, PNG, WebP) wählen.");
        }

        var key = $"chat/groups/{id:N}";
        await chat.Blobs.WriteAsync(key, square, thumbnail: false, cancellationToken);
        conversation.SetImage(key, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await chat.PublishAsync(id, new ChatEvent("conversation", id), cancellationToken);
        return TypedResults.Ok(await SummaryAsync(chat, db, user.UserId, conversation, cancellationToken));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RemoveImageAsync(
        Guid id, ChatService chat, EstrichOneDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken cancellationToken)
    {
        var (conversation, member) = await FindAsync(db, id, user.UserId, cancellationToken);
        if (conversation is null)
        {
            return NotFound();
        }

        if (!await chat.CanManageAsync(conversation, member, cancellationToken))
        {
            return Forbidden("Nur Gruppenadmins können das Gruppenbild ändern.");
        }

        if (conversation.ImageKey is { } key)
        {
            chat.Blobs.Delete([key]);
        }

        conversation.SetImage(null, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await chat.PublishAsync(id, new ChatEvent("conversation", id), cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ImageAsync(
        Guid id, ChatService chat, EstrichOneDbContext db, ICurrentUser user, HttpContext http, CancellationToken cancellationToken)
    {
        var (conversation, _) = await FindAsync(db, id, user.UserId, cancellationToken);
        if (conversation?.ImageKey is not { } key || await chat.Blobs.ReadAsync(key, false, cancellationToken) is not { } content)
        {
            return NotFound();
        }

        http.Response.Headers.CacheControl = "private, max-age=86400";
        return TypedResults.File(content, "image/webp");
    }

    private static async Task<Ok<ChatSearchResponse>> SearchAsync(
        ChatService chat, EstrichOneDbContext db, ICurrentUser user, CancellationToken cancellationToken,
        [FromQuery] string? q = null, [FromQuery] Guid? conversationId = null)
    {
        const int Scan = 5000;
        var term = q?.Trim();
        if (string.IsNullOrEmpty(term) || term.Length < 2)
        {
            return TypedResults.Ok(new ChatSearchResponse([], 0, false));
        }

        var mine = db.Set<ChatMember>().Where(m => m.UserId == user.UserId).Select(m => m.ConversationId);
        var query = db.Set<ChatMessage>().AsNoTracking().Where(m => mine.Contains(m.ConversationId) && m.ProtectedText != null);
        if (conversationId is { } only)
        {
            query = query.Where(m => m.ConversationId == only);
        }

        // Texte liegen nur verschlüsselt vor: die neuesten Nachrichten entschlüsseln und vergleichen (nie Klartext-Index).
        var rows = await query.OrderByDescending(m => m.CreatedAt).Take(Scan + 1).ToListAsync(cancellationToken);
        var hits = new List<(ChatMessage Message, string Text)>();
        foreach (var row in rows.Take(Scan))
        {
            if (chat.Unprotect(row.ProtectedText) is { } text && text.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            {
                hits.Add((row, text));
                if (hits.Count >= 50)
                {
                    break;
                }
            }
        }

        var conversationIds = hits.Select(h => h.Message.ConversationId).Distinct().ToList();
        var conversations = await db.Set<ChatConversation>().AsNoTracking().Where(c => conversationIds.Contains(c.Id)).ToListAsync(cancellationToken);
        var summaries = (await SummariesAsync(chat, db, user.UserId, conversations,
            await db.Set<ChatMember>().AsNoTracking().Where(m => m.UserId == user.UserId).ToListAsync(cancellationToken), cancellationToken)).ToDictionary(s => s.Id);
        var names = await chat.NamesAsync(hits.Select(h => h.Message.SenderUserId), cancellationToken);
        return TypedResults.Ok(new ChatSearchResponse(hits.Select(h => new ChatSearchHit(h.Message.Id, h.Message.ConversationId,
            summaries.GetValueOrDefault(h.Message.ConversationId)?.Title ?? "Unterhaltung", names.GetValueOrDefault(h.Message.SenderUserId) ?? "Ehemaliger Benutzer",
            Snippet(h.Text, term), h.Message.CreatedAt)).ToList(), Math.Min(rows.Count, Scan), rows.Count > Scan));
    }

    /// <summary>Ausschnitt um den Treffer (höchstens ~140 Zeichen).</summary>
    internal static string Snippet(string text, string term)
    {
        var flat = ChatService.Preview(text, int.MaxValue);
        var index = flat.IndexOf(term, StringComparison.CurrentCultureIgnoreCase);
        if (flat.Length <= 140 || index < 0)
        {
            return ChatService.Preview(flat, 140);
        }

        var start = Math.Max(0, index - 50);
        var length = Math.Min(140, flat.Length - start);
        return string.Concat(start > 0 ? "…" : string.Empty, flat.AsSpan(start, length), start + length < flat.Length ? "…" : string.Empty);
    }

    private static async Task<NoContent> AcceptPrivacyAsync(
        ChatPrivacyNoticeRequest request, ChatService chat, EstrichOneDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken cancellationToken)
    {
        _ = request;
        await SaveMySettingsAsync(new SaveChatMySettingsRequest(PrivacyAccepted: true), chat, db, user, clock, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<ChatMySettings>> MySettingsAsync(ChatService chat, ICurrentUser user, CancellationToken cancellationToken)
    {
        var row = await chat.UserSettingsAsync(user.UserId, cancellationToken) ?? ChatUserSettings.Create(user.UserId);
        var settings = await chat.SettingsAsync(cancellationToken);
        return TypedResults.Ok(new ChatMySettings(row.ShowPreview, row.ReadReceipts, row.PrivacyAcceptedAt, row.BlockedAt is not null, settings.ShowPreview));
    }

    private static async Task<Ok<ChatMySettings>> SaveMySettingsAsync(
        SaveChatMySettingsRequest request, ChatService chat, EstrichOneDbContext db, ICurrentUser user, TimeProvider clock, CancellationToken cancellationToken)
    {
        var row = await chat.UserSettingsAsync(user.UserId, cancellationToken);
        if (row is null)
        {
            row = ChatUserSettings.Create(user.UserId);
            db.Add(row);
        }

        row.ShowPreview = request.ShowPreview ?? row.ShowPreview;
        row.ReadReceipts = request.ReadReceipts ?? row.ReadReceipts;
        if (request.PrivacyAccepted == true)
        {
            row.PrivacyAcceptedAt ??= clock.GetUtcNow();
        }

        await db.SaveChangesAsync(cancellationToken);
        return await MySettingsAsync(chat, user, cancellationToken);
    }

    private static async Task<Results<Ok<ChatBauakteGroup>, ProblemHttpResult>> BauakteGroupAsync(
        Guid bauakteId, ChatService chat, EstrichOneDbContext db, ICurrentUser user, IBauakteAccess bauakten, IBauakteReportSource reports,
        CancellationToken cancellationToken)
    {
        if (!await bauakten.CanReadAsync(bauakteId, cancellationToken))
        {
            return NotFound();
        }

        var group = await db.Set<ChatConversation>().AsNoTracking().SingleOrDefaultAsync(c => c.BauakteId == bauakteId, cancellationToken);
        var members = group is null ? [] : await db.Set<ChatMember>().AsNoTracking().Where(m => m.ConversationId == group.Id).Select(m => m.UserId)
            .ToListAsync(cancellationToken);
        var canCreate = group is null && (await chat.IsChatAdminAsync(cancellationToken)
            || (await reports.TeamUserIdsAsync(bauakteId, cancellationToken)).Contains(user.UserId));
        return TypedResults.Ok(new ChatBauakteGroup(bauakteId, group?.Id, group?.Title, members.Contains(user.UserId), members.Count, canCreate));
    }

    private static async Task<Results<Created<ChatConversationSummary>, Ok<ChatConversationSummary>, ProblemHttpResult>> CreateBauakteGroupAsync(
        Guid bauakteId, ChatService chat, EstrichOneDbContext db, ICurrentUser user, IBauakteAccess bauakten, IBauakteReportSource reports, IAuditLog audit,
        TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!await bauakten.CanReadAsync(bauakteId, cancellationToken) || await reports.GetAsync(bauakteId, cancellationToken) is not { } info)
        {
            return NotFound();
        }

        var existing = await db.Set<ChatConversation>().SingleOrDefaultAsync(c => c.BauakteId == bauakteId, cancellationToken);
        if (existing is not null)
        {
            if (!await db.Set<ChatMember>().AnyAsync(m => m.ConversationId == existing.Id && m.UserId == user.UserId, cancellationToken))
            {
                return Forbidden("Die Baustellen-Gruppe gibt es schon; Sie sind kein Mitglied.");
            }

            return TypedResults.Ok(await SummaryAsync(chat, db, user.UserId, existing, cancellationToken));
        }

        var team = await reports.TeamUserIdsAsync(bauakteId, cancellationToken);
        if (!team.Contains(user.UserId) && !await chat.IsChatAdminAsync(cancellationToken))
        {
            return Forbidden("Baustellen-Gruppen legen Team-Mitglieder der Bauakte oder die Chat-Verwaltung an.");
        }

        var now = clock.GetUtcNow();
        var title = ChatConversation.Clip($"{info.Number} {info.Title}".Trim(), ChatConversation.MaxTitle) ?? "Baustelle";
        var address = string.Join(", ", new[] { info.Street, $"{info.PostalCode} {info.City}".Trim() }.Where(p => !string.IsNullOrWhiteSpace(p)));
        var conversation = ChatConversation.Group(title, null, user.UserId, now, bauakteId,
            address.Length > 0 ? $"Baustellen-Gruppe · {address}" : "Baustellen-Gruppe");
        db.Add(conversation);
        db.Add(ChatMember.Create(conversation.Id, user.UserId, now, fromCrew: team.Contains(user.UserId), isAdmin: true));
        audit.Record("chat.conversation_created", user.UserId, "chat_conversation", conversation.Id.ToString(),
            new Dictionary<string, string?> { ["kind"] = "bauakte", ["bauakte"] = bauakteId.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        await chat.SyncCrewGroupsAsync(cancellationToken);
        await chat.PublishAsync(conversation.Id, new ChatEvent("conversation", conversation.Id), cancellationToken);
        return TypedResults.Created($"/api/v1/chat/conversations/{conversation.Id}", await SummaryAsync(chat, db, user.UserId, conversation, cancellationToken));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> BlockAsync(
        Guid userId, ChatBlockRequest request, ChatService chat, EstrichOneDbContext db, ICurrentUser user, IAuditLog audit, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if ((await chat.NamesAsync([userId], cancellationToken)).Count == 0)
        {
            return NotFound();
        }

        var row = await chat.UserSettingsAsync(userId, cancellationToken);
        if (row is null)
        {
            row = ChatUserSettings.Create(userId);
            db.Add(row);
        }

        row.BlockedAt = request.Blocked ? row.BlockedAt ?? clock.GetUtcNow() : null;
        row.BlockedByUserId = request.Blocked ? user.UserId : null;
        audit.Record(request.Blocked ? "chat.user_blocked" : "chat.user_unblocked", user.UserId, "user", userId.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> ArchiveAsync(ChatService chat, EstrichOneDbContext db, Guid userId, string name, Guid actor, string by, IAuditLog audit,
        TimeProvider clock, HttpContext http, CancellationToken cancellationToken)
    {
        var export = await ExportAsync(chat, db, userId, name, clock, cancellationToken);
        var messageIds = export.Messages.Select(m => m.Id).ToList();
        var attachments = await db.Set<ChatAttachment>().AsNoTracking().Where(a => a.MessageId != null && messageIds.Contains(a.MessageId.Value))
            .ToListAsync(cancellationToken);
        var buffer = new MemoryStream();
        await using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using (var entry = await zip.CreateEntry("chat-export.json", CompressionLevel.Optimal).OpenAsync(cancellationToken))
            {
                await JsonSerializer.SerializeAsync(entry, export, ExportJson, cancellationToken);
            }

            foreach (var attachment in attachments)
            {
                if (await chat.Blobs.ReadAsync(attachment.StorageKey, false, cancellationToken) is { } content)
                {
                    var fileName = string.Join("_", (chat.Unprotect(attachment.ProtectedName) ?? "anhang").Split(Path.GetInvalidFileNameChars()));
                    await using var entry = await zip.CreateEntry($"anhaenge/{attachment.Id:N}_{fileName}", CompressionLevel.NoCompression).OpenAsync(cancellationToken);
                    await entry.WriteAsync(content, cancellationToken);
                }
            }
        }

        audit.Record("chat.data_exported", actor, "user", userId.ToString(), new Dictionary<string, string?>
        {
            ["messages"] = export.Messages.Count.ToString(Invariant), ["attachments"] = attachments.Count.ToString(Invariant), ["by"] = by,
        });
        await db.SaveChangesAsync(cancellationToken);
        buffer.Position = 0;
        return TypedResults.File(buffer, "application/zip", $"chat-export-{clock.GetUtcNow():yyyy-MM-dd}.zip");
    }

    private static async Task<IResult> OwnArchiveAsync(ChatService chat, EstrichOneDbContext db, ICurrentUser user, IAuditLog audit, TimeProvider clock,
        HttpContext http, CancellationToken cancellationToken)
    {
        var name = (await chat.NamesAsync([user.UserId], cancellationToken)).GetValueOrDefault(user.UserId) ?? "Benutzer";
        return await ArchiveAsync(chat, db, user.UserId, name, user.UserId, "self", audit, clock, http, cancellationToken);
    }

    private static async Task<IResult> UserArchiveAsync(Guid userId, ChatService chat, EstrichOneDbContext db, ICurrentUser user, IAuditLog audit,
        TimeProvider clock, HttpContext http, CancellationToken cancellationToken)
    {
        var name = (await chat.NamesAsync([userId], cancellationToken)).GetValueOrDefault(userId);
        return name is null ? NotFound() : await ArchiveAsync(chat, db, userId, name, user.UserId, "admin", audit, clock, http, cancellationToken);
    }
}
