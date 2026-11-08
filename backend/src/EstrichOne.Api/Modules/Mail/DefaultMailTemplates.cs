using EstrichOne.Api.Modules.Mail.Exports;

namespace EstrichOne.Api.Modules.Mail;

public sealed record MailTemplateDefinition(string Key, string Name, string Subject, string Body, IReadOnlyList<string> Placeholders);

/// <summary>Eingebaute Vorlagen (deutsch). Anpassungen speichert die Tabelle <c>mail_templates</c>.</summary>
public static class DefaultMailTemplates
{
    public static IReadOnlyList<MailTemplateDefinition> All { get; } =
    [
        new(MailTemplateKeys.Test, "Testmail", "Testmail von EstrichONE",
            "Guten Tag,\n\ndiese Testmail bestätigt, dass das E-Mail-Konto von {{firma}} in EstrichONE korrekt eingerichtet ist.\n\n" +
            "Gesendet: {{zeitpunkt}}\nServer: {{adresse}}\n\nMit freundlichen Grüßen\nEstrichONE", ["firma", "adresse", "zeitpunkt"]),
        new(MailTemplateKeys.UserWelcome, "Zugangsdaten für neue Benutzer", "Ihr Zugang zu EstrichONE ({{firma}})",
            "Guten Tag {{name}},\n\nfür Sie wurde ein Zugang zu EstrichONE eingerichtet.\n\nAdresse: {{adresse}}\nBenutzername: {{benutzername}}\n" +
            "Einmalpasswort: {{passwort}}\n\nBitte melden Sie sich an und vergeben Sie ein eigenes Passwort. Die Anleitung zur Einrichtung der App " +
            "finden Sie im beigefügten Schreiben.\n\nMit freundlichen Grüßen\n{{firma}}", ["name", "benutzername", "passwort", "firma", "adresse"]),
        new(MailTemplateKeys.PasswordReset, "Passwort zurückgesetzt", "Neues Einmalpasswort für EstrichONE",
            "Guten Tag {{name}},\n\nIhr Passwort für EstrichONE wurde zurückgesetzt.\n\nAdresse: {{adresse}}\nBenutzername: {{benutzername}}\n" +
            "Einmalpasswort: {{passwort}}\n\nBitte vergeben Sie nach der Anmeldung ein eigenes Passwort.\n\nMit freundlichen Grüßen\n{{firma}}",
            ["name", "benutzername", "passwort", "firma", "adresse"]),
        new(MailTemplateKeys.ApprovalRequested, "Freigabe angefordert", "Freigabe angefordert: {{dokument}}",
            "Guten Tag {{name}},\n\nfolgendes Dokument wartet auf Ihre Freigabe: {{dokument}} (Bauakte {{bauakte}}).\n\n{{nachricht}}\n\n{{link}}\n\nMit freundlichen Grüßen\n{{firma}}",
            ["name", "dokument", "bauakte", "nachricht", "link", "firma"]),
        new(MailTemplateKeys.ApprovalDecided, "Freigabe entschieden", "Freigabe {{entscheidung}}: {{dokument}}",
            "Guten Tag {{name}},\n\ndas Dokument {{dokument}} (Bauakte {{bauakte}}) wurde {{entscheidung}}.\n\n{{kommentar}}\n\n{{link}}\n\nMit freundlichen Grüßen\n{{firma}}",
            ["name", "dokument", "bauakte", "entscheidung", "kommentar", "link", "firma"]),
        new(MailTemplateKeys.HelpdeskUpdated, "Helpdesk-Ticket geändert", "Ticket {{ticket}}: {{titel}}",
            "Guten Tag {{name}},\n\nIhr Ticket {{ticket}} „{{titel}}“ wurde aktualisiert. Status: {{status}}.\n\n{{kommentar}}\n\n{{link}}\n\nMit freundlichen Grüßen\n{{firma}}",
            ["name", "ticket", "titel", "status", "kommentar", "link", "firma"]),
        new(MailTemplateKeys.AcceptanceCustomerApproval, "Abnahme: Freigabe durch den Kunden", "Abnahmeprotokoll {{bauakte}}: Bitte um Freigabe",
            "Guten Tag {{name}},\n\nanbei bzw. unter folgendem Link finden Sie das Abnahmeprotokoll für das Bauvorhaben {{bauakte}}:\n\n{{link}}\n\n" +
            "Wir bitten Sie, das Protokoll bis {{frist}} zu prüfen und freizugeben.\n\nMit freundlichen Grüßen\n{{firma}}", ["name", "bauakte", "link", "frist", "firma"]),
        new(MailTemplateKeys.ArchiveFailed, "Archivierung fehlgeschlagen", "EstrichONE: Archivierung fehlgeschlagen",
            "Guten Tag,\n\ndas Dokument „{{dokument}}“ (Bauakte {{bauakte}}) konnte nicht archiviert werden.\n\nFehler: {{fehler}}\n\n" +
            "Die Archivierung wird automatisch erneut versucht. Status: {{link}}\n\nMit freundlichen Grüßen\nEstrichONE", ["dokument", "bauakte", "fehler", "link"]),
    ];

    public static MailTemplateDefinition? Find(string key) => All.FirstOrDefault(template => template.Key == key);

    /// <summary>Ersetzt <c>{{name}}</c>; unbekannte Platzhalter werden leer.</summary>
    public static string Render(string text, IReadOnlyDictionary<string, string?> values) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\{\{\s*([a-zA-Z0-9_.]+)\s*\}\}",
            match => values.TryGetValue(match.Groups[1].Value, out var value) ? value ?? string.Empty : string.Empty,
            System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
}
