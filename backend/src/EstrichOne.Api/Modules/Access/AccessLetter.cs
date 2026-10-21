using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Pdf;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Mail.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
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
        PdfReport.EnsureFonts();
        var company = await branding.GetAsync(cancellationToken);
        var (document, section) = PdfReport.Create(reset ? "Neues Einmalpasswort" : "Ihr Zugang zu EstrichONE", "Zugangsbrief", company);
        var setup = section.PageSetup;
        setup.LeftMargin = Unit.FromMillimeter(25);
        setup.RightMargin = Unit.FromMillimeter(20);

        // Falzmarken (105 mm, 210 mm) und Lochmarke (148,5 mm) am linken Rand.
        foreach (var y in new[] { 105.0, 148.5, 210.0 })
        {
            var mark = section.AddTextFrame();
            mark.RelativeHorizontal = RelativeHorizontal.Page;
            mark.RelativeVertical = RelativeVertical.Page;
            mark.Left = Unit.FromMillimeter(4);
            mark.Top = Unit.FromMillimeter(y);
            mark.Width = Unit.FromMillimeter(y == 148.5 ? 7 : 5);
            mark.Height = Unit.FromMillimeter(0.5);
            mark.LineFormat.Width = 0.4;
            mark.LineFormat.Color = PdfReport.Muted;
            mark.WrapFormat.Style = WrapStyle.None;
        }

        // Anschriftfeld (Form B: 45 mm von oben, 20 mm von links, 85 × 45 mm) mit Rücksendeangabe.
        var address = section.AddTextFrame();
        address.RelativeHorizontal = RelativeHorizontal.Page;
        address.RelativeVertical = RelativeVertical.Page;
        address.Left = Unit.FromMillimeter(20);
        address.Top = Unit.FromMillimeter(45);
        address.Width = Unit.FromMillimeter(85);
        address.Height = Unit.FromMillimeter(45);
        address.WrapFormat.Style = WrapStyle.None;
        var sender = address.AddParagraph(string.Join(" · ", new[] { company.Name, company.AddressLine }.Where(v => !string.IsNullOrWhiteSpace(v))));
        sender.Format.Font.Size = 7;
        sender.Format.Font.Color = PdfReport.Muted;
        sender.Format.SpaceAfter = Unit.FromMillimeter(4);
        address.AddParagraph("Persönlich – vertraulich").Format.Font.Bold = true;
        address.AddParagraph(user.DisplayName);
        address.AddParagraph($"z. Hd. {user.DisplayName}, im Hause");
        if (!string.IsNullOrWhiteSpace(company.Street))
        {
            address.AddParagraph(company.Street);
        }

        if (!string.IsNullOrWhiteSpace(company.City))
        {
            address.AddParagraph($"{company.PostalCode} {company.City}".Trim());
        }

        // Informationsblock rechts.
        var info = section.AddTextFrame();
        info.RelativeHorizontal = RelativeHorizontal.Page;
        info.RelativeVertical = RelativeVertical.Page;
        info.Left = Unit.FromMillimeter(125);
        info.Top = Unit.FromMillimeter(50);
        info.Width = Unit.FromMillimeter(75);
        info.Height = Unit.FromMillimeter(40);
        info.WrapFormat.Style = WrapStyle.None;
        foreach (var (label, value) in new[]
        {
            ("Datum", clock.GetUtcNow().ToOffset(TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin").GetUtcOffset(clock.GetUtcNow())).ToString("dd.MM.yyyy", PdfReport.German)),
            ("Ansprechpartner", company.Name),
            ("Telefon", company.Phone ?? "–"),
            ("E-Mail", company.Email ?? "–"),
        })
        {
            var line = info.AddParagraph();
            line.Format.Font.Size = 8.5;
            line.AddFormattedText($"{label}: ", new Font { Color = PdfReport.Muted });
            line.AddText(value);
        }

        // Brieftext beginnt nach DIN 5008 Form B bei 98,46 mm (oberer Rand 26 mm).
        var subject = section.AddParagraph(reset ? "Neues Einmalpasswort für EstrichONE" : "Ihr Zugang zu EstrichONE");
        subject.Format.SpaceBefore = Unit.FromMillimeter(72);
        subject.Format.Font.Bold = true;
        subject.Format.Font.Size = 11;
        subject.Format.SpaceAfter = Unit.FromMillimeter(8);
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
