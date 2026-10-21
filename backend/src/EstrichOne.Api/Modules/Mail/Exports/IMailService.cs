namespace EstrichOne.Api.Modules.Mail.Exports;

/// <summary>Anhang einer E-Mail (z. B. Benutzerbrief als PDF).</summary>
public sealed record MailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>Freie E-Mail. <paramref name="Category"/> und <paramref name="RelatedEntity"/> erscheinen im Versandprotokoll.</summary>
public sealed record MailRequest(
    string To, string Subject, string TextBody, string? Category = null, string? RelatedEntity = null,
    IReadOnlyList<MailAttachment>? Attachments = null);

/// <summary>
/// E-Mail-Versand für alle Module (SMTP-Konto in Einstellungen → E-Mail). Versand im Hintergrund mit Wiederholung;
/// jede Mail steht im Versandprotokoll. Ohne eingerichtetes Konto liefern die Methoden <c>null</c> (kein Fehler) –
/// Aufrufer bieten dann den bisherigen Weg (z. B. Ausdruck) an.
/// </summary>
public interface IMailService
{
    Task<bool> IsConfiguredAsync(CancellationToken cancellationToken);

    /// <summary>Reiht eine Mail ein; Rückgabe = Protokoll-ID oder null, wenn kein Konto eingerichtet ist.</summary>
    Task<Guid?> QueueAsync(MailRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Mail aus einer Vorlage (<see cref="MailTemplateKeys"/>); Platzhalter <c>{{name}}</c> werden aus
    /// <paramref name="values"/> ersetzt, dazu immer <c>{{firma}}</c> und <c>{{adresse}}</c> (Web-Adresse des Servers).
    /// </summary>
    Task<Guid?> QueueTemplateAsync(string templateKey, string recipient, IReadOnlyDictionary<string, string?> values,
        string? relatedEntity = null, IReadOnlyList<MailAttachment>? attachments = null, CancellationToken cancellationToken = default);
}

/// <summary>Vorlagen, die andere Module verwenden. Texte sind in Einstellungen → E-Mail → Vorlagen anpassbar.</summary>
public static class MailTemplateKeys
{
    public const string Test = "test";

    /// <summary>Zugangsdaten für neue Benutzer (Platzhalter: name, benutzername, passwort). Anhang: Benutzerbrief (PDF).</summary>
    public const string UserWelcome = "user.welcome";

    /// <summary>Passwort zurückgesetzt (Platzhalter: name, benutzername, passwort).</summary>
    public const string PasswordReset = "user.password_reset";

    /// <summary>Freigabe angefordert (Platzhalter: name, dokument, bauakte, nachricht, link).</summary>
    public const string ApprovalRequested = "approval.requested";

    /// <summary>Freigabe entschieden (Platzhalter: name, dokument, bauakte, entscheidung, kommentar, link).</summary>
    public const string ApprovalDecided = "approval.decided";

    /// <summary>Helpdesk-Ticket geändert (Platzhalter: name, ticket, titel, status, kommentar, link).</summary>
    public const string HelpdeskUpdated = "helpdesk.ticket_updated";

    /// <summary>Abnahme zur Freigabe an den Kunden (Platzhalter: name, bauakte, link, frist). Anhang optional: Abnahmeprotokoll (PDF).</summary>
    public const string AcceptanceCustomerApproval = "acceptance.customer_approval";

    /// <summary>Archivierung fehlgeschlagen (Platzhalter: dokument, bauakte, fehler, link).</summary>
    public const string ArchiveFailed = "archive.failed";
}
