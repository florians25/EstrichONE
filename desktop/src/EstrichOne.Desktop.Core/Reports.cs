using System.Globalization;
using System.Text;
using EstrichOne.Client;

namespace EstrichOne.Desktop;

/// <summary>Foto für einen Bericht: Bild als data-URI (JPEG/PNG/WebP) und Bildunterschrift.</summary>
public sealed record ReportPhoto(string DataUri, string Caption);

/// <summary>Was in den Baustellenbericht gehört.</summary>
public sealed record SiteReportOptions(
    bool Rooms = true, bool Work = true, bool Notes = true, bool Diary = true, bool Inspections = true, bool Photos = true, bool Assignments = true);

/// <summary>Alle Daten des Baustellenberichts (bereits geladen).</summary>
public sealed record SiteReportData(
    BauakteDetail Bauakte, Company? Company, Contact? Customer, string? Responsible, IReadOnlyList<BauakteMember> Members, RoomOverview? Rooms,
    IReadOnlyList<WorkItem> Work, IReadOnlyList<Note> Notes, IReadOnlyList<DiaryEntry> Diary, IReadOnlyList<Inspection> Inspections,
    IReadOnlyList<Assignment> Assignments, IReadOnlyList<ReportPhoto> Photos, IReadOnlyDictionary<Guid, string> People, DateTimeOffset PrintedAt);

/// <summary>
/// Druckvorlagen als HTML (A4): Baustellenbericht, Erstbegehungsprotokoll, Bestellschein, Wochen- und Arbeitsplan.
/// Die Windows-App zeigt sie in WebView2, druckt über den Windows-Druckdialog oder speichert direkt als PDF.
/// </summary>
public static class Reports
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private const string Css = """
        @page { size: A4; margin: 14mm 14mm 16mm 14mm; }
        * { box-sizing: border-box; }
        body { font-family: 'Segoe UI', Arial, sans-serif; font-size: 10pt; color: #1b1f24; margin: 0; }
        .sheet { max-width: 186mm; margin: 0 auto; padding: 10px 0 24px; }
        header.brand { display: flex; justify-content: space-between; align-items: flex-end; border-bottom: 3px solid #0B2D5B; padding-bottom: 8px; margin-bottom: 14px; }
        header.brand .company { font-size: 14pt; font-weight: 700; color: #0B2D5B; }
        header.brand .small { font-size: 8.5pt; color: #5b6573; }
        header.brand .doc { text-align: right; }
        header.brand .doc .kind { font-size: 9pt; text-transform: uppercase; letter-spacing: .08em; color: #3CCB7F; font-weight: 700; }
        header.brand .doc .title { font-size: 16pt; font-weight: 700; color: #0B2D5B; }
        h2 { font-size: 11pt; color: #0B2D5B; border-bottom: 1px solid #d6dde8; padding-bottom: 3px; margin: 18px 0 8px; page-break-after: avoid; }
        .facts { display: grid; grid-template-columns: repeat(3, 1fr); gap: 6px 16px; }
        .facts.two { grid-template-columns: repeat(2, 1fr); }
        .fact .l { font-size: 7.5pt; text-transform: uppercase; letter-spacing: .06em; color: #6b7684; }
        .fact .v { font-weight: 600; white-space: pre-wrap; }
        table { width: 100%; border-collapse: collapse; page-break-inside: auto; }
        th { text-align: left; font-size: 8pt; text-transform: uppercase; letter-spacing: .04em; color: #0B2D5B; background: #eef3fa; padding: 5px 6px; }
        td { padding: 5px 6px; border-bottom: 1px solid #e6e9ef; vertical-align: top; }
        tr { page-break-inside: avoid; }
        td.num, th.num { text-align: right; font-variant-numeric: tabular-nums; }
        tfoot td { font-weight: 700; border-top: 2px solid #0B2D5B; border-bottom: none; }
        .badge { display: inline-block; padding: 1px 8px; border-radius: 10px; font-size: 8pt; font-weight: 600; }
        .muted { color: #6b7684; }
        .pre { white-space: pre-wrap; }
        .note { border-left: 3px solid #8EC5FF; padding: 4px 8px; margin-bottom: 6px; background: #f6f9fd; page-break-inside: avoid; }
        .note.pinned { border-left-color: #EA580C; }
        .photos { display: grid; grid-template-columns: repeat(3, 1fr); gap: 8px; }
        .photos figure { margin: 0; page-break-inside: avoid; }
        .photos img { width: 100%; height: 44mm; object-fit: cover; border-radius: 4px; border: 1px solid #e6e9ef; }
        .photos figcaption { font-size: 7.5pt; color: #5b6573; margin-top: 2px; }
        .box { border: 1px solid #d6dde8; border-radius: 6px; padding: 8px 10px; }
        .hint { padding: 5px 8px; border-radius: 4px; margin-bottom: 4px; background: #fff7e6; border-left: 3px solid #D97706; }
        .sign { display: grid; grid-template-columns: 1fr 1fr; gap: 24px; margin-top: 36px; }
        .sign div { border-top: 1px solid #1b1f24; padding-top: 4px; font-size: 8pt; color: #5b6573; }
        footer { margin-top: 20px; font-size: 7.5pt; color: #6b7684; border-top: 1px solid #e6e9ef; padding-top: 6px; display: flex; justify-content: space-between; }
        .lane { display: inline-block; width: 9px; height: 9px; border-radius: 50%; margin-right: 5px; vertical-align: middle; }
        @media screen { body { background: #e9edf3; } .sheet { background: #fff; padding: 14mm; margin: 16px auto; box-shadow: 0 2px 12px rgba(11,45,91,.15); } }
        """;

    /// <summary>HTML-sicher (nur &amp;, &lt;, &gt;, Anführungszeichen); Umlaute bleiben lesbar (UTF-8).</summary>
    public static string Encode(string? text) => (text ?? string.Empty).Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal).Replace("\"", "&quot;", StringComparison.Ordinal).Replace("'", "&#39;", StringComparison.Ordinal);

    private static string Or(string? text) => string.IsNullOrWhiteSpace(text) ? "–" : Encode(text);

    private static string Badge(string text, string color) =>
        $"<span class=\"badge\" style=\"background:{color}22;color:{color}\">{Encode(text)}</span>";

    private static string Fact(string label, string valueHtml) => $"<div class=\"fact\"><div class=\"l\">{Encode(label)}</div><div class=\"v\">{valueHtml}</div></div>";

    private static string Document(string title, string body) =>
        $"<!DOCTYPE html><html lang=\"de\"><head><meta charset=\"utf-8\"><title>{Encode(title)}</title><style>{Css}</style></head><body><div class=\"sheet\">{body}</div></body></html>";

    private static string Header(Company? company, string kind, string title, string? sub = null)
    {
        var address = company is null ? string.Empty : string.Join(" · ", new[] { company.Street, string.Join(' ', new[] { company.PostalCode, company.City }.Where(s => !string.IsNullOrWhiteSpace(s))) }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        var contact = company is null ? string.Empty : string.Join(" · ", new[] { company.Phone is null ? null : $"Tel. {company.Phone}", company.Email, company.Website }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        return $"""
            <header class="brand"><div><div class="company">{Encode(company?.Name ?? "EstrichONE")}</div><div class="small">{Encode(address)}</div><div class="small">{Encode(contact)}</div></div>
            <div class="doc"><div class="kind">{Encode(kind)}</div><div class="title">{Encode(title)}</div>{(sub is null ? string.Empty : $"<div class=\"small\">{Encode(sub)}</div>")}</div></header>
            """;
    }

    private static string Footer(DateTimeOffset printedAt, string what) =>
        $"<footer><span>{Encode(what)}</span><span>Erstellt mit EstrichONE am {Encode(Labels.DateTime(printedAt))}</span></footer>";

    /// <summary>Baustellenbericht: Stammdaten, Räume mit Summen, Aufgaben/Mängel, Notizen, Bautagebuch, Prüfungen, Einsätze, Fotos.</summary>
    public static string SiteReport(SiteReportData data, SiteReportOptions options)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(options);
        var b = data.Bauakte;
        string Name(Guid id) => data.People.GetValueOrDefault(id, "–");
        var html = new StringBuilder();
        html.Append(Header(data.Company, "Baustellenbericht", $"{b.Number ?? "neu"} · {b.Title}", $"Stand {Labels.DateTime(data.PrintedAt)}"));

        html.Append("<h2>Bauvorhaben</h2><div class=\"facts\">");
        html.Append(Fact("Bauakte", Or(b.Number)));
        html.Append(Fact("Status", Badge(Labels.Of(b.Status), Palette.Of(b.Status))));
        html.Append(Fact("Zuständig", Or(data.Responsible)));
        html.Append(Fact("Baustelle", Encode(Labels.Address(b.Street, b.PostalCode, b.City))));
        html.Append(Fact("Ausführung", b.PlannedStart is null && b.PlannedEnd is null ? "–" : $"{Labels.Date(b.PlannedStart)} – {Labels.Date(b.PlannedEnd)}"));
        html.Append(Fact("Mitarbeiter", Or(string.Join(", ", data.Members.Select(m => m.DisplayName)))));
        html.Append(Fact("Kunde", Or(data.Customer?.Name)));
        html.Append(Fact("Kunde Kontakt", Or(string.Join(" · ", new[] { data.Customer?.Phone, data.Customer?.Email }.Where(s => !string.IsNullOrWhiteSpace(s))))));
        html.Append(Fact("Vor Ort", Or(string.Join(" · ", new[] { b.SiteContactName, b.SiteContactPhone }.Where(s => !string.IsNullOrWhiteSpace(s))))));
        html.Append("</div>");
        if (!string.IsNullOrWhiteSpace(b.SiteNotes))
        {
            html.Append(CultureInfo.InvariantCulture, $"<div class=\"hint\" style=\"margin-top:8px\"><strong>Hinweise / Behinderungen:</strong> <span class=\"pre\">{Encode(b.SiteNotes)}</span></div>");
        }

        if (!string.IsNullOrWhiteSpace(b.Description))
        {
            html.Append(CultureInfo.InvariantCulture, $"<h2>Beschreibung</h2><div class=\"pre\">{Encode(b.Description)}</div>");
        }

        if (options.Rooms && data.Rooms is { Rooms.Count: > 0 } rooms)
        {
            html.Append("<h2>Räume &amp; Flächen</h2><table><thead><tr><th>Bereich</th><th>Raum</th><th>Geschoss</th><th class=\"num\">Fläche</th><th>Estrich</th><th class=\"num\">Dicke</th><th class=\"num\">Dämmung</th><th>FBH</th><th>Status</th></tr></thead><tbody>");
            foreach (var r in rooms.Rooms.OrderBy(r => rooms.Sections.FirstOrDefault(s => s.Id == r.SectionId)?.SortOrder ?? int.MaxValue).ThenBy(r => r.Name, StringComparer.CurrentCulture))
            {
                html.Append(CultureInfo.InvariantCulture, $"<tr><td>{Or(rooms.Sections.FirstOrDefault(s => s.Id == r.SectionId)?.Name)}</td><td>{Encode(r.Name)}</td><td>{Or(r.Floor)}</td>");
                html.Append(CultureInfo.InvariantCulture, $"<td class=\"num\">{(r.AreaSquareMeters is { } a ? Labels.Area(a) : "–")}</td><td>{Or(r.ScreedTypeName)}</td>");
                html.Append(CultureInfo.InvariantCulture, $"<td class=\"num\">{(r.ThicknessMillimeters is { } t ? $"{t} mm" : "–")}</td><td class=\"num\">{(r.InsulationMillimeters is { } i ? $"{i} mm" : "–")}</td>");
                html.Append(CultureInfo.InvariantCulture, $"<td>{(r.UnderfloorHeating ? "ja" : "nein")}</td><td>{Badge(Labels.Of(r.Status), Palette.Of(r.Status))}</td></tr>");
            }

            html.Append(CultureInfo.InvariantCulture, $"</tbody><tfoot><tr><td colspan=\"3\">{rooms.Rooms.Count} Räume · davon beheizt {Labels.Area(rooms.HeatedAreaSquareMeters)}</td><td class=\"num\">{Labels.Area(rooms.TotalAreaSquareMeters)}</td><td colspan=\"5\"></td></tr></tfoot></table>");
        }

        if (options.Work && data.Work.Count > 0)
        {
            html.Append("<h2>Aufgaben &amp; Mängel</h2><table><thead><tr><th>Art</th><th>Titel</th><th>Fällig</th><th>Zuständig</th><th>Status</th></tr></thead><tbody>");
            foreach (var w in data.Work.OrderBy(w => w.Status == WorkItemStatus.Done).ThenBy(w => w.DueDate ?? DateOnly.MaxValue))
            {
                html.Append(CultureInfo.InvariantCulture, $"<tr><td>{Badge(Labels.Of(w.Kind), Palette.Of(w.Kind))}</td><td>{Encode(w.Title)}{(w.Description is null ? string.Empty : $"<div class=\"muted pre\">{Encode(w.Description)}</div>")}</td>");
                html.Append(CultureInfo.InvariantCulture, $"<td>{Labels.Date(w.DueDate)}</td><td>{(w.AssigneeUserId is { } u ? Encode(Name(u)) : "–")}</td><td>{Encode(Labels.Of(w.Status))}</td></tr>");
            }

            html.Append("</tbody></table>");
        }

        if (options.Assignments && data.Assignments.Count > 0)
        {
            html.Append("<h2>Einsätze</h2><table><thead><tr><th>Tag</th><th>Zeit</th><th>Terminart</th><th>Tätigkeit</th><th>Kolonne / Mitarbeiter</th></tr></thead><tbody>");
            foreach (var a in data.Assignments.OrderBy(a => a.Day).ThenBy(a => a.StartTime))
            {
                html.Append(CultureInfo.InvariantCulture, $"<tr><td>{Encode(Labels.ShortDay(a.Day))}</td><td>{Encode(PlanningBoard.TimeRange(a))}</td><td>{Badge(Labels.Of(a.Kind), Palette.Of(a.Kind))}</td>");
                html.Append(CultureInfo.InvariantCulture, $"<td>{Encode(a.Activity)}</td><td>{Or(string.Join(", ", new[] { a.CrewName }.Concat(a.People.Select(p => p.Name)).Where(s => !string.IsNullOrWhiteSpace(s))))}</td></tr>");
            }

            html.Append("</tbody></table>");
        }

        if (options.Inspections && data.Inspections.Count > 0)
        {
            html.Append(InspectionSection(data.Inspections, data.Rooms, Name));
        }

        if (options.Diary && data.Diary.Count > 0)
        {
            html.Append("<h2>Bautagebuch</h2>");
            foreach (var d in data.Diary.OrderByDescending(d => d.Day).ThenByDescending(d => d.CreatedAt))
            {
                html.Append(CultureInfo.InvariantCulture, $"<div class=\"note\"><strong>{Encode(Labels.LongDay(d.Day))}</strong> <span class=\"muted\">{Encode(WeatherLine(d))}</span>");
                html.Append(CultureInfo.InvariantCulture, $"<div class=\"pre\">{Encode(d.Text)}</div><div class=\"muted\" style=\"font-size:8pt\">{Encode(Name(d.AuthorUserId))}</div></div>");
            }
        }

        if (options.Notes && data.Notes.Count > 0)
        {
            html.Append("<h2>Notizen</h2>");
            foreach (var n in data.Notes.OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.CreatedAt))
            {
                html.Append(CultureInfo.InvariantCulture, $"<div class=\"note{(n.IsPinned ? " pinned" : string.Empty)}\"><div class=\"muted\" style=\"font-size:8pt\">{Encode(Name(n.AuthorUserId))} · {Encode(Labels.DateTime(n.CreatedAt))}{(n.IsPinned ? " · angeheftet" : string.Empty)}</div><div class=\"pre\">{Encode(n.Text)}</div></div>");
            }
        }

        if (options.Photos && data.Photos.Count > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<h2>Fotos ({data.Photos.Count})</h2><div class=\"photos\">");
            foreach (var p in data.Photos)
            {
                html.Append(CultureInfo.InvariantCulture, $"<figure><img src=\"{p.DataUri}\" alt=\"\"><figcaption>{Encode(p.Caption)}</figcaption></figure>");
            }

            html.Append("</div>");
        }

        html.Append(Footer(data.PrintedAt, $"Baustellenbericht {b.Number ?? string.Empty} {b.Title}"));
        return Document($"Baustellenbericht {b.Number} {b.Title}", html.ToString());
    }

    public static string WeatherLine(DiaryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return string.Join(" · ", new[]
        {
            Labels.Weather(entry.Weather), entry.TemperatureCelsius is { } t ? $"{t} °C" : null, entry.WorkersPresent is { } w ? $"{w} anwesend" : null,
        }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static string InspectionSection(IReadOnlyList<Inspection> inspections, RoomOverview? rooms, Func<Guid, string> name)
    {
        var html = new StringBuilder();
        var initial = inspections.Where(i => i.Kind == InspectionKind.InitialInspection).OrderByDescending(i => i.PerformedAt).ToList();
        var cm = inspections.Where(i => i.Kind == InspectionKind.CmMeasurement).OrderByDescending(i => i.PerformedAt).ToList();
        if (initial.FirstOrDefault() is { } latest)
        {
            var screeds = rooms?.Rooms.Select(r => r.ScreedTypeName).Where(s => s is not null).Distinct().ToList() ?? [];
            html.Append("<h2>Erstbegehung</h2><div class=\"facts\">");
            html.Append(Fact("Ergebnis", Badge(Labels.InspectionResult(latest), latest.Checks.Values.Contains(CheckResult.NotOk) ? Palette.Amber : Palette.Success)));
            html.Append(Fact("Prüfer", Encode(name(latest.PerformedByUserId))));
            html.Append(Fact("Datum", Encode(Labels.DateTime(latest.PerformedAt))));
            html.Append(Fact("Estrichart", Or(string.Join(", ", screeds))));
            html.Append(Fact("Fläche", rooms is null ? "–" : Encode(Labels.Area(rooms.TotalAreaSquareMeters))));
            html.Append(Fact("Fußbodenheizung", rooms is { Rooms.Count: > 0 } ? (rooms.Rooms.Any(r => r.UnderfloorHeating) ? "ja" : "nein") : "unbekannt"));
            html.Append("</div><table style=\"margin-top:8px\"><thead><tr><th>Prüfpunkt</th><th>Ergebnis</th></tr></thead><tbody>");
            foreach (var (key, label) in Labels.InspectionChecks)
            {
                var value = latest.NotRequired ? "–" : latest.Checks.TryGetValue(key, out var r)
                    ? Badge(Labels.Of(r), r == CheckResult.Ok ? Palette.Success : r == CheckResult.NotOk ? Palette.Amber : Palette.Slate) : "–";
                html.Append(CultureInfo.InvariantCulture, $"<tr><td>{Encode(label)}</td><td>{value}</td></tr>");
            }

            html.Append(CultureInfo.InvariantCulture, $"</tbody></table><div class=\"box\" style=\"margin-top:8px\"><div class=\"fact\"><div class=\"l\">Bemerkungen / Mängel</div><div class=\"v pre\">{Or(latest.Remarks)}</div></div></div>");
        }

        if (cm.Count > 0)
        {
            html.Append("<h2>CM-Messungen</h2><table><thead><tr><th>Datum</th><th>Messstelle</th><th>Estrich</th><th class=\"num\">Dicke</th><th class=\"num\">CM-%</th><th class=\"num\">Grenzwert</th><th>Ergebnis</th><th>Prüfer</th></tr></thead><tbody>");
            foreach (var i in cm)
            {
                html.Append(CultureInfo.InvariantCulture, $"<tr><td>{Encode(Labels.DateTime(i.PerformedAt))}</td><td>{Or(i.Location)}</td><td>{Encode(Labels.Binder(i.Binder))}{(i.Heated == true ? ", beheizt" : string.Empty)}</td>");
                html.Append(CultureInfo.InvariantCulture, $"<td class=\"num\">{(i.ThicknessMm is { } t ? $"{t} mm" : "–")}</td><td class=\"num\">{Labels.Cm(i.MeasuredCmPercent)}</td><td class=\"num\">{Labels.Cm(i.LimitCmPercent)}</td>");
                html.Append(CultureInfo.InvariantCulture, $"<td>{Badge(i.IsReadyForCovering == true ? "Belegreif" : "Noch nicht belegreif", i.IsReadyForCovering == true ? Palette.Success : Palette.Amber)}</td><td>{Encode(name(i.PerformedByUserId))}</td></tr>");
            }

            html.Append("</tbody></table>");
        }

        return html.ToString();
    }

    /// <summary>Erstbegehungsprotokoll mit CM-Messungen und Unterschriftenfeldern.</summary>
    public static string InspectionProtocol(BauakteDetail bauakte, Company? company, Contact? customer, IReadOnlyList<Inspection> inspections,
        RoomOverview? rooms, IReadOnlyDictionary<Guid, string> people, DateTimeOffset printedAt)
    {
        ArgumentNullException.ThrowIfNull(bauakte);
        ArgumentNullException.ThrowIfNull(people);
        var html = new StringBuilder();
        html.Append(Header(company, "Protokoll", "Erstbegehung & CM-Messung", $"{bauakte.Number ?? "neu"} · {bauakte.Title}"));
        html.Append("<div class=\"facts\">");
        html.Append(Fact("Bauvorhaben", Encode(bauakte.Title)));
        html.Append(Fact("Baustelle", Encode(Labels.Address(bauakte.Street, bauakte.PostalCode, bauakte.City))));
        html.Append(Fact("Kunde", Or(customer?.Name)));
        html.Append("</div>");
        html.Append(InspectionSection(inspections, rooms, id => people.GetValueOrDefault(id, "–")));
        html.Append("<div class=\"sign\"><div>Ort, Datum, Unterschrift Prüfer</div><div>Ort, Datum, Unterschrift Auftraggeber</div></div>");
        html.Append(Footer(printedAt, $"Protokoll {bauakte.Number} {bauakte.Title}"));
        return Document($"Protokoll {bauakte.Number} {bauakte.Title}", html.ToString());
    }

    /// <summary>Bestellschein im Briefformat (wie im Web).</summary>
    public static string OrderSheet(MaterialOrder order, Company? company, DateTimeOffset printedAt)
    {
        ArgumentNullException.ThrowIfNull(order);
        var priced = order.Lines.Any(l => l.UnitPrice is not null);
        var delivery = Labels.Address(order.DeliveryStreet, order.DeliveryPostalCode, order.DeliveryCity);
        var html = new StringBuilder();
        html.Append(Header(company, "Bestellung", order.Number, $"Datum {Labels.Date(DateOnly.FromDateTime((order.OrderedAt ?? order.UpdatedAt).ToLocalTime().DateTime))}"));
        html.Append("<div class=\"facts two\">");
        html.Append(Fact("Lieferant", $"{Encode(order.SupplierName)}{(order.SupplierCustomerNumber is null ? string.Empty : $"<div class=\"muted\">Unsere Kundennummer: {Encode(order.SupplierCustomerNumber)}</div>")}"));
        html.Append(Fact("Liefertermin", Encode((order.DeliveryDate is { } d ? Labels.ShortDay(d) + " " + d.Year.ToString(CultureInfo.InvariantCulture) : "nach Absprache") + (order.DeliveryTime is { } t ? $", {t}" : string.Empty))));
        html.Append(Fact("Lieferadresse", delivery == "–" ? "wird telefonisch mitgeteilt" : Encode(delivery)));
        html.Append(Fact("Ansprechpartner vor Ort", Or(order.SiteContact)));
        html.Append(Fact("Bauvorhaben", order.BauakteNumber is null ? "–" : Encode($"{order.BauakteNumber} {order.BauakteTitle}")));
        html.Append(Fact("Besteller", Or(order.CreatedByName)));
        html.Append("</div><h2>Positionen</h2><table><thead><tr><th>Pos.</th><th>Art.-Nr.</th><th>Bezeichnung</th><th class=\"num\">Menge</th><th>Einheit</th>");
        html.Append(priced ? "<th class=\"num\">Preis</th><th class=\"num\">Summe</th>" : string.Empty).Append("</tr></thead><tbody>");
        foreach (var l in order.Lines.OrderBy(l => l.Position))
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td>{l.Position}</td><td>{Encode(l.ArticleNumber)}</td><td>{Encode(l.Description)}</td><td class=\"num\">{Labels.Quantity(l.Quantity)}</td><td>{Encode(l.Unit)}</td>");
            html.Append(priced ? $"<td class=\"num\">{(l.UnitPrice is null ? string.Empty : Labels.Money(l.UnitPrice))}</td><td class=\"num\">{(l.Total is null ? string.Empty : Labels.Money(l.Total))}</td>" : string.Empty);
            html.Append("</tr>");
        }

        if (order.Lines.Count == 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tr><td colspan=\"{(priced ? 7 : 5)}\" class=\"muted\">Noch keine Positionen.</td></tr>");
        }

        html.Append("</tbody>");
        if (priced && order.Total is not null)
        {
            html.Append(CultureInfo.InvariantCulture, $"<tfoot><tr><td colspan=\"6\">{(order.Lines.All(l => l.UnitPrice is not null) ? "Summe netto" : "Summe netto der Positionen mit Preis")}</td><td class=\"num\">{Labels.Money(order.Total)}</td></tr></tfoot>");
        }

        html.Append("</table>");
        if (!string.IsNullOrWhiteSpace(order.Notes))
        {
            html.Append(CultureInfo.InvariantCulture, $"<h2>Hinweise</h2><div class=\"pre\">{Encode(order.Notes)}</div>");
        }

        html.Append("<p style=\"margin-top:20px\">Bitte bestätigen Sie uns den Liefertermin. Vielen Dank.</p>");
        html.Append(Footer(printedAt, $"Bestellung {order.Number}"));
        return Document($"Bestellung {order.Number}", html.ToString());
    }

    /// <summary>Bestellung als Klartext (E-Mail an den Lieferanten, Zwischenablage).</summary>
    public static string OrderText(MaterialOrder order, Company? company)
    {
        ArgumentNullException.ThrowIfNull(order);
        var lines = new List<string?>
        {
            "Guten Tag,", string.Empty,
            $"hiermit bestellen wir{(order.SupplierCustomerNumber is null ? string.Empty : $" (Kundennummer {order.SupplierCustomerNumber})")}:", string.Empty,
        };
        lines.AddRange(order.Lines.OrderBy(l => l.Position).Select(l =>
            $"{l.Position}. {Labels.Quantity(l.Quantity)} {l.Unit}  {l.Description}{(l.ArticleNumber is null ? string.Empty : $" (Art.-Nr. {l.ArticleNumber})")}"));
        var address = Labels.Address(order.DeliveryStreet, order.DeliveryPostalCode, order.DeliveryCity);
        lines.Add(string.Empty);
        lines.Add($"Liefertermin: {(order.DeliveryDate is { } d ? Labels.Date(d) : "nach Absprache")}{(order.DeliveryTime is { } t ? $", {t}" : string.Empty)}");
        lines.Add(address == "–" ? null : $"Lieferadresse: {address}");
        lines.Add(order.SiteContact is null ? null : $"Ansprechpartner vor Ort: {order.SiteContact}");
        lines.Add(order.BauakteNumber is null ? null : $"Bauvorhaben: {order.BauakteNumber} {order.BauakteTitle}".Trim());
        lines.Add(order.Notes is null ? null : $"{Environment.NewLine}Hinweise: {order.Notes}");
        lines.AddRange([string.Empty, "Bitte bestätigen Sie uns den Liefertermin.", string.Empty, "Mit freundlichen Grüßen", company?.Name ?? string.Empty,
            company?.Phone is null ? null : $"Tel. {company.Phone}"]);
        return string.Join(Environment.NewLine, lines.Where(l => l is not null));
    }

    /// <summary>Plantafel (Woche) bzw. Arbeitsplan als Druck: je Tag die Einsätze mit Zeit, Bauakte, Kolonne, Mitarbeitern.</summary>
    public static string Agenda(string title, Company? company, IReadOnlyList<DateOnly> days, IReadOnlyList<Assignment> assignments,
        IReadOnlyList<Absence> absences, DateTimeOffset printedAt)
    {
        ArgumentNullException.ThrowIfNull(days);
        ArgumentNullException.ThrowIfNull(assignments);
        ArgumentNullException.ThrowIfNull(absences);
        var html = new StringBuilder();
        html.Append(Header(company, "Planung", title, days.Count > 0 ? $"{Labels.Date(days[0])} – {Labels.Date(days[^1])}" : null));
        foreach (var day in days)
        {
            var items = assignments.Where(a => a.Day == day).OrderBy(a => a.StartTime ?? TimeOnly.MinValue).ThenBy(a => a.CrewName).ToList();
            var absent = absences.Where(a => a.From <= day && a.To >= day).ToList();
            html.Append(CultureInfo.InvariantCulture, $"<h2>{Encode(Labels.LongDay(day))} <span class=\"muted\" style=\"font-weight:400\">· {items.Count} {(items.Count == 1 ? "Einsatz" : "Einsätze")}</span></h2>");
            if (absent.Count > 0)
            {
                html.Append(CultureInfo.InvariantCulture, $"<div class=\"muted\" style=\"margin-bottom:6px\">Abwesend: {Encode(string.Join(", ", absent.Select(a => $"{a.UserName} ({Labels.Of(a.Kind)})")))}</div>");
            }

            if (items.Count == 0)
            {
                html.Append("<div class=\"muted\">Keine Einsätze.</div>");
                continue;
            }

            html.Append("<table><thead><tr><th>Zeit</th><th>Bauvorhaben</th><th>Tätigkeit</th><th>Kolonne</th><th>Mitarbeiter / Geräte</th></tr></thead><tbody>");
            foreach (var a in items)
            {
                html.Append(CultureInfo.InvariantCulture, $"<tr><td>{Encode(PlanningBoard.TimeRange(a))}<div>{Badge(Labels.Of(a.Kind), Palette.Of(a.Kind))}</div></td>");
                html.Append(CultureInfo.InvariantCulture, $"<td><strong>{Encode(a.BauakteNumber)} {Encode(a.BauakteTitle)}</strong><div class=\"muted\">{Encode(a.BauakteAddress)}</div></td>");
                html.Append(CultureInfo.InvariantCulture, $"<td>{Encode(a.Activity)}{(a.Notes is null ? string.Empty : $"<div class=\"muted pre\">{Encode(a.Notes)}</div>")}{(a.Warnings.Count == 0 ? string.Empty : $"<div style=\"color:{Palette.Amber}\">⚠ {Encode(string.Join(" ", a.Warnings))}</div>")}</td>");
                html.Append(CultureInfo.InvariantCulture, $"<td>{(a.CrewName is null ? "–" : $"<span class=\"lane\" style=\"background:{Encode(a.CrewColor ?? Palette.Slate)}\"></span>{Encode(a.CrewName)}")}</td>");
                html.Append(CultureInfo.InvariantCulture, $"<td>{Or(string.Join(", ", a.People.Select(p => p.Name)))}<div class=\"muted\">{Encode(string.Join(", ", a.Resources.Select(r => r.Name)))}</div></td></tr>");
            }

            html.Append("</tbody></table>");
        }

        html.Append(Footer(printedAt, title));
        return Document(title, html.ToString());
    }

    /// <summary>Bild als data-URI für Berichte.</summary>
    public static string DataUri(byte[] bytes, string contentType)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";
    }
}
