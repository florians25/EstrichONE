using System.Text.RegularExpressions;
using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>
/// Konfigurierbare Abwesenheitsart (Urlaub, Krankheit, Kind krank, Berufsschule …). Die Grundart <see cref="Kind"/> bestimmt,
/// wie Planung, Tagesplan und Apps die Abwesenheit darstellen; Krankheitsarten (Grundart Krank) sieht nur die Personalverwaltung.
/// </summary>
public sealed partial class AbsenceType
{
    public Guid Id { get; private set; }

    /// <summary>Stabiler Schlüssel (Standardarten: vacation, sick, child_sick …; eigene Arten: custom_…).</summary>
    public string Key { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>Kürzel im Teamkalender (1 bis 3 Zeichen).</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>Farbe #RRGGBB.</summary>
    public string Color { get; private set; } = "#9E9E9E";

    public AbsenceKind Kind { get; private set; }

    /// <summary>Anträge dieser Art müssen genehmigt werden; sonst gelten sie sofort.</summary>
    public bool RequiresApproval { get; private set; }

    /// <summary>Tage werden vom Urlaubskonto abgezogen.</summary>
    public bool CountsAsVacation { get; private set; }

    /// <summary>Mitarbeiter dürfen diese Art selbst beantragen (Krankheitsarten nie: die erfasst das Büro).</summary>
    public bool Requestable { get; private set; }

    /// <summary>Bezahlte Abwesenheit (Hinweis für die Jahresübersicht, keine Lohnabrechnung).</summary>
    public bool IsPaid { get; private set; } = true;

    public bool IsActive { get; private set; } = true;
    public int SortOrder { get; private set; }

    /// <summary>Standardart (Schlüssel und Grundart fest, Name, Farbe und Regeln änderbar).</summary>
    public bool IsSystem { get; private set; }

    public bool IsSickness => Kind == AbsenceKind.Sick;

    private AbsenceType()
    {
    }

    public static AbsenceType Create(Guid id, string key, AbsenceKind kind, bool isSystem)
    {
        if (!KeyPattern().IsMatch(key))
        {
            throw new ArgumentException("Ungültiger Schlüssel.", nameof(key));
        }

        return new AbsenceType { Id = id, Key = key, Kind = kind, IsSystem = isSystem };
    }

    public void Update(string name, string code, string color, AbsenceKind kind, bool requiresApproval, bool countsAsVacation, bool requestable, bool isPaid,
        bool isActive, int sortOrder)
    {
        Name = PlanningText.Required(name, 60, "Bitte einen Namen angeben.");
        Code = PlanningText.Required(code, 3, "Bitte ein Kürzel (bis 3 Zeichen) angeben.");
        if (!ColorPattern().IsMatch(color))
        {
            throw new ArgumentException("Die Farbe muss als #RRGGBB angegeben werden.", nameof(color));
        }

        if (IsSystem && kind != Kind)
        {
            throw new ArgumentException("Die Grundart einer Standardart lässt sich nicht ändern.", nameof(kind));
        }

        if (kind == AbsenceKind.Sick && (requestable || requiresApproval || countsAsVacation))
        {
            throw new ArgumentException("Krankheitsarten erfasst das Büro ohne Genehmigung; sie zählen nicht als Urlaub.", nameof(kind));
        }

        Color = color.ToUpperInvariant();
        Kind = kind;
        RequiresApproval = requiresApproval;
        CountsAsVacation = countsAsVacation;
        Requestable = requestable;
        IsPaid = isPaid;
        IsActive = isActive;
        SortOrder = sortOrder;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{1,39}$")]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex ColorPattern();
}

/// <summary>Standardarten (feste IDs; die Migration legt sie an, Tests und leere Datenbanken nutzen dieselben Werte).</summary>
public static class StandardAbsenceTypes
{
    public static readonly Guid Vacation = Guid.Parse("01a10000-0000-7000-8000-000000000001");
    public static readonly Guid Sick = Guid.Parse("01a10000-0000-7000-8000-000000000002");
    public static readonly Guid ChildSick = Guid.Parse("01a10000-0000-7000-8000-000000000003");
    public static readonly Guid School = Guid.Parse("01a10000-0000-7000-8000-000000000004");
    public static readonly Guid Training = Guid.Parse("01a10000-0000-7000-8000-000000000005");
    public static readonly Guid SpecialLeave = Guid.Parse("01a10000-0000-7000-8000-000000000006");
    public static readonly Guid Overtime = Guid.Parse("01a10000-0000-7000-8000-000000000007");
    public static readonly Guid Unpaid = Guid.Parse("01a10000-0000-7000-8000-000000000008");
    public static readonly Guid Closure = Guid.Parse("01a10000-0000-7000-8000-000000000009");
    public static readonly Guid Other = Guid.Parse("01a10000-0000-7000-8000-00000000000a");

    /// <summary>Id, Schlüssel, Name, Kürzel, Farbe, Grundart, genehmigungspflichtig, Urlaubskonto, beantragbar, bezahlt.</summary>
    public static IReadOnlyList<(Guid Id, string Key, string Name, string Code, string Color, AbsenceKind Kind, bool Approval, bool Counts, bool Requestable, bool Paid)> All { get; } =
    [
        (Vacation, "vacation", "Urlaub", "U", "#43A047", AbsenceKind.Vacation, true, true, true, true),
        (Sick, "sick", "Krankheit", "K", "#E53935", AbsenceKind.Sick, false, false, false, true),
        (ChildSick, "child_sick", "Kind krank", "KK", "#F06292", AbsenceKind.Sick, false, false, false, false),
        (School, "school", "Berufsschule", "B", "#1E88E5", AbsenceKind.School, false, false, true, true),
        (Training, "training", "Fortbildung", "F", "#00ACC1", AbsenceKind.Training, true, false, true, true),
        (SpecialLeave, "special_leave", "Sonderurlaub", "SU", "#8E24AA", AbsenceKind.SpecialLeave, true, false, true, true),
        (Overtime, "overtime", "Überstundenabbau", "ÜA", "#FB8C00", AbsenceKind.Other, true, false, true, true),
        (Unpaid, "unpaid", "Unbezahlter Urlaub", "UU", "#6D4C41", AbsenceKind.Other, true, false, true, false),
        (Closure, "closure", "Betriebsferien", "BF", "#7CB342", AbsenceKind.Vacation, false, true, false, true),
        (Other, "other", "Sonstige Abwesenheit", "A", "#9E9E9E", AbsenceKind.Other, true, false, true, true),
    ];

    /// <summary>Standardart zur Grundart (ältere Abwesenheiten ohne Art).</summary>
    public static Guid ForKind(AbsenceKind kind) => kind switch
    {
        AbsenceKind.Vacation => Vacation,
        AbsenceKind.Sick => Sick,
        AbsenceKind.Training => Training,
        AbsenceKind.SpecialLeave => SpecialLeave,
        AbsenceKind.School => School,
        _ => Other,
    };

    public static List<AbsenceType> Create() => All.Select((t, i) =>
    {
        var type = AbsenceType.Create(t.Id, t.Key, t.Kind, true);
        type.Update(t.Name, t.Code, t.Color, t.Kind, t.Approval, t.Counts, t.Requestable, t.Paid, true, (i + 1) * 10);
        return type;
    }).ToList();
}

/// <summary>Betriebsferien oder Brückentag: firmenweite Abwesenheit, für alle Mitarbeiter mit Konto als Abwesenheit eingetragen.</summary>
public sealed class CompanyClosure : IVersionedEntity
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }
    public Guid TypeId { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private CompanyClosure()
    {
    }

    public static CompanyClosure Create(Guid id) => new() { Id = id };

    public void Update(string name, DateOnly from, DateOnly to, Guid typeId)
    {
        if (to < from)
        {
            throw new ArgumentException("Das Ende liegt vor dem Beginn.", nameof(to));
        }

        if (to.DayNumber - from.DayNumber > 62)
        {
            throw new ArgumentException("Betriebsferien dauern höchstens zwei Monate.", nameof(to));
        }

        Name = PlanningText.Required(name, 100, "Bitte eine Bezeichnung angeben.");
        From = from;
        To = to;
        TypeId = typeId;
    }
}

/// <summary>Urlaubskonto eines Mitarbeiters für ein Jahr: abweichender Anspruch, Übertrag, Verfall, Korrektur. Ohne Eintrag wird gerechnet.</summary>
public sealed class VacationAccount : IVersionedEntity
{
    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public int Year { get; private set; }

    /// <summary>Abweichender Jahresanspruch (sonst Anspruch laut Personalstamm, ggf. anteilig).</summary>
    public decimal? Entitlement { get; private set; }

    /// <summary>Übertrag aus dem Vorjahr (sonst automatisch: Rest des Vorjahres).</summary>
    public decimal? CarryOver { get; private set; }

    /// <summary>Abweichendes Verfallsdatum des Übertrags (z. B. verlängert wegen Krankheit).</summary>
    public DateOnly? CarryOverExpiresOn { get; private set; }

    /// <summary>Korrektur in Tagen (+/−), z. B. Ausgleich oder Auszahlung.</summary>
    public decimal Adjustment { get; private set; }

    public string? Note { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private VacationAccount()
    {
    }

    public static VacationAccount Create(Guid id, Guid employeeId, int year) => new() { Id = id, EmployeeId = employeeId, Year = year };

    public void Update(decimal? entitlement, decimal? carryOver, DateOnly? carryOverExpiresOn, decimal adjustment, string? note)
    {
        if (entitlement is < 0 or > 100 || carryOver is < -100 or > 200 || adjustment is < -100 or > 100)
        {
            throw new ArgumentException("Bitte plausible Tage angeben (Anspruch 0 bis 100, Übertrag −100 bis 200, Korrektur −100 bis 100).", nameof(entitlement));
        }

        if (carryOverExpiresOn is { } expires && expires.Year != Year && expires.Year != Year + 1)
        {
            throw new ArgumentException("Das Verfallsdatum liegt im Kontojahr oder im Folgejahr.", nameof(carryOverExpiresOn));
        }

        Entitlement = entitlement is null ? null : Math.Round(entitlement.Value * 2, MidpointRounding.AwayFromZero) / 2;
        CarryOver = carryOver is null ? null : Math.Round(carryOver.Value * 2, MidpointRounding.AwayFromZero) / 2;
        CarryOverExpiresOn = carryOverExpiresOn;
        Adjustment = Math.Round(adjustment * 2, MidpointRounding.AwayFromZero) / 2;
        Note = PlanningText.Optional(note, 500);
    }

    public void SetCarryOver(decimal? carryOver) => CarryOver = carryOver;
}

internal sealed class AbsenceTypeConfiguration : IEntityTypeConfiguration<AbsenceType>
{
    public void Configure(EntityTypeBuilder<AbsenceType> builder)
    {
        builder.ToTable("absence_types");
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Key).HasMaxLength(40);
        builder.Property(t => t.Name).HasMaxLength(60);
        builder.Property(t => t.Code).HasMaxLength(3);
        builder.Property(t => t.Color).HasMaxLength(7);
        builder.Property(t => t.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(t => t.IsSickness);
        builder.HasIndex(t => t.Key).IsUnique();
        builder.HasData(StandardAbsenceTypes.Create());
    }
}

internal sealed class CompanyClosureConfiguration : IEntityTypeConfiguration<CompanyClosure>
{
    public void Configure(EntityTypeBuilder<CompanyClosure> builder)
    {
        builder.ToTable("company_closures", table => table.HasCheckConstraint("ck_company_closures_range", "\"to\" >= \"from\""));
        builder.ConfigureVersioned();
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(100);
        builder.HasOne<AbsenceType>().WithMany().HasForeignKey(c => c.TypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.From);
    }
}

internal sealed class VacationAccountConfiguration : IEntityTypeConfiguration<VacationAccount>
{
    public void Configure(EntityTypeBuilder<VacationAccount> builder)
    {
        builder.ToTable("vacation_accounts");
        builder.ConfigureVersioned();
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Entitlement).HasPrecision(5, 1);
        builder.Property(a => a.CarryOver).HasPrecision(5, 1);
        builder.Property(a => a.Adjustment).HasPrecision(5, 1);
        builder.Property(a => a.Note).HasMaxLength(500);
        builder.HasOne<Employee>().WithMany().HasForeignKey(a => a.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => new { a.EmployeeId, a.Year }).IsUnique();
    }
}
