using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Feedback;

/// <summary>Fehler melden oder Funktion wünschen.</summary>
public enum FeedbackKind
{
    Bug,
    Feature,
}

/// <summary>Neu → In Prüfung → Geplant → In Umsetzung → Erledigt; außerdem Abgelehnt und Duplikat (mit Verweis).</summary>
public enum FeedbackStatus
{
    New,
    InReview,
    Planned,
    InProgress,
    Done,
    Rejected,
    Duplicate,
}

/// <summary>Priorität der IT (setzt nur die Verwaltung).</summary>
public enum FeedbackPriority
{
    Low,
    Normal,
    High,
    Urgent,
}

/// <summary>Fehler: Wie oft tritt er auf?</summary>
public enum FeedbackFrequency
{
    Once,
    Sometimes,
    Always,
}

/// <summary>Fehler: Auswirkung bzw. Dringlichkeit; Funktionswunsch: Wichtigkeit – jeweils aus Sicht des Einreichers.</summary>
public enum FeedbackImpact
{
    Low,
    Medium,
    High,
}

/// <summary>
/// Automatisch mitgeschickte technische Angaben (Seite, Browser, Bildschirm, App-Version). Nur diese Felder werden
/// gespeichert, jeweils gekürzt – keine Formularinhalte, keine Cookies.
/// </summary>
public sealed record FeedbackContext(
    string? Page = null,
    string? PageTitle = null,
    string? Browser = null,
    string? UserAgent = null,
    string? Platform = null,
    string? Screen = null,
    string? Viewport = null,
    string? PixelRatio = null,
    string? AppVersion = null,
    string? Language = null,
    string? TimeZone = null)
{
    private static string? Cut(string? value, int max)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    public FeedbackContext Sanitized() => new(
        Cut(Page, 500), Cut(PageTitle, 200), Cut(Browser, 100), Cut(UserAgent, 500), Cut(Platform, 100), Cut(Screen, 50), Cut(Viewport, 50),
        Cut(PixelRatio, 20), Cut(AppVersion, 50), Cut(Language, 20), Cut(TimeZone, 60));
}

/// <summary>Meldung eines Benutzers: Fehler oder Funktionswunsch, verwaltet von der IT (Recht feedback.manage).</summary>
public sealed class FeedbackItem : IVersionedEntity
{
    public const int MaxTitleLength = 200;
    public const int MaxTextLength = 10_000;
    public const int MaxAreaLength = 100;

    public Guid Id { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public FeedbackKind Kind { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>Bereich bzw. Seite der Anwendung (z. B. „Bauakten“, „Planung“).</summary>
    public string Area { get; private set; } = string.Empty;

    // Fehler
    public string? StepsToReproduce { get; private set; }
    public string? ExpectedResult { get; private set; }
    public string? ActualResult { get; private set; }
    public FeedbackFrequency? Frequency { get; private set; }

    // Funktionswunsch
    public string? Problem { get; private set; }
    public string? DesiredSolution { get; private set; }

    /// <summary>Fehler: Auswirkung; Funktionswunsch: Wichtigkeit.</summary>
    public FeedbackImpact Impact { get; private set; } = FeedbackImpact.Medium;

    /// <summary>Technische Angaben als JSON (<see cref="FeedbackContext"/>).</summary>
    public string? Context { get; private set; }
    public List<Guid> AttachmentFileIds { get; private set; } = [];

    public Guid ReporterUserId { get; private set; }
    public Guid? AssigneeUserId { get; private set; }
    public FeedbackPriority Priority { get; private set; } = FeedbackPriority.Normal;
    public FeedbackStatus Status { get; private set; } = FeedbackStatus.New;

    /// <summary>Bei Status Duplikat: die ursprüngliche Meldung.</summary>
    public Guid? DuplicateOfId { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public DateTimeOffset LastActivityAt { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private FeedbackItem()
    {
    }

    public static bool IsClosed(FeedbackStatus status) => status is FeedbackStatus.Done or FeedbackStatus.Rejected or FeedbackStatus.Duplicate;

    private static string? Text(string? value, string label, bool required)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return required ? throw new ArgumentException($"Bitte „{label}“ ausfüllen.") : null;
        }

        return trimmed.Length > MaxTextLength ? throw new ArgumentException($"„{label}“ ist zu lang (höchstens 10.000 Zeichen).") : trimmed;
    }

    public static FeedbackItem Create(Guid id, string number, FeedbackKind kind, string title, string? description, string area,
        string? steps, string? expected, string? actual, FeedbackFrequency? frequency, string? problem, string? solution, FeedbackImpact impact,
        string? context, IReadOnlyCollection<Guid> attachmentFileIds, Guid reporterUserId, DateTimeOffset now)
    {
        var trimmedTitle = title?.Trim() ?? string.Empty;
        if (trimmedTitle.Length is 0 or > MaxTitleLength)
        {
            throw new ArgumentException("Bitte einen kurzen Titel angeben (höchstens 200 Zeichen).", nameof(title));
        }

        var trimmedArea = area?.Trim() ?? string.Empty;
        if (trimmedArea.Length is 0 or > MaxAreaLength)
        {
            throw new ArgumentException("Bitte den Bereich bzw. die Seite angeben.", nameof(area));
        }

        if (attachmentFileIds.Count > 10)
        {
            throw new ArgumentException("Höchstens 10 Anhänge.", nameof(attachmentFileIds));
        }

        var bug = kind == FeedbackKind.Bug;
        if (bug && frequency is null)
        {
            throw new ArgumentException("Bitte angeben, wie oft der Fehler auftritt.", nameof(frequency));
        }

        return new FeedbackItem
        {
            Id = id, Number = number, Kind = kind, Title = trimmedTitle, Description = Text(description, "Beschreibung", false), Area = trimmedArea,
            StepsToReproduce = bug ? Text(steps, "Schritte zum Nachstellen", false) : null,
            ExpectedResult = bug ? Text(expected, "Erwartetes Ergebnis", false) : null,
            ActualResult = bug ? Text(actual, "Was ist passiert", true) : null,
            Frequency = bug ? frequency : null,
            Problem = bug ? null : Text(problem, "Problem bzw. Nutzen", true),
            DesiredSolution = bug ? null : Text(solution, "Gewünschte Lösung", true),
            Impact = impact, Context = context, AttachmentFileIds = attachmentFileIds.Distinct().ToList(), ReporterUserId = reporterUserId,
            LastActivityAt = now,
        };
    }

    /// <summary>Nummer erst nach erfolgreicher Prüfung vergeben (keine Lücken durch abgelehnte Eingaben).</summary>
    public void SetNumber(string number) => Number = number;

    public void Touch(DateTimeOffset now) => LastActivityAt = now;

    public void ChangeStatus(FeedbackStatus status, Guid? duplicateOfId, DateTimeOffset now)
    {
        if (status == FeedbackStatus.Duplicate)
        {
            if (duplicateOfId is null || duplicateOfId == Id)
            {
                throw new ArgumentException("Bitte die ursprüngliche Meldung angeben (Duplikat von …).", nameof(duplicateOfId));
            }

            DuplicateOfId = duplicateOfId;
        }
        else
        {
            DuplicateOfId = null;
        }

        ClosedAt = IsClosed(status) ? ClosedAt ?? now : null;
        Status = status;
        LastActivityAt = now;
    }

    public void Assign(Guid? userId) => AssigneeUserId = userId;

    public void Prioritize(FeedbackPriority priority) => Priority = priority;

    public void Classify(FeedbackKind kind, string area)
    {
        var trimmed = area.Trim();
        if (trimmed.Length is 0 or > MaxAreaLength)
        {
            throw new ArgumentException("Bitte den Bereich bzw. die Seite angeben.", nameof(area));
        }

        Kind = kind;
        Area = trimmed;
    }
}

/// <summary>Antwort (für den Einreicher sichtbar) oder interne Notiz der IT.</summary>
public sealed class FeedbackComment
{
    public Guid Id { get; private set; }
    public Guid FeedbackId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public bool IsInternal { get; private set; }
    public List<Guid> AttachmentFileIds { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }

    private FeedbackComment()
    {
    }

    public static FeedbackComment Create(Guid feedbackId, Guid authorUserId, string text, bool isInternal, IReadOnlyCollection<Guid> attachmentFileIds,
        DateTimeOffset now)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        var attachments = attachmentFileIds.Distinct().ToList();
        if ((trimmed.Length == 0 && attachments.Count == 0) || trimmed.Length > FeedbackItem.MaxTextLength)
        {
            throw new ArgumentException("Die Antwort ist leer oder zu lang.", nameof(text));
        }

        if (attachments.Count > 10)
        {
            throw new ArgumentException("Höchstens 10 Anhänge.", nameof(attachmentFileIds));
        }

        return new FeedbackComment
        {
            Id = Guid.CreateVersion7(now), FeedbackId = feedbackId, AuthorUserId = authorUserId, Text = trimmed, IsInternal = isInternal,
            AttachmentFileIds = attachments, CreatedAt = now,
        };
    }
}

/// <summary>Verlauf: Meldung, Statuswechsel, Priorität, Zuweisung, Einordnung.</summary>
public sealed class FeedbackEvent
{
    public Guid Id { get; private set; }
    public Guid FeedbackId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string Kind { get; private set; } = string.Empty;
    public string? FromValue { get; private set; }
    public string? ToValue { get; private set; }
    public DateTimeOffset At { get; private set; }

    private FeedbackEvent()
    {
    }

    public static FeedbackEvent Create(Guid feedbackId, Guid? actorUserId, string kind, string? from, string? to, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(now), FeedbackId = feedbackId, ActorUserId = actorUserId, Kind = kind, FromValue = from, ToValue = to, At = now };
}

public sealed class FeedbackNumberSequence
{
    public int Year { get; private set; }
    public int LastNumber { get; private set; }
}

internal sealed class FeedbackItemConfiguration : IEntityTypeConfiguration<FeedbackItem>
{
    public void Configure(EntityTypeBuilder<FeedbackItem> builder)
    {
        builder.ToTable("feedback_items");
        builder.ConfigureVersioned();
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.Number).HasMaxLength(20);
        builder.HasIndex(f => f.Number).IsUnique();
        builder.Property(f => f.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.Title).HasMaxLength(FeedbackItem.MaxTitleLength);
        builder.Property(f => f.Description).HasMaxLength(FeedbackItem.MaxTextLength);
        builder.Property(f => f.Area).HasMaxLength(FeedbackItem.MaxAreaLength);
        builder.Property(f => f.StepsToReproduce).HasMaxLength(FeedbackItem.MaxTextLength);
        builder.Property(f => f.ExpectedResult).HasMaxLength(FeedbackItem.MaxTextLength);
        builder.Property(f => f.ActualResult).HasMaxLength(FeedbackItem.MaxTextLength);
        builder.Property(f => f.Problem).HasMaxLength(FeedbackItem.MaxTextLength);
        builder.Property(f => f.DesiredSolution).HasMaxLength(FeedbackItem.MaxTextLength);
        builder.Property(f => f.Frequency).HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.Impact).HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.Priority).HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.Context).HasColumnType("jsonb");
        builder.Property(f => f.AttachmentFileIds).HasColumnType("uuid[]");
        builder.HasOne<User>().WithMany().HasForeignKey(f => f.ReporterUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(f => f.AssigneeUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FeedbackItem>().WithMany().HasForeignKey(f => f.DuplicateOfId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(f => new { f.Status, f.LastActivityAt });
        builder.HasIndex(f => new { f.ReporterUserId, f.CreatedAt });
        builder.HasIndex(f => f.AttachmentFileIds).HasMethod("gin");
    }
}

internal sealed class FeedbackCommentConfiguration : IEntityTypeConfiguration<FeedbackComment>
{
    public void Configure(EntityTypeBuilder<FeedbackComment> builder)
    {
        builder.ToTable("feedback_comments");
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Text).HasMaxLength(FeedbackItem.MaxTextLength);
        builder.Property(c => c.AttachmentFileIds).HasColumnType("uuid[]");
        builder.HasIndex(c => c.AttachmentFileIds).HasMethod("gin");
        builder.HasOne<FeedbackItem>().WithMany().HasForeignKey(c => c.FeedbackId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.FeedbackId, c.CreatedAt });
    }
}

internal sealed class FeedbackEventConfiguration : IEntityTypeConfiguration<FeedbackEvent>
{
    public void Configure(EntityTypeBuilder<FeedbackEvent> builder)
    {
        builder.ToTable("feedback_events");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasMaxLength(30);
        builder.Property(e => e.FromValue).HasMaxLength(100);
        builder.Property(e => e.ToValue).HasMaxLength(100);
        builder.HasOne<FeedbackItem>().WithMany().HasForeignKey(e => e.FeedbackId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => new { e.FeedbackId, e.At });
    }
}

internal sealed class FeedbackNumberSequenceConfiguration : IEntityTypeConfiguration<FeedbackNumberSequence>
{
    public void Configure(EntityTypeBuilder<FeedbackNumberSequence> builder)
    {
        builder.ToTable("feedback_number_sequences");
        builder.HasKey(s => s.Year);
        builder.Property(s => s.Year).ValueGeneratedNever();
    }
}
