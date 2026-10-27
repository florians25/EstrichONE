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

    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; }

    public static ChatConversation Direct(Guid a, Guid b, Guid createdBy, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(), Kind = ChatConversationKind.Direct, DirectKey = DirectKeyOf(a, b), CreatedByUserId = createdBy, CreatedAt = now,
        LastActivityAt = now,
    };

    public static ChatConversation Group(string title, Guid? crewId, Guid createdBy, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(), Kind = ChatConversationKind.Group, Title = Clip(title, MaxTitle), CrewId = crewId, CreatedByUserId = createdBy,
        CreatedAt = now, LastActivityAt = now,
    };

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

    /// <summary>Stumm: keine Push-/Postfach-Hinweise für neue Nachrichten.</summary>
    public bool Muted { get; private set; }

    /// <summary>Über die Kolonne Mitglied (folgt der Kolonne); sonst von Hand hinzugefügt.</summary>
    public bool FromCrew { get; private set; }

    /// <param name="readUpTo">Bei späterem Beitritt: Verlauf bis hier gilt als gelesen; <c>null</c> = alles ungelesen.</param>
    public static ChatMember Create(Guid conversationId, Guid userId, DateTimeOffset now, bool fromCrew = false, DateTimeOffset? readUpTo = null) =>
        new() { ConversationId = conversationId, UserId = userId, JoinedAt = now, DeliveredUpTo = readUpTo, ReadUpTo = readUpTo, FromCrew = fromCrew };

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

    public void Mute(bool muted) => Muted = muted;
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

    public static ChatMessage Create(Guid conversationId, Guid sender, string protectedText, Guid? clientMessageId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now), ConversationId = conversationId, SenderUserId = sender, CreatedAt = now, ProtectedText = protectedText,
        ClientMessageId = clientMessageId,
    };

    public void Delete(DateTimeOffset now)
    {
        ProtectedText = null;
        DeletedAt ??= now;
    }
}

/// <summary>Einstellungen des Chat-Moduls (eine Zeile).</summary>
public sealed class ChatSettingsRow : IVersionedEntity
{
    public const int SingletonId = 1;
    public const int DefaultRetentionDays = 90;

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

    /// <summary>Einmalige Grundeinrichtung (Rechte für Standardrollen) erledigt.</summary>
    public bool Seeded { get; set; }

    public DateTimeOffset? LastCleanupAt { get; set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
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
