using System.Text.Json;
using System.Text.Json.Serialization;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Documentation;

public enum InspectionKind
{
    /// <summary>Erstbegehung vor dem Einbau: Untergrund, Randdämmung, Heizung, Zufahrt, Wasser/Strom.</summary>
    InitialInspection,

    /// <summary>CM-Messung der Restfeuchte vor dem Belegen.</summary>
    CmMeasurement,
}

/// <summary>Bindemittel für die Belegreife-Grenzwerte.</summary>
public enum ScreedBinderKind
{
    /// <summary>Zementestrich (CT).</summary>
    Cement,

    /// <summary>Calciumsulfatestrich (CA, CAF).</summary>
    CalciumSulfate,
}

public enum CheckResult
{
    Ok,
    NotOk,
    NotApplicable,
}

/// <summary>
/// Prüfung in der Bauakte. Prüfungen sind Nachweise: Sie werden nur angelegt, nie geändert. Eine Korrektur ist
/// eine neue Prüfung mit Verweis auf die korrigierte (docs/reengineering/15, Abschnitt 2).
/// </summary>
public sealed class Inspection : IVersionedEntity
{
    public const int MaxRemarksLength = 4_000;

    /// <summary>Freie Notizen (Erstbegehung): bewusst großzügig, mehrzeilig.</summary>
    public const int MaxNotesLength = 100_000;
    public const int MaxLocationLength = 200;

    /// <summary>Feste Punkte der Erstbegehung (Reihenfolge = Anzeige).</summary>
    public static IReadOnlyList<string> InitialChecks { get; } =
        ["substrate", "edgeInsulation", "heatingPressureTest", "access", "utilities"];

    public Guid Id { get; private set; }
    public Guid BauakteId { get; private set; }
    public InspectionKind Kind { get; private set; }
    public Guid PerformedByUserId { get; private set; }
    public DateTimeOffset PerformedAt { get; private set; }
    public Guid? CorrectsInspectionId { get; private set; }
    public string? Remarks { get; private set; }

    /// <summary>Freie Notizen der Begehung (beliebig lang, mehrzeilig) – zusätzlich zur kurzen Bemerkung.</summary>
    public string? Notes { get; private set; }

    // Erstbegehung
    public bool NotRequired { get; private set; }
    public Dictionary<string, CheckResult> Checks { get; private set; } = [];
    public List<Guid> PhotoFileIds { get; private set; } = [];

    /// <summary>Werte der übrigen Felder aus dem Erstbegehungs-Formular (Schlüssel → Wert).</summary>
    public Dictionary<string, JsonElement> Values { get; private set; } = [];

    /// <summary>Bemerkung je Prüfpunkt.</summary>
    public Dictionary<string, string> CheckRemarks { get; private set; } = [];

    /// <summary>Fotos je Prüfpunkt.</summary>
    public Dictionary<string, List<Guid>> CheckPhotos { get; private set; } = [];

    /// <summary>Fassung des Formular-Schemas bei der Erfassung (leer bei älteren Erstbegehungen).</summary>
    public long? SchemaVersion { get; private set; }

    // CM-Messung
    public string? Location { get; private set; }
    public ScreedBinderKind? Binder { get; private set; }
    public bool? Heated { get; private set; }
    public int? ThicknessMm { get; private set; }
    public decimal? MeasuredCmPercent { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Grenzwert für die Belegreife in CM-% nach den gängigen Merkblättern (BEB/ZDB) für Zement- bzw.
    /// Calciumsulfatestrich, unbeheizt/beheizt. Bodenbelagshersteller können strengere Werte vorgeben.
    /// </summary>
    public decimal? LimitCmPercent => (Binder, Heated) switch
    {
        (ScreedBinderKind.Cement, false) => 2.0m,
        (ScreedBinderKind.Cement, true) => 1.8m,
        (ScreedBinderKind.CalciumSulfate, false) => 0.5m,
        (ScreedBinderKind.CalciumSulfate, true) => 0.3m,
        _ => null,
    };

    public bool? IsReadyForCovering => Kind == InspectionKind.CmMeasurement && MeasuredCmPercent is { } measured && LimitCmPercent is { } limit
        ? measured <= limit
        : null;

    private Inspection()
    {
    }

    public static Inspection CreateInitial(Guid id, Guid bauakteId, Guid userId, DateTimeOffset performedAt, bool notRequired,
        IReadOnlyDictionary<string, CheckResult> checks, IReadOnlyList<Guid> photoFileIds, string? remarks, Guid? correctsInspectionId,
        string? notes = null)
    {
        // Prüfpunkte kommen aus dem Formular-Schema (Stammdaten); nur die Form der Schlüssel wird geprüft.
        var unknown = checks.Keys.Where(k => k.Length is 0 or > 40 || !k.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')).ToList();
        if (unknown.Count > 0 || checks.Count > 100)
        {
            throw new ArgumentException($"Ungültige Prüfpunkte: {string.Join(", ", unknown)}.", nameof(checks));
        }

        if (!notRequired && checks.Count == 0)
        {
            throw new ArgumentException("Mindestens ein Prüfpunkt ist erforderlich (oder „nicht notwendig“).", nameof(checks));
        }

        if (notRequired && string.IsNullOrWhiteSpace(remarks))
        {
            throw new ArgumentException("Bitte begründen, warum keine Erstbegehung notwendig ist.", nameof(remarks));
        }

        return new Inspection
        {
            Id = id, BauakteId = bauakteId, Kind = InspectionKind.InitialInspection, PerformedByUserId = userId, PerformedAt = performedAt,
            NotRequired = notRequired, Checks = notRequired ? [] : new Dictionary<string, CheckResult>(checks),
            PhotoFileIds = photoFileIds.Distinct().Take(50).ToList(), Remarks = CleanRemarks(remarks), CorrectsInspectionId = correctsInspectionId,
            Notes = CleanNotes(notes),
        };
    }

    /// <summary>Werte, Bemerkungen und Fotos je Punkt aus dem konfigurierbaren Formular übernehmen (nur beim Anlegen).</summary>
    public void Capture(IReadOnlyDictionary<string, JsonElement>? values, IReadOnlyDictionary<string, string>? remarks,
        IReadOnlyDictionary<string, List<Guid>>? photos, long? schemaVersion)
    {
        if (values is { Count: > 200 } || remarks is { Count: > 100 } || photos is { Count: > 100 })
        {
            throw new ArgumentException("Zu viele Felder in der Erstbegehung.", nameof(values));
        }

        Values = values?.Where(v => v.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.Object or JsonValueKind.Array)
                && (v.Value.ValueKind != JsonValueKind.String || v.Value.GetString()!.Length <= 4000))
            .ToDictionary(v => v.Key, v => v.Value.Clone()) ?? [];
        CheckRemarks = remarks?.Where(r => !string.IsNullOrWhiteSpace(r.Value)).ToDictionary(r => r.Key, r => r.Value.Trim()[..Math.Min(r.Value.Trim().Length, 2000)]) ?? [];
        CheckPhotos = photos?.ToDictionary(r => r.Key, r => r.Value.Distinct().Take(20).ToList()) ?? [];
        SchemaVersion = schemaVersion;
    }

    public static Inspection CreateCmMeasurement(Guid id, Guid bauakteId, Guid userId, DateTimeOffset performedAt, string location,
        ScreedBinderKind binder, bool heated, int? thicknessMm, decimal measuredCmPercent, string? remarks, Guid? correctsInspectionId,
        string? notes = null)
    {
        var trimmed = location?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > MaxLocationLength)
        {
            throw new ArgumentException("Bitte den Messort (Raum) angeben.", nameof(location));
        }

        if (measuredCmPercent is < 0 or > 20)
        {
            throw new ArgumentException("Der Messwert muss zwischen 0 und 20 CM-% liegen.", nameof(measuredCmPercent));
        }

        if (thicknessMm is < 10 or > 300)
        {
            throw new ArgumentException("Die Estrichdicke muss zwischen 10 und 300 mm liegen.", nameof(thicknessMm));
        }

        return new Inspection
        {
            Id = id, BauakteId = bauakteId, Kind = InspectionKind.CmMeasurement, PerformedByUserId = userId, PerformedAt = performedAt,
            Location = trimmed, Binder = binder, Heated = heated, ThicknessMm = thicknessMm,
            MeasuredCmPercent = decimal.Round(measuredCmPercent, 2), Remarks = CleanRemarks(remarks), CorrectsInspectionId = correctsInspectionId,
            Notes = CleanNotes(notes),
        };
    }

    private static string? CleanNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return null;
        }

        var text = notes.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        return text.Length <= MaxNotesLength
            ? text
            : throw new ArgumentException($"Die Notizen sind länger als {MaxNotesLength} Zeichen.", nameof(notes));
    }

    private static string? CleanRemarks(string? remarks)
    {
        var trimmed = remarks?.Trim();
        if (trimmed?.Length > MaxRemarksLength)
        {
            throw new ArgumentException($"Die Bemerkung ist länger als {MaxRemarksLength} Zeichen.", nameof(remarks));
        }

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

internal sealed class InspectionConfiguration : IEntityTypeConfiguration<Inspection>
{
    private static readonly JsonSerializerOptions ChecksJson = new() { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    public void Configure(EntityTypeBuilder<Inspection> builder)
    {
        builder.ToTable("inspections");
        builder.ConfigureVersioned();
        builder.Property(inspection => inspection.Id).ValueGeneratedNever();
        builder.Property(inspection => inspection.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(inspection => inspection.Binder).HasConversion<string>().HasMaxLength(30);
        builder.Property(inspection => inspection.Remarks).HasMaxLength(Inspection.MaxRemarksLength);
        builder.Property(inspection => inspection.Location).HasMaxLength(Inspection.MaxLocationLength);
        builder.Property(inspection => inspection.MeasuredCmPercent).HasPrecision(4, 2);
        builder.Property(inspection => inspection.Checks).HasColumnType("jsonb").HasConversion(
            checks => JsonSerializer.Serialize(checks, ChecksJson),
            json => JsonSerializer.Deserialize<Dictionary<string, CheckResult>>(json, ChecksJson) ?? new Dictionary<string, CheckResult>(),
            new ValueComparer<Dictionary<string, CheckResult>>(
                (a, b) => a!.Count == b!.Count && !a.Except(b).Any(),
                checks => checks.Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
                checks => new Dictionary<string, CheckResult>(checks)));
        builder.Property(inspection => inspection.Values).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, ChecksJson),
            json => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, ChecksJson) ?? new Dictionary<string, JsonElement>(),
            new ValueComparer<Dictionary<string, JsonElement>>((a, b) => JsonSerializer.Serialize(a, ChecksJson) == JsonSerializer.Serialize(b, ChecksJson),
                v => JsonSerializer.Serialize(v, ChecksJson).GetHashCode(StringComparison.Ordinal), v => new Dictionary<string, JsonElement>(v)));
        builder.Property(inspection => inspection.CheckRemarks).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, ChecksJson),
            json => JsonSerializer.Deserialize<Dictionary<string, string>>(json, ChecksJson) ?? new Dictionary<string, string>(),
            new ValueComparer<Dictionary<string, string>>((a, b) => JsonSerializer.Serialize(a, ChecksJson) == JsonSerializer.Serialize(b, ChecksJson),
                v => JsonSerializer.Serialize(v, ChecksJson).GetHashCode(StringComparison.Ordinal), v => new Dictionary<string, string>(v)));
        builder.Property(inspection => inspection.CheckPhotos).HasColumnType("jsonb").HasConversion(
            v => JsonSerializer.Serialize(v, ChecksJson),
            json => JsonSerializer.Deserialize<Dictionary<string, List<Guid>>>(json, ChecksJson) ?? new Dictionary<string, List<Guid>>(),
            new ValueComparer<Dictionary<string, List<Guid>>>((a, b) => JsonSerializer.Serialize(a, ChecksJson) == JsonSerializer.Serialize(b, ChecksJson),
                v => JsonSerializer.Serialize(v, ChecksJson).GetHashCode(StringComparison.Ordinal), v => v.ToDictionary(x => x.Key, x => x.Value.ToList())));
        builder.Property(inspection => inspection.PhotoFileIds).HasColumnType("uuid[]");
        builder.Ignore(inspection => inspection.LimitCmPercent);
        builder.Ignore(inspection => inspection.IsReadyForCovering);
        builder.HasOne<Bauakte>().WithMany().HasForeignKey(inspection => inspection.BauakteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(inspection => inspection.PerformedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Inspection>().WithMany().HasForeignKey(inspection => inspection.CorrectsInspectionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(inspection => new { inspection.BauakteId, inspection.PerformedAt });
    }
}
