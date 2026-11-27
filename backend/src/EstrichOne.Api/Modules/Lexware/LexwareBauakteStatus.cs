using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Notifications.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Regeln des Moduls „Lexware Office: Status der Bauakte automatisch“ als reine Funktionen.</summary>
public static class LexwareStatusRules
{
    public const string Source = "Lexware Office";
    public const string ReviewReason = "Rechnungen vollständig bezahlt, bitte prüfen und abschließen";

    /// <summary>Ein zugeordnetes Angebot ist in Lexware Office angenommen.</summary>
    public static bool QuotationAccepted(IEnumerable<LexwareVoucherLink> links) =>
        links.Any(l => l.VoucherType == LexwareVoucherTypes.Quotation && l.Status == "accepted");

    /// <summary>
    /// Alle zugeordneten Rechnungen sind bezahlt: mindestens eine Rechnung, keine offen, überfällig oder im Entwurf
    /// (stornierte zählen nicht), und keine Gutschrift ist noch offen. <c>paidoff</c> (ausgeglichen, etwa durch eine
    /// Gutschrift) gilt als bezahlt. <c>Stamp</c> beschreibt den Stand, damit „Prüfung zurückstellen“ erst bei einem
    /// anderen Zahlstand erneut zur Prüfung führt.
    /// </summary>
    public static (bool Paid, string Stamp) InvoicesPaid(IEnumerable<LexwareVoucherLink> links)
    {
        var list = links.ToList();
        var invoices = list.Where(l => l.VoucherType == LexwareVoucherTypes.Invoice && l.Status != "voided").OrderBy(l => l.LexwareId, StringComparer.Ordinal).ToList();
        var openCredit = list.Any(l => l.VoucherType == LexwareVoucherTypes.CreditNote && l.Status == "open");
        var paid = invoices.Count > 0 && invoices.All(l => l.Status is "paid" or "paidoff") && !openCredit;
        var stamp = string.Join(',', list.Where(l => l.VoucherType is LexwareVoucherTypes.Invoice or LexwareVoucherTypes.CreditNote)
            .OrderBy(l => l.LexwareId, StringComparer.Ordinal).Select(l => $"{l.LexwareId}:{l.Status}"));
        return (paid, stamp);
    }
}

/// <summary>
/// Status der Bauakte aus Angebots- und Rechnungsstatus (Modul „Lexware Office: Status der Bauakte automatisch“). Läuft nach
/// jedem Statusabgleich der zugeordneten Belege (beim Öffnen der Bauakte und im Hintergrund). Nur vorwärts: Eine Anfrage
/// wird nach angenommenem Angebot „Geplant“; sind alle Rechnungen bezahlt, wird die Bauakte zur Abschlussprüfung gestellt.
/// Abgeschlossen wird nie automatisch.
/// </summary>
public sealed class LexwareBauakteStatusService(LexwareService lexware, IBauakteExternalStatus bauakten, INotifier notifier, ISystemSettings system,
    ILogger<LexwareBauakteStatusService> logger)
{
    /// <summary>Bauakten je Hintergrundlauf (Lexware erlaubt zwei Anfragen je Sekunde).</summary>
    private const int BatchSize = 30;

    public async Task<bool> ActiveAsync(LexwareSettingsRow row, CancellationToken cancellationToken)
    {
        var modules = (await system.GetAsync(cancellationToken)).Modules;
        return modules.IsEnabled(AppModules.Lexware) && modules.IsEnabled(AppModules.LexwareStatus) && row.Configured
            && (row.StatusOnQuotationAccepted || row.StatusOnInvoicesPaid);
    }

    /// <summary>Regeln für eine Bauakte mit ihren (gerade abgeglichenen) Belegen anwenden; speichert und benachrichtigt.</summary>
    public async Task<int> EvaluateAsync(LexwareSettingsRow row, Guid bauakteId, IReadOnlyList<LexwareVoucherLink> links, CancellationToken cancellationToken)
    {
        if (!await ActiveAsync(row, cancellationToken))
        {
            return 0;
        }

        var notes = new List<(string Title, string Body)>();
        if (row.StatusOnQuotationAccepted && LexwareStatusRules.QuotationAccepted(links)
            && await bauakten.AdvanceToPlannedAsync(bauakteId, BauakteAutoTrigger.LexwareQuotationAccepted, LexwareStatusRules.Source,
                "Angebot in Lexware Office angenommen", cancellationToken))
        {
            notes.Add(("Angebot angenommen", "Die Bauakte steht jetzt auf „Geplant“."));
        }

        if (row.StatusOnInvoicesPaid)
        {
            var (paid, stamp) = LexwareStatusRules.InvoicesPaid(links);
            if (!paid)
            {
                await bauakten.ForgetReviewStampAsync(bauakteId, cancellationToken);
            }
            else if (await bauakten.RequestReviewAsync(bauakteId, LexwareStatusRules.ReviewReason, stamp, LexwareStatusRules.Source, cancellationToken))
            {
                notes.Add(("Zur Abschlussprüfung", "Rechnungen vollständig bezahlt, bitte prüfen und abschließen."));
            }
        }

        await lexware.Db.SaveChangesAsync(cancellationToken);
        if (notes.Count > 0
            && await lexware.Db.Set<Bauakte>().AsNoTracking().Where(b => b.Id == bauakteId).Select(b => new { b.ResponsibleUserId, b.Number, b.Title })
                .FirstOrDefaultAsync(cancellationToken) is { } info && info.ResponsibleUserId is { } responsible)
        {
            foreach (var (title, body) in notes)
            {
                await notifier.NotifyAsync([responsible], new NotificationRequest(NotificationCategory.Bauakte, title,
                    $"{$"{info.Number} {info.Title}".Trim()}: {body} Automatisch durch Lexware Office.", $"/bauakten/{bauakteId}", "bauakte", bauakteId,
                    Event: NotificationEvents.BauakteLexwareStatus), cancellationToken);
            }
        }

        return notes.Count;
    }

    /// <summary>Hintergrund: Belege laufender Bauakten abgleichen (ältester Abgleich zuerst) und die Regeln anwenden.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var row = await lexware.SettingsAsync(cancellationToken);
        if (!await ActiveAsync(row, cancellationToken))
        {
            return 0;
        }

        var db = lexware.Db;
        var ids = await db.Set<LexwareVoucherLink>().AsNoTracking()
            .Join(db.Set<Bauakte>().AsNoTracking(), l => l.BauakteId, b => b.Id, (l, b) => new { l.BauakteId, l.SyncedAt, b.Status })
            .Where(x => x.Status != BauakteStatus.Completed && x.Status != BauakteStatus.Cancelled)
            .GroupBy(x => x.BauakteId).Select(g => new { Id = g.Key, Oldest = g.Min(x => x.SyncedAt) })
            .OrderBy(x => x.Oldest).Take(BatchSize).Select(x => x.Id).ToListAsync(cancellationToken);
        var changes = 0;
        foreach (var id in ids)
        {
            var links = await db.Set<LexwareVoucherLink>().Where(l => l.BauakteId == id).ToListAsync(cancellationToken);
            if (await lexware.SyncAsync(row, links, force: false, cancellationToken) is { } error)
            {
                logger.LogInformation("Statusabgleich der Belege im Hintergrund unterbrochen; der nächste Lauf macht weiter");
                break;
            }

            changes += await EvaluateAsync(row, id, links, cancellationToken);
        }

        return changes;
    }
}
