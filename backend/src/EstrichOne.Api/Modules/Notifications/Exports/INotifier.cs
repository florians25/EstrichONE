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

    /// <summary>System (Testnachricht, Wartung).</summary>
    System,
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
public sealed record NotificationRequest(
    NotificationCategory Category,
    string Title,
    string? Body = null,
    string? Link = null,
    string? ObjectType = null,
    Guid? ObjectId = null,
    NotificationUrgency Urgency = NotificationUrgency.Normal,
    string? DedupKey = null);

/// <summary>Benachrichtigungen ins Postfach legen und (falls eingerichtet) als Push zustellen.</summary>
public interface INotifier
{
    /// <summary>
    /// Legt je Empfänger einen Eintrag an und speichert sofort (eigene Transaktion nach der fachlichen Änderung).
    /// Die handelnde Person selbst wird nicht benachrichtigt. Fehler werden protokolliert, nie geworfen.
    /// </summary>
    Task NotifyAsync(IEnumerable<Guid> userIds, NotificationRequest notification, CancellationToken cancellationToken);
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
