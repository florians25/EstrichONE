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
        var types = (plan.Types ?? []).ToDictionary(t => t.Id);
        var profile = await companies.GetProfileAsync(cancellationToken);
        var company = new PdfCompany(profile?.Name ?? "EstrichONE", profile?.Street, profile?.PostalCode, profile?.City, profile?.Phone, profile?.Email, profile?.Website);
        var title = month is null ? $"Teamkalender {y}" : $"Teamkalender {from.ToString("MMMM yyyy", German)}";
        var (document, section) = Create(title, "Personal", company, landscape: true);

        for (var index = 0; index < months.Count; index++)
        {
            var first = new DateOnly(y, months[index], 1);
            var days = System.DateTime.DaysInMonth(y, months[index]);
            if (index > 0)
            {
                section.AddPageBreak();
            }

            section.AddParagraph($"Teamkalender {first.ToString("MMMM yyyy", German)}", StyleNames.Heading1);
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
                        var (code, color) = entry.TypeId is { } typeId && types.TryGetValue(typeId, out var t) ? (t.Code, Light(t.Color)) : Kinds[entry.Kind];
                        if ((entry.From == date && entry.HalfDayStart) || (entry.To == date && entry.HalfDayEnd))
                        {
                            code = "½";
                        }

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
            var legend = section.AddParagraph(string.Join(" · ", types.Values.Where(t => t.IsActive && (!t.IsSickness || plan.Rows.Any(r => r.Entries.Any(e => e.TypeId == t.Id))))
                    .Select(t => $"{t.Code} {t.Name}")) + " · ½ halber Tag · ? beantragt · grau: Wochenende/Feiertag"
                + (monthHolidays.Count > 0 ? $" · Feiertage: {string.Join(", ", monthHolidays.Select(h => $"{h.Date:dd.MM.} {h.Name}"))}" : string.Empty));
            legend.Style = "Muted";
            legend.Format.SpaceBefore = Unit.FromPoint(6);
        }

        var printed = section.AddParagraph($"Bundesland: {GermanHolidays.States[plan.State]} · Stand: {PdfReport.DateTime(clock.GetUtcNow())}");
        printed.Style = "Muted";
        return TypedResults.File(Render(document), "application/pdf", month is null ? $"Teamkalender {y}.pdf" : $"Teamkalender {y}-{month:00}.pdf");
    }

    /// <summary>Farbe der Art aufgehellt (Lesbarkeit des Kürzels im Druck).</summary>
    private static Color Light(string hex)
    {
        var r = Convert.ToByte(hex.Substring(1, 2), 16);
        var g = Convert.ToByte(hex.Substring(3, 2), 16);
        var b = Convert.ToByte(hex.Substring(5, 2), 16);
        static byte Mix(byte c) => (byte)(c + ((255 - c) * 0.6));
        return Color.FromRgb(Mix(r), Mix(g), Mix(b));
    }

    /// <summary>Jahresübersicht der Urlaubskonten (Querformat): je Mitarbeiter Anspruch, Übertrag, Verfall, genommen, geplant, beantragt, Rest, Tage je Art.</summary>
    public static async Task<FileContentHttpResult> AccountsAsync(
        EstrichOneDbContext db, HrService hr, Access.Exports.IUserPermissions permissions, ICompanyProfiles companies, TimeProvider clock,
        CancellationToken cancellationToken, [FromQuery] int? year = null)
    {
        var y = year is >= 2000 and <= 2100 ? year.Value : hr.Today.Year;
        var rows = await AbsenceAdminEndpoints.RowsAsync(db, hr, permissions, y, cancellationToken);
        var profile = await companies.GetProfileAsync(cancellationToken);
        var company = new PdfCompany(profile?.Name ?? "EstrichONE", profile?.Street, profile?.PostalCode, profile?.City, profile?.Phone, profile?.Email, profile?.Website);
        var (document, section) = Create($"Urlaubskonten {y}", "Personal", company, landscape: true);
        section.AddParagraph($"Urlaubskonten {y}", StyleNames.Heading1);
        var other = rows.SelectMany(r => r.Vacation.ByType ?? []).Where(t => t.TypeId != StandardAbsenceTypes.Vacation).Select(t => t.Name).Distinct()
            .Order(StringComparer.CurrentCulture).Take(4).ToList();
        var columns = new List<(string, double, ParagraphAlignment)>
        {
            ("Mitarbeiter", 5.0, ParagraphAlignment.Left), ("Anspruch", 1.8, ParagraphAlignment.Right), ("Übertrag", 1.8, ParagraphAlignment.Right),
            ("verfallen", 1.8, ParagraphAlignment.Right), ("Korrektur", 1.8, ParagraphAlignment.Right), ("genommen", 1.9, ParagraphAlignment.Right),
            ("geplant", 1.8, ParagraphAlignment.Right), ("beantragt", 1.9, ParagraphAlignment.Right), ("Rest", 1.6, ParagraphAlignment.Right),
        };
        columns.AddRange(other.Select(o => (o, 7.3 / Math.Max(other.Count, 1), ParagraphAlignment.Right)));
        var table = AddTable(section, [.. columns]);
        table.Format.Font.Size = 8;
        string N(decimal v) => v == 0 ? "–" : v.ToString("0.#", German);
        foreach (var row in rows)
        {
            var v = row.Vacation;
            var r = table.AddRow();
            r.Borders.Bottom.Width = 0.3;
            r.Borders.Bottom.Color = Line;
            r.Cells[0].AddParagraph(row.Name);
            var dates = string.Join(" ", new[]
            {
                row.EntryDate?.Year == y ? $"Eintritt {row.EntryDate.Value.ToString("dd.MM.", German)}" : null,
                row.ExitDate?.Year == y ? $"Austritt {row.ExitDate.Value.ToString("dd.MM.", German)}" : null,
            }.OfType<string>());
            if (dates.Length > 0)
            {
                r.Cells[0].AddParagraph(dates).Format.Font.Size = 6.5;
            }

            var values = new[] { v.Entitlement, v.CarryOver, v.CarryOverExpired, v.Adjustment, v.Taken, v.Planned, v.Requested, v.Remaining }
                .Concat(other.Select(o => (v.ByType ?? []).Where(t => t.Name == o).Sum(t => t.Days))).ToList();
            for (var i = 0; i < values.Count; i++)
            {
                r.Cells[i + 1].AddParagraph(i == 7 ? values[i].ToString("0.#", German) : N(values[i]));
            }

            if (v.Remaining < 0)
            {
                r.Cells[8].Shading.Color = Color.FromRgb(0xFF, 0xCD, 0xD2);
            }
        }

        var settings = await hr.SettingsAsync(cancellationToken);
        var note = section.AddParagraph($"Arbeitstage nach Arbeitszeitmodell ohne Feiertage ({GermanHolidays.States[settings.State]}), halbe Tage als 0,5. "
            + (settings.CarryOverExpiryMonth == 0 ? "Übertrag ohne Verfall. " : $"Übertrag verfällt nach dem {settings.CarryOverExpiryDay:00}.{settings.CarryOverExpiryMonth:00}. "),
            "Muted");
        note.Format.SpaceBefore = Unit.FromPoint(6);
        section.AddParagraph($"Stand: {PdfReport.DateTime(clock.GetUtcNow())}").Style = "Muted";
        return TypedResults.File(Render(document), "application/pdf", $"Urlaubskonten {y}.pdf");
    }
}
