using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Pdf;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Mail.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using MigraDoc.DocumentObjectModel;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Access;

/// <summary>
/// Zugangsbrief für neue Benutzer und nach „Passwort zurücksetzen“ – Geschäftsbrief nach DIN 5008, Form B
/// (Anschriftfeld 45 mm von oben, Falz- und Lochmarken), mit Firmenkopf, Login-Anleitung und Einmalpasswort.
/// </summary>
public sealed class AccessLetterService(IPdfBranding branding, IMailService mail, IOptions<EstrichOneOptions> options, TimeProvider clock)
{
    public string ServerUrl(HttpRequest request) => (options.Value.PublicUrl?.ToString() ?? $"{request.Scheme}://{request.Host}").TrimEnd('/');

    public async Task<byte[]> RenderAsync(User user, string password, string serverUrl, bool reset, CancellationToken cancellationToken)
    {
        var company = await branding.GetAsync(cancellationToken);
        var (document, section) = PdfLetter.Create(reset ? "Neues Einmalpasswort" : "Ihr Zugang zu EstrichONE", company,
            ["Persönlich – vertraulich", user.DisplayName, $"z. Hd. {user.DisplayName}, im Hause", company.Street ?? "", $"{company.PostalCode} {company.City}".Trim()],
            [("Datum", PdfLetter.Today(clock)), ("Ansprechpartner", company.Name), ("Telefon", company.Phone), ("E-Mail", company.Email)],
            reset ? "Neues Einmalpasswort für EstrichONE" : "Ihr Zugang zu EstrichONE");
        section.AddParagraph($"Guten Tag {user.DisplayName},").Format.SpaceAfter = Unit.FromMillimeter(4);
        section.AddParagraph(reset
            ? "Ihr Passwort für EstrichONE wurde zurückgesetzt. Mit den folgenden Daten melden Sie sich wieder an:"
            : "für Sie wurde ein Zugang zu EstrichONE eingerichtet – unserer Software für Bauakten, Planung und Baustellendokumentation. Mit diesen Daten melden Sie sich an:");

        var table = section.AddTable();
        table.Borders.Visible = false;
        table.AddColumn(Unit.FromMillimeter(45));
        table.AddColumn(Unit.FromMillimeter(115));
        table.Format.SpaceBefore = Unit.FromMillimeter(2);
        foreach (var (label, value, strong) in new[] { ("Adresse", serverUrl, false), ("Benutzername", user.UserName, true), ("Einmalpasswort", password, true) })
        {
            var row = table.AddRow();
            row.TopPadding = Unit.FromPoint(3);
            row.BottomPadding = Unit.FromPoint(3);
            row.Shading.Color = PdfReport.Shade;
            row.Cells[0].AddParagraph(label).Style = "Muted";
            var p = row.Cells[1].AddParagraph();
            var text = p.AddFormattedText(value, strong ? TextFormat.Bold : TextFormat.NotBold);
            text.Font.Size = strong ? 11.5 : 9.5;
        }

        section.AddParagraph("So geht es los").Format.SpaceBefore = Unit.FromMillimeter(6);
        section.LastParagraph!.Format.Font.Bold = true;
        foreach (var (n, step) in new[]
        {
            (1, $"Im Browser (PC, Tablet oder Handy) die Adresse {serverUrl} öffnen."),
            (2, "Mit Benutzername und Einmalpasswort anmelden."),
            (3, "Ein eigenes, sicheres Passwort festlegen – das wird bei der ersten Anmeldung verlangt. Das Einmalpasswort gilt danach nicht mehr."),
            (4, "App einrichten: die App „EstrichONE“ auf dem iPhone oder iPad installieren, die Adresse oben eintragen und mit Ihren Zugangsdaten anmelden. Firmengeräte richtet die IT über einen QR-Code ein."),
        })
        {
            var p = section.AddParagraph($"{n}.\t{step}");
            p.Format.LeftIndent = Unit.FromMillimeter(6);
            p.Format.FirstLineIndent = Unit.FromMillimeter(-6);
            p.Format.TabStops.AddTabStop(Unit.FromMillimeter(6));
        }

        var hint = section.AddParagraph("Bitte bewahren Sie dieses Schreiben vertraulich auf und geben Sie Ihr Passwort nicht weiter. Bei Fragen hilft Ihnen die IT-Administration.");
        hint.Format.SpaceBefore = Unit.FromMillimeter(5);
        hint.Style = "Muted";
        section.AddParagraph("Mit freundlichen Grüßen").Format.SpaceBefore = Unit.FromMillimeter(8);
        section.AddParagraph(company.Name).Format.SpaceBefore = Unit.FromMillimeter(6);
        return PdfReport.Render(document);
    }

    /// <summary>Brief per E-Mail (wenn eingeschaltet und ein Konto eingerichtet ist); liefert true bei Versand.</summary>
    public async Task<bool> MailAsync(User user, string password, string serverUrl, bool reset, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.Email))
        {
            return false;
        }

        var pdf = await RenderAsync(user, password, serverUrl, reset, cancellationToken);
        // Das Einmalpasswort steht nur im angehängten Brief, nicht im Klartext der Mail.
        var values = new Dictionary<string, string?>
        {
            ["name"] = user.DisplayName, ["benutzername"] = user.UserName, ["link"] = serverUrl, ["passwort"] = "siehe angehängten Zugangsbrief",
        };
        try
        {
            var id = await mail.QueueTemplateAsync(reset ? MailTemplateKeys.PasswordReset : MailTemplateKeys.UserWelcome, user.Email, values,
                $"user:{user.Id}", [new MailAttachment($"Zugangsbrief {user.UserName}.pdf", "application/pdf", pdf)], cancellationToken);
            return id is not null;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
