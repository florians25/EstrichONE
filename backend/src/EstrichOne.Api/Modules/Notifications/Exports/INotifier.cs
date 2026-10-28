namespace EstrichOne.Api.Modules.Notifications.Exports;

/// <summary>Art einer Benachrichtigung (Filter im Postfach, Symbol in den Apps).</summary>
public enum NotificationCategory
{
    /// <summary>Nachricht aus dem Büro (von Hand gesendet).</summary>
    Message,

    /// <summary>Helpdesk: Antwort oder Statusänderung an einem Ticket.</summary>
    Ticket,

    /// <summary>Dokumentfreigabe angefordert oder entschieden.</summary>
    Approval,

    /// <summary>Einsatzplan freigegeben oder nach der Freigabe geändert.</summary>
    Planning,

    /// <summary>Zuweisung zu einer Bauakte.</summary>
    Bauakte,

    /// <summary>Frist (fällige Aufgabe).</summary>
    Deadline,

    /// <summary>System (Testnachricht, Wartung, Backup- und Archivstörung).</summary>
    System,

    /// <summary>Neue Chatnachricht (Modul Chat).</summary>
    Chat,

    /// <summary>Aufgabe oder Mangel zugewiesen.</summary>
    Work,

    /// <summary>Urlaubsantrag entschieden.</summary>
    Absence,
}

public enum NotificationUrgency
{
    Low,
    Normal,
    High,
}

/// <summary>
/// Eine Benachrichtigung für das Postfach. <paramref name="Title"/> und <paramref name="Body"/> erscheinen auch in der
/// Pushnachricht – deshalb keine sensiblen Inhalte (keine Kommentare, Preise, Gesundheitsdaten), nur Hinweis und Objekt.
/// </summary>
/// <param name="Link">Relativer Pfad der Weboberfläche, z. B. <c>/bauakten/{id}</c> (Apps leiten daraus den Deep Link ab).</param>
/// <param name="DedupKey">Gleicher Schlüssel für denselben Benutzer erzeugt keine zweite Benachrichtigung (z. B. Fristen).</param>
/// <param name="Event">Ereignis aus <see cref="NotificationEvents"/> – danach richtet sich, ob der Empfänger es abbestellt hat.</param>
public sealed record NotificationRequest(
    NotificationCategory Category,
    string Title,
    string? Body = null,
    string? Link = null,
    string? ObjectType = null,
    Guid? ObjectId = null,
    NotificationUrgency Urgency = NotificationUrgency.Normal,
    string? DedupKey = null,
    string? Event = null);

/// <summary>Benachrichtigungen ins Postfach legen und (falls eingerichtet) als Push zustellen.</summary>
public interface INotifier
{
    /// <summary>
    /// Legt je Empfänger einen Eintrag an und speichert sofort (eigene Transaktion nach der fachlichen Änderung).
    /// Die handelnde Person selbst wird nicht benachrichtigt. Fehler werden protokolliert, nie geworfen.
    /// </summary>
    Task NotifyAsync(IEnumerable<Guid> userIds, NotificationRequest notification, CancellationToken cancellationToken);

    /// <summary>Einträge eines Benutzers zu einem Objekt als gelesen markieren (z. B. Chat gelesen → Hinweis gelesen).</summary>
    Task MarkReadAsync(Guid userId, string objectType, Guid objectId, CancellationToken cancellationToken);
}

/// <summary>Zustellung je Ereignis und Benutzer (Konto → Benachrichtigungen).</summary>
public enum NotificationDelivery
{
    /// <summary>Abbestellt: weder Postfach noch Push.</summary>
    Off,

    /// <summary>Nur im Postfach (Glocke), keine Pushnachricht.</summary>
    Inbox,

    /// <summary>Postfach und Pushnachricht.</summary>
    Push,
}

/// <param name="AdminOnly">Nur für Administratoren relevant (Backup, Archiv).</param>
/// <param name="Mandatory">Nicht abbestellbar (z. B. Wartung, Nachrichten aus dem Büro); nur Push lässt sich abschalten.</param>
public sealed record NotificationEventInfo(string Code, string Group, string Label, string Description, bool AdminOnly = false, bool Mandatory = false);

/// <summary>Ereigniskatalog. Jeder Benutzer kann je Ereignis „aus“, „nur Postfach“ oder „Postfach und Push“ wählen.</summary>
public static class NotificationEvents
{
    public const string BauakteAssigned = "bauakte.assigned";
    public const string BauakteComment = "bauakte.comment";
    public const string WorkAssigned = "work.assigned";
    public const string Deadline = "deadline.due";
    public const string ApprovalRequested = "approval.requested";
    public const string ApprovalDecided = "approval.decided";
    public const string AcceptanceSigned = "acceptance.signed";
    public const string PlanReleased = "planning.released";
    public const string PlanChanged = "planning.changed";
    public const string AbsenceDecided = "absence.decided";
    public const string TicketUpdated = "ticket.updated";
    public const string ChatMessage = "chat.message";
    public const string OfficeMessage = "office.message";
    public const string MaintenanceAnnounced = "system.maintenance";
    public const string BackupSucceeded = "backup.succeeded";
    public const string BackupFailed = "backup.failed";
    public const string BackupTargetDown = "backup.target_down";
    public const string ArchiveDown = "archive.down";
    public const string PeerUpdateAvailable = "peer.update_available";
    public const string PeerUpdateSucceeded = "peer.update_succeeded";
    public const string PeerUpdateFailed = "peer.update_failed";
    public const string Test = "system.test";

    public static IReadOnlyList<NotificationEventInfo> All { get; } =
    [
        new(BauakteAssigned, "Bauakten", "Bauakte mir zugewiesen", "Sie wurden Mitglied oder zuständig für eine Bauakte."),
        new(BauakteComment, "Bauakten", "Notiz in meiner Bauakte", "Jemand hat eine Notiz in einer Bauakte geschrieben, in der Sie Mitglied oder zuständig sind."),
        new(AcceptanceSigned, "Bauakten", "Abnahme unterschrieben", "Ein Abnahmeprotokoll Ihrer Bauakte wurde unterschrieben."),
        new(WorkAssigned, "Aufgaben", "Aufgabe oder Mangel mir zugewiesen", "Ihnen wurde eine Aufgabe oder ein Mangel zugewiesen."),
        new(Deadline, "Aufgaben", "Fristen", "Zugewiesene Aufgaben und Mängel sind heute oder morgen fällig oder überfällig."),
        new(ApprovalRequested, "Freigaben", "Dokument zur Genehmigung", "Ein Dokument wartet auf Ihre Freigabe."),
        new(ApprovalDecided, "Freigaben", "Genehmigung erteilt oder abgelehnt", "Über Ihre Freigabeanfrage wurde entschieden."),
        new(PlanReleased, "Planung", "Plan freigegeben", "Ihr Einsatzplan wurde freigegeben (eine Nachricht je Freigabe, gebündelt)."),
        new(PlanChanged, "Planung", "Planänderung für mich", "Nach der Freigabe haben sich Ihre Einsätze geändert."),
        new(AbsenceDecided, "Personal", "Urlaubsantrag genehmigt oder abgelehnt", "Ihr Urlaubsantrag wurde entschieden."),
        new(TicketUpdated, "Support", "Supportticket", "Antwort oder Statusänderung an Ihrem Ticket."),
        new(ChatMessage, "Chat", "Neue Chatnachricht", "Neue Nachricht in einer Unterhaltung (stumm geschaltete Unterhaltungen ausgenommen)."),
        new(OfficeMessage, "Büro", "Nachricht aus dem Büro", "Vom Büro gesendete Nachrichten.", Mandatory: true),
        new(MaintenanceAnnounced, "System", "Wartung angekündigt", "Eine Wartung wurde geplant oder beginnt.", Mandatory: true),
        new(BackupSucceeded, "System", "Backup erfolgreich", "Kurze Meldung nach jeder erfolgreichen Sicherung.", AdminOnly: true),
        new(BackupFailed, "System", "Backup fehlgeschlagen", "Eine Sicherung ist fehlgeschlagen.", AdminOnly: true, Mandatory: true),
        new(BackupTargetDown, "System", "Backup-Server nicht erreichbar", "Das Backup-Ziel ist ausgefallen oder die letzte Sicherung ist zu alt.", AdminOnly: true, Mandatory: true),
        new(ArchiveDown, "System", "Archiv-Server nicht erreichbar", "Der Archivserver ist ausgefallen oder nicht erreichbar.", AdminOnly: true, Mandatory: true),
        new(PeerUpdateAvailable, "System", "Zusatzserver aktualisieren", "Nach einem Update dieses Servers: Backup-Ziel oder Archivserver hat noch die ältere Version.", AdminOnly: true),
        new(PeerUpdateSucceeded, "System", "Zusatzserver aktualisiert", "Die Fernaktualisierung eines Backup-Ziels oder Archivservers war erfolgreich.", AdminOnly: true),
        new(PeerUpdateFailed, "System", "Aktualisierung eines Zusatzservers fehlgeschlagen", "Fernaktualisierung fehlgeschlagen oder zurückgerollt (vorige Version läuft wieder).", AdminOnly: true, Mandatory: true),
    ];

    public static NotificationEventInfo? Find(string? code) => code is null ? null : All.FirstOrDefault(e => e.Code == code);

    /// <summary>Ereignis für ältere Aufrufer ohne <see cref="NotificationRequest.Event"/>.</summary>
    public static string Default(NotificationCategory category) => category switch
    {
        NotificationCategory.Message => OfficeMessage,
        NotificationCategory.Ticket => TicketUpdated,
        NotificationCategory.Approval => ApprovalRequested,
        NotificationCategory.Planning => PlanReleased,
        NotificationCategory.Bauakte => BauakteAssigned,
        NotificationCategory.Deadline => Deadline,
        NotificationCategory.Chat => ChatMessage,
        NotificationCategory.Work => WorkAssigned,
        NotificationCategory.Absence => AbsenceDecided,
        _ => Test,
    };
}

/// <summary>Fälliges mit Frist (z. B. Aufgaben), das die Fristen-Prüfung einmal je Tag meldet.</summary>
public sealed record DeadlineItem(Guid UserId, string Key, DateOnly DueDate, string Title, string? Link, string? ObjectType, Guid? ObjectId);

/// <summary>Quelle für Fristen; Module registrieren eine Umsetzung.</summary>
public interface IDeadlineSource
{
    /// <summary>Offene Einträge mit Frist bis einschließlich <paramref name="until"/>.</summary>
    Task<IReadOnlyList<DeadlineItem>> DueAsync(DateOnly until, CancellationToken cancellationToken);
}

/// <summary>Mitglieder von Kolonnen (Empfängerauswahl „Kolonne“); umgesetzt von der Planung.</summary>
public interface INotificationAudience
{
    Task<IReadOnlyList<Guid>> CrewMembersAsync(IReadOnlyCollection<Guid> crewIds, CancellationToken cancellationToken);

    /// <summary>Aktive Kolonnen zur Auswahl.</summary>
    Task<IReadOnlyList<AudienceOption>> CrewsAsync(CancellationToken cancellationToken);
}

/// <summary>Auswahl für Empfänger (Rolle oder Kolonne) mit Anzahl der Personen.</summary>
public sealed record AudienceOption(Guid Id, string Name, int Members);
