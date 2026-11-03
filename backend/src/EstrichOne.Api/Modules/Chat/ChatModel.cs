using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Chat;

public enum ChatConversationKind
{
    /// <summary>Zwei Personen.</summary>
    Direct,

    /// <summary>Gruppe (frei zusammengestellt oder an eine Kolonne gebunden).</summary>
    Group,
}

/// <summary>Unterhaltung (Einzel- oder Gruppenchat).</summary>
public sealed class ChatConversation
{
    public const int MaxTitle = 120;

    public Guid Id { get; private set; }
    public ChatConversationKind Kind { get; private set; }
    public string? Title { get; private set; }

    /// <summary>Nur Einzelchat: beide Benutzer-IDs sortiert („a:b“) – höchstens ein Einzelchat je Paar.</summary>
    public string? DirectKey { get; private set; }

    /// <summary>Gruppe einer Kolonne: Mitglieder folgen der Kolonne.</summary>
    public Guid? CrewId { get; private set; }

    /// <summary>Baustellen-Gruppe: Mitglieder folgen Team und Zuständigem der Bauakte.</summary>
    public Guid? BauakteId { get; private set; }

    /// <summary>Gruppenbeschreibung (z. B. Zweck, Regeln).</summary>
    public string? Description { get; private set; }

    /// <summary>Gruppenbild (verschlüsselt gespeichert, Schlüssel relativ zum Dateispeicher).</summary>
    public string? ImageKey { get; private set; }

    public DateTimeOffset? ImageUpdatedAt { get; private set; }

    /// <summary>Ankündigungsgruppe: nur Gruppenadmins (und chat.admin) schreiben.</summary>
    public bool AnnouncementOnly { get; private set; }

    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; }

    public const int MaxDescription = 1000;

    public static ChatConversation Direct(Guid a, Guid b, Guid createdBy, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(), Kind = ChatConversationKind.Direct, DirectKey = DirectKeyOf(a, b), CreatedByUserId = createdBy, CreatedAt = now,
        LastActivityAt = now,
    };

    public static ChatConversation Group(string title, Guid? crewId, Guid createdBy, DateTimeOffset now, Guid? bauakteId = null,
        string? description = null, bool announcementOnly = false) => new()
    {
        Id = Guid.CreateVersion7(), Kind = ChatConversationKind.Group, Title = Clip(title, MaxTitle), CrewId = crewId, BauakteId = bauakteId,
        Description = Clip(description, MaxDescription), AnnouncementOnly = announcementOnly, CreatedByUserId = createdBy, CreatedAt = now,
        LastActivityAt = now,
    };

    public void Describe(string? description) => Description = Clip(description, MaxDescription);

    public void SetAnnouncementOnly(bool value) => AnnouncementOnly = value;

    public void SetImage(string? key, DateTimeOffset now)
    {
        ImageKey = key;
        ImageUpdatedAt = key is null ? null : now;
    }

    public static string DirectKeyOf(Guid a, Guid b) => a.CompareTo(b) < 0 ? $"{a:N}:{b:N}" : $"{b:N}:{a:N}";

    public void Rename(string title) => Title = Clip(title, MaxTitle);

    public void Touch(DateTimeOffset now) => LastActivityAt = now > LastActivityAt ? now : LastActivityAt;

    internal static string? Clip(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > max ? value.Trim()[..max] : value.Trim();
}

/// <summary>Teilnahme an einer Unterhaltung mit Lese- und Zustellstand.</summary>
public sealed class ChatMember
{
    public Guid ConversationId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset JoinedAt { get; private set; }

    /// <summary>Alles bis zu diesem Zeitpunkt hat der Client abgeholt („zugestellt“).</summary>
    public DateTimeOffset? DeliveredUpTo { get; private set; }

    /// <summary>Alles bis zu diesem Zeitpunkt ist gelesen.</summary>
    public DateTimeOffset? ReadUpTo { get; private set; }

    /// <summary>Stumm: keine Push-/Postfach-Hinweise für neue Nachrichten (außer @Erwähnungen).</summary>
    public bool Muted { get; private set; }

    /// <summary>Stumm bis zu diesem Zeitpunkt; <c>null</c> bei <see cref="Muted"/> = immer.</summary>
    public DateTimeOffset? MutedUntil { get; private set; }

    /// <summary>Gruppenadmin: darf Gruppe bearbeiten, Mitglieder verwalten und in Ankündigungsgruppen schreiben.</summary>
    public bool IsAdmin { get; private set; }

    /// <summary>Über die Kolonne Mitglied (folgt der Kolonne); sonst von Hand hinzugefügt.</summary>
    public bool FromCrew { get; private set; }

    /// <param name="readUpTo">Bei späterem Beitritt: Verlauf bis hier gilt als gelesen; <c>null</c> = alles ungelesen.</param>
    public static ChatMember Create(Guid conversationId, Guid userId, DateTimeOffset now, bool fromCrew = false, DateTimeOffset? readUpTo = null,
        bool isAdmin = false) =>
        new() { ConversationId = conversationId, UserId = userId, JoinedAt = now, DeliveredUpTo = readUpTo, ReadUpTo = readUpTo, FromCrew = fromCrew, IsAdmin = isAdmin };

    public void SetAdmin(bool isAdmin) => IsAdmin = isAdmin;

    public bool IsMuted(DateTimeOffset now) => Muted && (MutedUntil is null || MutedUntil > now);

    public void SetFromCrew(bool fromCrew) => FromCrew = fromCrew;

    public void Delivered(DateTimeOffset upTo)
    {
        if (DeliveredUpTo is null || upTo > DeliveredUpTo)
        {
            DeliveredUpTo = upTo;
        }
    }

    public void Read(DateTimeOffset upTo)
    {
        if (ReadUpTo is null || upTo > ReadUpTo)
        {
            ReadUpTo = upTo;
        }

        Delivered(upTo);
    }

    public void Mute(bool muted, DateTimeOffset? until = null)
    {
        Muted = muted;
        MutedUntil = muted ? until : null;
    }
}

/// <summary>Textnachricht. Der Text liegt nur verschlüsselt vor (ASP.NET Data Protection, AES-256-GCM).</summary>
public sealed class ChatMessage
{
    public const int MaxText = 4000;

    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid SenderUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Verschlüsselter Text; <c>null</c>, wenn die Nachricht gelöscht wurde.</summary>
    public string? ProtectedText { get; private set; }

    /// <summary>Vom Client vergebene ID (Offline-Warteschlange): dieselbe ID erzeugt keine zweite Nachricht.</summary>
    public Guid? ClientMessageId { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>Antwort auf diese Nachricht (Zitat).</summary>
    public Guid? ReplyToMessageId { get; private set; }

    /// <summary>Zuletzt bearbeitet (innerhalb des Zeitfensters).</summary>
    public DateTimeOffset? EditedAt { get; private set; }

    /// <summary>Weitergeleitet aus einer anderen Unterhaltung.</summary>
    public bool Forwarded { get; private set; }

    public DateTimeOffset? PinnedAt { get; private set; }
    public Guid? PinnedByUserId { get; private set; }

    /// <summary>@Erwähnte Personen (durchbrechen die Stummschaltung).</summary>
    public Guid[] MentionedUserIds { get; private set; } = [];

    /// <summary>Geteilter Standort (verschlüsseltes JSON mit Breite, Länge, Bezeichnung).</summary>
    public string? ProtectedLocation { get; private set; }

    public static ChatMessage Create(Guid conversationId, Guid sender, string? protectedText, Guid? clientMessageId, DateTimeOffset now,
        Guid? replyTo = null, bool forwarded = false, IEnumerable<Guid>? mentions = null, string? protectedLocation = null) => new()
    {
        Id = Guid.CreateVersion7(now), ConversationId = conversationId, SenderUserId = sender, CreatedAt = now, ProtectedText = protectedText,
        ClientMessageId = clientMessageId, ReplyToMessageId = replyTo, Forwarded = forwarded, MentionedUserIds = mentions?.Distinct().ToArray() ?? [],
        ProtectedLocation = protectedLocation,
    };

    public void Edit(string protectedText, IEnumerable<Guid> mentions, DateTimeOffset now)
    {
        ProtectedText = protectedText;
        MentionedUserIds = mentions.Distinct().ToArray();
        EditedAt = now;
    }

    public void Pin(Guid? by, DateTimeOffset now)
    {
        PinnedAt = by is null ? null : now;
        PinnedByUserId = by;
    }

    public void Delete(DateTimeOffset now)
    {
        ProtectedText = null;
        ProtectedLocation = null;
        MentionedUserIds = [];
        PinnedAt = null;
        PinnedByUserId = null;
        DeletedAt ??= now;
    }
}

/// <summary>Einstellungen des Chat-Moduls (eine Zeile).</summary>
public sealed class ChatSettingsRow : IVersionedEntity
{
    public const int SingletonId = 1;
    public const int DefaultRetentionDays = 90;
    public const int DefaultEditWindowMinutes = 15;

    public int Id { get; private set; } = SingletonId;

    /// <summary>
    /// Standard: aus. Ein Chat unter Beschäftigten ist mitbestimmungspflichtig (Betriebsrat, § 87 Abs. 1 Nr. 6 BetrVG) und
    /// gehört ins Verzeichnis der Verarbeitungstätigkeiten – der Betrieb schaltet ihn bewusst ein.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Nachrichten älter als diese Anzahl Tage werden gelöscht (7–730).</summary>
    public int RetentionDays { get; set; } = DefaultRetentionDays;

    /// <summary>Nachrichtentext in Pushnachricht und Postfach zeigen. Standard aus: nur „Neue Nachricht von …“.</summary>
    public bool ShowPreview { get; set; }

    /// <summary>Mitarbeiter ohne chat.admin dürfen auch untereinander Einzelchats beginnen (sonst nur mit dem Büro).</summary>
    public bool WorkersCanChatEachOther { get; set; } = true;

    /// <summary>Eigene Nachrichten lassen sich so viele Minuten nach dem Senden bearbeiten (0 = nie).</summary>
    public int EditWindowMinutes { get; set; } = DefaultEditWindowMinutes;

    /// <summary>Fotos und PDFs im Chat erlaubt.</summary>
    public bool AttachmentsEnabled { get; set; } = true;

    /// <summary>Einmalige Grundeinrichtung (Rechte für Standardrollen) erledigt.</summary>
    public bool Seeded { get; set; }

    public DateTimeOffset? LastCleanupAt { get; set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum ChatAttachmentKind
{
    Photo,
    Document,
}

/// <summary>
/// Anhang (Foto oder PDF) einer Nachricht. Inhalt, Vorschaubild und Dateiname liegen nur verschlüsselt vor; vor dem Senden
/// hochgeladen (<see cref="MessageId"/> leer), mit der Nachricht verknüpft, mit ihr gelöscht (Löschfrist, Löschen für alle).
/// </summary>
public sealed class ChatAttachment
{
    public Guid Id { get; private set; }
    public Guid ConversationId { get; private set; }
    public Guid? MessageId { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public ChatAttachmentKind Kind { get; private set; }
    public string ProtectedName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public string StorageKey { get; private set; } = string.Empty;
    public bool HasThumbnail { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static ChatAttachment Create(Guid id, Guid conversationId, Guid uploadedBy, ChatAttachmentKind kind, string protectedName, string contentType,
        long size, int? width, int? height, string storageKey, bool hasThumbnail, DateTimeOffset now) => new()
    {
        Id = id, ConversationId = conversationId, UploadedByUserId = uploadedBy, Kind = kind, ProtectedName = protectedName, ContentType = contentType,
        SizeBytes = size, Width = width, Height = height, StorageKey = storageKey, HasThumbnail = hasThumbnail, CreatedAt = now,
    };

    public void AttachTo(Guid messageId) => MessageId = messageId;
}

/// <summary>Reaktion (ein Emoji je Person und Nachricht).</summary>
public sealed class ChatReaction
{
    public const int MaxEmoji = 16;

    public Guid MessageId { get; private set; }
    public Guid UserId { get; private set; }
    public string Emoji { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public static ChatReaction Create(Guid messageId, Guid userId, string emoji, DateTimeOffset now) =>
        new() { MessageId = messageId, UserId = userId, Emoji = emoji, CreatedAt = now };

    public void Change(string emoji, DateTimeOffset now)
    {
        Emoji = emoji;
        CreatedAt = now;
    }
}

/// <summary>Persönliche Chat-Einstellungen und Moderationsstatus einer Person.</summary>
public sealed class ChatUserSettings
{
    public Guid UserId { get; private set; }

    /// <summary>Nachrichtentext in eigenen Pushnachrichten zeigen (nur wirksam, wenn die Verwaltung die Vorschau erlaubt).</summary>
    public bool ShowPreview { get; set; } = true;

    /// <summary>Lesebestätigungen senden. Aus: andere sehen nicht, wann diese Person gelesen hat.</summary>
    public bool ReadReceipts { get; set; } = true;

    /// <summary>Datenschutzhinweis beim ersten Öffnen bestätigt.</summary>
    public DateTimeOffset? PrivacyAcceptedAt { get; set; }

    /// <summary>Durch die Verwaltung gesperrt: darf lesen, aber nicht schreiben.</summary>
    public DateTimeOffset? BlockedAt { get; set; }

    public Guid? BlockedByUserId { get; set; }

    public static ChatUserSettings Create(Guid userId) => new() { UserId = userId };
}

internal sealed class ChatConversationConfiguration : IEntityTypeConfiguration<ChatConversation>
{
    public void Configure(EntityTypeBuilder<ChatConversation> builder)
    {
        builder.ToTable("chat_conversations");
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.Title).HasMaxLength(ChatConversation.MaxTitle);
        builder.Property(c => c.DirectKey).HasMaxLength(70);
        builder.HasIndex(c => c.DirectKey).IsUnique().HasFilter("direct_key IS NOT NULL");
        builder.HasIndex(c => c.CrewId);
        builder.HasIndex(c => c.BauakteId).IsUnique().HasFilter("bauakte_id IS NOT NULL");
        builder.Property(c => c.Description).HasMaxLength(ChatConversation.MaxDescription);
        builder.Property(c => c.ImageKey).HasMaxLength(200);
    }
}

internal sealed class ChatMemberConfiguration : IEntityTypeConfiguration<ChatMember>
{
    public void Configure(EntityTypeBuilder<ChatMember> builder)
    {
        builder.ToTable("chat_members");
        builder.HasKey(m => new { m.ConversationId, m.UserId });
        builder.HasIndex(m => m.UserId);
        builder.HasOne<ChatConversation>().WithMany().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("chat_messages");
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.ProtectedText).HasMaxLength(20_000);
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
        builder.HasIndex(m => m.SenderUserId);
        builder.HasIndex(m => m.CreatedAt);
        builder.HasIndex(m => new { m.SenderUserId, m.ClientMessageId }).IsUnique().HasFilter("client_message_id IS NOT NULL");
        builder.HasOne<ChatConversation>().WithMany().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(m => m.ProtectedLocation).HasMaxLength(4000);
        builder.HasIndex(m => m.ReplyToMessageId);
    }
}

internal sealed class ChatAttachmentConfiguration : IEntityTypeConfiguration<ChatAttachment>
{
    public void Configure(EntityTypeBuilder<ChatAttachment> builder)
    {
        builder.ToTable("chat_attachments");
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(a => a.ProtectedName).HasMaxLength(4000);
        builder.Property(a => a.ContentType).HasMaxLength(100);
        builder.Property(a => a.StorageKey).HasMaxLength(200);
        builder.HasIndex(a => a.MessageId);
        builder.HasIndex(a => new { a.ConversationId, a.CreatedAt });
        builder.HasOne<ChatConversation>().WithMany().HasForeignKey(a => a.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ChatMessage>().WithMany().HasForeignKey(a => a.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ChatReactionConfiguration : IEntityTypeConfiguration<ChatReaction>
{
    public void Configure(EntityTypeBuilder<ChatReaction> builder)
    {
        builder.ToTable("chat_reactions");
        builder.HasKey(r => new { r.MessageId, r.UserId });
        builder.Property(r => r.Emoji).HasMaxLength(ChatReaction.MaxEmoji);
        builder.HasIndex(r => r.UserId);
        builder.HasOne<ChatMessage>().WithMany().HasForeignKey(r => r.MessageId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ChatUserSettingsConfiguration : IEntityTypeConfiguration<ChatUserSettings>
{
    public void Configure(EntityTypeBuilder<ChatUserSettings> builder)
    {
        builder.ToTable("chat_user_settings");
        builder.HasKey(s => s.UserId);
        builder.Property(s => s.UserId).ValueGeneratedNever();
    }
}

internal sealed class ChatSettingsRowConfiguration : IEntityTypeConfiguration<ChatSettingsRow>
{
    public void Configure(EntityTypeBuilder<ChatSettingsRow> builder)
    {
        builder.ToTable("chat_settings");
        builder.ConfigureVersioned();
        builder.Property(s => s.Id).ValueGeneratedNever();
    }
}
