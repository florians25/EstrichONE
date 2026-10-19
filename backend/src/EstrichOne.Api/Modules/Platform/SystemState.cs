using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Platform;

/// <summary>
/// Genau eine Zeile je Installation. <see cref="InstanceEpoch"/> ändert sich bei jeder Wiederherstellung
/// aus einem Backup; Clients erkennen daran, dass sie vollständig neu abgleichen müssen.
/// </summary>
public sealed class SystemState
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;
    public Guid InstanceEpoch { get; private set; }
    public DateTimeOffset InstalledAt { get; private set; }

    private SystemState()
    {
    }

    public static SystemState Create(DateTimeOffset now) =>
        new() { InstanceEpoch = Guid.CreateVersion7(now), InstalledAt = now };
}

internal sealed class SystemStateConfiguration : IEntityTypeConfiguration<SystemState>
{
    public void Configure(EntityTypeBuilder<SystemState> builder)
    {
        builder.ToTable("system_state", table =>
            table.HasCheckConstraint("ck_system_state_singleton", $"id = {SystemState.SingletonId}"));
        builder.HasKey(state => state.Id);
        builder.Property(state => state.Id).ValueGeneratedNever();
    }
}
