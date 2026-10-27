using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Pdf;
using EstrichOne.Api.Modules.Access.Exports;
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
    public string ServerUrl(HttpRequest request) => PublicAddress.Resolve(options.Value, request);

    public async Task<byte[]> RenderAsync(User user, string password, string serverUrl, bool reset, CancellationToken cancellationToken,
        OnboardingDetails? onboarding = null)
    {
        var company = await branding.GetAsync(cancellationToken);
        var subject = onboarding is not null ? $"Willkommen bei {company.Name}" : reset ? "Neues Einmalpasswort für EstrichONE" : "Ihr Zugang zu EstrichONE";
        var (document, section) = PdfLetter.Create(onboarding is not null ? "Onboarding" : reset ? "Neues Einmalpasswort" : "Ihr Zugang zu EstrichONE", company,
            ["Persönlich – vertraulich", user.DisplayName, $"z. Hd. {user.DisplayName}, im Hause", company.Street ?? "", $"{company.PostalCode} {company.City}".Trim()],
            [("Datum", PdfLetter.Today(clock)), ("Ansprechpartner", company.Name), ("Telefon", company.Phone), ("E-Mail", company.Email)],
            subject);
        section.AddParagraph($"Guten Tag {user.DisplayName},").Format.SpaceAfter = Unit.FromMillimeter(4);
        if (onboarding is not null)
        {
            var welcome = section.AddParagraph($"herzlich willkommen im Team von {company.Name}! Damit Sie vom ersten Tag an loslegen können, finden Sie hier alles Wichtige: "
                + "Ihre Zugangsdaten für EstrichONE – unsere Software für Bauakten, Planung und Baustellendokumentation –, die Einrichtung der App und die ersten Schritte.");
            welcome.Format.SpaceAfter = Unit.FromMillimeter(3);
            var facts = new List<(string, string)>();
            if (!string.IsNullOrWhiteSpace(onboarding.JobTitle)) facts.Add(("Tätigkeit", onboarding.JobTitle));
            if (onboarding.Crews.Count > 0) facts.Add(("Kolonne", string.Join(", ", onboarding.Crews)));
            if (onboarding.EntryDate is { } entry) facts.Add(("Eintritt", entry.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.GetCultureInfo("de-DE"))));
            if (facts.Count > 0)
            {
                Table(section, facts.Select(f => (f.Item1, f.Item2, false)));
            }

            Heading(section, "Ihre Zugangsdaten");
        }

        section.AddParagraph(onboarding is not null ? "Mit diesen Daten melden Sie sich im Browser und in der App an:" : reset
            ? "Ihr Passwort für EstrichONE wurde zurückgesetzt. Mit den folgenden Daten melden Sie sich wieder an:"
            : "für Sie wurde ein Zugang zu EstrichONE eingerichtet – unserer Software für Bauakten, Planung und Baustellendokumentation. Mit diesen Daten melden Sie sich an:");

        Table(section, [("Adresse", serverUrl, false), ("Benutzername", user.UserName, true), ("Einmalpasswort", password, true)]);
        if (onboarding is not null)
        {
            var must = section.AddParagraph("Das Einmalpasswort gilt nur für die erste Anmeldung – danach legen Sie ein eigenes Passwort fest.");
            must.Style = "Muted";
            must.Format.SpaceBefore = Unit.FromMillimeter(1);
        }

        Heading(section, onboarding is not null ? "Erste Schritte" : "So geht es los");
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

        if (onboarding?.DeviceCode is { } code)
        {
            Heading(section, "Handy einrichten");
            var device = section.AddTable();
            device.Borders.Visible = false;
            device.AddColumn(Unit.FromMillimeter(40));
            device.AddColumn(Unit.FromMillimeter(120));
            var row = device.AddRow();
            if (onboarding.DeviceQrPng is { Length: > 0 } qr)
            {
                var image = row.Cells[0].AddImage(PdfReport.ImageSource(qr));
                image.Width = Unit.FromMillimeter(34);
                image.LockAspectRatio = true;
            }

            var text = row.Cells[1].AddParagraph("App „EstrichONE“ öffnen, „Gerät einrichten“ wählen und den QR-Code scannen – oder den Code eingeben:");
            text.Format.SpaceAfter = Unit.FromMillimeter(2);
            var codeText = row.Cells[1].AddParagraph().AddFormattedText(code, TextFormat.Bold);
            codeText.Font.Size = 14;
            if (onboarding.DeviceCodeExpiresAt is { } expires)
            {
                var valid = row.Cells[1].AddParagraph($"Gültig bis {PdfReport.DateTime(expires)} – danach erzeugt die IT einen neuen Code.");
                valid.Style = "Muted";
                valid.Format.SpaceBefore = Unit.FromMillimeter(2);
            }
        }

        if (onboarding is not null && (onboarding.ContactName ?? onboarding.ContactPhone ?? onboarding.ContactEmail) is not null)
        {
            Heading(section, "Ihr Ansprechpartner");
            section.AddParagraph(string.Join(" · ", new[] { onboarding.ContactName, onboarding.ContactPhone, onboarding.ContactEmail }.Where(v => !string.IsNullOrWhiteSpace(v))));
        }

        if (onboarding is not null)
        {
            Heading(section, "Datenschutz");
            var privacy = section.AddParagraph("EstrichONE verarbeitet Ihre Daten (Name, Kontakt, Einsätze, Arbeitszeiten, Fotos der Baustellendokumentation) nur für "
                + "die Organisation Ihrer Arbeit im Unternehmen. Personalunterlagen und Krankmeldungen sehen nur berechtigte Personen der Personalverwaltung. "
                + "Auskunft und Fragen: Ihre Personalabteilung.");
            privacy.Style = "Muted";
        }

        var hint = section.AddParagraph("Bitte bewahren Sie dieses Schreiben vertraulich auf und geben Sie Ihr Passwort nicht weiter. Bei Fragen hilft Ihnen die IT-Administration.");
        hint.Format.SpaceBefore = Unit.FromMillimeter(5);
        hint.Style = "Muted";
        section.AddParagraph("Mit freundlichen Grüßen").Format.SpaceBefore = Unit.FromMillimeter(8);
        section.AddParagraph(company.Name).Format.SpaceBefore = Unit.FromMillimeter(6);
        return PdfReport.Render(document);
    }

    private static void Heading(Section section, string text)
    {
        var p = section.AddParagraph(text);
        p.Format.SpaceBefore = Unit.FromMillimeter(6);
        p.Format.SpaceAfter = Unit.FromMillimeter(1);
        p.Format.Font.Bold = true;
        p.Format.KeepWithNext = true;
    }

    private static void Table(Section section, IEnumerable<(string Label, string Value, bool Strong)> rows)
    {
        var table = section.AddTable();
        table.Borders.Visible = false;
        table.AddColumn(Unit.FromMillimeter(45));
        table.AddColumn(Unit.FromMillimeter(115));
        table.Format.SpaceBefore = Unit.FromMillimeter(2);
        foreach (var (label, value, strong) in rows)
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
