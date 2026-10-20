namespace EstrichOne.Api.Modules.Notices;

public sealed class NoticeSettings
{
    public const string SectionName = "EstrichOne:Notices";

    /// <summary>
    /// Geheimer Schlüssel, mit dem die Hosting-Konsole die Hinweise setzt (<c>Authorization: Bearer …</c>).
    /// Leer = Setzen von außen abgeschaltet (Selbstbetrieb ohne Konsole).
    /// </summary>
    public string? ManagementToken { get; set; }

    /// <summary>Datei, in der der Stand dauerhaft liegt (z. B. <c>/data/files/.system-notices.json</c>); leer = nur im Speicher.</summary>
    public string? StoragePath { get; set; }

    /// <summary>Geplante Wartung wird Clients so lange vorher angekündigt.</summary>
    public TimeSpan MaintenancePreview { get; set; } = TimeSpan.FromDays(14);
}
