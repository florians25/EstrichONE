using System.ComponentModel.DataAnnotations;

namespace EstrichOne.Api.Modules.Bauakten.Exports;

/// <summary>
/// Kennwerte des Bauvorhabens, schon bei der Anlage erfasst (Fläche, Estrichart, Aufbau, Heizung, Dämmung, Zugang).
/// Alle Felder sind optional; Auswahlwerte als Schlüssel (siehe <see cref="Allowed"/>), sonst freier Text.
/// Sync-Feld „project“ der Entität „bauakte“ (docs/sync-protokoll.md).
/// </summary>
public sealed record BauakteProject(
    [property: MaxLength(20)] string? ObjectType = null,
    [property: Range(0, 1_000_000)] decimal? TotalArea = null,
    [property: MaxLength(20)] string? ScreedType = null,
    [property: MaxLength(20)] string? Construction = null,
    [property: Range(0, 1000)] int? NominalThicknessMm = null,
    [property: MaxLength(30)] string? StrengthClass = null,
    bool? UnderfloorHeating = null,
    [property: MaxLength(100)] string? HeatingType = null,
    [property: MaxLength(100)] string? Insulation = null,
    [property: Range(0, 1000)] int? InsulationThicknessMm = null,
    [property: MaxLength(100)] string? Floors = null,
    [property: MaxLength(20)] string? Priority = null,
    [property: MaxLength(1000)] string? SiteAccess = null,
    bool? WaterOnSite = null,
    bool? PowerOnSite = null)
{
    public static readonly BauakteProject Empty = new();

    /// <summary>Erlaubte Schlüssel der Auswahlfelder.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Allowed = new Dictionary<string, string[]>
    {
        ["objectType"] = ["efh", "mfh", "commercial", "industrial", "public", "renovation", "other"],
        ["screedType"] = ["ct", "caf", "as", "dry", "other"],
        ["construction"] = ["bonded", "separatingLayer", "floating", "heated"],
        ["priority"] = ["low", "normal", "high", "urgent"],
    };

    /// <summary>Leerzeichen entfernen, leere Texte zu null; unbekannte Auswahlwerte lehnt <see cref="Validate"/> ab.</summary>
    public BauakteProject Normalize() => this with
    {
        ObjectType = Blank(ObjectType), ScreedType = Blank(ScreedType), Construction = Blank(Construction),
        StrengthClass = Blank(StrengthClass)?.ToUpperInvariant(), HeatingType = Blank(HeatingType), Insulation = Blank(Insulation),
        Floors = Blank(Floors), Priority = Blank(Priority), SiteAccess = Blank(SiteAccess),
    };

    public string? Validate()
    {
        foreach (var (field, value) in new[] { ("objectType", ObjectType), ("screedType", ScreedType), ("construction", Construction), ("priority", Priority) })
        {
            if (value is not null && !Allowed[field].Contains(value))
            {
                return $"Unbekannter Wert „{value}“ für {field}.";
            }
        }

        return TotalArea is < 0 || NominalThicknessMm is < 0 || InsulationThicknessMm is < 0 ? "Negative Werte sind nicht zulässig." : null;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
