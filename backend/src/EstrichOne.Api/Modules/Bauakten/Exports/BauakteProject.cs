using System.Text.Json;
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
    bool? PowerOnSite = null,
    IReadOnlyDictionary<string, JsonElement>? Custom = null)
{
    public static readonly BauakteProject Empty = new();

    /// <summary>Auswahlfelder, die gegen die Wertelisten der Stammdaten geprüft werden.</summary>
    public static readonly IReadOnlyDictionary<string, Func<BauakteProject, string?>> ListFields = new Dictionary<string, Func<BauakteProject, string?>>
    {
        ["objectType"] = p => p.ObjectType, ["screedType"] = p => p.ScreedType, ["construction"] = p => p.Construction, ["priority"] = p => p.Priority,
    };

    /// <summary>Leerzeichen entfernen, leere Texte zu null; unbekannte Auswahlwerte lehnt <see cref="Validate"/> ab.</summary>
    public BauakteProject Normalize() => this with
    {
        ObjectType = Blank(ObjectType), ScreedType = Blank(ScreedType), Construction = Blank(Construction),
        StrengthClass = Blank(StrengthClass)?.ToUpperInvariant(), HeatingType = Blank(HeatingType), Insulation = Blank(Insulation),
        Floors = Blank(Floors), Priority = Blank(Priority), SiteAccess = Blank(SiteAccess),
        Custom = Custom is { Count: > 0 } ? Custom.Where(c => c.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            .ToDictionary(c => c.Key, c => c.Value) : null,
    };

    /// <summary>Form der Werte prüfen (Wertelisten prüft der Aufrufer, siehe <see cref="ListFields"/>).</summary>
    public string? ValidateShape()
    {
        if (TotalArea is < 0 || NominalThicknessMm is < 0 || InsulationThicknessMm is < 0)
        {
            return "Negative Werte sind nicht zulässig.";
        }

        if (Custom is null)
        {
            return null;
        }

        if (Custom.Count > 50)
        {
            return "Höchstens 50 eigene Felder.";
        }

        foreach (var (key, value) in Custom)
        {
            if (key.Length is 0 or > 40 || !key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                return $"Ungültiger Feldname „{key}“.";
            }

            if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array || (value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 2000))
            {
                return $"Ungültiger Wert für „{key}“ (Text bis 2000 Zeichen, Zahl, Ja/Nein oder Datum).";
            }
        }

        return null;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
