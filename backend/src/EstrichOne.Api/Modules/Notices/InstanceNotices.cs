using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Notices;

/// <summary>Hinweise und Wartung der Instanz-Administratoren (eine Zeile, JSON); getrennt von denen der Hosting-Konsole.</summary>
public sealed class InstanceNoticesRow : IVersionedEntity
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;
    public string Data { get; private set; } = "{}";

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void Update(string data) => Data = data;
}

internal sealed class InstanceNoticesRowConfiguration : IEntityTypeConfiguration<InstanceNoticesRow>
{
    public void Configure(EntityTypeBuilder<InstanceNoticesRow> builder)
    {
        builder.ToTable("instance_notices");
        builder.ConfigureVersioned();
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Data).HasColumnType("jsonb");
    }
}
