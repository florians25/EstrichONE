using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Work;

public enum WorkItemKind
{
    Task,
    /// <summary>Mangel – in Estrich Web ein eigenes Modul, hier eine Aufgabe mit eigenem Typ.</summary>
    Defect,
}

public enum WorkItemStatus
{
    Open,
    InProgress,
    Done,
}

/// <summary>Aufgabe oder Mangel, optional zu einer Bauakte.</summary>
public sealed class WorkItem : IVersionedEntity, ISoftDeletable
{
    public Guid Id { get; private set; }
    public Guid? BauakteId { get; private set; }
    public WorkItemKind Kind { get; private set; }
    public WorkItemStatus Status { get; private set; } = WorkItemStatus.Open;
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public Guid? AssigneeUserId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private WorkItem()
    {
    }

    public static WorkItem Create(Guid id, WorkItemKind kind, string title, Guid createdByUserId, Guid? bauakteId = null) =>
        new() { Id = id, Kind = kind, Title = Required(title), CreatedByUserId = createdByUserId, BauakteId = bauakteId };

    public void Update(string title, string? description, DateOnly? dueDate, Guid? assigneeUserId)
    {
        Title = Required(title);
        Description = description;
        DueDate = dueDate;
        AssigneeUserId = assigneeUserId;
    }

    public void ChangeStatus(WorkItemStatus status, DateTimeOffset at)
    {
        Status = status;
        CompletedAt = status == WorkItemStatus.Done ? CompletedAt ?? at : null;
    }

    public void Delete(DateTimeOffset at) => DeletedAt ??= at;

    private static string Required(string title) =>
        string.IsNullOrWhiteSpace(title) ? throw new ArgumentException("Titel fehlt.", nameof(title)) : title.Trim();
}

internal sealed class WorkItemConfiguration : IEntityTypeConfiguration<WorkItem>
{
    public void Configure(EntityTypeBuilder<WorkItem> builder)
    {
        builder.ToTable("work_items");
        builder.ConfigureVersioned();
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(item => item.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(item => item.Title).HasMaxLength(200);
        builder.Property(item => item.Description).HasMaxLength(4000);
        builder.HasOne<Bauakte>().WithMany().HasForeignKey(item => item.BauakteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(item => item.AssigneeUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(item => item.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => new { item.AssigneeUserId, item.Status });
        builder.HasIndex(item => item.BauakteId);
    }
}
