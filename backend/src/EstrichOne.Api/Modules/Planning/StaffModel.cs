using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Planning;

public enum EmploymentType
{
    FullTime,
    PartTime,
    MiniJob,
    Apprentice,
    Temporary,
    Freelancer,
}

/// <summary>
/// Personalstamm: Mitarbeiter mit Kontakt, Anschrift, Notfallkontakt, Beschäftigung, Arbeitszeitmodell, Stundensatz und
/// Urlaubsanspruch. Mit einem Benutzerkonto verknüpft, ist er in Kolonnen, Planung und Abwesenheiten verfügbar.
/// </summary>
public sealed class Employee : IVersionedEntity
{
    public Guid Id { get; private set; }
    public string? PersonnelNumber { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? JobTitle { get; private set; }
    public Guid? UserId { get; private set; }

    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Mobile { get; private set; }
    public string? Street { get; private set; }
    public string? PostalCode { get; private set; }
    public string? City { get; private set; }
    public DateOnly? BirthDate { get; private set; }

    public string? EmergencyContactName { get; private set; }
    public string? EmergencyContactPhone { get; private set; }
    public string? EmergencyContactRelation { get; private set; }

    public DateOnly? EntryDate { get; private set; }
    public DateOnly? ExitDate { get; private set; }

    /// <summary>Ende der Probezeit (Erinnerung in den Fristen).</summary>
    public DateOnly? ProbationEnd { get; private set; }
    public EmploymentType EmploymentType { get; private set; }

    /// <summary>Arbeitszeitmodell, z. B. „Vollzeit 40 h, Mo–Fr“ oder „Saison (Mär–Nov)“.</summary>
    public string? WorkTimeModel { get; private set; }
    public decimal? WeeklyHours { get; private set; }

    /// <summary>Arbeitstage als Bitmaske (Bit 0 = Sonntag … Bit 6 = Samstag); Standard Mo–Fr.</summary>
    public int WorkDays { get; private set; } = 0b0111110;
    public decimal? HourlyRate { get; private set; }
    public decimal VacationDaysPerYear { get; private set; } = 30;
    public decimal VacationCarryOver { get; private set; }
    public string? Notes { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private Employee()
    {
    }

    public static Employee Create(Guid id) => new() { Id = id };

    public string DisplayName => $"{FirstName} {LastName}".Trim();

    public bool IsActiveOn(DateOnly day) => (EntryDate is null || EntryDate <= day) && (ExitDate is null || ExitDate >= day);

    public void Update(string firstName, string lastName, string? personnelNumber, string? jobTitle, EmploymentType employmentType)
    {
        FirstName = PlanningText.Optional(firstName, 100) ?? string.Empty;
        LastName = PlanningText.Required(lastName, 100, "Bitte den Nachnamen angeben.");
        PersonnelNumber = PlanningText.Optional(personnelNumber, 20);
        JobTitle = PlanningText.Optional(jobTitle, 100);
        EmploymentType = employmentType;
    }

    public void ChangeContact(string? email, string? phone, string? mobile, string? street, string? postalCode, string? city, DateOnly? birthDate)
    {
        Email = PlanningText.Optional(email, 320);
        if (Email is not null && !Email.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("Die E-Mail-Adresse ist ungültig.", nameof(email));
        }

        Phone = PlanningText.Optional(phone, 50);
        Mobile = PlanningText.Optional(mobile, 50);
        Street = PlanningText.Optional(street, 200);
        PostalCode = PlanningText.Optional(postalCode, 10);
        City = PlanningText.Optional(city, 100);
        BirthDate = birthDate;
    }

    public void ChangeEmergencyContact(string? name, string? phone, string? relation)
    {
        EmergencyContactName = PlanningText.Optional(name, 200);
        EmergencyContactPhone = PlanningText.Optional(phone, 50);
        EmergencyContactRelation = PlanningText.Optional(relation, 50);
    }

    public void ChangeEmployment(DateOnly? entryDate, DateOnly? exitDate, string? workTimeModel, decimal? weeklyHours, IReadOnlyCollection<DayOfWeek>? workDays,
        decimal vacationDaysPerYear, decimal vacationCarryOver)
    {
        if (entryDate is { } entry && exitDate is { } exit && exit < entry)
        {
            throw new ArgumentException("Der Austritt liegt vor dem Eintritt.", nameof(exitDate));
        }

        if (weeklyHours is < 0 or > 80)
        {
            throw new ArgumentException("Die Wochenstunden müssen zwischen 0 und 80 liegen.", nameof(weeklyHours));
        }

        if (vacationDaysPerYear is < 0 or > 60 || vacationCarryOver is < -60 or > 120)
        {
            throw new ArgumentException("Bitte einen gültigen Urlaubsanspruch angeben.", nameof(vacationDaysPerYear));
        }

        EntryDate = entryDate;
        ExitDate = exitDate;
        WorkTimeModel = PlanningText.Optional(workTimeModel, 100);
        WeeklyHours = weeklyHours;
        WorkDays = workDays is null || workDays.Count == 0 ? 0b0111110 : workDays.Aggregate(0, (mask, day) => mask | (1 << (int)day));
        VacationDaysPerYear = vacationDaysPerYear;
        VacationCarryOver = vacationCarryOver;
    }

    public void ChangeProbation(DateOnly? probationEnd)
    {
        if (probationEnd is { } end && EntryDate is { } entry && end < entry)
        {
            throw new ArgumentException("Das Ende der Probezeit liegt vor dem Eintritt.", nameof(probationEnd));
        }

        ProbationEnd = probationEnd;
    }

    public void ChangeRate(decimal? hourlyRate)
    {
        if (hourlyRate is < 0 or > 1000)
        {
            throw new ArgumentException("Bitte einen gültigen Stundensatz angeben.", nameof(hourlyRate));
        }

        HourlyRate = hourlyRate;
    }

    public void ChangeNotes(string? notes) => Notes = PlanningText.Optional(notes, 4000);

    public void LinkUser(Guid? userId) => UserId = userId;

    public IReadOnlyList<DayOfWeek> WorkDayList => Enumerable.Range(0, 7).Where(bit => (WorkDays & (1 << bit)) != 0).Select(bit => (DayOfWeek)bit).ToList();

    /// <summary>Arbeitstage eines Zeitraums nach dem Arbeitszeitmodell, ohne gesetzliche Feiertage.</summary>
    public int WorkingDays(DateOnly from, DateOnly to, IReadOnlySet<DateOnly>? holidays = null)
    {
        var count = 0;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if ((WorkDays & (1 << (int)day.DayOfWeek)) != 0 && holidays?.Contains(day) != true)
            {
                count++;
            }
        }

        return count;
    }
}

public enum QualificationKind
{
    /// <summary>Führerscheinklasse (B, BE, C1, C1E, CE …).</summary>
    DriverLicense,
    Certificate,
    Training,
    /// <summary>Arbeitsmedizinische Vorsorge (G25, G26 …).</summary>
    Medical,
    Other,
}

/// <summary>Qualifikation oder Führerscheinklasse mit optionalem Ablaufdatum.</summary>
public sealed class EmployeeQualification
{
    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public QualificationKind Kind { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Number { get; private set; }
    public DateOnly? IssuedOn { get; private set; }
    public DateOnly? ExpiresOn { get; private set; }
    public string? Notes { get; private set; }

    private EmployeeQualification()
    {
    }

    public static EmployeeQualification Create(Guid id, Guid employeeId) => new() { Id = id, EmployeeId = employeeId };

    public void Update(QualificationKind kind, string name, string? number, DateOnly? issuedOn, DateOnly? expiresOn, string? notes)
    {
        if (issuedOn is { } issued && expiresOn is { } expires && expires < issued)
        {
            throw new ArgumentException("Das Ablaufdatum liegt vor dem Ausstellungsdatum.", nameof(expiresOn));
        }

        Kind = kind;
        Name = PlanningText.Required(name, 100, "Bitte die Qualifikation bzw. Führerscheinklasse angeben.");
        Number = PlanningText.Optional(number, 50);
        IssuedOn = issuedOn;
        ExpiresOn = expiresOn;
        Notes = PlanningText.Optional(notes, 500);
    }
}

/// <summary>Ausgegebenes Gerät oder Fahrzeug (Dienstwagen, Handy, Laser, Schlüssel …).</summary>
public sealed class EmployeeEquipment
{
    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }

    /// <summary>Verknüpft mit Fahrzeug/Gerät der Planung (optional).</summary>
    public Guid? ResourceId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? SerialNumber { get; private set; }
    public DateOnly? IssuedOn { get; private set; }
    public DateOnly? ReturnedOn { get; private set; }
    public string? Notes { get; private set; }

    private EmployeeEquipment()
    {
    }

    public static EmployeeEquipment Create(Guid id, Guid employeeId) => new() { Id = id, EmployeeId = employeeId };

    public void Update(Guid? resourceId, string name, string? serialNumber, DateOnly? issuedOn, DateOnly? returnedOn, string? notes)
    {
        if (issuedOn is { } issued && returnedOn is { } returned && returned < issued)
        {
            throw new ArgumentException("Die Rückgabe liegt vor der Ausgabe.", nameof(returnedOn));
        }

        ResourceId = resourceId;
        Name = PlanningText.Required(name, 100, "Bitte das Gerät bzw. Fahrzeug angeben.");
        SerialNumber = PlanningText.Optional(serialNumber, 100);
        IssuedOn = issuedOn;
        ReturnedOn = returnedOn;
        Notes = PlanningText.Optional(notes, 500);
    }
}

public enum EmployeeDocumentCategory
{
    Contract,
    Certificate,
    DriverLicense,
    Medical,
    Training,
    Other,
}

/// <summary>Personaldokument (Datei aus dem Dateispeicher); nur mit Recht „Personalstamm pflegen“ sichtbar.</summary>
public sealed class EmployeeDocument
{
    public Guid Id { get; private set; }
    public Guid EmployeeId { get; private set; }
    public Guid FileId { get; private set; }
    public EmployeeDocumentCategory Category { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public DateOnly? ValidUntil { get; private set; }
    public Guid AddedByUserId { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }

    private EmployeeDocument()
    {
    }

    public static EmployeeDocument Create(Guid id, Guid employeeId, Guid fileId, EmployeeDocumentCategory category, string title, DateOnly? validUntil,
        Guid addedBy, DateTimeOffset now) => new()
        {
            Id = id, EmployeeId = employeeId, FileId = fileId, Category = category,
            Title = PlanningText.Required(title, 200, "Bitte einen Titel für das Dokument angeben."), ValidUntil = validUntil, AddedByUserId = addedBy, AddedAt = now,
        };
}

internal sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("employees");
        builder.ConfigureVersioned();
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.PersonnelNumber).HasMaxLength(20);
        builder.Property(e => e.FirstName).HasMaxLength(100);
        builder.Property(e => e.LastName).HasMaxLength(100);
        builder.Property(e => e.JobTitle).HasMaxLength(100);
        builder.Property(e => e.Email).HasMaxLength(320);
        builder.Property(e => e.Phone).HasMaxLength(50);
        builder.Property(e => e.Mobile).HasMaxLength(50);
        builder.Property(e => e.Street).HasMaxLength(200);
        builder.Property(e => e.PostalCode).HasMaxLength(10);
        builder.Property(e => e.City).HasMaxLength(100);
        builder.Property(e => e.EmergencyContactName).HasMaxLength(200);
        builder.Property(e => e.EmergencyContactPhone).HasMaxLength(50);
        builder.Property(e => e.EmergencyContactRelation).HasMaxLength(50);
        builder.Property(e => e.EmploymentType).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.WorkTimeModel).HasMaxLength(100);
        builder.Property(e => e.WeeklyHours).HasPrecision(5, 2);
        builder.Property(e => e.HourlyRate).HasPrecision(8, 2);
        builder.Property(e => e.VacationDaysPerYear).HasPrecision(5, 1);
        builder.Property(e => e.VacationCarryOver).HasPrecision(5, 1);
        builder.Property(e => e.Notes).HasMaxLength(4000);
        builder.Ignore(e => e.DisplayName);
        builder.Ignore(e => e.WorkDayList);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => e.UserId).IsUnique();
        builder.HasIndex(e => e.PersonnelNumber).IsUnique();
        builder.HasIndex(e => new { e.LastName, e.FirstName });
    }
}

internal sealed class EmployeeQualificationConfiguration : IEntityTypeConfiguration<EmployeeQualification>
{
    public void Configure(EntityTypeBuilder<EmployeeQualification> builder)
    {
        builder.ToTable("employee_qualifications");
        builder.Property(q => q.Id).ValueGeneratedNever();
        builder.Property(q => q.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(q => q.Name).HasMaxLength(100);
        builder.Property(q => q.Number).HasMaxLength(50);
        builder.Property(q => q.Notes).HasMaxLength(500);
        builder.HasOne<Employee>().WithMany().HasForeignKey(q => q.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(q => q.EmployeeId);
        builder.HasIndex(q => q.ExpiresOn);
    }
}

internal sealed class EmployeeEquipmentConfiguration : IEntityTypeConfiguration<EmployeeEquipment>
{
    public void Configure(EntityTypeBuilder<EmployeeEquipment> builder)
    {
        builder.ToTable("employee_equipment");
        builder.Property(q => q.Id).ValueGeneratedNever();
        builder.Property(q => q.Name).HasMaxLength(100);
        builder.Property(q => q.SerialNumber).HasMaxLength(100);
        builder.Property(q => q.Notes).HasMaxLength(500);
        builder.HasOne<Employee>().WithMany().HasForeignKey(q => q.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Resource>().WithMany().HasForeignKey(q => q.ResourceId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(q => q.EmployeeId);
    }
}

internal sealed class EmployeeDocumentConfiguration : IEntityTypeConfiguration<EmployeeDocument>
{
    public void Configure(EntityTypeBuilder<EmployeeDocument> builder)
    {
        builder.ToTable("employee_documents");
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Title).HasMaxLength(200);
        builder.HasOne<Employee>().WithMany().HasForeignKey(d => d.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(d => d.EmployeeId);
        builder.HasIndex(d => d.FileId).IsUnique();
    }
}
