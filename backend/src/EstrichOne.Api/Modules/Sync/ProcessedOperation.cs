using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Sync.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Sync;

/// <summary>Verarbeitete Operation; macht Wiederholungen idempotent (gleiche <c>opId</c> → gleiches Ergebnis).</summary>
public sealed class ProcessedOperation
{
    public Guid OpId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? DeviceId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public SyncOperationStatus Status { get; private set; }
    public string Result { get; private set; } = "{}";
    public DateTimeOffset ProcessedAt { get; private set; }

    private ProcessedOperation()
    {
    }

    public ProcessedOperation(SyncOperation operation, Guid userId, Guid? deviceId, SyncOperationStatus status, string result, DateTimeOffset at)
    {
        OpId = operation.OpId;
        UserId = userId;
        DeviceId = deviceId;
        Type = operation.Type;
        EntityId = operation.EntityId;
        Status = status;
        Result = result;
        ProcessedAt = at;
    }
}

internal sealed class ProcessedOperationConfiguration : IEntityTypeConfiguration<ProcessedOperation>
{
    public void Configure(EntityTypeBuilder<ProcessedOperation> builder)
    {
        builder.ToTable("sync_operations");
        builder.HasKey(op => op.OpId);
        builder.Property(op => op.Type).HasMaxLength(50);
        builder.Property(op => op.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(op => op.Result).HasColumnType("jsonb");
        builder.HasOne<User>().WithMany().HasForeignKey(op => op.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(op => new { op.UserId, op.ProcessedAt });
        builder.HasIndex(op => op.Status).HasFilter("status = 'Rejected'");
    }
}
