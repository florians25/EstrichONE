using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Planning;

public enum ResourceKind
{
    Vehicle,
    /// <summary>Estrichpumpe, Mischpumpe.</summary>
    Pump,
    Machine,
}

/// <summary>Fahrzeug, Pumpe oder Maschine, die einem Einsatz zugeordnet werden kann.</summary>
public sealed class Resource : IVersionedEntity
{
    public Guid Id { get; private set; }
    public ResourceKind Kind { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>Kennzeichen oder Seriennummer.</summary>
    public string? Identifier { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private Resource()
    {
    }

    public static Resource Create(Guid id, ResourceKind kind, string name)
    {
        var resource = new Resource { Id = id };
        resource.Update(kind, name, null, null, true);
        return resource;
    }

    public void Update(ResourceKind kind, string name, string? identifier, string? notes, bool isActive)
    {
        Kind = kind;
        Name = PlanningText.Required(name, 100, "Bitte einen Namen angeben.");
        Identifier = PlanningText.Optional(identifier, 50);
        Notes = PlanningText.Optional(notes, 2000);
        IsActive = isActive;
    }
}

/// <summary>Kolonne: feste Gruppe von Mitarbeitern mit Kolonnenführer.</summary>
public sealed class Crew : IVersionedEntity
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Guid? LeaderUserId { get; private set; }
    public List<Guid> MemberUserIds { get; private set; } = [];

    /// <summary>Farbe im Wochenplan (#RRGGBB), immer zusammen mit dem Namen angezeigt.</summary>
    public string? Color { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private Crew()
    {
    }

    public static Crew Create(Guid id, string name)
    {
        var crew = new Crew { Id = id };
        crew.Update(name, null, [], null, true, 0);
        return crew;
    }

    public void Update(string name, Guid? leaderUserId, IReadOnlyCollection<Guid> memberUserIds, string? color, bool isActive, int sortOrder)
    {
        if (color is not null && !System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$"))
        {
            throw new ArgumentException("Die Farbe muss als #RRGGBB angegeben werden.", nameof(color));
        }

        Name = PlanningText.Required(name, 100, "Bitte einen Namen angeben.");
        LeaderUserId = leaderUserId;
        MemberUserIds = memberUserIds.Concat(leaderUserId is { } leader ? [leader] : []).Distinct().ToList();
        Color = color;
        IsActive = isActive;
        SortOrder = sortOrder;
    }
}

public enum AbsenceKind
{
    Vacation,
    Sick,
    Training,
    Other,
}

/// <summary>Abwesenheit eines Mitarbeiters (Tage einschließlich).</summary>
public sealed class Absence
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }
    public AbsenceKind Kind { get; private set; }
    public string? Note { get; private set; }

    private Absence()
    {
    }

    public static Absence Create(Guid id, Guid userId, DateOnly from, DateOnly to, AbsenceKind kind, string? note)
    {
        if (to < from)
        {
            throw new ArgumentException("Das Ende liegt vor dem Beginn.", nameof(to));
        }

        if (to.DayNumber - from.DayNumber > 366)
        {
            throw new ArgumentException("Eine Abwesenheit dauert höchstens ein Jahr.", nameof(to));
        }

        return new Absence { Id = id, UserId = userId, From = from, To = to, Kind = kind, Note = PlanningText.Optional(note, 500) };
    }

    public bool Covers(DateOnly day) => From <= day && day <= To;
}

/// <summary>
/// Einsatz: an einem Tag auf einer Baustelle, mit Kolonne und/oder einzelnen Mitarbeitern und Geräten.
/// Mehrtägige Arbeiten sind mehrere Einsätze (jeder Tag einzeln verschiebbar).
/// </summary>
public sealed class Assignment : IVersionedEntity
{
    public Guid Id { get; private set; }
    public Guid BauakteId { get; private set; }
    public DateOnly Day { get; private set; }

    /// <summary>Beginn vor Ort (optional), z. B. 07:00.</summary>
    public TimeOnly? StartTime { get; private set; }
    public string Activity { get; private set; } = string.Empty;
    public Guid? CrewId { get; private set; }
    public List<Guid> UserIds { get; private set; } = [];
    public List<Guid> ResourceIds { get; private set; } = [];
    public string? Notes { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private Assignment()
    {
    }

    public static Assignment Create(Guid id, Guid bauakteId) => new() { Id = id, BauakteId = bauakteId };

    public void Update(DateOnly day, TimeOnly? startTime, string activity, Guid? crewId, IReadOnlyCollection<Guid> userIds,
        IReadOnlyCollection<Guid> resourceIds, string? notes)
    {
        if (crewId is null && userIds.Count == 0)
        {
            throw new ArgumentException("Bitte eine Kolonne oder mindestens einen Mitarbeiter einplanen.", nameof(userIds));
        }

        Day = day;
        StartTime = startTime;
        Activity = PlanningText.Required(activity, 200, "Bitte angeben, was zu tun ist (z. B. „Estrich einbringen EG“).");
        CrewId = crewId;
        UserIds = userIds.Distinct().ToList();
        ResourceIds = resourceIds.Distinct().ToList();
        Notes = PlanningText.Optional(notes, 2000);
    }

    public void MoveTo(Guid bauakteId) => BauakteId = bauakteId;
}

internal static class PlanningText
{
    public static string Required(string? value, int max, string message)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length is > 0 && trimmed.Length <= max ? trimmed : throw new ArgumentException(message, nameof(value));
    }

    public static string? Optional(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (trimmed?.Length > max)
        {
            throw new ArgumentException($"Höchstens {max} Zeichen.", nameof(value));
        }

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

internal sealed class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> builder)
    {
        builder.ToTable("resources");
        builder.ConfigureVersioned();
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Name).HasMaxLength(100);
        builder.Property(r => r.Identifier).HasMaxLength(50);
        builder.Property(r => r.Notes).HasMaxLength(2000);
    }
}

internal sealed class CrewConfiguration : IEntityTypeConfiguration<Crew>
{
    public void Configure(EntityTypeBuilder<Crew> builder)
    {
        builder.ToTable("crews");
        builder.ConfigureVersioned();
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(100);
        builder.Property(c => c.Color).HasMaxLength(7);
        builder.Property(c => c.MemberUserIds).HasColumnType("uuid[]");
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.LeaderUserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(c => c.MemberUserIds).HasMethod("gin");
    }
}

internal sealed class AbsenceConfiguration : IEntityTypeConfiguration<Absence>
{
    public void Configure(EntityTypeBuilder<Absence> builder)
    {
        builder.ToTable("absences", table => table.HasCheckConstraint("ck_absences_range", "\"to\" >= \"from\""));
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Note).HasMaxLength(500);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => new { a.UserId, a.From, a.To });
    }
}

internal sealed class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> builder)
    {
        builder.ToTable("assignments");
        builder.ConfigureVersioned();
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Activity).HasMaxLength(200);
        builder.Property(a => a.Notes).HasMaxLength(2000);
        builder.Property(a => a.UserIds).HasColumnType("uuid[]");
        builder.Property(a => a.ResourceIds).HasColumnType("uuid[]");
        builder.HasOne<Bauakte>().WithMany().HasForeignKey(a => a.BauakteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Crew>().WithMany().HasForeignKey(a => a.CrewId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(a => a.Day);
        builder.HasIndex(a => new { a.BauakteId, a.Day });
    }
}
