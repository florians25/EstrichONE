using EstrichOne.Api.Infrastructure.Pdf;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using MigraDoc.DocumentObjectModel;
using static EstrichOne.Api.Infrastructure.Pdf.PdfReport;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>„Wer fährt wohin“ als PDF: je Baustelle Beginn, Adresse mit Ansprechpartner, Team, Fahrzeuge und Tätigkeit.</summary>
internal static class DispatchPrint
{
    public static async Task<Results<FileContentHttpResult, ProblemHttpResult>> CreateAsync(
        string day, DispatchService service, ICompanyProfiles companies, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (!DispatchEndpoints.TryDay(day, clock, out var date))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Ungültiger Tag",
                extensions: new Dictionary<string, object?> { ["code"] = "INVALID_DAY" });
        }

        var board = await service.BoardAsync(date, cancellationToken);
        var profile = await companies.GetProfileAsync(cancellationToken);
        var company = new PdfCompany(profile?.Name ?? "EstrichONE", profile?.Street, profile?.PostalCode, profile?.City, profile?.Phone, profile?.Email, profile?.Website);
        var title = $"Wer fährt wohin – {date.ToString("dddd, dd.MM.yyyy", German)}";
        var (document, section) = Create(title, "Planung", company, landscape: true);
        section.AddParagraph(title, StyleNames.Heading1);

        if (board.Sites.Count == 0)
        {
            section.AddParagraph("Für diesen Tag sind keine Baustellen eingeplant.").Style = "Muted";
        }
        else
        {
            var table = AddTable(section, ("Beginn", 1.8, ParagraphAlignment.Left), ("Baustelle", 7.4, ParagraphAlignment.Left),
                ("Wer", 6.2, ParagraphAlignment.Left), ("Fahrzeuge / Geräte", 4.6, ParagraphAlignment.Left), ("Tätigkeit", 6.7, ParagraphAlignment.Left));
            foreach (var site in board.Sites)
            {
                var contact = string.Join(" ", new[] { site.SiteContactName, site.SiteContactPhone }.Where(v => !string.IsNullOrWhiteSpace(v)));
                var row = AddRow(table,
                    site.StartTime is { } start ? $"{start:HH\\:mm}" : "–",
                    string.Join("\n", new[] { $"{site.Number} {site.Title}".Trim(), site.Address, contact.Length > 0 ? $"Vor Ort: {contact}" : null }
                        .Where(v => !string.IsNullOrWhiteSpace(v))),
                    site.People.Count > 0 ? string.Join("\n", site.People.Select(p => p.Name)) : "– niemand –",
                    site.Vehicles.Count > 0 ? string.Join("\n", site.Vehicles.Select(v => v.Name)) : "–",
                    string.Join("\n", site.Jobs.Select(j => $"{Time(j)} {j.Activity}{(j.CrewName is null ? string.Empty : $" ({j.CrewName})")}".Trim())));
                if (site.Warnings.Count > 0)
                {
                    var warning = row.Cells[4].AddParagraph("Hinweis: " + string.Join(" ", site.Warnings));
                    warning.Format.Font.Color = Color.FromRgb(0xAA, 0x08, 0x08);
                    warning.Format.Font.Size = 7;
                }
            }
        }

        var free = board.People.Where(p => p.SiteIds.Count == 0 && p.Note is null).Select(p => p.Name).ToList();
        var away = board.People.Where(p => p.SiteIds.Count == 0 && p.Note is not null).Select(p => $"{p.Name} ({p.Note})").ToList();
        var idle = board.Vehicles.Where(v => v.SiteIds.Count == 0).Select(v => v.Name).ToList();
        AddList(section, "Noch nicht eingeteilt", free);
        AddList(section, "Abwesend / Termine", away);
        AddList(section, "Freie Fahrzeuge und Geräte", idle);

        var printed = section.AddParagraph($"Stand: {PdfReport.DateTime(clock.GetUtcNow())}");
        printed.Style = "Muted";
        printed.Format.SpaceBefore = Unit.FromPoint(8);
        return TypedResults.File(Render(document), "application/pdf", $"Wer fährt wohin {date:yyyy-MM-dd}.pdf");
    }

    private static string Time(DispatchJob job) =>
        job.StartTime is { } s ? $"{s:HH\\:mm}{(job.EndTime is { } e ? $"–{e:HH\\:mm}" : string.Empty)}" : string.Empty;

    private static void AddList(Section section, string heading, List<string> names)
    {
        if (names.Count == 0)
        {
            return;
        }

        var paragraph = section.AddParagraph();
        paragraph.Format.SpaceBefore = Unit.FromPoint(8);
        paragraph.AddFormattedText($"{heading}: ", TextFormat.Bold);
        paragraph.AddText(string.Join(", ", names));
    }
}
