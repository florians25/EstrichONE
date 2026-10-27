using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>
/// Tagesbesetzung einer Kolonne: wer an diesem Tag tatsächlich mitfährt und welches Fahrzeug/Gerät die Kolonne nutzt.
/// Ohne Eintrag gilt die Stammbesetzung (Mitglieder der Kolonne, fest zugeordnete Fahrzeuge).
/// </summary>
public sealed class CrewDay : IVersionedEntity
{
    public Guid Id { get; private set; }
    public Guid CrewId { get; private set; }
    public DateOnly Day { get; private set; }
    public List<Guid> MemberUserIds { get; private set; } = [];
    public List<Guid> ResourceIds { get; private set; } = [];
    public string? Note { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private CrewDay()
    {
    }

    public static CrewDay Create(Guid crewId, DateOnly day, IEnumerable<Guid> members, IEnumerable<Guid> resources)
    {
        var roster = new CrewDay { Id = Guid.CreateVersion7(), CrewId = crewId, Day = day };
        roster.Update(members, resources, null);
        return roster;
    }

    public void Update(IEnumerable<Guid> members, IEnumerable<Guid> resources, string? note)
    {
        MemberUserIds = members.Distinct().Take(50).ToList();
        ResourceIds = resources.Distinct().Take(20).ToList();
        Note = PlanningText.Optional(note, 500);
    }
}

/// <summary>Eintrag im Stand der Freigabe: welcher Einsatz in welcher Version freigegeben wurde.</summary>
public sealed record PlanReleaseItem(Guid AssignmentId, long Version, string Title, string? CrewName, string Activity);

/// <summary>
/// Freigabe des Plans für einen Tag: Ab jetzt sehen Mitarbeiter den Plan als verbindlich („Wer fährt wohin“);
/// Änderungen danach werden gegenüber diesem Stand markiert.
/// </summary>
public sealed class PlanRelease
{
    public DateOnly Day { get; private set; }
    public DateTimeOffset ReleasedAt { get; private set; }
    public Guid? ReleasedByUserId { get; private set; }
    public string ReleasedByName { get; private set; } = string.Empty;
    public string? Note { get; private set; }

    /// <summary>Stand der Einsätze bei Freigabe (JSON-Liste von <see cref="PlanReleaseItem"/>).</summary>
    public string SnapshotJson { get; private set; } = "[]";

    private PlanRelease()
    {
    }

    public static PlanRelease Create(DateOnly day) => new() { Day = day };

    public void Release(DateTimeOffset at, Guid? userId, string userName, string? note, IReadOnlyList<PlanReleaseItem> items)
    {
        ReleasedAt = at;
        ReleasedByUserId = userId;
        ReleasedByName = userName;
        Note = PlanningText.Optional(note, 500);
        SnapshotJson = System.Text.Json.JsonSerializer.Serialize(items);
    }

    public IReadOnlyList<PlanReleaseItem> Items() =>
        System.Text.Json.JsonSerializer.Deserialize<List<PlanReleaseItem>>(SnapshotJson) ?? [];
}

internal sealed class CrewDayConfiguration : IEntityTypeConfiguration<CrewDay>
{
    public void Configure(EntityTypeBuilder<CrewDay> builder)
    {
        builder.ToTable("crew_days");
        builder.ConfigureVersioned();
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.MemberUserIds).HasColumnType("uuid[]");
        builder.Property(c => c.ResourceIds).HasColumnType("uuid[]");
        builder.Property(c => c.Note).HasMaxLength(500);
        builder.HasOne<Crew>().WithMany().HasForeignKey(c => c.CrewId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.CrewId, c.Day }).IsUnique();
        builder.HasIndex(c => c.Day);
    }
}

internal sealed class PlanReleaseConfiguration : IEntityTypeConfiguration<PlanRelease>
{
    public void Configure(EntityTypeBuilder<PlanRelease> builder)
    {
        builder.ToTable("plan_releases");
        builder.HasKey(r => r.Day);
        builder.Property(r => r.ReleasedByName).HasMaxLength(200);
        builder.Property(r => r.Note).HasMaxLength(500);
        builder.Property(r => r.SnapshotJson).HasColumnType("jsonb");
    }
}
