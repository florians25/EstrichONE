using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Devices.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Helpdesk;

public enum TicketCategory
{
    SignIn,
    Sync,
    AppError,
    DeviceDefect,
    OfficeIt,
    Access,
    Other,
}

public enum TicketPriority
{
    Low,
    Normal,
    High,
    Urgent,
}

/// <summary>Neu → In Bearbeitung → Rückfrage → Gelöst → Geschlossen (docs/reengineering/14, Abschnitt 1).</summary>
public enum TicketStatus
{
    New,
    InProgress,
    WaitingForReporter,
    Resolved,
    Closed,
}

public enum TicketSource
{
    Web,
    Desktop,
    Ios,
    Android,
}

/// <summary>IT-Problem, gemeldet von einem Benutzer oder – ohne Anmeldung – von einem eingerichteten Gerät.</summary>
public sealed class Ticket : IVersionedEntity
{
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 10_000;
    public const int MaxResolutionLength = 10_000;

    /// <summary>Gelöste Tickets schließen sich nach dieser Frist ohne Widerspruch.</summary>
    public static readonly TimeSpan AutoCloseAfter = TimeSpan.FromDays(7);

    public Guid Id { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public TicketCategory Category { get; private set; }
    public TicketPriority Priority { get; private set; } = TicketPriority.Normal;
    public TicketStatus Status { get; private set; } = TicketStatus.New;
    public TicketSource Source { get; private set; }

    /// <summary>Melder „Ich kann nicht arbeiten“ – Hinweis für die Bearbeitung, setzt keine Priorität.</summary>
    public bool ReporterBlocked { get; private set; }
    public Guid? ReporterUserId { get; private set; }

    /// <summary>Bei Meldung ohne Anmeldung: der vom Melder eingegebene Name.</summary>
    public string? ReporterName { get; private set; }
    public Guid? DeviceId { get; private set; }
    public Guid? BauakteId { get; private set; }
    public Guid? AssigneeUserId { get; private set; }
    public string? Resolution { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>Freigegebene Diagnosedaten (JSON, nur erlaubte Felder, siehe <see cref="TicketDiagnostics"/>).</summary>
    public string? Diagnostics { get; private set; }
    public List<Guid> AttachmentFileIds { get; private set; } = [];
    public DateTimeOffset LastActivityAt { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private Ticket()
    {
    }

    public static Ticket Create(Guid id, string number, string title, string? description, TicketCategory category, TicketSource source,
        bool reporterBlocked, Guid? reporterUserId, string? reporterName, Guid? deviceId, Guid? bauakteId, string? diagnostics,
        IReadOnlyCollection<Guid> attachmentFileIds, DateTimeOffset now)
    {
        var trimmedTitle = title?.Trim() ?? string.Empty;
        if (trimmedTitle.Length is 0 or > MaxTitleLength)
        {
            throw new ArgumentException("Bitte kurz beschreiben, was passiert ist (höchstens 200 Zeichen).", nameof(title));
        }

        var trimmedDescription = description?.Trim();
        if (trimmedDescription?.Length > MaxDescriptionLength)
        {
            throw new ArgumentException("Die Beschreibung ist zu lang.", nameof(description));
        }

        if (attachmentFileIds.Count > 20)
        {
            throw new ArgumentException("Höchstens 20 Anhänge.", nameof(attachmentFileIds));
        }

        return new Ticket
        {
            Id = id, Number = number, Title = trimmedTitle, Description = string.IsNullOrEmpty(trimmedDescription) ? null : trimmedDescription,
            Category = category, Source = source, ReporterBlocked = reporterBlocked, ReporterUserId = reporterUserId,
            ReporterName = reporterName?.Trim() is { Length: > 0 } name ? name[..Math.Min(name.Length, 200)] : null,
            DeviceId = deviceId, BauakteId = bauakteId, Diagnostics = diagnostics, AttachmentFileIds = attachmentFileIds.Distinct().ToList(),
            LastActivityAt = now,
        };
    }

    public void Touch(DateTimeOffset now) => LastActivityAt = now;

    public void ChangeStatus(TicketStatus status, string? resolution, DateTimeOffset now)
    {
        if (status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            var text = resolution?.Trim() ?? Resolution;
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("Bitte die Lösung dokumentieren.", nameof(resolution));
            }

            if (text.Length > MaxResolutionLength)
            {
                throw new ArgumentException("Die Lösung ist zu lang.", nameof(resolution));
            }

            Resolution = text;
            ResolvedAt ??= now;
            ClosedAt = status == TicketStatus.Closed ? now : null;
        }
        else
        {
            ResolvedAt = null;
            ClosedAt = null;
        }

        Status = status;
        LastActivityAt = now;
    }

    public void Assign(Guid? userId) => AssigneeUserId = userId;

    public void Prioritize(TicketPriority priority) => Priority = priority;

    public void Categorize(TicketCategory category) => Category = category;
}

public sealed class TicketComment
{
    public const int MaxTextLength = 10_000;

    public Guid Id { get; private set; }
    public Guid TicketId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Text { get; private set; } = string.Empty;

    /// <summary>Interne Notiz: nur für die Bearbeitung sichtbar, nie für den Melder (14, AK-2).</summary>
    public bool IsInternal { get; private set; }

    /// <summary>Anhänge der Antwort (eigene Dateien ohne Bauakte, z. B. Bildschirmfotos).</summary>
    public List<Guid> AttachmentFileIds { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }

    private TicketComment()
    {
    }

    public static TicketComment Create(Guid ticketId, Guid authorUserId, string text, bool isInternal, DateTimeOffset now,
        IReadOnlyCollection<Guid>? attachmentFileIds = null)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        var attachments = (attachmentFileIds ?? []).Distinct().ToList();
        if ((trimmed.Length == 0 && attachments.Count == 0) || trimmed.Length > MaxTextLength)
        {
            throw new ArgumentException("Der Kommentar ist leer oder zu lang.", nameof(text));
        }

        if (attachments.Count > 20)
        {
            throw new ArgumentException("Höchstens 20 Anhänge.", nameof(attachmentFileIds));
        }

        return new TicketComment
        {
            Id = Guid.CreateVersion7(now), TicketId = ticketId, AuthorUserId = authorUserId, Text = trimmed, IsInternal = isInternal, CreatedAt = now,
            AttachmentFileIds = attachments,
        };
    }
}

/// <summary>Statuswechsel, Zuweisung, Priorität, Kategorie – der Verlauf eines Tickets.</summary>
public sealed class TicketEvent
{
    public Guid Id { get; private set; }
    public Guid TicketId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string? FromValue { get; private set; }
    public string? ToValue { get; private set; }
    public DateTimeOffset At { get; private set; }

    private TicketEvent()
    {
    }

    public static TicketEvent Create(Guid ticketId, Guid? actorUserId, string kind, string? from, string? to, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(now), TicketId = ticketId, ActorUserId = actorUserId, Kind = kind, FromValue = from, ToValue = to, At = now };
}

public sealed class TicketNumberSequence
{
    public int Year { get; private set; }
    public int LastNumber { get; private set; }
}

internal sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");
        builder.ConfigureVersioned();
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Number).HasMaxLength(20);
        builder.HasIndex(t => t.Number).IsUnique();
        builder.Property(t => t.Title).HasMaxLength(Ticket.MaxTitleLength);
        builder.Property(t => t.Description).HasMaxLength(Ticket.MaxDescriptionLength);
        builder.Property(t => t.Resolution).HasMaxLength(Ticket.MaxResolutionLength);
        builder.Property(t => t.ReporterName).HasMaxLength(200);
        builder.Property(t => t.Category).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.Priority).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.Source).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Diagnostics).HasColumnType("jsonb");
        builder.Property(t => t.AttachmentFileIds).HasColumnType("uuid[]");
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.ReporterUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.AssigneeUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Device>().WithMany().HasForeignKey(t => t.DeviceId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Bauakte>().WithMany().HasForeignKey(t => t.BauakteId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(t => new { t.Status, t.LastActivityAt });
        builder.HasIndex(t => new { t.ReporterUserId, t.CreatedAt });
        builder.HasIndex(t => t.AttachmentFileIds).HasMethod("gin");
    }
}

internal sealed class TicketCommentConfiguration : IEntityTypeConfiguration<TicketComment>
{
    public void Configure(EntityTypeBuilder<TicketComment> builder)
    {
        builder.ToTable("ticket_comments");
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Text).HasMaxLength(TicketComment.MaxTextLength);
        builder.Property(c => c.AttachmentFileIds).HasColumnType("uuid[]").HasDefaultValueSql("'{}'::uuid[]");
        builder.HasIndex(c => c.AttachmentFileIds).HasMethod("gin");
        builder.HasOne<Ticket>().WithMany().HasForeignKey(c => c.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.TicketId, c.CreatedAt });
    }
}

internal sealed class TicketEventConfiguration : IEntityTypeConfiguration<TicketEvent>
{
    public void Configure(EntityTypeBuilder<TicketEvent> builder)
    {
        builder.ToTable("ticket_events");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasMaxLength(30);
        builder.Property(e => e.FromValue).HasMaxLength(100);
        builder.Property(e => e.ToValue).HasMaxLength(100);
        builder.HasOne<Ticket>().WithMany().HasForeignKey(e => e.TicketId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.TicketId, e.At });
    }
}

internal sealed class TicketNumberSequenceConfiguration : IEntityTypeConfiguration<TicketNumberSequence>
{
    public void Configure(EntityTypeBuilder<TicketNumberSequence> builder)
    {
        builder.ToTable("ticket_number_sequences");
        builder.HasKey(s => s.Year);
        builder.Property(s => s.Year).ValueGeneratedNever();
    }
}
