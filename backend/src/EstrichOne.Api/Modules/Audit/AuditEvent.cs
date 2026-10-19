using System.Text.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit.Exports;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Audit;

/// <summary>Audit-Ereignis. Die Tabelle ist per Trigger nur anfügbar (siehe Migration).</summary>
public sealed class AuditEvent
{
    public Guid Id { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public Guid? ActorUserId { get; private set; }
    public string? EntityType { get; private set; }
    public string? EntityId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? TraceId { get; private set; }
    public string? Details { get; private set; }

    private AuditEvent()
    {
    }

    public AuditEvent(DateTimeOffset occurredAt, string action, Guid? actorUserId, string? entityType, string? entityId,
        string? ipAddress, string? traceId, string? details)
    {
        Id = Guid.CreateVersion7(occurredAt);
        OccurredAt = occurredAt;
        Action = action;
        ActorUserId = actorUserId;
        EntityType = entityType;
        EntityId = entityId;
        IpAddress = ipAddress;
        TraceId = traceId;
        Details = details;
    }
}

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Action).HasMaxLength(100);
        builder.Property(e => e.EntityType).HasMaxLength(100);
        builder.Property(e => e.EntityId).HasMaxLength(100);
        builder.Property(e => e.IpAddress).HasMaxLength(45);
        builder.Property(e => e.TraceId).HasMaxLength(100);
        builder.Property(e => e.Details).HasColumnType("jsonb");
        builder.HasIndex(e => e.OccurredAt);
        builder.HasIndex(e => new { e.ActorUserId, e.OccurredAt });
    }
}

internal sealed class AuditLog(EstrichOneDbContext db, TimeProvider clock, IHttpContextAccessor http) : IAuditLog
{
    public void Record(string action, Guid? actorUserId, string? entityType = null, string? entityId = null,
        IReadOnlyDictionary<string, string?>? details = null)
    {
        var context = http.HttpContext;
        db.Add(new AuditEvent(
            clock.GetUtcNow(),
            action,
            actorUserId,
            entityType,
            entityId,
            context?.Connection.RemoteIpAddress?.ToString(),
            context?.Features.Get<IHttpActivityFeature>()?.Activity.TraceId.ToString() ?? context?.TraceIdentifier,
            details is null ? null : JsonSerializer.Serialize(details)));
    }
}
