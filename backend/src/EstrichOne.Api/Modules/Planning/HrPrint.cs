using EstrichOne.Api.Infrastructure.Pdf;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using MigraDoc.DocumentObjectModel;
using static EstrichOne.Api.Infrastructure.Pdf.PdfReport;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>Urlaubsplan als PDF (Querformat): je Monat eine Seite, Zeilen = Mitarbeiter, Spalten = Tage, Kürzel und Farben je Art.</summary>
internal static class HrPrint
{
    private static readonly Dictionary<AbsenceKind, (string Code, Color Color)> Kinds = new()
    {
        [AbsenceKind.Vacation] = ("U", Color.FromRgb(0xC8, 0xE6, 0xC9)), [AbsenceKind.Sick] = ("K", Color.FromRgb(0xFF, 0xCD, 0xD2)),
        [AbsenceKind.Training] = ("S", Color.FromRgb(0xB2, 0xEB, 0xF2)), [AbsenceKind.School] = ("B", Color.FromRgb(0xBB, 0xDE, 0xFB)),
        [AbsenceKind.SpecialLeave] = ("SU", Color.FromRgb(0xE1, 0xBE, 0xE7)), [AbsenceKind.Other] = ("A", Color.FromRgb(0xEE, 0xEE, 0xEE)),
    };

    public static async Task<Results<FileContentHttpResult, ProblemHttpResult>> CreateAsync(
        EstrichOneDbContext db, HrService hr, ICompanyProfiles companies, TimeProvider clock, CancellationToken cancellationToken,
        [FromQuery] int? year = null, [FromQuery] int? month = null)
    {
        var y = year is >= 2000 and <= 2100 ? year.Value : hr.Today.Year;
        if (month is < 1 or > 12)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Ungültiger Monat", extensions: new Dictionary<string, object?> { ["code"] = "INVALID_MONTH" });
        }

        var months = month is { } m ? [m] : Enumerable.Range(1, 12).ToList();
        var from = new DateOnly(y, months[0], 1);
        var to = new DateOnly(y, months[^1], System.DateTime.DaysInMonth(y, months[^1]));
        var plan = await HrEndpoints.BuildPlanAsync(from, to, db, hr, cancellationToken);
        var holidays = plan.Holidays.ToDictionary(h => h.Date, h => h.Name);
        var profile = await companies.GetProfileAsync(cancellationToken);
        var company = new PdfCompany(profile?.Name ?? "EstrichONE", profile?.Street, profile?.PostalCode, profile?.City, profile?.Phone, profile?.Email, profile?.Website);
        var title = month is null ? $"Urlaubsplan {y}" : $"Urlaubsplan {from.ToString("MMMM yyyy", German)}";
        var (document, section) = Create(title, "Personal", company, landscape: true);

        for (var index = 0; index < months.Count; index++)
        {
            var first = new DateOnly(y, months[index], 1);
            var days = System.DateTime.DaysInMonth(y, months[index]);
            if (index > 0)
            {
                section.AddPageBreak();
            }

            section.AddParagraph($"Urlaubsplan {first.ToString("MMMM yyyy", German)}", StyleNames.Heading1);
            var nameWidth = 4.2;
            var dayWidth = (26.7 - nameWidth - 1.2) / days;
            var columns = new List<(string, double, ParagraphAlignment)> { ("Mitarbeiter", nameWidth, ParagraphAlignment.Left) };
            for (var d = 1; d <= days; d++)
            {
                var date = first.AddDays(d - 1);
                columns.Add(($"{d}\n{date.ToString("ddd", German)[..2]}", dayWidth, ParagraphAlignment.Center));
            }

            columns.Add(("Rest", 1.2, ParagraphAlignment.Right));
            var table = AddTable(section, [.. columns]);
            table.Format.Font.Size = 6.5;
            table.LeftPadding = Unit.FromPoint(1);
            table.RightPadding = Unit.FromPoint(1);
            for (var d = 1; d <= days; d++)
            {
                var date = first.AddDays(d - 1);
                if (holidays.ContainsKey(date) || date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    table.Rows[0].Cells[d].Shading.Color = Color.FromRgb(0xE0, 0xE0, 0xE0);
                }
            }

            foreach (var row in plan.Rows)
            {
                var r = table.AddRow();
                r.Borders.Bottom.Width = 0.3;
                r.Borders.Bottom.Color = Line;
                r.Cells[0].AddParagraph(row.Name);
                for (var d = 1; d <= days; d++)
                {
                    var date = first.AddDays(d - 1);
                    var cell = r.Cells[d];
                    var entry = row.Entries.FirstOrDefault(e => e.From <= date && date <= e.To);
                    if (entry is not null)
                    {
                        var (code, color) = Kinds[entry.Kind];
                        cell.Shading.Color = color;
                        cell.AddParagraph(entry.Status == "pending" ? $"{code}?" : code);
                    }
                    else if (holidays.ContainsKey(date) || date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    {
                        cell.Shading.Color = Color.FromRgb(0xF2, 0xF2, 0xF2);
                    }
                }

                r.Cells[days + 1].AddParagraph(row.VacationRemaining is { } rest ? rest.ToString("0.#", German) : "–");
            }

            var monthHolidays = plan.Holidays.Where(h => h.Date.Month == months[index]).ToList();
            var legend = section.AddParagraph("U Urlaub · K krank · S Schulung · B Berufsschule · SU Sonderurlaub · A abwesend · ? beantragt · grau: Wochenende/Feiertag"
                + (monthHolidays.Count > 0 ? $" · Feiertage: {string.Join(", ", monthHolidays.Select(h => $"{h.Date:dd.MM.} {h.Name}"))}" : string.Empty));
            legend.Style = "Muted";
            legend.Format.SpaceBefore = Unit.FromPoint(6);
        }

        var printed = section.AddParagraph($"Bundesland: {GermanHolidays.States[plan.State]} · Stand: {PdfReport.DateTime(clock.GetUtcNow())}");
        printed.Style = "Muted";
        return TypedResults.File(Render(document), "application/pdf", month is null ? $"Urlaubsplan {y}.pdf" : $"Urlaubsplan {y}-{month:00}.pdf");
    }
}
