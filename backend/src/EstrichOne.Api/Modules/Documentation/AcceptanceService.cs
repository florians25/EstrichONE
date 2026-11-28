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
    ReportPhotos photoSource,
    IOptions<EstrichOneOptions> options,
    TimeProvider clock,
    Notifications.Exports.INotifier notifier,
    Teams.Exports.ITeamsIntegration teams)
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

        if (!approve)
        {
            teams.Publish(new Teams.Exports.TeamsEvent(protocol.BauakteId, Teams.Exports.TeamsEventKind.AcceptanceRejected,
                "Der Kunde hat die Abnahme über den Link abgelehnt.", Link: $"/bauakten/{protocol.BauakteId}"));
        }

        // Team der Bauakte: nur Ergebnis und Bauakte, nie Kommentar oder Name des Kunden.
        var info = await bauakten.GetAsync(protocol.BauakteId, cancellationToken);
        await notifier.NotifyAsync(await bauakten.TeamUserIdsAsync(protocol.BauakteId, cancellationToken),
            new Notifications.Exports.NotificationRequest(Notifications.Exports.NotificationCategory.Bauakte,
                approve ? "Abnahme unterschrieben" : "Abnahme abgelehnt",
                info is null ? null : $"{info.Number} {info.Title}".Trim(), $"/bauakten/{protocol.BauakteId}", "bauakte", protocol.BauakteId,
                approve ? Notifications.Exports.NotificationUrgency.Normal : Notifications.Exports.NotificationUrgency.High,
                Event: Notifications.Exports.NotificationEvents.AcceptanceSigned), cancellationToken);
    }

    /// <summary>PDF erzeugen, unter „Abnahmen“ ablegen und – ohne Vorbehalt und offene Mängel – die Bauakte abschließen.</summary>
    public async Task FinalizeAsync(AcceptanceProtocol protocol, Guid? actor, CancellationToken cancellationToken)
    {
        var info = await bauakten.GetAsync(protocol.BauakteId, cancellationToken)
            ?? throw new InvalidOperationException("Bauakte nicht gefunden.");
        var rows = await work.ListAsync(protocol.BauakteId, cancellationToken);
        var pdf = await RenderAsync(protocol, info, rows, cancellationToken);
        var name = $"Abnahmeprotokoll {info.Number ?? info.Title} {protocol.PerformedAt.ToOffset(TimeSpan.FromHours(1)):yyyy-MM-dd}.pdf";
        var file = await files.StoreGeneratedAsync(protocol.BauakteId, protocol.PerformedByUserId, name.Replace('/', '-'), "application/pdf", pdf, "acceptance",
            null, $"Abnahme: {Result(protocol.Result)}", clock.GetUtcNow(), cancellationToken);
        protocol.Complete(file.Id);
        audit.Record("bauakte.file_added", actor, EntityType, protocol.BauakteId.ToString(), new Dictionary<string, string?> { ["kind"] = "Document" });
        audit.Record("bauakte.acceptance_completed", actor, EntityType, protocol.BauakteId.ToString(), new Dictionary<string, string?> { ["result"] = protocol.Result.ToString() });

        var openDefects = rows.Any(r => r.IsDefect && r.IsOpen);
        if (protocol.Result == AcceptanceResult.Accepted && !openDefects)
        {
            await autoStatus.TryCompleteAsync(protocol.BauakteId, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        teams.Publish(new Teams.Exports.TeamsEvent(protocol.BauakteId, Teams.Exports.TeamsEventKind.AcceptanceSigned,
            $"Ergebnis: {Result(protocol.Result)}", Link: $"/bauakten/{protocol.BauakteId}"));
    }

    public static string Result(AcceptanceResult result) => result switch
    {
        AcceptanceResult.Accepted => "abgenommen",
        AcceptanceResult.AcceptedWithReservations => "abgenommen mit Vorbehalt",
        _ => "Abnahme verweigert",
    };

    /// <summary>Leerer Vordruck zum Ausfüllen von Hand (alle Räume, Ergebnis zum Ankreuzen).</summary>
    public async Task<byte[]?> RenderBlankAsync(Guid bauakteId, Guid userId, CancellationToken cancellationToken)
    {
        var info = await bauakten.GetAsync(bauakteId, cancellationToken);
        if (info is null)
        {
            return null;
        }

        var blank = AcceptanceProtocol.Create(Guid.Empty, bauakteId, userId, clock.GetUtcNow(), null, null, null, null, [], AcceptanceResult.Accepted,
            null, [], [], null, null, null, null);
        return await RenderAsync(blank, info, [], cancellationToken, blankForm: true);
    }

    private async Task<byte[]> RenderAsync(AcceptanceProtocol p, BauakteReportInfo info, IReadOnlyList<WorkReportRow> rows, CancellationToken cancellationToken,
        bool blankForm = false)
    {
        PdfReport.EnsureFonts();
        var company = await branding.GetAsync(cancellationToken);
        var (document, section) = PdfReport.Create("Abnahmeprotokoll", $"Abnahme {info.Number} {info.Title}", company);
        var width = 21.0 - 2.0 - 1.8;
        section.AddParagraph("Abnahmeprotokoll", StyleNames.Heading1);
        var lead = section.AddParagraph($"nach § 12 VOB/B bzw. § 640 BGB, {info.Number ?? "ohne Nummer"}, {info.Title}");
        lead.Style = "Muted";

        var address = string.Join(", ", new[] { info.Street, string.Join(' ', new[] { info.PostalCode, info.City }.Where(v => !string.IsNullOrWhiteSpace(v))) }
            .Where(v => !string.IsNullOrWhiteSpace(v)));
        PdfReport.AddFacts(section, width,
            ("Bauvorhaben", $"{info.Number} {info.Title}".Trim()),
            ("Baustelle", string.IsNullOrEmpty(address) ? "–" : address),
            ("Auftraggeber", info.Customer?.Name ?? "–"),
            ("Datum der Abnahme", blankForm ? "______________________" : PdfReport.DateTime(p.PerformedAt)),
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
        if (blankForm)
        {
            result.AddText("☐ abgenommen     ☐ abgenommen mit Vorbehalt     ☐ Abnahme verweigert");
            section.AddParagraph("Mängel und Restarbeiten (mit Frist)", StyleNames.Heading2);
            for (var i = 0; i < 6; i++)
            {
                var line = section.AddParagraph(" ");
                line.Format.Borders.Bottom.Width = 0.5;
                line.Format.Borders.Bottom.Color = PdfReport.Line;
                line.Format.SpaceBefore = Unit.FromMillimeter(4);
            }
        }

        var resultText = result.AddFormattedText(blankForm ? "" : Result(p.Result), TextFormat.Bold);
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

        // Reihenfolge wie bei der Abnahme ausgewählt; Videos als Hinweisfeld, Fotos aufgerichtet und unverzerrt.
        var files = await db.Set<FileRecord>().AsNoTracking().Where(f => ids.Contains(f.Id) && f.State == FileState.Stored).ToListAsync(cancellationToken);
        var photos = ids.Select(id => files.FirstOrDefault(f => f.Id == id)).OfType<FileRecord>().Take(8).ToList();
        if (photos.Count == 0)
        {
            return;
        }

        section.AddParagraph("Fotos", StyleNames.Heading2);
        var entries = new List<ReportPhoto>(photos.Count);
        for (var i = 0; i < photos.Count; i++)
        {
            var photo = photos[i];
            entries.Add(new ReportPhoto(await photoSource.LoadAsync(photo, cancellationToken),
                $"{(photo.Kind == FileKind.Video ? "Video" : "Foto")} {i + 1}, {PdfReport.DateTime(photo.CapturedAt ?? photo.CreatedAt)}", [photo.Caption]));
        }

        ReportPhotos.AddGrid(section, width, 2, entries);
    }
}
