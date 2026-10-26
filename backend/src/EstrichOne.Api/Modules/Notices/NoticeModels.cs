namespace EstrichOne.Api.Modules.Notices;

/// <summary>Ankündigung für alle Benutzer (Web-Banner, Hinweis in den Apps).</summary>
/// <param name="Severity"><c>info</c>, <c>warning</c> oder <c>critical</c>.</param>
/// <param name="RoleIds">Zielgruppe: leer = alle; sonst nur angemeldete Benutzer mit einer dieser Rollen.</param>
/// <param name="Source"><c>console</c> (Betreiber/Hosting-Konsole) oder <c>instance</c> (Administratoren der Instanz); vom Server gesetzt.</param>
/// <param name="Notify">Als Mitteilung in den Apps hervorheben (Abruf der Hinweise).</param>
public sealed record SystemAnnouncement(
    string Id,
    string Title,
    string Text,
    string Severity,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    IReadOnlyList<Guid>? RoleIds = null,
    string? Source = null,
    bool Notify = false);

/// <summary>Geplanter Wartungszeitraum. Während der Wartung sind nur Administratoren angemeldet arbeitsfähig.</summary>
public sealed record MaintenanceWindow(
    string? Message,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    bool AdminsMayWork = true);

/// <summary>Vollständiger Stand der Systemhinweise (wird von der Hosting-Konsole als Ganzes gesetzt).</summary>
public sealed record SystemNoticesDocument(
    IReadOnlyList<SystemAnnouncement> Announcements,
    MaintenanceWindow? Maintenance);

/// <summary>Wartung aus Sicht der Clients: geplant (<c>Active = false</c>) oder laufend.</summary>
public sealed record MaintenanceState(
    bool Active,
    string? Message,
    DateTimeOffset StartsAt,
    DateTimeOffset? EndsAt,
    bool AdminsMayWork,
    string? Source = null);

/// <summary>Verwaltungsansicht für Instanz-Administratoren: eigene Hinweise (bearbeitbar) und die der Konsole (nur lesen).</summary>
public sealed record AdminNoticesResponse(SystemNoticesDocument Instance, SystemNoticesDocument Console, bool ConsoleManaged, DateTimeOffset ServerTime);

/// <param name="SendEmail">Zusätzlich per E-Mail an alle aktiven Benutzer mit Adresse (bzw. an die Zielgruppe).</param>
public sealed record SaveAnnouncementRequest(
    string Title, string Text, string Severity, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt,
    IReadOnlyList<Guid>? RoleIds = null, bool Notify = false, bool SendEmail = false);

/// <param name="SentEmails">Anzahl eingereihter E-Mails (0, wenn kein E-Mail-Konto eingerichtet ist).</param>
public sealed record SaveAnnouncementResponse(SystemAnnouncement Announcement, int SentEmails);

/// <summary>Antwort für Clients: aktuell gültige Ankündigungen und die nächste bzw. laufende Wartung.</summary>
public sealed record SystemNoticesResponse(
    IReadOnlyList<SystemAnnouncement> Announcements,
    MaintenanceState? Maintenance,
    DateTimeOffset ServerTime);
