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

    /// <summary>Sonderurlaub (Hochzeit, Umzug, Todesfall …) – zählt nicht vom Urlaubsanspruch.</summary>
    SpecialLeave,

    /// <summary>Berufsschule (Auszubildende).</summary>
    School,
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

    /// <summary>Krankmeldung: AU-Bescheinigung (Datei ohne Bauakte, nur für die Personalverwaltung lesbar).</summary>
    public Guid? CertificateFileId { get; private set; }

    /// <summary>Aus einem genehmigten Urlaubsantrag entstanden.</summary>
    public Guid? RequestId { get; private set; }

    private Absence()
    {
    }

    public static Absence Create(Guid id, Guid userId, DateOnly from, DateOnly to, AbsenceKind kind, string? note, Guid? certificateFileId = null,
        Guid? requestId = null)
    {
        if (to < from)
        {
            throw new ArgumentException("Das Ende liegt vor dem Beginn.", nameof(to));
        }

        if (to.DayNumber - from.DayNumber > 366)
        {
            throw new ArgumentException("Eine Abwesenheit dauert höchstens ein Jahr.", nameof(to));
        }

        return new Absence
        {
            Id = id, UserId = userId, From = from, To = to, Kind = kind, Note = PlanningText.Optional(note, 500),
            CertificateFileId = kind == AbsenceKind.Sick ? certificateFileId : null, RequestId = requestId,
        };
    }

    public bool Covers(DateOnly day) => From <= day && day <= To;
}

/// <summary>
/// Einsatz: an einem Tag auf einer Baustelle, mit Kolonne und/oder einzelnen Mitarbeitern und Geräten.
/// Mehrtägige Arbeiten sind mehrere Einsätze (jeder Tag einzeln verschiebbar).
/// </summary>
/// <summary>Art des Termins wie in Estrich Web (Tageseinteilung, Kalender).</summary>
public enum AssignmentKind
{
    /// <summary>Estrich einbringen bzw. Ausführung.</summary>
    Screed,
    InitialInspection,
    SecondInspection,
    CmMeasurement,
    Acceptance,
    /// <summary>Aufmaß.</summary>
    Survey,
    /// <summary>Kontrolle / Nacharbeit.</summary>
    Check,
    CustomerAppointment,
    Delivery,
    Other,
}

public sealed class Assignment : IVersionedEntity
{
    public Guid Id { get; private set; }
    public Guid BauakteId { get; private set; }
    public DateOnly Day { get; private set; }

    /// <summary>Beginn vor Ort (optional), z. B. 07:00.</summary>
    public TimeOnly? StartTime { get; private set; }

    /// <summary>Voraussichtliches Ende (optional).</summary>
    public TimeOnly? EndTime { get; private set; }
    public AssignmentKind Kind { get; private set; } = AssignmentKind.Screed;
    public string Activity { get; private set; } = string.Empty;
    public Guid? CrewId { get; private set; }
    public List<Guid> UserIds { get; private set; } = [];

    /// <summary>Kolonnenmitglieder, die bei diesem Einsatz nicht dabei sind (z. B. heute auf einer anderen Baustelle).</summary>
    public List<Guid> ExcludedUserIds { get; private set; } = [];
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
        IReadOnlyCollection<Guid> resourceIds, string? notes, AssignmentKind kind = AssignmentKind.Screed, TimeOnly? endTime = null,
        IReadOnlyCollection<Guid>? excludedUserIds = null)
    {
        if (crewId is null && userIds.Count == 0)
        {
            throw new ArgumentException("Bitte eine Kolonne oder mindestens einen Mitarbeiter einplanen.", nameof(userIds));
        }

        if (startTime is { } start && endTime is { } end && end <= start)
        {
            throw new ArgumentException("Das Ende muss nach dem Beginn liegen.", nameof(endTime));
        }

        Day = day;
        StartTime = startTime;
        EndTime = endTime;
        Kind = kind;
        Activity = PlanningText.Required(activity, 200, "Bitte angeben, was zu tun ist (z. B. „Estrich einbringen EG“).");
        CrewId = crewId;
        UserIds = userIds.Distinct().ToList();
        ExcludedUserIds = crewId is null ? [] : (excludedUserIds ?? []).Except(UserIds).Distinct().ToList();
        ResourceIds = resourceIds.Distinct().ToList();
        Notes = PlanningText.Optional(notes, 2000);
    }

    /// <summary>Wer tatsächlich dabei ist: Kolonne ohne Ausnahmen plus einzeln eingeplante Mitarbeiter.</summary>
    public IReadOnlyList<Guid> PeopleWith(IReadOnlyCollection<Guid> crewMembers) =>
        crewMembers.Except(ExcludedUserIds).Concat(UserIds).Distinct().ToList();

    /// <summary>Schnelleinsatzplanung: Person kommt dazu oder fällt weg (Kolonnenmitglied wird ausgenommen).</summary>
    public void SetPerson(Guid userId, bool present, IReadOnlyCollection<Guid> crewMembers)
    {
        if (present)
        {
            ExcludedUserIds = ExcludedUserIds.Where(id => id != userId).ToList();
            if (!crewMembers.Contains(userId) && !UserIds.Contains(userId))
            {
                UserIds = [.. UserIds, userId];
            }

            return;
        }

        UserIds = UserIds.Where(id => id != userId).ToList();
        if (crewMembers.Contains(userId) && !ExcludedUserIds.Contains(userId))
        {
            ExcludedUserIds = [.. ExcludedUserIds, userId];
        }
    }

    public void SetResource(Guid resourceId, bool present) =>
        ResourceIds = present ? ResourceIds.Append(resourceId).Distinct().ToList() : ResourceIds.Where(id => id != resourceId).ToList();

    /// <summary>Uhrzeit ändern (Teamplaner: Ziehen, Dauer ziehen).</summary>
    public void Reschedule(DateOnly day, TimeOnly? startTime, TimeOnly? endTime)
    {
        if (startTime is { } start && endTime is { } end && end <= start)
        {
            throw new ArgumentException("Das Ende muss nach dem Beginn liegen.", nameof(endTime));
        }

        Day = day;
        StartTime = startTime;
        EndTime = endTime;
    }

    /// <summary>Zeitraum am Tag für Überschneidungen; ohne Uhrzeit gilt der ganze Tag, ohne Ende der Rest des Tages.</summary>
    public (TimeOnly From, TimeOnly To) Span() =>
        StartTime is { } s ? (s, EndTime ?? TimeOnly.MaxValue) : (TimeOnly.MinValue, TimeOnly.MaxValue);

    public void MoveTo(Guid bauakteId) => BauakteId = bauakteId;
}

internal static class PlanningText
{
    public static string Required(string? value, int max, string message)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length is > 0 && trimmed.Length <= max ? trimmed : throw new ArgumentException(message, nameof(value));
    }

    /// <summary>Farbe als #RRGGBB (leer = keine).</summary>
    public static string? Color(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(trimmed, "^#[0-9A-Fa-f]{6}$")
            ? trimmed.ToUpperInvariant()
            : throw new ArgumentException("Die Farbe muss als #RRGGBB angegeben werden.", nameof(value));
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
        builder.HasIndex(a => a.CertificateFileId);
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
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(30).HasDefaultValue(AssignmentKind.Screed).HasSentinel(AssignmentKind.Screed);
        builder.Property(a => a.Notes).HasMaxLength(2000);
        builder.Property(a => a.UserIds).HasColumnType("uuid[]");
        builder.Property(a => a.ExcludedUserIds).HasColumnType("uuid[]").HasDefaultValueSql("'{}'::uuid[]");
        builder.Property(a => a.ResourceIds).HasColumnType("uuid[]");
        builder.HasOne<Bauakte>().WithMany().HasForeignKey(a => a.BauakteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Crew>().WithMany().HasForeignKey(a => a.CrewId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(a => a.Day);
        builder.HasIndex(a => new { a.BauakteId, a.Day });
    }
}
