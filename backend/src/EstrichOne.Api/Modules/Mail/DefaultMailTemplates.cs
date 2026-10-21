using EstrichOne.Api.Modules.Mail.Exports;

namespace EstrichOne.Api.Modules.Mail;

public sealed record MailTemplateDefinition(string Key, string Name, string Subject, string Body, IReadOnlyList<string> Placeholders);

/// <summary>Eingebaute Vorlagen (deutsch). Anpassungen speichert die Tabelle <c>mail_templates</c>.</summary>
public static class DefaultMailTemplates
{
    public static IReadOnlyList<MailTemplateDefinition> All { get; } =
    [
        new(MailTemplateKeys.Test, "Testmail", "Testmail von EstrichONE",
            "Hallo,\n\ndiese Testmail bestätigt, dass das E-Mail-Konto von {{firma}} in EstrichONE richtig eingerichtet ist.\n\n" +
            "Gesendet: {{zeitpunkt}}\nServer: {{adresse}}\n\nViele Grüße\nEstrichONE", ["firma", "adresse", "zeitpunkt"]),
        new(MailTemplateKeys.UserWelcome, "Neuer Benutzer – Zugangsdaten", "Ihr Zugang zu EstrichONE ({{firma}})",
            "Hallo {{name}},\n\nfür Sie wurde ein Zugang zu EstrichONE eingerichtet.\n\nAdresse: {{adresse}}\nBenutzername: {{benutzername}}\n" +
            "Einmalpasswort: {{passwort}}\n\nBitte melden Sie sich an und vergeben Sie ein eigenes Passwort. Die Anleitung für die App " +
            "finden Sie im angehängten Brief.\n\nViele Grüße\n{{firma}}", ["name", "benutzername", "passwort", "firma", "adresse"]),
        new(MailTemplateKeys.PasswordReset, "Passwort zurückgesetzt", "Neues Einmalpasswort für EstrichONE",
            "Hallo {{name}},\n\nIhr Passwort für EstrichONE wurde zurückgesetzt.\n\nAdresse: {{adresse}}\nBenutzername: {{benutzername}}\n" +
            "Einmalpasswort: {{passwort}}\n\nBitte vergeben Sie nach der Anmeldung ein eigenes Passwort.\n\nViele Grüße\n{{firma}}",
            ["name", "benutzername", "passwort", "firma", "adresse"]),
        new(MailTemplateKeys.ApprovalRequested, "Freigabe angefordert", "Bitte um Freigabe: {{dokument}}",
            "Hallo {{name}},\n\nbitte prüfen und freigeben: {{dokument}} (Bauakte {{bauakte}}).\n\n{{nachricht}}\n\n{{link}}\n\nViele Grüße\n{{firma}}",
            ["name", "dokument", "bauakte", "nachricht", "link", "firma"]),
        new(MailTemplateKeys.ApprovalDecided, "Freigabe entschieden", "Freigabe {{entscheidung}}: {{dokument}}",
            "Hallo {{name}},\n\n{{dokument}} (Bauakte {{bauakte}}) wurde {{entscheidung}}.\n\n{{kommentar}}\n\n{{link}}\n\nViele Grüße\n{{firma}}",
            ["name", "dokument", "bauakte", "entscheidung", "kommentar", "link", "firma"]),
        new(MailTemplateKeys.HelpdeskUpdated, "Helpdesk-Ticket geändert", "Ticket {{ticket}}: {{titel}}",
            "Hallo {{name}},\n\nIhr Ticket {{ticket}} „{{titel}}“ hat einen neuen Stand: {{status}}.\n\n{{kommentar}}\n\n{{link}}\n\nViele Grüße\n{{firma}}",
            ["name", "ticket", "titel", "status", "kommentar", "link", "firma"]),
        new(MailTemplateKeys.AcceptanceCustomerApproval, "Abnahme – Freigabe durch den Kunden", "Abnahmeprotokoll {{bauakte}} – bitte freigeben",
            "Guten Tag {{name}},\n\nanbei bzw. unter folgendem Link finden Sie das Abnahmeprotokoll für das Bauvorhaben {{bauakte}}:\n\n{{link}}\n\n" +
            "Bitte prüfen und freigeben Sie es bis {{frist}}.\n\nMit freundlichen Grüßen\n{{firma}}", ["name", "bauakte", "link", "frist", "firma"]),
        new(MailTemplateKeys.ArchiveFailed, "Archivierung fehlgeschlagen", "EstrichONE: Archivierung fehlgeschlagen",
            "Hallo,\n\ndas Dokument „{{dokument}}“ (Bauakte {{bauakte}}) konnte nicht archiviert werden:\n\n{{fehler}}\n\n" +
            "EstrichONE versucht es automatisch weiter. Status: {{link}}\n\nViele Grüße\nEstrichONE", ["dokument", "bauakte", "fehler", "link"]),
    ];

    public static MailTemplateDefinition? Find(string key) => All.FirstOrDefault(template => template.Key == key);

    /// <summary>Ersetzt <c>{{name}}</c>; unbekannte Platzhalter werden leer.</summary>
    public static string Render(string text, IReadOnlyDictionary<string, string?> values) =>
        System.Text.RegularExpressions.Regex.Replace(text, @"\{\{\s*([a-zA-Z0-9_.]+)\s*\}\}",
            match => values.TryGetValue(match.Groups[1].Value, out var value) ? value ?? string.Empty : string.Empty,
            System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
}
