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
    INotifier notifier, ICurrentUser currentUser, TimeProvider clock, ILogger<ChatService> logger)
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

    /// <summary>Mitglieder der Kolonnengruppen an die Kolonnen angleichen (nur Personen mit Chat-Recht).</summary>
    public async Task SyncCrewGroupsAsync(CancellationToken cancellationToken)
    {
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

    /// <summary>Hinweis (Postfach/Push) an die übrigen Mitglieder; höchstens einer je Unterhaltung und 10 Minuten, solange ungelesen.</summary>
    public async Task NotifyAsync(ChatConversation conversation, ChatMessage message, string text, CancellationToken cancellationToken)
    {
        var settings = await SettingsAsync(cancellationToken);
        var members = await db.Set<ChatMember>().AsNoTracking()
            .Where(m => m.ConversationId == conversation.Id && m.UserId != message.SenderUserId && !m.Muted).ToListAsync(cancellationToken);
        if (members.Count == 0)
        {
            return;
        }

        var sender = (await NamesAsync([message.SenderUserId], cancellationToken)).GetValueOrDefault(message.SenderUserId) ?? "Unbekannt";
        var title = conversation.Kind == ChatConversationKind.Group ? $"{sender} in {conversation.Title}" : $"Nachricht von {sender}";
        var body = settings.ShowPreview ? Preview(text, 120) : "Neue Nachricht im Chat";
        var bucket = message.CreatedAt.ToUnixTimeSeconds() / 600;
        foreach (var member in members)
        {
            var streak = member.ReadUpTo?.ToUnixTimeSeconds() ?? 0;
            await notifier.NotifyAsync([member.UserId], new NotificationRequest(NotificationCategory.Chat, title, body, $"/chat/{conversation.Id}",
                ObjectType, conversation.Id, DedupKey: $"chat:{conversation.Id:N}:{streak}:{bucket}", Event: NotificationEvents.ChatMessage),
                cancellationToken);
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
