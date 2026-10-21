using EstrichOne.Api.Infrastructure.Pdf;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using static EstrichOne.Api.Infrastructure.Pdf.PdfReport;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>„Wer fährt wohin“ als PDF, nach Kolonnen: Kolonne → Baustelle mit Adresse, Beginn, Ansprechpartner, Fahrzeug und Tätigkeit.</summary>
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
            // Gegliedert nach Kolonnen: Kolonne → Baustelle mit Adresse, Beginn, Ansprechpartner, Fahrzeug, Tätigkeit.
            var table = AddTable(section, ("Kolonne", 4.6, ParagraphAlignment.Left), ("Beginn", 1.6, ParagraphAlignment.Left),
                ("Baustelle", 7.6, ParagraphAlignment.Left), ("Ansprechpartner vor Ort", 4.4, ParagraphAlignment.Left),
                ("Fahrzeug", 3.6, ParagraphAlignment.Left), ("Tätigkeit", 4.9, ParagraphAlignment.Left));
            var sites = board.Sites.ToDictionary(s => s.BauakteId);
            foreach (var crew in board.Crews.Where(c => c.SiteIds.Count > 0))
            {
                foreach (var site in crew.SiteIds.Select(id => sites[id]))
                {
                    var job = site.Crews.First(c => c.Id == crew.Id);
                    var who = string.Join("\n", new[] { crew.Name, crew.LeaderName is null ? null : $"Leitung: {crew.LeaderName}",
                        crew.AbsentMembers.Count > 0 ? "Fehlt: " + string.Join(", ", crew.AbsentMembers.Select(a => $"{a.Name} ({a.Note})")) : null }
                        .Where(v => v is not null));
                    var vehicles = site.Crews.Count == 1 && site.Vehicles.Count > 0 ? site.Vehicles.Select(v => v.Name) : crew.Vehicles.Select(v => v.Name);
                    var row = AddRow(table, who, job.StartTime is { } start ? $"{start:HH\\:mm}" : "–", SiteText(site), Contact(site),
                        string.Join("\n", vehicles.DefaultIfEmpty("–")), job.Activity);
                    row.Cells[0].Format.Font.Bold = true;
                    if (crew.SiteIds.Count > 1)
                    {
                        Warn(row, "Kolonne an dem Tag mehrfach eingeplant.");
                    }
                }
            }

            // Einsätze ohne Kolonne (einzeln eingeplante Mitarbeiter).
            foreach (var site in board.Sites.Where(s => s.Jobs.Any(j => j.CrewId is null)))
            {
                var jobs = site.Jobs.Where(j => j.CrewId is null).ToList();
                var crewMembers = board.People.Where(p => p.CrewId is not null && site.Crews.Any(c => c.Id == p.CrewId)).Select(p => p.Id).ToHashSet();
                var people = site.People.Where(p => !crewMembers.Contains(p.Id)).Select(p => p.Name).ToList();
                AddRow(table, people.Count > 0 ? "Ohne Kolonne:\n" + string.Join("\n", people) : "Ohne Kolonne",
                    jobs.Min(j => j.StartTime) is { } start ? $"{start:HH\\:mm}" : "–", SiteText(site), Contact(site),
                    site.Crews.Count == 0 && site.Vehicles.Count > 0 ? string.Join("\n", site.Vehicles.Select(v => v.Name)) : "–",
                    string.Join("\n", jobs.Select(j => $"{Time(j)} {j.Activity}".Trim())));
            }
        }

        AddList(section, "Kolonnen ohne Einsatz", board.Crews.Where(c => c.SiteIds.Count == 0).Select(c => c.Name).ToList());
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

    private static string SiteText(DispatchSite site) =>
        string.Join("\n", new[] { $"{site.Number} {site.Title}".Trim(), site.Address }.Where(v => !string.IsNullOrWhiteSpace(v)));

    private static string Contact(DispatchSite site)
    {
        var contact = string.Join("\n", new[] { site.SiteContactName, site.SiteContactPhone }.Where(v => !string.IsNullOrWhiteSpace(v)));
        return contact.Length > 0 ? contact : "–";
    }

    private static void Warn(Row row, string text)
    {
        var warning = row.Cells[5].AddParagraph("Hinweis: " + text);
        warning.Format.Font.Color = Color.FromRgb(0xAA, 0x08, 0x08);
        warning.Format.Font.Size = 7;
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
