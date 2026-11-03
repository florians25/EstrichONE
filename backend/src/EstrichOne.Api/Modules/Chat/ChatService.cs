using System.Security.Cryptography;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Notifications.Exports;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Chat;

/// <summary>
/// Kern des Chats: Verschlüsselung der Texte (Data Protection, AES-256-GCM, Schlüsselring der Installation),
/// Mitgliedschaften (auch Kolonnengruppen), Zustell-/Lesestand und Hinweise für neue Nachrichten.
/// </summary>
internal sealed partial class ChatService(
    EstrichOneDbContext db, IDataProtectionProvider protection, IRoleAssignments roles, IEnumerable<INotificationAudience> audiences,
    INotifier notifier, ICurrentUser currentUser, TimeProvider clock, ILogger<ChatService> logger, ChatHub hub, ChatBlobs blobs,
    Bauakten.Exports.IBauakteReportSource bauakten, IUserPermissions permissions)
{
    public const string ProtectorPurpose = "EstrichOne.Chat.Messages.v1";
    public const string ObjectType = "chat";

    private IDataProtector Protector => protection.CreateProtector(ProtectorPurpose);

    public string Protect(string text) => Protector.Protect(text);

    public string? Unprotect(string? protectedText)
    {
        if (protectedText is null)
        {
            return null;
        }

        try
        {
            return Protector.Unprotect(protectedText);
        }
        catch (CryptographicException exception)
        {
            LogUnreadable(logger, exception);
            return null; // Schlüsselring verloren (z. B. Wiederherstellung ohne .keys) – Nachricht nicht mehr lesbar.
        }
    }

    private sealed record LocationPayload(double Latitude, double Longitude, string? Label);

    public string ProtectLocation(double latitude, double longitude, string? label) =>
        Protector.Protect(System.Text.Json.JsonSerializer.Serialize(new LocationPayload(latitude, longitude, ChatConversation.Clip(label, 300))));

    public ChatLocation? UnprotectLocation(string? protectedLocation)
    {
        if (Unprotect(protectedLocation) is not { } json)
        {
            return null;
        }

        var payload = System.Text.Json.JsonSerializer.Deserialize<LocationPayload>(json);
        return payload is null ? null : new ChatLocation(payload.Latitude, payload.Longitude, payload.Label);
    }

    public async Task<ChatSettingsRow> SettingsAsync(CancellationToken cancellationToken) =>
        await db.Set<ChatSettingsRow>().AsNoTracking().SingleOrDefaultAsync(cancellationToken) ?? new ChatSettingsRow();

    public static async Task<bool> IsAdminAsync(IUserPermissions permissions, CancellationToken cancellationToken) =>
        await permissions.HasAsync(Permissions.ChatAdmin, cancellationToken);

    /// <summary>Aktive Benutzer mit chat.use.</summary>
    public async Task<HashSet<Guid>> ChatUsersAsync(CancellationToken cancellationToken)
    {
        var users = new HashSet<Guid>(await roles.GetUserIdsWithPermissionAsync(Permissions.ChatUse, cancellationToken));
        users.UnionWith(await roles.GetUserIdsWithPermissionAsync(Permissions.ChatAdmin, cancellationToken));
        return users;
    }

    public async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var list = ids.Distinct().ToList();
        return await db.Set<User>().AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
    }

    public ChatHub Hub => hub;

    public async Task<bool> IsChatAdminAsync(CancellationToken cancellationToken) => await permissions.HasAsync(Permissions.ChatAdmin, cancellationToken);

    /// <summary>Gruppe verwalten: chat.admin oder Gruppenadmin.</summary>
    public async Task<bool> CanManageAsync(ChatConversation conversation, ChatMember? member, CancellationToken cancellationToken) =>
        conversation.Kind == ChatConversationKind.Group && (member?.IsAdmin == true || await IsChatAdminAsync(cancellationToken));

    public ChatBlobs Blobs => blobs;

    public async Task<ChatUserSettings?> UserSettingsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Set<ChatUserSettings>().SingleOrDefaultAsync(s => s.UserId == userId, cancellationToken);

    /// <summary>Ereignis an alle aktuellen Mitglieder (plus <paramref name="extra"/>, z. B. gerade Entfernte).</summary>
    public async Task PublishAsync(Guid conversationId, ChatEvent chatEvent, CancellationToken cancellationToken, IEnumerable<Guid>? extra = null,
        Guid? except = null)
    {
        var members = await db.Set<ChatMember>().AsNoTracking().Where(m => m.ConversationId == conversationId).Select(m => m.UserId).ToListAsync(cancellationToken);
        hub.Publish(members.Concat(extra ?? []).Where(id => id != except), chatEvent);
    }

    /// <summary>Anhänge (Dateien und Zeilen) zu den gewählten Nachrichten bzw. Unterhaltungen entfernen.</summary>
    public async Task<int> DeleteAttachmentsAsync(IQueryable<ChatAttachment> query, CancellationToken cancellationToken)
    {
        var keys = await query.Select(a => a.StorageKey).ToListAsync(cancellationToken);
        if (keys.Count == 0)
        {
            return 0;
        }

        var deleted = await query.ExecuteDeleteAsync(cancellationToken);
        blobs.Delete(keys);
        return deleted;
    }

    /// <summary>Gruppen mit automatischen Mitgliedern angleichen: Kolonnengruppen und Baustellen-Gruppen.</summary>
    public async Task SyncCrewGroupsAsync(CancellationToken cancellationToken)
    {
        await SyncBauakteGroupsAsync(cancellationToken);
        var groups = await db.Set<ChatConversation>().Where(c => c.CrewId != null).ToListAsync(cancellationToken);
        if (groups.Count == 0)
        {
            return;
        }

        var chatUsers = await ChatUsersAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var ids = groups.Select(g => g.Id).ToList();
        var members = await db.Set<ChatMember>().Where(m => ids.Contains(m.ConversationId)).ToListAsync(cancellationToken);
        foreach (var group in groups)
        {
            var crew = new HashSet<Guid>();
            foreach (var audience in audiences)
            {
                crew.UnionWith(await audience.CrewMembersAsync([group.CrewId!.Value], cancellationToken));
            }

            crew.IntersectWith(chatUsers);
            var current = members.Where(m => m.ConversationId == group.Id).ToList();
            foreach (var member in current.Where(m => m.FromCrew && !crew.Contains(m.UserId)))
            {
                db.Remove(member);
            }

            foreach (var userId in crew)
            {
                var existing = current.FirstOrDefault(m => m.UserId == userId);
                if (existing is null)
                {
                    db.Add(ChatMember.Create(group.Id, userId, now, fromCrew: true, readUpTo: group.LastActivityAt));
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Mitglieder der Baustellen-Gruppen an Team und Zuständigen der Bauakte angleichen (nur Personen mit Chat-Recht).</summary>
    private async Task SyncBauakteGroupsAsync(CancellationToken cancellationToken)
    {
        var groups = await db.Set<ChatConversation>().AsNoTracking().Where(c => c.BauakteId != null).ToListAsync(cancellationToken);
        if (groups.Count == 0)
        {
            return;
        }

        var chatUsers = await ChatUsersAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var ids = groups.Select(g => g.Id).ToList();
        var members = await db.Set<ChatMember>().Where(m => ids.Contains(m.ConversationId)).ToListAsync(cancellationToken);
        foreach (var group in groups)
        {
            var team = (await bauakten.TeamUserIdsAsync(group.BauakteId!.Value, cancellationToken)).Where(chatUsers.Contains).ToHashSet();
            var current = members.Where(m => m.ConversationId == group.Id).ToList();
            db.RemoveRange(current.Where(m => m.FromCrew && !team.Contains(m.UserId)));
            foreach (var userId in team.Where(id => current.All(m => m.UserId != id)))
            {
                db.Add(ChatMember.Create(group.Id, userId, now, fromCrew: true, readUpTo: group.LastActivityAt));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Hinweis (Postfach/Push) je Nachricht an die übrigen Mitglieder. Gruppiert je Unterhaltung (APNs thread-id); im Postfach
    /// bleibt nur der neueste. Stumm geschaltete Mitglieder nur bei @Erwähnung. Text nur, wenn Verwaltung und Person die Vorschau erlauben.
    /// </summary>
    public async Task NotifyAsync(ChatConversation conversation, ChatMessage message, string? text, string? attachmentHint, CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var members = (await db.Set<ChatMember>().AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id && m.UserId != message.SenderUserId).ToListAsync(cancellationToken))
            .Where(m => !m.IsMuted(now) || message.MentionedUserIds.Contains(m.UserId)).ToList();
        if (members.Count == 0)
        {
            return;
        }

        var userIds = members.Select(m => m.UserId).ToList();
        var personal = await db.Set<ChatUserSettings>().AsNoTracking().Where(s => userIds.Contains(s.UserId)).ToDictionaryAsync(s => s.UserId, cancellationToken);
        var sender = (await NamesAsync([message.SenderUserId], cancellationToken)).GetValueOrDefault(message.SenderUserId) ?? "Unbekannt";
        var title = conversation.Kind == ChatConversationKind.Group ? $"{sender} in {conversation.Title}" : $"Nachricht von {sender}";
        var content = string.IsNullOrWhiteSpace(text) ? attachmentHint ?? "Neue Nachricht" : attachmentHint is null ? text : $"{attachmentHint}: {text}";
        foreach (var member in members)
        {
            var mentioned = message.MentionedUserIds.Contains(member.UserId);
            var preview = settings.ShowPreview && (personal.GetValueOrDefault(member.UserId)?.ShowPreview ?? true);
            var body = preview ? Preview(content, 120) : mentioned ? "Hat Sie erwähnt" : "Neue Nachricht im Chat";
            await notifier.NotifyAsync([member.UserId], new NotificationRequest(NotificationCategory.Chat, title, body, $"/chat/{conversation.Id}",
                ObjectType, conversation.Id, mentioned ? NotificationUrgency.High : NotificationUrgency.Normal, DedupKey: $"chatmsg:{message.Id:N}",
                Event: NotificationEvents.ChatMessage, ReplacePrevious: true), cancellationToken);
        }
    }

    /// <summary>Gelesen bis jetzt; zugehörige Hinweise im Postfach ebenfalls gelesen.</summary>
    public async Task MarkReadAsync(ChatMember member, CancellationToken cancellationToken)
    {
        var latest = await db.Set<ChatMessage>().Where(m => m.ConversationId == member.ConversationId)
            .MaxAsync(m => (DateTimeOffset?)m.CreatedAt, cancellationToken);
        member.Read(latest ?? clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await notifier.MarkReadAsync(member.UserId, ObjectType, member.ConversationId, cancellationToken);
        var receipts = (await UserSettingsAsync(member.UserId, cancellationToken))?.ReadReceipts ?? true;
        await PublishAsync(member.ConversationId, new ChatEvent("read", member.ConversationId, UserId: receipts ? member.UserId : null), cancellationToken);
    }

    public Guid UserId => currentUser.UserId;

    public static string Preview(string text, int max)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return flat.Length > max ? flat[..(max - 1)] + "…" : flat;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Chatnachricht nicht entschlüsselbar (Schlüsselring fehlt oder beschädigt).")]
    private static partial void LogUnreadable(ILogger logger, Exception exception);
}

/// <summary>Löschfrist: Nachrichten älter als die eingestellte Anzahl Tage werden stündlich endgültig gelöscht.</summary>
internal sealed partial class ChatRetention(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ChatRetention> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), clock, stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
            do
            {
                try
                {
                    await CleanupAsync(stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogFailed(logger, exception);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Herunterfahren.
        }
    }

    public async Task<int> CleanupAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var settings = await db.Set<ChatSettingsRow>().SingleOrDefaultAsync(cancellationToken);
        var days = settings?.RetentionDays ?? ChatSettingsRow.DefaultRetentionDays;
        var cutoff = clock.GetUtcNow().AddDays(-days);
        var chat = scope.ServiceProvider.GetRequiredService<ChatService>();
        var orphaned = clock.GetUtcNow().AddDays(-1);
        await chat.DeleteAttachmentsAsync(db.Set<ChatAttachment>().Where(a => a.MessageId == null && a.CreatedAt < orphaned), cancellationToken);
        await chat.DeleteAttachmentsAsync(
            db.Set<ChatAttachment>().Where(a => a.MessageId != null && db.Set<ChatMessage>().Any(m => m.Id == a.MessageId && m.CreatedAt < cutoff)),
            cancellationToken);
        var deleted = await db.Set<ChatMessage>().Where(m => m.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        if (settings is not null)
        {
            settings.LastCleanupAt = clock.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }

        if (deleted > 0)
        {
            LogDeleted(logger, deleted, days);
        }

        return deleted;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Chat: {Count} Nachricht(en) nach Ablauf der Löschfrist ({Days} Tage) gelöscht.")]
    private static partial void LogDeleted(ILogger logger, int count, int days);

    [LoggerMessage(Level = LogLevel.Error, Message = "Chat: Löschlauf fehlgeschlagen.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
