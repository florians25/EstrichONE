using System.Globalization;
using System.Text.Json;

namespace EstrichOne.Hosting.Status;

/// <summary>Stand der Apps einer Instanz aus <c>/api/v1/app-updates/check</c> (ohne Anmeldung, ab Suite 1.0.13).</summary>
/// <param name="DownloadKind">Bezugsquelle laut Suite: <c>package</c> (APK vom eigenen Server), <c>link</c>, <c>store</c> oder <c>none</c>.</param>
/// <param name="PackageAvailable">Android: Der Server liefert ein gültiges APK aus.</param>
public sealed record AppPlatformInfo(string Platform, string? LatestVersion, string? MinimumVersion, string? DownloadKind,
    bool PackageAvailable, long? PackageSize);

/// <summary>Ein- oder ausgeschaltetes Modul der Suite (Einstellungen › Module).</summary>
/// <param name="Area">Bereich als Text, z. B. „Büro“.</param>
/// <param name="Core">Kernmodul der Suite (immer an, nicht abschaltbar).</param>
public sealed record SuiteModuleState(string Key, string Title, string Area, bool Enabled, bool Core = false);

/// <summary>Woher die Module stammen: Schnittstelle der Suite oder (ältere Versionen) direkt aus der Datenbank.</summary>
public static class ModuleSources
{
    public const string Suite = "suite";
    public const string Database = "database";
}

/// <summary>Fachlicher Stand einer Instanz: Apps und Module. Wird seltener erhoben als der Betriebszustand.</summary>
public sealed record SuiteInfo(
    DateTime CollectedAt,
    AppPlatformInfo? Android,
    AppPlatformInfo? Ios,
    string? AppsNote,
    IReadOnlyList<SuiteModuleState> Modules,
    string? ModulesNote,
    string? ModulesSource = null)
{
    /// <summary>Eingeschaltete abschaltbare Module (Kernmodule zählen nicht mit).</summary>
    public int EnabledModules => Modules.Count(m => m.Enabled && !m.Core);

    /// <summary>Abschaltbare Module.</summary>
    public int SwitchableModules => Modules.Count(m => !m.Core);

    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(15);
}

/// <summary>
/// Rückfall für Suite-Versionen ohne <c>GET /api/v1/system/modules</c> (vor 1.0.15): Module direkt aus
/// <c>system_settings</c> der Instanz. Der Katalog ist der eingefrorene Stand dieser Versionen und wird nicht mehr
/// gepflegt – neue Module meldet die Suite selbst über die Schnittstelle. Fehlt ein Schlüssel in den Einstellungen,
/// gilt das Modul wie in der Suite als eingeschaltet; unbekannte Schlüssel erscheinen mit ihrem Schlüssel.
/// </summary>
public static class SuiteModules
{
    public static readonly IReadOnlyList<(string Key, string Area, string Title)> Catalog =
    [
        ("calls", "Büro", "Anrufe und Anfragen"),
        ("map", "Büro", "Baustellenkarte"),
        ("planning", "Planung", "Disposition und Kolonnenplanung"),
        ("machines", "Planung", "Maschinen und Fahrzeuge"),
        ("materials", "Material", "Material und Lieferanten"),
        ("orders", "Material", "Bestellungen"),
        ("staff", "Personal", "Personal"),
        ("absence", "Personal", "Urlaub und Abwesenheit"),
        ("inspections", "Baustelle und Dokumentation", "Erstbegehung und Protokolle"),
        ("diary", "Baustelle und Dokumentation", "Bautagebuch"),
        ("acceptance", "Baustelle und Dokumentation", "Abnahmen"),
        ("tasks", "Baustelle und Dokumentation", "Aufgaben"),
        ("defects", "Baustelle und Dokumentation", "Mängel"),
        ("approvals", "Baustelle und Dokumentation", "Dokumentfreigaben"),
        ("dossiers", "Baustelle und Dokumentation", "Aktenarchiv"),
        ("helpdesk", "Kommunikation und Service", "Helpdesk"),
        ("feedback", "Kommunikation und Service", "Feedback"),
    ];

    /// <summary>Wertet <c>system_settings.data-&gt;'modules'</c> aus (JSON-Objekt Schlüssel → bool; leer = alles an).</summary>
    public static IReadOnlyList<SuiteModuleState> Parse(string? json)
    {
        var values = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(json))
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        values[property.Name] = property.Value.GetBoolean();
                    }
                }
            }
        }

        var list = Catalog.Select(m => new SuiteModuleState(m.Key, m.Title, m.Area, values.GetValueOrDefault(m.Key, true))).ToList();
        list.AddRange(values.Keys.Where(k => Catalog.All(m => !string.Equals(m.Key, k, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .Select(k => new SuiteModuleState(k, k, "Weitere", values[k])));
        return list;
    }

    public static string Describe(IReadOnlyList<SuiteModuleState> modules) =>
        string.Create(CultureInfo.InvariantCulture, $"{modules.Count(m => m.Enabled && !m.Core)} von {modules.Count(m => !m.Core)}");
}
