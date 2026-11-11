using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Helpdesk;

/// <summary>Bearbeitungsgruppe, z. B. „IT“ oder „Büro“. Tickets können einer Gruppe zugeordnet werden.</summary>
public sealed class HelpdeskGroup : IVersionedEntity
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public List<Guid> MemberUserIds { get; private set; } = [];
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private HelpdeskGroup()
    {
    }

    public static HelpdeskGroup Create(Guid id, string name, string? description, IEnumerable<Guid> members, bool isActive, int sortOrder)
    {
        var group = new HelpdeskGroup { Id = id };
        group.Update(name, description, members, isActive, sortOrder);
        return group;
    }

    public void Update(string name, string? description, IEnumerable<Guid> members, bool isActive, int sortOrder)
    {
        Name = HelpdeskText.Required(name, 100, "Bitte einen Namen für die Gruppe angeben.");
        Description = HelpdeskText.Optional(description, 500);
        MemberUserIds = members.Distinct().Take(200).ToList();
        IsActive = isActive;
        SortOrder = sortOrder;
    }
}

/// <summary>Unterkategorie einer festen Kategorie (z. B. Drucker/Büro-IT › „Drucker“, „Telefon“, „E-Mail“).</summary>
public sealed class TicketSubcategory : IVersionedEntity
{
    public Guid Id { get; private set; }
    public TicketCategory Category { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private TicketSubcategory()
    {
    }

    public static TicketSubcategory Create(Guid id, TicketCategory category, string name, bool isActive, int sortOrder)
    {
        var item = new TicketSubcategory { Id = id };
        item.Update(category, name, isActive, sortOrder);
        return item;
    }

    public void Update(TicketCategory category, string name, bool isActive, int sortOrder)
    {
        Category = category;
        Name = HelpdeskText.Required(name, 100, "Bitte einen Namen für die Unterkategorie angeben.");
        IsActive = isActive;
        SortOrder = sortOrder;
    }
}

/// <summary>Wie eine Regel die zuständige Person bestimmt.</summary>
public enum AssignmentStrategy
{
    /// <summary>Nur Gruppe setzen, niemandem persönlich zuweisen.</summary>
    GroupOnly,

    /// <summary>Fest an <see cref="HelpdeskRule.AssigneeUserId"/>.</summary>
    Fixed,

    /// <summary>Mitglied der Gruppe mit den wenigsten offenen Tickets.</summary>
    LeastBusy,
}

/// <summary>
/// Regel für die automatische Zuweisung neuer Tickets. Die Regeln werden nach <see cref="SortOrder"/> geprüft; die erste
/// passende gilt. Leere Bedingungen passen immer.
/// </summary>
public sealed class HelpdeskRule : IVersionedEntity
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public TicketCategory? Category { get; private set; }
    public Guid? SubcategoryId { get; private set; }

    /// <summary>Stichwort im Titel oder in der Beschreibung (ohne Groß-/Kleinschreibung).</summary>
    public string? Keyword { get; private set; }
    public Guid? GroupId { get; private set; }
    public AssignmentStrategy Strategy { get; private set; }
    public Guid? AssigneeUserId { get; private set; }

    /// <summary>Priorität setzen, falls der Melder keine gewählt hat (z. B. Anmeldung → hoch).</summary>
    public TicketPriority? Priority { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private HelpdeskRule()
    {
    }

    public static HelpdeskRule Create(Guid id, string name, TicketCategory? category, Guid? subcategoryId, string? keyword, Guid? groupId,
        AssignmentStrategy strategy, Guid? assigneeUserId, TicketPriority? priority, bool isActive, int sortOrder)
    {
        var rule = new HelpdeskRule { Id = id };
        rule.Update(name, category, subcategoryId, keyword, groupId, strategy, assigneeUserId, priority, isActive, sortOrder);
        return rule;
    }

    public void Update(string name, TicketCategory? category, Guid? subcategoryId, string? keyword, Guid? groupId,
        AssignmentStrategy strategy, Guid? assigneeUserId, TicketPriority? priority, bool isActive, int sortOrder)
    {
        if (strategy == AssignmentStrategy.Fixed && assigneeUserId is null)
        {
            throw new ArgumentException("Für die feste Zuweisung bitte eine Person wählen.", nameof(assigneeUserId));
        }

        if (strategy == AssignmentStrategy.LeastBusy && groupId is null)
        {
            throw new ArgumentException("Für „wenigste offene Tickets“ bitte eine Gruppe wählen.", nameof(groupId));
        }

        Name = HelpdeskText.Required(name, 100, "Bitte einen Namen für die Regel angeben.");
        Category = category;
        SubcategoryId = subcategoryId;
        Keyword = HelpdeskText.Optional(keyword, 100);
        GroupId = groupId;
        Strategy = strategy;
        AssigneeUserId = strategy == AssignmentStrategy.Fixed ? assigneeUserId : null;
        Priority = priority;
        IsActive = isActive;
        SortOrder = sortOrder;
    }
}

/// <summary>Verknüpfung zweier Tickets („hängt zusammen mit“, „Duplikat von“).</summary>
public sealed class TicketLink
{
    public const string Related = "related";
    public const string Duplicate = "duplicate";

    public Guid Id { get; private set; }
    public Guid TicketId { get; private set; }
    public Guid OtherTicketId { get; private set; }
    public string Kind { get; private set; } = Related;
    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private TicketLink()
    {
    }

    public static TicketLink Create(Guid ticketId, Guid otherTicketId, string kind, Guid? userId, DateTimeOffset now)
    {
        if (ticketId == otherTicketId)
        {
            throw new ArgumentException("Ein Ticket kann nicht mit sich selbst verknüpft werden.", nameof(otherTicketId));
        }

        // Ein Paar nur einmal speichern (kleinere ID zuerst).
        var (a, b) = ticketId.CompareTo(otherTicketId) < 0 ? (ticketId, otherTicketId) : (otherTicketId, ticketId);
        return new TicketLink
        {
            Id = Guid.CreateVersion7(now), TicketId = a, OtherTicketId = b, Kind = kind == Duplicate ? Duplicate : Related,
            CreatedByUserId = userId, CreatedAt = now,
        };
    }
}

/// <summary>Erfasste Arbeitszeit an einem Ticket (optional, Einstellung „Zeiterfassung“).</summary>
public sealed class TicketTimeEntry
{
    public Guid Id { get; private set; }
    public Guid TicketId { get; private set; }
    public Guid UserId { get; private set; }
    public int Minutes { get; private set; }
    public DateOnly WorkDate { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private TicketTimeEntry()
    {
    }

    public static TicketTimeEntry Create(Guid ticketId, Guid userId, int minutes, DateOnly workDate, string? note, DateTimeOffset now)
    {
        if (minutes is < 1 or > 24 * 60)
        {
            throw new ArgumentException("Bitte eine Dauer zwischen 1 Minute und 24 Stunden angeben.", nameof(minutes));
        }

        return new TicketTimeEntry
        {
            Id = Guid.CreateVersion7(now), TicketId = ticketId, UserId = userId, Minutes = minutes, WorkDate = workDate,
            Note = HelpdeskText.Optional(note, 500), CreatedAt = now,
        };
    }
}

/// <summary>Artikel der Wissensdatenbank: Anleitungen und bekannte Lösungen.</summary>
public sealed class KnowledgeArticle : IVersionedEntity
{
    public const int MaxBodyLength = 20_000;

    public Guid Id { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public TicketCategory? Category { get; private set; }

    /// <summary>Suchbegriffe, durch Komma oder Leerzeichen getrennt.</summary>
    public string? Keywords { get; private set; }

    /// <summary>Veröffentlicht: für alle sichtbar und Vorschlag beim Melden. Sonst Entwurf bzw. nur für die Bearbeitung.</summary>
    public bool IsPublished { get; private set; }
    public Guid? AuthorUserId { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public int Views { get; private set; }
    public int HelpfulCount { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private KnowledgeArticle()
    {
    }

    public static KnowledgeArticle Create(Guid id, string title, string body, TicketCategory? category, string? keywords, bool isPublished, Guid userId)
    {
        var article = new KnowledgeArticle { Id = id, AuthorUserId = userId };
        article.Update(title, body, category, keywords, isPublished, userId);
        return article;
    }

    public void Update(string title, string body, TicketCategory? category, string? keywords, bool isPublished, Guid userId)
    {
        Title = HelpdeskText.Required(title, 200, "Bitte einen Titel angeben.");
        Body = HelpdeskText.Required(body, MaxBodyLength, "Bitte den Inhalt des Artikels angeben (höchstens 20.000 Zeichen).");
        Category = category;
        Keywords = HelpdeskText.Optional(keywords, 500);
        IsPublished = isPublished;
        UpdatedByUserId = userId;
    }

    public void CountView() => Views++;

    public void CountHelpful() => HelpfulCount++;
}

/// <summary>Textbaustein für Antworten der Bearbeitung. Platzhalter: {Melder}, {Ticket}, {Bearbeiter}.</summary>
public sealed class TicketCannedResponse : IVersionedEntity
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Text { get; private set; } = string.Empty;
    public TicketCategory? Category { get; private set; }

    /// <summary>Persönlicher Baustein (nur für diese Person sichtbar); null = für alle Bearbeiter.</summary>
    public Guid? OwnerUserId { get; private set; }
    public int SortOrder { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private TicketCannedResponse()
    {
    }

    public static TicketCannedResponse Create(Guid id, string name, string text, TicketCategory? category, Guid? ownerUserId, int sortOrder)
    {
        var item = new TicketCannedResponse { Id = id, OwnerUserId = ownerUserId };
        item.Update(name, text, category, sortOrder);
        return item;
    }

    public void Update(string name, string text, TicketCategory? category, int sortOrder)
    {
        Name = HelpdeskText.Required(name, 100, "Bitte einen Namen für den Textbaustein angeben.");
        Text = HelpdeskText.Required(text, 8000, "Bitte den Text angeben (höchstens 8.000 Zeichen).");
        Category = category;
        SortOrder = sortOrder;
    }
}

/// <summary>Einstellungen des Helpdesks (eine Zeile, JSON): SLA-Ziele, Geschäftszeiten, Optionen.</summary>
public sealed class HelpdeskSettingsRow : IVersionedEntity
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;
    public string Data { get; private set; } = "{}";

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void Set(string json) => Data = json;
}

internal static class HelpdeskText
{
    public static string Required(string? value, int max, string message)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length is 0 || text.Length > max ? throw new ArgumentException(message, nameof(value)) : text;
    }

    public static string? Optional(string? value, int max)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length > max ? text[..max] : text;
    }
}

internal sealed class HelpdeskGroupConfiguration : IEntityTypeConfiguration<HelpdeskGroup>
{
    public void Configure(EntityTypeBuilder<HelpdeskGroup> builder)
    {
        builder.ToTable("helpdesk_groups");
        builder.ConfigureVersioned();
        builder.Property(g => g.Id).ValueGeneratedNever();
        builder.Property(g => g.Name).HasMaxLength(100);
        builder.Property(g => g.Description).HasMaxLength(500);
        builder.Property(g => g.MemberUserIds).HasColumnType("uuid[]").HasDefaultValueSql("'{}'::uuid[]");
    }
}

internal sealed class TicketSubcategoryConfiguration : IEntityTypeConfiguration<TicketSubcategory>
{
    public void Configure(EntityTypeBuilder<TicketSubcategory> builder)
    {
        builder.ToTable("ticket_subcategories");
        builder.ConfigureVersioned();
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Category).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.Name).HasMaxLength(100);
        builder.HasIndex(s => new { s.Category, s.SortOrder });
    }
}

internal sealed class HelpdeskRuleConfiguration : IEntityTypeConfiguration<HelpdeskRule>
{
    public void Configure(EntityTypeBuilder<HelpdeskRule> builder)
    {
        builder.ToTable("helpdesk_rules");
        builder.ConfigureVersioned();
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(100);
        builder.Property(r => r.Keyword).HasMaxLength(100);
        builder.Property(r => r.Category).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.Strategy).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Priority).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<HelpdeskGroup>().WithMany().HasForeignKey(r => r.GroupId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<TicketSubcategory>().WithMany().HasForeignKey(r => r.SubcategoryId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.AssigneeUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class TicketLinkConfiguration : IEntityTypeConfiguration<TicketLink>
{
    public void Configure(EntityTypeBuilder<TicketLink> builder)
    {
        builder.ToTable("ticket_links");
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Kind).HasMaxLength(20);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(l => l.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(l => l.OtherTicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(l => new { l.TicketId, l.OtherTicketId }).IsUnique();
        builder.HasIndex(l => l.OtherTicketId);
    }
}

internal sealed class TicketTimeEntryConfiguration : IEntityTypeConfiguration<TicketTimeEntry>
{
    public void Configure(EntityTypeBuilder<TicketTimeEntry> builder)
    {
        builder.ToTable("ticket_time_entries");
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Note).HasMaxLength(500);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(t => t.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => new { t.TicketId, t.WorkDate });
    }
}

internal sealed class KnowledgeArticleConfiguration : IEntityTypeConfiguration<KnowledgeArticle>
{
    public void Configure(EntityTypeBuilder<KnowledgeArticle> builder)
    {
        builder.ToTable("knowledge_articles");
        builder.ConfigureVersioned();
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Title).HasMaxLength(200);
        builder.Property(a => a.Body).HasMaxLength(KnowledgeArticle.MaxBodyLength);
        builder.Property(a => a.Keywords).HasMaxLength(500);
        builder.Property(a => a.Category).HasConversion<string>().HasMaxLength(30);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.AuthorUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class TicketCannedResponseConfiguration : IEntityTypeConfiguration<TicketCannedResponse>
{
    public void Configure(EntityTypeBuilder<TicketCannedResponse> builder)
    {
        builder.ToTable("ticket_canned_responses");
        builder.ConfigureVersioned();
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(100);
        builder.Property(r => r.Text).HasMaxLength(8000);
        builder.Property(r => r.Category).HasConversion<string>().HasMaxLength(30);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class HelpdeskSettingsConfiguration : IEntityTypeConfiguration<HelpdeskSettingsRow>
{
    public void Configure(EntityTypeBuilder<HelpdeskSettingsRow> builder)
    {
        builder.ToTable("helpdesk_settings");
        builder.ConfigureVersioned();
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Data).HasColumnType("jsonb");
    }
}
