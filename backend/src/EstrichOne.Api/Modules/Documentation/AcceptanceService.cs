using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Pdf;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Mail.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using EstrichOne.Api.Modules.Work.Exports;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetVips;

namespace EstrichOne.Api.Modules.Documentation;

/// <summary>Ablauf der Abnahme: erfassen, Kundenlink, Freigabe, PDF ablegen, Bauakte ggf. abschließen.</summary>
public sealed class AcceptanceService(
    EstrichOneDbContext db,
    IAuditLog audit,
    IBauakteReportSource bauakten,
    IWorkReportSource work,
    IPdfBranding branding,
    IBauakteAutoStatus autoStatus,
    ISystemSettings settings,
    IMailService mail,
    FileService files,
    LocalFileStorage storage,
    IOptions<EstrichOneOptions> options,
    TimeProvider clock,
    ILogger<AcceptanceService> logger)
{
    private const string EntityType = "bauakte";

    /// <summary>Nach dem Anlegen: Verlauf; mit beiden Unterschriften sofort abschließen (PDF, ggf. Akte abschließen).</summary>
    public async Task RecordCreatedAsync(AcceptanceProtocol protocol, Guid actor, CancellationToken cancellationToken)
    {
        audit.Record("bauakte.acceptance_added", actor, EntityType, protocol.BauakteId.ToString(), new Dictionary<string, string?>
        {
            ["result"] = protocol.Result.ToString(), ["signed"] = protocol.IsSigned ? "true" : "false",
        });
        await db.SaveChangesAsync(cancellationToken);
        if (protocol.IsSigned)
        {
            await FinalizeAsync(protocol, actor, cancellationToken);
        }
    }

    /// <summary>Freigabelink für den Kunden; mit E-Mail-Adresse und eingerichtetem Konto wird er verschickt.</summary>
    public async Task<(string Url, DateTimeOffset ExpiresAt, bool Emailed)> IssueLinkAsync(AcceptanceProtocol protocol, string? email, string baseUrl,
        Guid actor, CancellationToken cancellationToken)
    {
        var days = (await settings.GetAsync(cancellationToken)).Bauakte.AcceptanceLinkDays;
        var expires = clock.GetUtcNow().AddDays(days);
        var token = protocol.IssueCustomerLink(expires, email);
        var url = $"{(options.Value.PublicUrl?.ToString() ?? baseUrl).TrimEnd('/')}/abnahme/{token}";
        var emailed = false;
        if (!string.IsNullOrWhiteSpace(email))
        {
            var info = await bauakten.GetAsync(protocol.BauakteId, cancellationToken);
            var name = protocol.ClientName ?? info?.Customer?.Name ?? "Sehr geehrte Damen und Herren";
            var title = info is null ? "Ihr Bauvorhaben" : $"{info.Number} {info.Title}".Trim();
            var deadline = PdfReport.DateTime(expires);
            var values = new Dictionary<string, string?> { ["name"] = name, ["bauakte"] = title, ["link"] = url, ["frist"] = deadline };
            Guid? queued;
            try
            {
                queued = await mail.QueueTemplateAsync(MailTemplateKeys.AcceptanceCustomerApproval, email, values, $"bauakte:{protocol.BauakteId}", null, cancellationToken);
            }
            catch (Exception exception) when (exception is KeyNotFoundException or InvalidOperationException)
            {
                // Vorlage noch nicht vorhanden: einfache Mail.
                queued = await mail.QueueAsync(new MailRequest(email, $"Abnahmeprotokoll zur Freigabe: {title}",
                    $"Guten Tag {name},\n\nbitte prüfen Sie das Abnahmeprotokoll für „{title}“ und geben Sie es frei:\n{url}\n\nDer Link ist gültig bis {deadline}.\n",
                    "acceptance", $"bauakte:{protocol.BauakteId}"), cancellationToken);
            }

            emailed = queued is not null;
        }

        audit.Record("bauakte.acceptance_link_sent", actor, EntityType, protocol.BauakteId.ToString(), new Dictionary<string, string?>
        {
            ["emailed"] = emailed ? "true" : "false", ["expires"] = expires.ToString("O"),
        });
        await db.SaveChangesAsync(cancellationToken);
        return (url, expires, emailed);
    }

    /// <summary>Entscheidung des Kunden über den Link (ohne Anmeldung). Mit Freigabe wird das Protokoll abgeschlossen.</summary>
    public async Task DecideAsync(AcceptanceProtocol protocol, bool approve, string name, string? comment, string? ip, byte[]? signature,
        CancellationToken cancellationToken)
    {
        protocol.Decide(approve, name, comment, ip, clock.GetUtcNow(), signature);
        audit.Record(approve ? "bauakte.acceptance_customer_approved" : "bauakte.acceptance_customer_rejected", null, EntityType,
            protocol.BauakteId.ToString(), new Dictionary<string, string?> { ["name"] = protocol.CustomerDecisionName, ["comment"] = protocol.CustomerComment, ["ip"] = ip });
        await db.SaveChangesAsync(cancellationToken);
        if (approve)
        {
            await FinalizeAsync(protocol, null, cancellationToken);
        }
    }

    /// <summary>PDF erzeugen, unter „Abnahmen“ ablegen und – ohne Vorbehalt und offene Mängel – die Bauakte abschließen.</summary>
    public async Task FinalizeAsync(AcceptanceProtocol protocol, Guid? actor, CancellationToken cancellationToken)
    {
        var info = await bauakten.GetAsync(protocol.BauakteId, cancellationToken)
            ?? throw new InvalidOperationException("Bauakte nicht gefunden.");
        var rows = await work.ListAsync(protocol.BauakteId, cancellationToken);
        var pdf = await RenderAsync(protocol, info, rows, cancellationToken);
        var name = $"Abnahmeprotokoll {info.Number ?? info.Title} {protocol.PerformedAt.ToOffset(TimeSpan.FromHours(1)):yyyy-MM-dd}.pdf";
        var file = files.StoreGenerated(protocol.BauakteId, protocol.PerformedByUserId, name.Replace('/', '-'), "application/pdf", pdf, "acceptance",
            $"Abnahme: {Result(protocol.Result)}", clock.GetUtcNow());
        protocol.Complete(file.Id);
        audit.Record("bauakte.file_added", actor, EntityType, protocol.BauakteId.ToString(), new Dictionary<string, string?> { ["kind"] = "Document" });
        audit.Record("bauakte.acceptance_completed", actor, EntityType, protocol.BauakteId.ToString(), new Dictionary<string, string?> { ["result"] = protocol.Result.ToString() });

        var openDefects = rows.Any(r => r.IsDefect && r.IsOpen);
        if (protocol.Result == AcceptanceResult.Accepted && !openDefects)
        {
            await autoStatus.TryCompleteAsync(protocol.BauakteId, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public static string Result(AcceptanceResult result) => result switch
    {
        AcceptanceResult.Accepted => "abgenommen",
        AcceptanceResult.AcceptedWithReservations => "abgenommen mit Vorbehalt",
        _ => "Abnahme verweigert",
    };

    private async Task<byte[]> RenderAsync(AcceptanceProtocol p, BauakteReportInfo info, IReadOnlyList<WorkReportRow> rows, CancellationToken cancellationToken)
    {
        PdfReport.EnsureFonts();
        var company = await branding.GetAsync(cancellationToken);
        var (document, section) = PdfReport.Create("Abnahmeprotokoll", $"Abnahme {info.Number} {info.Title}", company);
        var width = 21.0 - 2.0 - 1.8;
        section.AddParagraph("Abnahmeprotokoll", StyleNames.Heading1);
        var lead = section.AddParagraph($"nach § 12 VOB/B bzw. § 640 BGB · {info.Number ?? "ohne Nummer"} · {info.Title}");
        lead.Style = "Muted";

        var address = string.Join(", ", new[] { info.Street, string.Join(' ', new[] { info.PostalCode, info.City }.Where(v => !string.IsNullOrWhiteSpace(v))) }
            .Where(v => !string.IsNullOrWhiteSpace(v)));
        PdfReport.AddFacts(section, width,
            ("Bauvorhaben", $"{info.Number} {info.Title}".Trim()),
            ("Baustelle", string.IsNullOrEmpty(address) ? "–" : address),
            ("Auftraggeber", info.Customer?.Name ?? "–"),
            ("Datum der Abnahme", PdfReport.DateTime(p.PerformedAt)),
            ("Ort", p.Location ?? (string.IsNullOrEmpty(address) ? "–" : address)),
            ("Für den Auftraggeber", p.ClientName ?? p.CustomerDecisionName ?? "–"),
            ("Für den Auftragnehmer", p.ContractorName ?? company.Name));

        section.AddParagraph("Abgenommene Leistung", StyleNames.Heading2);
        section.AddParagraph(p.Scope ?? "Estricharbeiten gemäß Auftrag.");
        var rooms = info.Rooms.Where(r => p.RoomIds.Count == 0 || p.RoomIds.Contains(r.Id)).ToList();
        if (rooms.Count > 0)
        {
            var table = PdfReport.AddTable(section, ("Raum", 6.0, ParagraphAlignment.Left), ("Geschoss", 2.6, ParagraphAlignment.Left),
                ("Fläche", 2.4, ParagraphAlignment.Right), ("Estrich", 6.2, ParagraphAlignment.Left));
            foreach (var room in rooms)
            {
                PdfReport.AddRow(table, room.Name, room.Floor ?? "", room.AreaSquareMeters is { } a ? $"{PdfReport.Number(a)} m²" : "–",
                    $"{room.ScreedType ?? "–"}{(room.ThicknessMillimeters is { } t ? $", {t} mm" : "")}{(room.UnderfloorHeating ? ", beheizt" : "")}");
            }

            var total = rooms.Sum(r => r.AreaSquareMeters ?? 0);
            var sum = PdfReport.AddRow(table, "Summe", "", $"{PdfReport.Number(total)} m²", "");
            sum.Format.Font.Bold = true;
        }

        section.AddParagraph("Ergebnis", StyleNames.Heading2);
        var result = section.AddParagraph();
        var resultText = result.AddFormattedText(Result(p.Result), TextFormat.Bold);
        resultText.Font.Size = 12;
        resultText.Font.Color = p.Result switch
        {
            AcceptanceResult.Accepted => Color.FromRgb(0x10, 0x7E, 0x3E),
            AcceptanceResult.AcceptedWithReservations => Color.FromRgb(0xB4, 0x5A, 0x00),
            _ => Color.FromRgb(0xAA, 0x08, 0x08),
        };

        var defects = rows.Where(r => p.DefectWorkItemIds.Contains(r.Id)).ToList();
        if (defects.Count > 0)
        {
            section.AddParagraph("Mängel und Restarbeiten", StyleNames.Heading2);
            var table = PdfReport.AddTable(section, ("Beschreibung", 11.0, ParagraphAlignment.Left), ("Frist", 3.0, ParagraphAlignment.Left),
                ("Stand", 3.2, ParagraphAlignment.Left));
            foreach (var d in defects)
            {
                PdfReport.AddRow(table, d.Title, PdfReport.Date(d.DueDate), d.Status);
            }
        }

        if (!string.IsNullOrWhiteSpace(p.Notes))
        {
            section.AddParagraph("Notizen", StyleNames.Heading2);
            foreach (var line in p.Notes.Split('\n'))
            {
                section.AddParagraph(line.Length == 0 ? " " : line);
            }
        }

        await AddPhotosAsync(section, width, p.PhotoFileIds, cancellationToken);

        section.AddParagraph("Unterschriften", StyleNames.Heading2).Format.KeepWithNext = true;
        var signatures = section.AddTable();
        signatures.Borders.Visible = false;
        signatures.AddColumn(Unit.FromCentimeter(width / 2 - 0.3));
        signatures.AddColumn(Unit.FromCentimeter(0.6));
        signatures.AddColumn(Unit.FromCentimeter(width / 2 - 0.3));
        var images = signatures.AddRow();
        images.Height = Unit.FromCentimeter(2.6);
        images.VerticalAlignment = VerticalAlignment.Bottom;
        AddSignature(images.Cells[0], p.ClientSignature);
        AddSignature(images.Cells[2], p.ContractorSignature);
        var captions = signatures.AddRow();
        captions.Cells[0].Borders.Top.Width = 0.75;
        captions.Cells[2].Borders.Top.Width = 0.75;
        captions.Cells[0].AddParagraph($"Auftraggeber: {p.ClientName ?? p.CustomerDecisionName ?? "–"}").Style = "Small";
        captions.Cells[0].AddParagraph(p.ClientSignedAt is { } c ? $"unterschrieben {PdfReport.DateTime(c)}" : "nicht unterschrieben").Style = "Muted";
        captions.Cells[2].AddParagraph($"Auftragnehmer: {p.ContractorName ?? company.Name}").Style = "Small";
        captions.Cells[2].AddParagraph(p.ContractorSignedAt is { } k ? $"unterschrieben {PdfReport.DateTime(k)}" : "nicht unterschrieben").Style = "Muted";

        if (p.CustomerDecision is { } decision && p.CustomerDecidedAt is { } decidedAt)
        {
            section.AddParagraph("Freigabe durch den Auftraggeber (online)", StyleNames.Heading2);
            section.AddParagraph($"{(decision == CustomerDecision.Approved ? "Freigegeben" : "Abgelehnt")} von {p.CustomerDecisionName} am {PdfReport.DateTime(decidedAt)}"
                + (p.CustomerIp is null ? "" : $" (IP-Adresse {p.CustomerIp})") + (p.CustomerComment is null ? "." : $": „{p.CustomerComment}“"));
        }

        return PdfReport.Render(document);
    }

    private static void AddSignature(Cell cell, byte[]? png)
    {
        if (png is null)
        {
            cell.AddParagraph(" ");
            return;
        }

        var image = cell.AddParagraph().AddImage(PdfReport.ImageSource(png));
        image.Height = Unit.FromCentimeter(2.2);
        image.LockAspectRatio = true;
    }

    private async Task AddPhotosAsync(Section section, double width, List<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var photos = await db.Set<FileRecord>().AsNoTracking().Where(f => ids.Contains(f.Id) && f.State == FileState.Stored).Take(8).ToListAsync(cancellationToken);
        var images = new List<byte[]>();
        foreach (var photo in photos)
        {
            if (await JpegAsync(photo, cancellationToken) is { } jpeg)
            {
                images.Add(jpeg);
            }
        }

        if (images.Count == 0)
        {
            return;
        }

        section.AddParagraph("Fotos", StyleNames.Heading2);
        var table = section.AddTable();
        table.Borders.Visible = false;
        var column = (width - 0.4) / 2;
        table.AddColumn(Unit.FromCentimeter(column));
        table.AddColumn(Unit.FromCentimeter(0.4));
        table.AddColumn(Unit.FromCentimeter(column));
        for (var i = 0; i < images.Count; i += 2)
        {
            var row = table.AddRow();
            row.BottomPadding = Unit.FromPoint(6);
            for (var j = 0; j < 2 && i + j < images.Count; j++)
            {
                var image = row.Cells[j * 2].AddParagraph().AddImage(PdfReport.ImageSource(images[i + j]));
                image.Width = Unit.FromCentimeter(column);
                image.LockAspectRatio = true;
            }
        }
    }

    private async Task<byte[]?> JpegAsync(FileRecord photo, CancellationToken cancellationToken)
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
            using var image = Image.ThumbnailBuffer(bytes, 1000, height: 1000, size: Enums.Size.Down);
            using var flat = image.HasAlpha() ? image.Flatten(background: [255, 255, 255]) : image.Copy();
            return flat.JpegsaveBuffer(q: 78, keep: Enums.ForeignKeep.None);
        }
        catch (Exception exception) when (exception is VipsException or IOException)
        {
            logger.LogWarning(exception, "Foto {FileId} konnte nicht in das Abnahmeprotokoll übernommen werden.", photo.Id);
            return null;
        }
    }
}
