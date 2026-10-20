using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Documentation;

public enum DiaryWeather
{
    Sunny,
    Cloudy,
    Rain,
    Snow,
    Frost,
    Wind,
}

/// <summary>
/// Bautagebuch-Eintrag: was an einem Tag auf der Baustelle geschah. Einträge werden angefügt; ändern und löschen
/// darf nur der Verfasser (offline konfliktfrei, docs/reengineering/15, Abschnitt 2). Wetter ist optional.
/// </summary>
public sealed class DiaryEntry : IVersionedEntity, ISoftDeletable
{
    public const int MaxTextLength = 10_000;
    public const int MaxCrewLength = 100;
    public const int MaxPhotos = 50;

    public Guid Id { get; private set; }
    public Guid BauakteId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public DateOnly Day { get; private set; }
    public string Text { get; private set; } = string.Empty;
    public DiaryWeather? Weather { get; private set; }
    public int? TemperatureCelsius { get; private set; }
    public int? WorkersPresent { get; private set; }

    /// <summary>Arbeitsbeginn und -ende auf der Baustelle (optional).</summary>
    public TimeOnly? StartTime { get; private set; }
    public TimeOnly? EndTime { get; private set; }

    /// <summary>Kolonne bzw. anwesende Mannschaft als Text, z. B. „Kolonne Nord (3 Pers.)“.</summary>
    public string? Crew { get; private set; }

    /// <summary>Verknüpfte Fotos der Bauakte (Nachweis des Tages).</summary>
    public List<Guid> PhotoFileIds { get; private set; } = [];
    public DateTimeOffset? EditedAt { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private DiaryEntry()
    {
    }

    public static DiaryEntry Create(Guid id, Guid bauakteId, Guid authorUserId, DateOnly day, string text,
        DiaryWeather? weather, int? temperatureCelsius, int? workersPresent)
    {
        var entry = new DiaryEntry { Id = id, BauakteId = bauakteId, AuthorUserId = authorUserId, Day = day };
        entry.Apply(text, weather, temperatureCelsius, workersPresent);
        return entry;
    }

    public void Edit(string text, DiaryWeather? weather, int? temperatureCelsius, int? workersPresent, DateTimeOffset at)
    {
        Apply(text, weather, temperatureCelsius, workersPresent);
        EditedAt = at;
    }

    public void Delete(DateTimeOffset at) => DeletedAt ??= at;

    /// <summary>Arbeitszeit, Kolonne und Fotos des Tages (zusätzliche Angaben, getrennt vom Text änderbar).</summary>
    public void SetWorkDetails(TimeOnly? startTime, TimeOnly? endTime, string? crew, IReadOnlyCollection<Guid> photoFileIds)
    {
        if (startTime is not null && endTime is not null && endTime <= startTime)
        {
            throw new ArgumentException("Das Arbeitsende muss nach dem Beginn liegen.", nameof(endTime));
        }

        var trimmed = crew?.Trim();
        if (trimmed?.Length > MaxCrewLength)
        {
            throw new ArgumentException($"Die Kolonne ist länger als {MaxCrewLength} Zeichen.", nameof(crew));
        }

        if (photoFileIds.Count > MaxPhotos)
        {
            throw new ArgumentException($"Höchstens {MaxPhotos} Fotos je Eintrag.", nameof(photoFileIds));
        }

        StartTime = startTime;
        EndTime = endTime;
        Crew = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        PhotoFileIds = photoFileIds.Distinct().ToList();
    }

    private void Apply(string text, DiaryWeather? weather, int? temperatureCelsius, int? workersPresent)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Der Eintrag ist leer.", nameof(text));
        }

        if (trimmed.Length > MaxTextLength)
        {
            throw new ArgumentException($"Der Eintrag ist länger als {MaxTextLength} Zeichen.", nameof(text));
        }

        if (temperatureCelsius is < -40 or > 60)
        {
            throw new ArgumentException("Die Temperatur muss zwischen -40 und 60 °C liegen.", nameof(temperatureCelsius));
        }

        if (workersPresent is < 0 or > 500)
        {
            throw new ArgumentException("Die Zahl der Anwesenden ist ungültig.", nameof(workersPresent));
        }

        Text = trimmed;
        Weather = weather;
        TemperatureCelsius = temperatureCelsius;
        WorkersPresent = workersPresent;
    }
}

internal sealed class DiaryEntryConfiguration : IEntityTypeConfiguration<DiaryEntry>
{
    public void Configure(EntityTypeBuilder<DiaryEntry> builder)
    {
        builder.ToTable("diary_entries");
        builder.ConfigureVersioned();
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Text).HasMaxLength(DiaryEntry.MaxTextLength);
        builder.Property(entry => entry.Weather).HasConversion<string>().HasMaxLength(20);
        builder.Property(entry => entry.Crew).HasMaxLength(DiaryEntry.MaxCrewLength);
        builder.Property(entry => entry.PhotoFileIds).HasColumnType("uuid[]");
        builder.HasOne<Bauakte>().WithMany().HasForeignKey(entry => entry.BauakteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(entry => entry.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entry => new { entry.BauakteId, entry.Day });
    }
}
