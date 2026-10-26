using EstrichOne.Api.Infrastructure.Pdf;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Documentation.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using EstrichOne.Api.Modules.Work.Exports;
using MigraDoc.DocumentObjectModel;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetVips;
using static EstrichOne.Api.Infrastructure.Pdf.PdfReport;

namespace EstrichOne.Api.Modules.Documentation;

/// <summary>
/// Bauakte als PDF (Server, MigraDoc): Deckblatt mit Logo, Stammdaten und Kunde, Räume und Flächen, Bautagebuch,
/// Prüfungen (Erstbegehung, CM-Messung/Belegreife), Aufgaben und Mängel sowie Fotodokumentation mit Beschriftung.
/// </summary>
internal static class BauakteReportEndpoints
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/bauakten/{id:guid}/report.pdf", CreateAsync).WithTags("Ausdrucke").WithName("BauakteReportPdf")
            .WithSummary("Bauakte als PDF; parts=uebersicht,raeume,tagebuch,pruefungen,maengel,fotos (Standard: alle); maxPhotos (Standard 60); from/to für das Bautagebuch")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.BauaktenRead);

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> CreateAsync(
        Guid id, BauakteReportBuilder builder, IBauakteAccess access, IGeneratedDocuments generated, ICurrentUser currentUser, EstrichOneDbContext db,
        CancellationToken cancellationToken, [FromQuery] string? parts = null, [FromQuery] int? maxPhotos = null,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null)
    {
        if (!await access.CanReadAsync(id, cancellationToken))
        {
            return TypedResults.Problem(statusCode: 404, title: "Bauakte nicht gefunden", extensions: new Dictionary<string, object?> { ["code"] = "BAUAKTE_NOT_FOUND" });
        }

        var selected = string.IsNullOrWhiteSpace(parts)
            ? BauakteReportBuilder.AllParts
            : parts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = await builder.BuildAsync(id, selected, Math.Clamp(maxPhotos ?? 60, 0, 200), cancellationToken, from: from, to: to);
        if (result is null)
        {
            return TypedResults.Problem(statusCode: 404, title: "Bauakte nicht gefunden", extensions: new Dictionary<string, object?> { ["code"] = "BAUAKTE_NOT_FOUND" });
        }

        // Erzeugte PDFs liegen automatisch in der Bauakte (Protokolle / Berichte, je Druck eine neue Fassung).
        var full = selected.SetEquals(BauakteReportBuilder.AllParts) && from is null && to is null;
        await generated.StoreAsync(id, currentUser.UserId, full ? "Bauaktenbericht.pdf" : "Bauaktenauszug.pdf", result.Value.Pdf, "protocol", "Berichte",
            full ? "Bauakte als PDF (vollständig)" : $"Auszug: {string.Join(", ", selected.Order())}", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.File(result.Value.Pdf, "application/pdf", result.Value.FileName);
    }
}

internal sealed class BauakteReportBuilder(
    EstrichOneDbContext db, IBauakteReportSource bauakten, IWorkReportSource work, IPdfBranding branding, LocalFileStorage storage,
    ICurrentUser currentUser, IAuditLog audit, TimeProvider clock, ILogger<BauakteReportBuilder> logger, IFormSchemas forms, IValueLists valueLists)
{
    public static readonly IReadOnlySet<string> AllParts =
        new HashSet<string>(["uebersicht", "raeume", "tagebuch", "pruefungen", "maengel", "fotos"], StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<BauakteStatus, string> StatusText = new()
    {
        [BauakteStatus.Inquiry] = "Anfrage", [BauakteStatus.Planned] = "Geplant", [BauakteStatus.InProgress] = "In Ausführung",
        [BauakteStatus.Drying] = "Trocknung", [BauakteStatus.Completed] = "Abgeschlossen", [BauakteStatus.Cancelled] = "Storniert",
    };

    private static readonly Dictionary<DiaryWeather, string> Weather = new()
    {
        [DiaryWeather.Sunny] = "sonnig", [DiaryWeather.Cloudy] = "bewölkt", [DiaryWeather.Rain] = "Regen", [DiaryWeather.Snow] = "Schnee",
        [DiaryWeather.Frost] = "Frost", [DiaryWeather.Wind] = "Wind",
    };

    private static readonly Dictionary<string, string> CheckTitles = new()
    {
        ["substrate"] = "Untergrund tragfähig, eben und sauber", ["edgeInsulation"] = "Randdämmstreifen vorhanden",
        ["heatingPressureTest"] = "Fußbodenheizung abgedrückt", ["access"] = "Zufahrt und Pumpenstandort", ["utilities"] = "Wasser und Strom vorhanden",
    };

    /// <param name="inspectionId">Nur dieses Protokoll (Erstbegehung) mit seinen Fotos.</param>
    public async Task<(byte[] Pdf, string FileName)?> BuildAsync(Guid bauakteId, IReadOnlySet<string> parts, int maxPhotos, CancellationToken cancellationToken,
        Guid? inspectionId = null, DateOnly? from = null, DateOnly? to = null)
    {
        var info = await bauakten.GetAsync(bauakteId, cancellationToken);
        if (info is null)
        {
            return null;
        }

        var pdfCompany = await branding.GetAsync(cancellationToken);
        var label = inspectionId is null
            ? $"Bauakte {info.Number ?? "(ohne Nummer)"} – {info.Title}"
            : $"Erstbegehungsprotokoll {info.Number ?? ""} – {info.Title}";
        var (document, section) = Create(label, "Bauakte", pdfCompany);
        const double width = 17.2;

        var diary = parts.Contains("tagebuch")
            ? await db.Set<DiaryEntry>().AsNoTracking().Where(d => d.BauakteId == bauakteId && (from == null || d.Day >= from) && (to == null || d.Day <= to)).OrderBy(d => d.Day).ThenBy(d => d.CreatedAt).ToListAsync(cancellationToken)
            : [];
        var inspections = parts.Contains("pruefungen")
            ? await db.Set<Inspection>().AsNoTracking().Where(i => i.BauakteId == bauakteId && (inspectionId == null || i.Id == inspectionId))
                .OrderBy(i => i.PerformedAt).ToListAsync(cancellationToken)
            : [];
        var onlyPhotos = inspectionId is null ? null : inspections.SelectMany(i => i.PhotoFileIds).ToList();
        var workItems = parts.Contains("maengel") ? await work.ListAsync(bauakteId, cancellationToken) : [];
        var photos = parts.Contains("fotos")
            ? await db.Set<FileRecord>().AsNoTracking()
                .Where(f => f.BauakteId == bauakteId && f.Kind == FileKind.Photo && f.State == FileState.Stored && (onlyPhotos == null || onlyPhotos.Contains(f.Id)))
                .OrderBy(f => f.CapturedAt ?? f.CreatedAt).Take(maxPhotos).ToListAsync(cancellationToken)
            : [];
        var userIds = diary.Select(d => d.AuthorUserId).Concat(inspections.Select(i => i.PerformedByUserId)).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        Cover(section, info, pdfCompany, width, diary.Count, inspections.Count, workItems.Count, photos.Count);

        if (parts.Contains("uebersicht"))
        {
            section.AddPageBreak();
            section.AddParagraph("Übersicht", StyleNames.Heading1);
            AddFacts(section, width,
                ("Bauvorhaben", info.Title), ("Aktennummer", info.Number), ("Status", StatusText.GetValueOrDefault(info.Status, info.Status.ToString())),
                ("Baustelle", Address(info.Street, info.PostalCode, info.City)), ("Ausführung", Period(info.PlannedStart, info.PlannedEnd)),
                ("Zuständig", info.ResponsibleName), ("Team", info.Team.Count > 0 ? string.Join(", ", info.Team) : null),
                ("Ansprechpartner vor Ort", string.Join(" · ", new[] { info.SiteContactName, info.SiteContactPhone }.Where(v => !string.IsNullOrWhiteSpace(v)))),
                ("Angelegt", $"{PdfReport.DateTime(info.CreatedAt)} von {info.CreatedByName}"));
            if (info.Customer is { } c)
            {
                section.AddParagraph("Kunde", StyleNames.Heading2);
                AddFacts(section, width, ("Name", c.Name), ("Kundennummer", c.CustomerNumber), ("Anschrift", Address(c.Street, c.PostalCode, c.City)),
                    ("Telefon", c.Phone), ("E-Mail", c.Email));
            }

            if (!string.IsNullOrWhiteSpace(info.Description) || !string.IsNullOrWhiteSpace(info.SiteNotes))
            {
                section.AddParagraph("Beschreibung und Hinweise", StyleNames.Heading2);
                foreach (var text in new[] { info.Description, info.SiteNotes }.Where(t => !string.IsNullOrWhiteSpace(t)))
                {
                    section.AddParagraph(text!);
                }
            }
        }

        if (parts.Contains("raeume"))
        {
            section.AddParagraph("Räume und Flächen", StyleNames.Heading2);
            if (info.Rooms.Count == 0)
            {
                section.AddParagraph("Keine Räume erfasst.").Style = "Muted";
            }
            else
            {
                var table = AddTable(section, ("Raum", 4.4, ParagraphAlignment.Left), ("Geschoss", 2.0, ParagraphAlignment.Left),
                    ("Fläche", 2.0, ParagraphAlignment.Right), ("Estrich", 3.4, ParagraphAlignment.Left), ("Dicke / Dämmung", 2.6, ParagraphAlignment.Left),
                    ("Status", 2.8, ParagraphAlignment.Left));
                foreach (var r in info.Rooms)
                {
                    AddRow(table, r.Name, r.Floor, r.AreaSquareMeters is { } a ? $"{Number(a)} m²" : null,
                        string.Join(", ", new[] { r.ScreedType, r.UnderfloorHeating ? "Heizestrich" : null }.Where(v => v is not null)),
                        string.Join(" / ", new[] { r.ThicknessMillimeters is { } t ? $"{t} mm" : null, r.InsulationMillimeters is { } i ? $"{i} mm" : null }.Where(v => v is not null)),
                        r.Status);
                }

                var total = table.AddRow();
                total.Format.Font.Bold = true;
                total.Cells[0].AddParagraph("Summe");
                total.Cells[2].AddParagraph($"{Number(info.Rooms.Sum(r => r.AreaSquareMeters ?? 0))} m²");
            }
        }

        if (parts.Contains("tagebuch"))
        {
            section.AddParagraph("Bautagebuch", StyleNames.Heading2);
            if (diary.Count == 0)
            {
                section.AddParagraph("Keine Einträge.").Style = "Muted";
            }

            foreach (var entry in diary)
            {
                var head = section.AddParagraph();
                head.Format.KeepWithNext = true;
                head.Format.SpaceBefore = Unit.FromPoint(6);
                head.AddFormattedText(entry.Day.ToString("dddd, dd.MM.yyyy", German), TextFormat.Bold);
                var meta = new[]
                {
                    entry.StartTime is { } s ? $"{s:HH\\:mm}{(entry.EndTime is { } e ? $"–{e:HH\\:mm}" : string.Empty)} Uhr" : null,
                    entry.Weather is { } w ? Weather.GetValueOrDefault(w) : null,
                    entry.TemperatureCelsius is { } temp ? $"{temp} °C" : null,
                    entry.WorkersPresent is { } n ? $"{n} anwesend" : null,
                    entry.Crew, names.GetValueOrDefault(entry.AuthorUserId),
                }.Where(v => !string.IsNullOrWhiteSpace(v));
                section.AddParagraph(string.Join(" · ", meta)).Style = "Muted";
                section.AddParagraph(entry.Text);
            }
        }

        if (parts.Contains("pruefungen"))
        {
            section.AddParagraph("Prüfungen", StyleNames.Heading2);
            var initial = inspections.Where(i => i.Kind == InspectionKind.InitialInspection).ToList();
            var cm = inspections.Where(i => i.Kind == InspectionKind.CmMeasurement).ToList();
            if (inspections.Count == 0)
            {
                section.AddParagraph("Keine Prüfungen erfasst.").Style = "Muted";
            }

            foreach (var i in initial)
            {
                var title = section.AddParagraph();
                title.Format.KeepWithNext = true;
                title.AddFormattedText($"Erstbegehung am {PdfReport.DateTime(i.PerformedAt)}", TextFormat.Bold);
                title.AddText($" · {names.GetValueOrDefault(i.PerformedByUserId) ?? "?"}{(i.CorrectsInspectionId is null ? string.Empty : " · Korrektur")}");
                if (i.NotRequired)
                {
                    section.AddParagraph($"Nicht notwendig: {i.Remarks}");
                    AddNotes(section, i.Notes);
                    continue;
                }

                var table = AddTable(section, ("Prüfpunkt", 11.2, ParagraphAlignment.Left), ("Ergebnis", 6.0, ParagraphAlignment.Left));
                // Prüfpunkte in der Reihenfolge des Formulars (Stammdaten), ältere Punkte mit ihren festen Bezeichnungen.
                var schemaFields = await forms.FieldsAsync(FormSchemaKeys.Erstbegehung, cancellationToken);
                var titles = schemaFields.Where(f => f.Type == "check").Select(f => (f.Key, f.Label))
                    .Concat(CheckTitles.Select(c => (c.Key, c.Value))).Concat(i.Checks.Keys.Select(k => (k, k))).DistinctBy(c => c.Item1).ToList();
                foreach (var (key, text) in titles)
                {
                    if (i.Checks.TryGetValue(key, out var result))
                    {
                        var checkLabel = i.CheckRemarks.TryGetValue(key, out var remark) ? $"{text} – {remark}" : text;
                        var row = AddRow(table, checkLabel, result switch { CheckResult.Ok => "in Ordnung", CheckResult.NotOk => "Mangel", _ => "entfällt" });
                        if (result == CheckResult.NotOk)
                        {
                            row.Cells[1].Format.Font.Color = Color.FromRgb(0xAA, 0x08, 0x08);
                            row.Cells[1].Format.Font.Bold = true;
                        }
                    }
                }

                if (i.Values.Count > 0)
                {
                    var lists = await valueLists.GetAllAsync(cancellationToken);
                    var values = AddTable(section, ("Angabe", 11.2, ParagraphAlignment.Left), ("Wert", 6.0, ParagraphAlignment.Left));
                    foreach (var f in schemaFields.Where(f => f.Type != "check" && i.Values.ContainsKey(f.Key))
                        .Concat(i.Values.Keys.Where(k => schemaFields.All(f => f.Key != k)).Select(k => new FormFieldInfo(k, k, "text", null, null))))
                    {
                        var v = i.Values[f.Key];
                        var text = v.ValueKind switch
                        {
                            System.Text.Json.JsonValueKind.True => "ja",
                            System.Text.Json.JsonValueKind.False => "nein",
                            System.Text.Json.JsonValueKind.Number => $"{v.GetDecimal().ToString("0.##", PdfReport.German)}{(f.Unit is null ? "" : $" {f.Unit}")}",
                            _ => f.List is not null && lists.TryGetValue(f.List, out var l) ? l.FirstOrDefault(e => e.Key == v.GetString())?.Label ?? v.GetString() ?? "" : v.GetString() ?? "",
                        };
                        AddRow(values, f.Label, text);
                    }
                }

                if (!string.IsNullOrWhiteSpace(i.Remarks))
                {
                    section.AddParagraph($"Bemerkungen: {i.Remarks}");
                }

                AddNotes(section, i.Notes);
            }

            if (cm.Count > 0)
            {
                var p = section.AddParagraph();
                p.Format.SpaceBefore = Unit.FromPoint(8);
                p.AddFormattedText("CM-Messungen (Belegreife)", TextFormat.Bold);
                var table = AddTable(section, ("Datum", 3.0, ParagraphAlignment.Left), ("Messort", 4.2, ParagraphAlignment.Left),
                    ("Estrich", 3.4, ParagraphAlignment.Left), ("Messwert", 2.3, ParagraphAlignment.Right), ("Grenzwert", 2.3, ParagraphAlignment.Right),
                    ("Ergebnis", 2.0, ParagraphAlignment.Left));
                foreach (var i in cm)
                {
                    var row = AddRow(table, PdfReport.DateTime(i.PerformedAt), i.Location,
                        $"{(i.Binder == ScreedBinderKind.Cement ? "Zement (CT)" : "Calciumsulfat (CA)")}{(i.Heated == true ? ", beheizt" : string.Empty)}{(i.ThicknessMm is { } t ? $", {t} mm" : string.Empty)}",
                        $"{Number(i.MeasuredCmPercent, 1)} CM-%", i.LimitCmPercent is { } l ? $"{Number(l, 1)} CM-%" : null,
                        i.IsReadyForCovering switch { true => "belegreif", false => "nicht belegreif", _ => "–" });
                    row.Cells[5].Format.Font.Bold = true;
                }
            }
        }

        if (parts.Contains("maengel"))
        {
            section.AddParagraph("Aufgaben und Mängel", StyleNames.Heading2);
            if (workItems.Count == 0)
            {
                section.AddParagraph("Keine Aufgaben oder Mängel.").Style = "Muted";
            }
            else
            {
                var table = AddTable(section, ("Art", 1.8, ParagraphAlignment.Left), ("Beschreibung", 7.8, ParagraphAlignment.Left),
                    ("Fällig", 2.2, ParagraphAlignment.Left), ("Zuständig", 3.0, ParagraphAlignment.Left), ("Status", 2.4, ParagraphAlignment.Left));
                foreach (var w in workItems)
                {
                    AddRow(table, w.IsDefect ? "Mangel" : "Aufgabe", string.IsNullOrWhiteSpace(w.Description) ? w.Title : $"{w.Title}\n{w.Description}",
                        Date(w.DueDate), w.AssigneeName, w.CompletedAt is { } done ? $"{w.Status} {PdfReport.Date(DateOnly.FromDateTime(done.Date))}" : w.Status);
                }
            }
        }

        if (parts.Contains("fotos") && photos.Count > 0)
        {
            section.AddPageBreak();
            section.AddParagraph("Fotodokumentation", StyleNames.Heading1);
            var grid = section.AddTable();
            grid.Borders.Width = 0;
            grid.AddColumn(Unit.FromCentimeter(width / 2));
            grid.AddColumn(Unit.FromCentimeter(width / 2));
            MigraDoc.DocumentObjectModel.Tables.Row? row = null;
            var index = 0;
            foreach (var photo in photos)
            {
                var image = await PhotoAsync(photo, cancellationToken);
                if (index % 2 == 0)
                {
                    row = grid.AddRow();
                    row.KeepWith = 0;
                    row.TopPadding = Unit.FromPoint(4);
                    row.BottomPadding = Unit.FromPoint(8);
                }

                var cell = row!.Cells[index % 2];
                if (image is not null)
                {
                    var picture = cell.AddParagraph().AddImage(ImageSource(image));
                    picture.Width = Unit.FromCentimeter(width / 2 - 0.4);
                    picture.LockAspectRatio = true;
                }
                else
                {
                    cell.AddParagraph("(Vorschau nicht verfügbar)").Style = "Muted";
                }

                var caption = cell.AddParagraph();
                caption.Format.SpaceBefore = Unit.FromPoint(2);
                caption.AddFormattedText($"Foto {index + 1}", TextFormat.Bold);
                caption.AddText($" · {PdfReport.DateTime(photo.CapturedAt ?? photo.CreatedAt)}");
                if (!string.IsNullOrWhiteSpace(photo.Caption))
                {
                    caption.AddLineBreak();
                    caption.AddText(photo.Caption);
                }

                index++;
            }
        }

        if (inspectionId is null)
        {
            audit.Record("bauakte.report_printed", currentUser.UserId, "bauakte", bauakteId.ToString(),
                new Dictionary<string, string?> { ["parts"] = string.Join(",", parts.Order(StringComparer.Ordinal)), ["photos"] = photos.Count.ToString(German) });
            await db.SaveChangesAsync(cancellationToken);
        }
        var pdf = Render(document);
        var name = SafeName($"Bauakte {info.Number ?? string.Empty} {info.Title}".Trim());
        return (pdf, $"{name} ({clock.GetUtcNow():yyyy-MM-dd}).pdf");
    }

    /// <summary>Freie Notizen der Begehung, Absätze und Zeilenumbrüche bleiben erhalten.</summary>
    private static void AddNotes(Section section, string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return;
        }

        var heading = section.AddParagraph();
        heading.Format.SpaceBefore = Unit.FromPoint(4);
        heading.Format.KeepWithNext = true;
        heading.AddFormattedText("Notizen", TextFormat.Bold);
        foreach (var line in notes.Split('\n'))
        {
            section.AddParagraph(line.Length == 0 ? " " : line);
        }
    }

    private void Cover(Section section, BauakteReportInfo info, PdfCompany company, double width, int diary, int inspections, int work, int photos)
    {
        var spacer = section.AddParagraph();
        spacer.Format.SpaceBefore = Unit.FromCentimeter(2.2);
        var logo = section.AddParagraph().AddImage(ImageSource(Logo));
        logo.Width = Unit.FromCentimeter(8);
        logo.LockAspectRatio = true;
        var kind = section.AddParagraph("BAUAKTE");
        kind.Format.SpaceBefore = Unit.FromCentimeter(2.0);
        kind.Format.Font.Size = 10;
        kind.Format.Font.Color = Muted;
        kind.Format.Font.Bold = true;
        var title = section.AddParagraph(info.Title);
        title.Format.Font.Size = 24;
        title.Format.Font.Bold = true;
        title.Format.Font.Color = Brand;
        title.Format.SpaceAfter = Unit.FromPoint(4);
        var number = section.AddParagraph(string.Join(" · ", new[] { info.Number, StatusText.GetValueOrDefault(info.Status) }.Where(v => v is not null)));
        number.Format.Font.Size = 12;
        number.Format.SpaceAfter = Unit.FromCentimeter(1.2);
        AddFacts(section, width,
            ("Baustelle", Address(info.Street, info.PostalCode, info.City)),
            ("Kunde", info.Customer is { } c ? string.Join(", ", new[] { c.Name, Address(c.Street, c.PostalCode, c.City) }.Where(v => !string.IsNullOrWhiteSpace(v))) : null),
            ("Ausführung", Period(info.PlannedStart, info.PlannedEnd)),
            ("Fläche", info.Rooms.Count > 0 ? $"{Number(info.Rooms.Sum(r => r.AreaSquareMeters ?? 0))} m² in {info.Rooms.Count} Räumen" : null),
            ("Inhalt", $"{diary} Tagesberichte · {inspections} Prüfungen · {work} Aufgaben/Mängel · {photos} Fotos"),
            ("Angelegt von", $"{info.CreatedByName} am {PdfReport.DateTime(info.CreatedAt)}"),
            ("Erstellt", PdfReport.DateTime(clock.GetUtcNow())));
        var from = section.AddParagraph();
        from.Format.SpaceBefore = Unit.FromCentimeter(2.5);
        from.AddFormattedText(company.Name, TextFormat.Bold);
        foreach (var line in new[] { company.AddressLine, company.ContactLine }.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            from.AddLineBreak();
            from.AddText(line);
        }
    }

    /// <summary>Foto als JPEG für das PDF (Vorschau-Variante bzw. Original, verkleinert); null, wenn nicht lesbar (z. B. HEIC ohne Variante).</summary>
    private async Task<byte[]?> PhotoAsync(FileRecord photo, CancellationToken cancellationToken)
    {
        try
        {
            var variant = storage.VariantPath(photo.Id, "display");
            var path = File.Exists(variant) ? variant : photo.StorageKey is { } key ? storage.PathOf(key) : null;
            if (path is null || !File.Exists(path))
            {
                return null;
            }

            var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
            using var image = Image.ThumbnailBuffer(bytes, 1200, height: 1200, size: Enums.Size.Down);
            using var flat = image.HasAlpha() ? image.Flatten(background: [255, 255, 255]) : image.Copy();
            return flat.JpegsaveBuffer(q: 80, keep: Enums.ForeignKeep.None);
        }
        catch (Exception exception) when (exception is VipsException or IOException)
        {
            logger.LogWarning(exception, "Foto {FileId} konnte nicht in das PDF übernommen werden.", photo.Id);
            return null;
        }
    }

    private static string? Address(string? street, string? postalCode, string? city)
    {
        var place = string.Join(' ', new[] { postalCode, city }.Where(v => !string.IsNullOrWhiteSpace(v)));
        var text = string.Join(", ", new[] { street, place }.Where(v => !string.IsNullOrWhiteSpace(v)));
        return text.Length == 0 ? null : text;
    }

    private static string? Period(DateOnly? from, DateOnly? to) =>
        from is null && to is null ? null : $"{Date(from)} – {Date(to)}";

    private static string SafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '"']).ToHashSet();
        return new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
    }
}
