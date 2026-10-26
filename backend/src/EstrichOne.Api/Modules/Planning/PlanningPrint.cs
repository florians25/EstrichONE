using EstrichOne.Api.Infrastructure.Pdf;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using MigraDoc.DocumentObjectModel;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static EstrichOne.Api.Infrastructure.Pdf.PdfReport;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>
/// Kolonnenplan als PDF zum Aushang oder für die Kolonnen: Woche (Querformat, Zeilen Kolonnen oder Mitarbeiter,
/// Spalten Tage) oder Tag (je Kolonne die Einsätze mit Adresse, Team, Geräten und Hinweisen). Abwesenheiten und
/// Konflikte (Doppelbelegung) sind markiert.
/// </summary>
internal static class PlanningPrint
{
    private static readonly Dictionary<AssignmentKind, string> KindText = new()
    {
        [AssignmentKind.Screed] = "Estrich", [AssignmentKind.InitialInspection] = "Erstbegehung", [AssignmentKind.SecondInspection] = "Zweitbegehung",
        [AssignmentKind.CmMeasurement] = "CM-Messung", [AssignmentKind.Acceptance] = "Abnahme", [AssignmentKind.Survey] = "Aufmaß",
        [AssignmentKind.Check] = "Kontrolle", [AssignmentKind.CustomerAppointment] = "Kundentermin", [AssignmentKind.Delivery] = "Lieferung",
        [AssignmentKind.Other] = "Sonstiges",
    };

    private static readonly Dictionary<AbsenceKind, string> AbsenceText = new()
    {
        [AbsenceKind.Vacation] = "Urlaub", [AbsenceKind.Sick] = "krank", [AbsenceKind.Training] = "Schulung", [AbsenceKind.Other] = "abwesend",
        [AbsenceKind.SpecialLeave] = "Sonderurlaub", [AbsenceKind.School] = "Berufsschule",
    };

    public static void Map(RouteGroupBuilder planning)
    {
        planning.MapGet("/print.pdf", CreateAsync).WithName("PlanningPdf")
            .WithSummary("Kolonnenplan als PDF: mode=week|day, from (Tag bzw. Montag), rows=crews|people, weekend=true|false")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .RequirePermission(Permissions.PlanningRead);
        planning.MapGet("/work-plan.pdf", WorkPlanAsync).WithName("WorkPlanPdf")
            .WithSummary("Arbeitsplan als PDF: from (Standard heute), days (1–31, Standard 7), crewId, mine=true (nur eigene Einsätze)")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .RequirePermission(Permissions.PlanningRead);
    }

    /// <summary>Arbeitsplan: je Tag die Einsätze mit Uhrzeit, Baustelle, Tätigkeit, Kolonne/Team und Geräten; Abwesende (ohne Gründe).</summary>
    private static async Task<FileContentHttpResult> WorkPlanAsync(
        PlanningService service, IPdfBranding branding, ICurrentUser currentUser, TimeProvider clock, CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null, [FromQuery] int? days = null, [FromQuery] Guid? crewId = null, [FromQuery] bool mine = false)
    {
        var start = from ?? DateOnly.FromDateTime(clock.GetLocalNow().Date);
        var count = Math.Clamp(days ?? 7, 1, 31);
        var end = start.AddDays(count - 1);
        var range = await service.RangeAsync(start, end, mine ? currentUser.UserId : null, cancellationToken);
        var company = await branding.GetAsync(cancellationToken);
        var title = $"Arbeitsplan {start:dd.MM.} – {end:dd.MM.yyyy}";
        var (document, section) = Create(title, "Planung", company, landscape: true);
        section.AddParagraph(title, StyleNames.Heading1);
        var assignments = range.Assignments.Where(a => crewId is null || a.CrewId == crewId).ToList();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var day = d;
            var jobs = assignments.Where(a => a.Day == day).OrderBy(a => a.StartTime ?? TimeOnly.MinValue).ThenBy(a => a.CrewName).ToList();
            var away = range.Absences.Where(a => a.From <= day && day <= a.To).Select(a => a.UserName).Distinct().ToList();
            if (jobs.Count == 0 && away.Count == 0 && (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                continue;
            }

            var heading = section.AddParagraph(day.ToString("dddd, dd.MM.yyyy", German), StyleNames.Heading2);
            heading.Format.KeepWithNext = true;
            if (jobs.Count == 0)
            {
                section.AddParagraph("Keine Einsätze.").Style = "Muted";
            }
            else
            {
                var table = AddTable(section, ("Zeit", 2.3, ParagraphAlignment.Left), ("Baustelle", 8.4, ParagraphAlignment.Left),
                    ("Tätigkeit", 6.0, ParagraphAlignment.Left), ("Kolonne / Team", 6.0, ParagraphAlignment.Left), ("Geräte", 4.0, ParagraphAlignment.Left));
                foreach (var a in jobs)
                {
                    AddRow(table,
                        a.StartTime is { } s ? $"{s:HH\\:mm}{(a.EndTime is { } e ? $"–{e:HH\\:mm}" : string.Empty)}" : "ganztags",
                        string.Join("\n", new[] { $"{a.BauakteNumber} {a.BauakteTitle}".Trim(), a.BauakteAddress }.Where(v => !string.IsNullOrWhiteSpace(v))),
                        a.Activity,
                        string.Join("\n", new[] { a.CrewName, string.Join(", ", a.People.Select(p => p.Name)) }.Where(v => !string.IsNullOrWhiteSpace(v))),
                        a.Resources.Count > 0 ? string.Join(", ", a.Resources.Select(r => r.Name)) : "–");
                }
            }

            if (away.Count > 0)
            {
                var p = section.AddParagraph($"Abwesend: {string.Join(", ", away)}");
                p.Style = "Muted";
                p.Format.SpaceBefore = Unit.FromPoint(3);
            }
        }

        return TypedResults.File(Render(document), "application/pdf", $"Arbeitsplan {start:yyyy-MM-dd}.pdf");
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> CreateAsync(
        PlanningService service, EstrichOneDbContext db, IPdfBranding branding, TimeProvider clock, CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null, [FromQuery] string? mode = null, [FromQuery] string? rows = null, [FromQuery] bool weekend = false)
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().Date);
        var day = mode?.ToLowerInvariant() == "day";
        var start = from ?? today;
        if (!day)
        {
            start = start.AddDays(-(((int)start.DayOfWeek + 6) % 7));
        }

        var days = Enumerable.Range(0, day ? 1 : weekend ? 7 : 5).Select(start.AddDays).ToList();
        var range = await service.RangeAsync(days[0], days[^1], null, cancellationToken);
        var crews = await db.Set<Crew>().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);
        var company = await branding.GetAsync(cancellationToken);
        var byPeople = rows?.ToLowerInvariant() == "people";

        var title = day
            ? $"Tageseinteilung {days[0].ToString("dddd, dd.MM.yyyy", German)}"
            : $"Kolonnenplan KW {System.Globalization.ISOWeek.GetWeekOfYear(days[0].ToDateTime(TimeOnly.MinValue))} ({days[0]:dd.MM.} – {days[^1]:dd.MM.yyyy})";
        var (document, section) = Create(title, "Planung", company, landscape: !day);
        section.AddParagraph(title, StyleNames.Heading1);
        var usable = day ? 17.2 : 26.7;

        // Zeilen: Kolonnen (+ ohne Kolonne) oder Mitarbeiter.
        List<(string Name, Func<AssignmentView, bool> Match, IReadOnlyList<Guid> People)> lanes;
        if (byPeople)
        {
            var peopleIds = range.Assignments.SelectMany(a => a.People.Select(p => p.Id)).Concat(crews.SelectMany(c => c.MemberUserIds)).Distinct().ToList();
            var users = await db.Set<User>().AsNoTracking().Where(u => u.IsActive && peopleIds.Contains(u.Id)).OrderBy(u => u.DisplayName).ToListAsync(cancellationToken);
            lanes = users.Select(u => (u.DisplayName, (Func<AssignmentView, bool>)(a => a.People.Any(p => p.Id == u.Id)), (IReadOnlyList<Guid>)[u.Id])).ToList();
        }
        else
        {
            lanes = crews.Select(c => (c.Name, (Func<AssignmentView, bool>)(a => a.CrewId == c.Id),
                (IReadOnlyList<Guid>)c.MemberUserIds.Concat(c.LeaderUserId is { } l ? [l] : []).Distinct().ToList())).ToList();
            if (range.Assignments.Any(a => a.CrewId is null))
            {
                lanes.Add(("Ohne Kolonne", a => a.CrewId is null, []));
            }
        }

        if (day)
        {
            foreach (var (name, match, people) in lanes)
            {
                var items = range.Assignments.Where(match).OrderBy(a => a.StartTime).ToList();
                var absent = range.Absences.Where(a => people.Contains(a.UserId) && a.From <= days[0] && days[0] <= a.To).ToList();
                if (items.Count == 0 && absent.Count == 0)
                {
                    continue;
                }

                section.AddParagraph(name, StyleNames.Heading2);
                if (absent.Count > 0)
                {
                    section.AddParagraph("Abwesend: " + string.Join(", ", absent.Select(a => $"{a.UserName} ({AbsenceText.GetValueOrDefault(a.Kind)})"))).Style = "Muted";
                }

                if (items.Count == 0)
                {
                    section.AddParagraph("Keine Einsätze.").Style = "Muted";
                    continue;
                }

                var table = AddTable(section, ("Zeit", 2.2, ParagraphAlignment.Left), ("Bauakte / Adresse", 6.0, ParagraphAlignment.Left),
                    ("Tätigkeit", 4.0, ParagraphAlignment.Left), ("Team / Geräte", 5.0, ParagraphAlignment.Left));
                foreach (var a in items)
                {
                    var row = AddRow(table, Time(a), $"{a.BauakteNumber} {a.BauakteTitle}\n{a.BauakteAddress}".Trim(),
                        $"{KindText.GetValueOrDefault(a.Kind)}: {a.Activity}{(string.IsNullOrWhiteSpace(a.Notes) ? string.Empty : $"\n{a.Notes}")}",
                        string.Join("\n", new[] { string.Join(", ", a.People.Select(p => p.Name)), string.Join(", ", a.Resources.Select(r => r.Name)) }.Where(v => v.Length > 0)));
                    Warn(row.Cells[1], a);
                }
            }

            if (lanes.Count == 0 || range.Assignments.Count == 0)
            {
                section.AddParagraph("Für diesen Tag sind keine Einsätze geplant.").Style = "Muted";
            }
        }
        else
        {
            var laneWidth = 3.4;
            var dayWidth = (usable - laneWidth) / days.Count;
            var columns = new List<(string, double, ParagraphAlignment)> { (byPeople ? "Mitarbeiter" : "Kolonne", laneWidth, ParagraphAlignment.Left) };
            columns.AddRange(days.Select(d => (d.ToString("ddd dd.MM.", German), dayWidth, ParagraphAlignment.Left)));
            var table = AddTable(section, [.. columns]);
            table.Format.Font.Size = 7.5;
            if (!byPeople)
            {
                var absRow = table.AddRow();
                absRow.Shading.Color = Color.FromRgb(0xFF, 0xF8, 0xE6);
                absRow.Cells[0].AddParagraph("Abwesend").Format.Font.Bold = true;
                for (var i = 0; i < days.Count; i++)
                {
                    var absent = range.Absences.Where(a => a.From <= days[i] && days[i] <= a.To).Select(a => $"{a.UserName} ({AbsenceText.GetValueOrDefault(a.Kind)})").ToList();
                    absRow.Cells[i + 1].AddParagraph(absent.Count == 0 ? string.Empty : string.Join("\n", absent));
                }
            }

            foreach (var (name, match, people) in lanes)
            {
                var row = table.AddRow();
                row.Borders.Bottom.Width = 0.4;
                row.Borders.Bottom.Color = Line;
                row.Cells[0].AddParagraph(name).Format.Font.Bold = true;
                for (var i = 0; i < days.Count; i++)
                {
                    var cell = row.Cells[i + 1];
                    if (byPeople && range.Absences.FirstOrDefault(a => people.Contains(a.UserId) && a.From <= days[i] && days[i] <= a.To) is { } absence)
                    {
                        cell.Shading.Color = Color.FromRgb(0xFF, 0xF1, 0xD6);
                        cell.AddParagraph(AbsenceText.GetValueOrDefault(absence.Kind) ?? "abwesend").Format.Font.Italic = true;
                    }

                    var items = range.Assignments.Where(a => a.Day == days[i] && match(a)).OrderBy(a => a.StartTime).ToList();
                    if (byPeople && items.Count > 1)
                    {
                        cell.Shading.Color = Color.FromRgb(0xFD, 0xE7, 0xE7);
                    }

                    foreach (var a in items)
                    {
                        var p = cell.AddParagraph();
                        p.Format.SpaceAfter = Unit.FromPoint(3);
                        p.AddFormattedText($"{Time(a)} {KindText.GetValueOrDefault(a.Kind)}", TextFormat.Bold);
                        p.AddLineBreak();
                        p.AddText($"{a.BauakteNumber} {a.BauakteTitle}".Trim());
                        if (!string.IsNullOrWhiteSpace(a.Activity))
                        {
                            p.AddLineBreak();
                            p.AddText(a.Activity);
                        }

                        if (!byPeople && a.People.Count > 0 && a.CrewId is null)
                        {
                            p.AddLineBreak();
                            p.AddText(string.Join(", ", a.People.Select(x => x.Name)));
                        }

                        if (a.Resources.Count > 0)
                        {
                            p.AddLineBreak();
                            p.AddFormattedText(string.Join(", ", a.Resources.Select(r => r.Name)), TextFormat.Italic);
                        }

                        Warn(cell, a);
                    }
                }
            }

            if (lanes.Count == 0)
            {
                section.AddParagraph("Noch keine Kolonnen angelegt.").Style = "Muted";
            }
        }

        var printed = section.AddParagraph($"Stand: {PdfReport.DateTime(clock.GetUtcNow())}");
        printed.Style = "Muted";
        printed.Format.SpaceBefore = Unit.FromPoint(8);
        var fileName = day ? $"Tageseinteilung {days[0]:yyyy-MM-dd}.pdf" : $"Kolonnenplan {days[0]:yyyy-MM-dd}.pdf";
        return TypedResults.File(Render(document), "application/pdf", fileName);
    }

    private static string Time(AssignmentView a) =>
        a.StartTime is { } s ? $"{s:HH\\:mm}{(a.EndTime is { } e ? $"–{e:HH\\:mm}" : string.Empty)}" : "ganztags";

    private static void Warn(MigraDoc.DocumentObjectModel.Tables.Cell cell, AssignmentView a)
    {
        if (a.Warnings.Count == 0)
        {
            return;
        }

        var warning = cell.AddParagraph("Hinweis: " + string.Join(" ", a.Warnings));
        warning.Format.Font.Color = Color.FromRgb(0xAA, 0x08, 0x08);
        warning.Format.Font.Size = 7;
    }
}
