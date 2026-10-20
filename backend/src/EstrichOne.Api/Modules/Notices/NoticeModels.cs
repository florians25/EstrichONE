namespace EstrichOne.Api.Modules.Notices;

/// <summary>Ankündigung für alle Benutzer (Web-Banner, Hinweis in den Apps).</summary>
/// <param name="Severity"><c>info</c>, <c>warning</c> oder <c>critical</c>.</param>
public sealed record SystemAnnouncement(
    string Id,
    string Title,
    string Text,
    string Severity,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);

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
    bool AdminsMayWork);

/// <summary>Antwort für Clients: aktuell gültige Ankündigungen und die nächste bzw. laufende Wartung.</summary>
public sealed record SystemNoticesResponse(
    IReadOnlyList<SystemAnnouncement> Announcements,
    MaintenanceState? Maintenance,
    DateTimeOffset ServerTime);
