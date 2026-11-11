namespace EstrichOne.Api.Modules.Organization.Exports;

/// <summary>Ein Modul der Suite (Einstellungen › Module).</summary>
/// <param name="Key">Schlüssel, gleich dem Feld in <see cref="ModuleSettings"/> (camelCase).</param>
/// <param name="Area">Bereich für die Gruppierung: office, planning, material, hr, site, service, core.</param>
/// <param name="Requires">Module, die eingeschaltet sein müssen (z. B. Urlaub braucht Personal).</param>
/// <param name="Core">Kernmodul: immer eingeschaltet, nicht abschaltbar.</param>
/// <param name="NotificationEvents">Benachrichtigungen des Moduls; sie entfallen, solange das Modul aus ist.</param>
public sealed record AppModuleInfo(
    string Key,
    string Area,
    string Title,
    string Description,
    IReadOnlyList<string> Requires,
    bool Core = false,
    IReadOnlyList<string>? NotificationEvents = null);

/// <summary>
/// Katalog der Module. Abschaltbare Module haben einen Schalter in <see cref="ModuleSettings"/>; Kernmodule (Bauakten,
/// Kunden und Kontakte, Benutzer und Rechte, Einstellungen, Sicherung) bleiben immer an. Der Chat hat eigene Einstellungen
/// (Chat › Verwaltung) und ist deshalb nicht Teil dieses Katalogs.
/// </summary>
public static class AppModules
{
    public const string Calls = "calls";
    public const string Planning = "planning";
    public const string Machines = "machines";
    public const string Map = "map";
    public const string Materials = "materials";
    public const string Orders = "orders";
    public const string Staff = "staff";
    public const string Absence = "absence";
    public const string Inspections = "inspections";
    public const string Diary = "diary";
    public const string Acceptance = "acceptance";
    public const string Tasks = "tasks";
    public const string Defects = "defects";
    public const string Approvals = "approvals";
    public const string Dossiers = "dossiers";
    public const string Helpdesk = "helpdesk";
    public const string Feedback = "feedback";

    /// <summary>Fehlercode der Endpunkte abgeschalteter Module.</summary>
    public const string DisabledCode = "MODULE_DISABLED";

    /// <summary>Reihenfolge: Voraussetzungen vor den abhängigen Modulen.</summary>
    public static IReadOnlyList<AppModuleInfo> All { get; } =
    [
        new("bauakten", "core", "Bauakten", "Bauakten mit Fotos, Dokumenten, Notizen und Team – das Herz von EstrichONE.", [], Core: true),
        new("contacts", "core", "Kunden und Kontakte", "Kundenkartei, Ansprechpartner und Beteiligte der Bauakten.", [], Core: true),
        new("access", "core", "Benutzer und Rechte", "Konten, Rollen, Geräte und Protokoll.", [], Core: true),
        new("settings", "core", "Einstellungen", "Firmendaten, Stammdaten, Systemeinstellungen und Hinweise.", [], Core: true),
        new("backup", "core", "Sicherung", "Backup Center, Überwachung und Wiederherstellung.", [], Core: true),

        new(Calls, "office", "Anrufe und Anfragen", "Telefonnotizen und Anfragen erfassen, nachverfolgen und in Bauakten übernehmen.", []),
        new(Map, "office", "Baustellenkarte", "Alle Baustellen auf der Karte (Web und Apps).", []),

        new(Planning, "planning", "Disposition und Kolonnenplanung",
            "Cockpit, Kolonnenplanung, Teamplaner, Kalender, Arbeitsplan, Kolonnen und „Wer fährt wohin“ inklusive Planfreigabe und Push.", [],
            NotificationEvents: ["planning.released", "planning.changed"]),
        new(Machines, "planning", "Maschinen und Fahrzeuge", "Fahrzeuge, Pumpen und Maschinen pflegen und Einsätzen zuordnen.", [Planning]),

        new(Materials, "material", "Material und Lieferanten", "Materialstamm, Lieferanten und Materialhinweise in Bauakten.", []),
        new(Orders, "material", "Bestellungen", "Materialbestellungen mit Bestell-PDF und Lieferstatus.", [Materials]),

        new(Staff, "hr", "Personal", "Mitarbeiter, Qualifikationen, Ausstattung, Unterlagen und Fristen.", []),
        new(Absence, "hr", "Urlaub und Abwesenheit", "Urlaubskonten, Anträge mit Genehmigung, Krankmeldungen, Betriebsferien und Teamkalender.", [Staff],
            NotificationEvents: ["absence.decided", "absence.requested", "absence.cancelled"]),

        new(Inspections, "site", "Erstbegehung und Protokolle", "Erstbegehung mit Prüfungen, Messwerten und Protokoll-PDF.", []),
        new(Diary, "site", "Bautagebuch", "Tägliche Einträge zu Wetter, Arbeiten und Personal je Bauakte.", []),
        new(Acceptance, "site", "Abnahmen", "Abnahmeprotokoll mit Unterschrift vor Ort oder Freigabelink für Kunden.", [],
            NotificationEvents: ["acceptance.signed"]),
        new(Tasks, "site", "Aufgaben", "Aufgaben mit Zuständigkeit und Frist, auch zu Bauakten.", [], NotificationEvents: ["work.assigned"]),
        new(Defects, "site", "Mängel", "Mängel erfassen, zuweisen und beheben (mit Fotos).", [], NotificationEvents: ["work.assigned"]),
        new(Approvals, "site", "Dokumentfreigaben", "Dokumente zur Genehmigung vorlegen und freigeben.", [],
            NotificationEvents: ["approval.requested", "approval.decided"]),
        new(Dossiers, "site", "Aktenarchiv", "Abgeschlossene Bauakten als unveränderliche Archivakte ablegen und abrufen.", []),

        new(Helpdesk, "service", "Helpdesk", "Supporttickets an die IT mit Diagnose und Anhängen.", [], NotificationEvents: ["ticket.updated", "ticket.created", "ticket.assigned", "ticket.mentioned", "ticket.overdue"]),
        new(Feedback, "service", "Feedback", "Fehlermeldungen und Funktionswünsche aus Web und Apps.", [],
            NotificationEvents: ["feedback.submitted", "feedback.updated"]),
    ];

    public static AppModuleInfo? Find(string key) => All.FirstOrDefault(m => m.Key == key);

    /// <summary>Abschaltbare Module.</summary>
    public static IEnumerable<AppModuleInfo> Switchable => All.Where(m => !m.Core);

    /// <summary>Module, die <paramref name="key"/> voraussetzen (direkt).</summary>
    public static IEnumerable<AppModuleInfo> Dependents(string key) => All.Where(m => m.Requires.Contains(key));

    /// <summary>
    /// Darf eine Benachrichtigung verschickt werden? Gehört das Ereignis zu Modulen und sind alle davon aus, entfällt sie
    /// (z. B. „work.assigned“ erst, wenn Aufgaben und Mängel aus sind).
    /// </summary>
    public static bool AllowsNotification(this ModuleSettings modules, string? eventCode)
    {
        if (eventCode is null)
        {
            return true;
        }

        var owners = All.Where(m => m.NotificationEvents?.Contains(eventCode) == true).ToList();
        return owners.Count == 0 || owners.Any(m => modules.IsEnabled(m.Key));
    }

    /// <summary>
    /// Endpunkt bzw. Gruppe gehört zu einem Modul: Ist es abgeschaltet, antwortet die API mit 409 <c>MODULE_DISABLED</c>.
    /// Mehrere Schlüssel: Es genügt, wenn eines der Module an ist (z. B. Aufgaben oder Mängel). Mehrfach angewendet
    /// (Gruppe und Endpunkt) müssen alle Bedingungen erfüllt sein.
    /// </summary>
    public static TBuilder RequireModule<TBuilder>(this TBuilder builder, params string[] anyOf)
        where TBuilder : IEndpointConventionBuilder => builder.WithMetadata(new ModuleRequirement(anyOf));
}

/// <summary>Metadaten für <see cref="AppModules.RequireModule{TBuilder}"/>.</summary>
public sealed record ModuleRequirement(IReadOnlyList<string> AnyOf);
