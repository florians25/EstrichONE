using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Documentation;

public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled,
}

/// <summary>
/// Kleine Freigabe für ein Dokument (z. B. Angebot, Plan, Protokoll): angefragt bei einer Person oder einer Rolle,
/// entschieden mit Kommentar. Entscheidungen werden nie überschrieben; eine neue Anfrage ist ein neuer Datensatz,
/// so bleibt der Verlauf vollständig.
/// </summary>
public sealed class FileApproval : IVersionedEntity
{
    public const int MaxMessageLength = 2_000;

    public Guid Id { get; private set; }
    public Guid FileId { get; private set; }
    public Guid BauakteId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid? AssigneeUserId { get; private set; }
    public Guid? AssigneeRoleId { get; private set; }
    public string? Message { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public ApprovalStatus Status { get; private set; } = ApprovalStatus.Pending;
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionComment { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private FileApproval()
    {
    }

    public static FileApproval Request(Guid id, Guid fileId, Guid bauakteId, Guid requestedBy, DateTimeOffset at, Guid? assigneeUserId,
        Guid? assigneeRoleId, string? message, DateOnly? dueDate)
    {
        if (assigneeUserId is null == assigneeRoleId is null)
        {
            throw new ArgumentException("Bitte genau eine Person oder eine Rolle für die Freigabe wählen.");
        }

        return new FileApproval
        {
            Id = id, FileId = fileId, BauakteId = bauakteId, RequestedByUserId = requestedBy, RequestedAt = at,
            AssigneeUserId = assigneeUserId, AssigneeRoleId = assigneeRoleId, Message = Clean(message), DueDate = dueDate,
        };
    }

    public void Decide(bool approve, Guid userId, DateTimeOffset at, string? comment)
    {
        if (Status != ApprovalStatus.Pending)
        {
            throw new InvalidOperationException("Über diese Freigabe wurde bereits entschieden.");
        }

        if (!approve && string.IsNullOrWhiteSpace(comment))
        {
            throw new ArgumentException("Bitte begründen, warum die Freigabe abgelehnt wird.", nameof(comment));
        }

        Status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        DecidedByUserId = userId;
        DecidedAt = at;
        DecisionComment = Clean(comment);
    }

    public void Cancel(Guid userId, DateTimeOffset at)
    {
        if (Status != ApprovalStatus.Pending)
        {
            throw new InvalidOperationException("Über diese Freigabe wurde bereits entschieden.");
        }

        Status = ApprovalStatus.Cancelled;
        DecidedByUserId = userId;
        DecidedAt = at;
    }

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length > MaxMessageLength ? trimmed[..MaxMessageLength] : trimmed;
    }
}

internal sealed class FileApprovalConfiguration : IEntityTypeConfiguration<FileApproval>
{
    public void Configure(EntityTypeBuilder<FileApproval> builder)
    {
        builder.ToTable("file_approvals", table =>
            table.HasCheckConstraint("ck_file_approvals_assignee", "(assignee_user_id IS NULL) <> (assignee_role_id IS NULL)"));
        builder.ConfigureVersioned();
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Message).HasMaxLength(FileApproval.MaxMessageLength);
        builder.Property(a => a.DecisionComment).HasMaxLength(FileApproval.MaxMessageLength);
        builder.HasOne<FileRecord>().WithMany().HasForeignKey(a => a.FileId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Bauakte>().WithMany().HasForeignKey(a => a.BauakteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.FileId);
        builder.HasIndex(a => new { a.Status, a.AssigneeUserId });
        builder.HasIndex(a => new { a.Status, a.AssigneeRoleId });
    }
}
