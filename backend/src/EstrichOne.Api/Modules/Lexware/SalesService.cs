using System.Globalization;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Planning.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Summen eines Verkaufsbelegs: netto, Steuer je Satz, brutto (Vorschau; maßgeblich rechnet Lexware Office).</summary>
public sealed record SalesTotals(decimal Net, decimal Tax, decimal Gross, IReadOnlyList<SalesTaxLine> TaxLines);

/// <summary>Steuer zu einem Steuersatz.</summary>
public sealed record SalesTaxLine(decimal Rate, decimal Net, decimal Tax);

/// <summary>Berechnungen und Prüfungen eines Verkaufsbelegs als reine Funktionen (mit Unit-Tests geprüft).</summary>
public static class SalesRules
{
    public static readonly decimal[] TaxRates = [19m, 7m, 0m];

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static string Euro(decimal value) => value.ToString("#,##0.00", German) + " €";

    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>Positionsbetrag netto: Menge mal Einzelpreis abzüglich Rabatt, auf Cent gerundet. Abzüge haben einen negativen Preis.</summary>
    public static decimal LineNet(decimal? quantity, decimal? price, decimal? discountPercent) =>
        Math.Round((quantity ?? 0m) * (price ?? 0m) * (1m - Math.Clamp(discountPercent ?? 0m, 0m, 100m) / 100m), 2, MidpointRounding.AwayFromZero);

    public static decimal LineNet(SalesVoucherLine line) => line.Priced ? LineNet(line.Kind == SalesVoucherLine.KindDeduction ? 1m : line.Quantity, line.NetPrice,
        line.Kind == SalesVoucherLine.KindDeduction ? null : line.DiscountPercent) : 0m;

    /// <summary>Summen: Steuer je Steuersatz auf die Summe der Positionen (Abzüge eingeschlossen); bei § 13b und steuerfrei keine Steuer.</summary>
    public static SalesTotals Totals(IEnumerable<SalesVoucherLine> lines, string taxType)
    {
        var priced = lines.Where(l => l.Priced).ToList();
        var reverse = LexwareTaxTypes.WithoutTax(taxType);
        var groups = priced.GroupBy(l => reverse ? 0m : l.TaxRate ?? 19m).OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var net = g.Sum(LineNet);
                return new SalesTaxLine(g.Key, net, Math.Round(net * g.Key / 100m, 2, MidpointRounding.AwayFromZero));
            }).ToList();
        var totalNet = groups.Sum(g => g.Net);
        var totalTax = groups.Sum(g => g.Tax);
        return new SalesTotals(totalNet, totalTax, totalNet + totalTax, groups);
    }

    /// <summary>Zwischensumme netto der Positionen seit der letzten Zwischensumme bis vor <paramref name="index"/>.</summary>
    public static decimal Subtotal(IReadOnlyList<SalesVoucherLine> ordered, int index)
    {
        var sum = 0m;
        for (var i = index - 1; i >= 0 && ordered[i].Kind != SalesVoucherLine.KindSubtotal; i--)
        {
            sum += LineNet(ordered[i]);
        }

        return sum;
    }

    /// <summary>Fehler, die das Senden verhindern (leer = sendebereit). Je Belegart eigene Pflichtangaben.</summary>
    public static List<string> Problems(SalesVoucher voucher, LexwareTaxTypeState? taxTypes = null)
    {
        var problems = new List<string>();
        var type = voucher.VoucherType;
        var name = SalesTypes.Name(type, voucher.Purpose);
        if (taxTypes is not null && LexwareTaxTypes.Problem(type, voucher.TaxType, taxTypes, SalesTypes.Accusative(type)) is { } taxProblem)
        {
            problems.Add(taxProblem);
        }

        if (string.IsNullOrWhiteSpace(voucher.Title))
        {
            problems.Add("Bitte geben Sie einen Titel an.");
        }

        var items = voucher.Lines.Where(l => l.Kind == SalesVoucherLine.KindItem).ToList();
        if (items.Count == 0)
        {
            problems.Add($"{(type == SalesTypes.Quotation ? "Das Angebot" : type == SalesTypes.Invoice ? "Die Rechnung" : type == SalesTypes.CreditNote ? "Die Gutschrift" : "Die Auftragsbestätigung")} braucht mindestens eine Position mit Menge und Preis.");
        }

        foreach (var line in voucher.Lines.OrderBy(l => l.Position))
        {
            var label = $"Position {line.Position.ToString(CultureInfo.InvariantCulture)}";
            if (line.Kind != SalesVoucherLine.KindSubtotal && string.IsNullOrWhiteSpace(line.Name))
            {
                problems.Add($"{label}: Bitte geben Sie eine Bezeichnung an.");
            }

            if (line.Kind == SalesVoucherLine.KindItem)
            {
                if (line.Quantity is null or <= 0)
                {
                    problems.Add($"{label}: Die Menge muss größer als 0 sein.");
                }

                if (string.IsNullOrWhiteSpace(line.Unit))
                {
                    problems.Add($"{label}: Bitte geben Sie eine Einheit an.");
                }

                if (line.NetPrice is null)
                {
                    problems.Add($"{label}: Bitte geben Sie einen Einzelpreis an.");
                }
            }

            if (line.Kind == SalesVoucherLine.KindDeduction)
            {
                if (type != SalesTypes.Invoice)
                {
                    problems.Add($"{label}: Abzüge gibt es nur in Rechnungen.");
                }
                else if (line.NetPrice is null or >= 0)
                {
                    problems.Add($"{label}: Ein Abzug braucht einen Betrag unter 0.");
                }
            }

            if (line.Priced && !LexwareTaxTypes.WithoutTax(voucher.TaxType) && line.TaxRate is { } rate && !TaxRates.Contains(rate))
            {
                problems.Add($"{label}: Der Steuersatz muss 19, 7 oder 0 Prozent betragen.");
            }
        }

        if (type == SalesTypes.Quotation && voucher.ValidUntil is null)
        {
            problems.Add("Bitte geben Sie an, bis wann das Angebot gültig ist.");
        }

        if (type == SalesTypes.Invoice && voucher.ServiceDate is null)
        {
            problems.Add("Bitte geben Sie das Leistungsdatum bzw. den Leistungszeitraum an (Pflichtangabe auf Rechnungen).");
        }

        if (voucher.ServiceDate is { } start && voucher.ServiceEndDate is { } end && end < start)
        {
            problems.Add("Das Ende des Leistungszeitraums liegt vor dem Beginn.");
        }

        var totals = Totals(voucher.Lines, voucher.TaxType);
        if (items.Count > 0 && totals.Gross < 0)
        {
            problems.Add($"Der Gesamtbetrag der {name} ist negativ. Bitte prüfen Sie die Abzüge.");
        }

        if (items.Count > 0 && type is SalesTypes.CreditNote && totals.Gross <= 0)
        {
            problems.Add("Die Gutschrift braucht einen Betrag über 0.");
        }

        return problems;
    }

    /// <summary>
    /// Beleg für Lexware Office. Katalogeinträge mit Lexware-Verknüpfung gehen als Artikel, sonst als freie Position.
    /// Zwischensummen gehen als Textposition mit dem Betrag, Abzüge als freie Position mit negativem Preis.
    /// </summary>
    public static LexwareSalesVoucher ToLexware(SalesVoucher voucher, string lexwareContactId, DateOnly today, IReadOnlyDictionary<Guid, CatalogItem> catalog)
    {
        var ordered = voucher.Lines.OrderBy(l => l.Position).ToList();
        var lines = new List<LexwareSalesLine>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var l = ordered[i];
            switch (l.Kind)
            {
                case SalesVoucherLine.KindText:
                    lines.Add(new LexwareSalesLine("text", null, l.Name, l.Description, null, null, null, null, null));
                    break;
                case SalesVoucherLine.KindSubtotal:
                    lines.Add(new LexwareSalesLine("text", null, string.IsNullOrWhiteSpace(l.Name) ? "Zwischensumme" : l.Name,
                        $"Zwischensumme netto: {Euro(Subtotal(ordered, i))}", null, null, null, null, null));
                    break;
                case SalesVoucherLine.KindDeduction:
                    lines.Add(new LexwareSalesLine("custom", null, l.Name, l.Description, 1m, "pauschal", l.NetPrice, l.TaxRate, null));
                    break;
                default:
                    var article = l.CatalogItemId is { } id && catalog.TryGetValue(id, out var item) && item.LexwareId is not null && item.RemoteState is null ? item : null;
                    var kind = article is null ? "custom" : article.Kind == CatalogItemKind.Product ? "material" : "service";
                    lines.Add(new LexwareSalesLine(kind, article?.LexwareId, l.Name, l.Description, l.Quantity, l.Unit, l.NetPrice, l.TaxRate, l.DiscountPercent));
                    break;
            }
        }

        var payment = voucher.PaymentLabel is { Length: > 0 } label
            ? new LexwarePaymentCondition(voucher.PaymentConditionId, label, voucher.PaymentDuration, voucher.PaymentDiscountPercent, voucher.PaymentDiscountRange, false)
            : null;
        return new LexwareSalesVoucher(lexwareContactId, voucher.VoucherDate ?? today, voucher.ValidUntil, voucher.Title, voucher.Introduction, voucher.Remark,
            voucher.TaxType, payment, lines, voucher.VoucherType, voucher.ServiceDate, voucher.ServiceEndDate);
    }

    /// <summary>Gleicher Beleg wie dieser? Für die Suche nach einem trotz Fehlermeldung angelegten Beleg.</summary>
    public static bool SameAs(SalesVoucher voucher, LexwareVoucher found) =>
        found.Type == voucher.VoucherType && (found.Status == "draft" || voucher.Finalized)
        && string.Equals(LexwareContactSync.Clean(found.Title), LexwareContactSync.Clean(voucher.Title), StringComparison.Ordinal)
        && (found.NetAmount is null || found.NetAmount == Totals(voucher.Lines, voucher.TaxType).Net);

    /// <summary>Lexware-Status als Wert für Übersicht und Belegkette (offene Rechnungen nach Fälligkeit „überfällig“).</summary>
    public static string StatusKey(SalesVoucher voucher, DateOnly today) => voucher.State switch
    {
        SalesVoucherState.Sent => voucher.LexwareStatus is "open" && voucher.VoucherType == SalesTypes.Invoice && DueDate(voucher) is { } due && due < today
            ? "overdue"
            : voucher.LexwareStatus ?? "draft",
        SalesVoucherState.Sending => "sending",
        SalesVoucherState.Failed => "failed",
        _ => "local",
    };

    /// <summary>Fälligkeit: Belegdatum plus Zahlungsziel (Rechnungen) bzw. „gültig bis“ (Angebote).</summary>
    public static DateOnly? DueDate(SalesVoucher voucher) => voucher.VoucherType switch
    {
        SalesTypes.Quotation => voucher.ValidUntil,
        SalesTypes.Invoice when voucher.PaymentDuration is { } days => (voucher.VoucherDate ?? (voucher.SentAt is { } sent ? DateOnly.FromDateTime(sent.UtcDateTime) : null))?.AddDays(days),
        _ => null,
    };

    /// <summary>Leistungszeitraum aus den Einsätzen der Bauakte: erster bis letzter Tag (ohne Aufmaß und Begehungen nur Ausführungstage, wenn es welche gibt).</summary>
    public static (DateOnly Start, DateOnly End)? ServicePeriod(IReadOnlyList<BauakteAssignmentInfo> assignments, DateOnly? until = null)
    {
        var work = assignments.Where(a => a.Kind is "screed" or "rework").ToList();
        var days = (work.Count > 0 ? work : assignments).Select(a => a.Day).Where(d => until is null || d <= until).ToList();
        return days.Count == 0 ? null : (days.Min(), days.Max());
    }
}

/// <summary>Vorschlag für eine Position aus der Bauakte (nur Vorschlag; übernommen wird erst nach Bestätigung).</summary>
/// <param name="Source"><c>rooms</c> (Summe aus „Räume und Flächen“), <c>project</c> (Kennwert „Fläche gesamt“) oder <c>hours</c> (Regiestunden aus Einsätzen).</param>
public sealed record SalesLineSuggestion(string Source, string Hint, string Name, string? Description, decimal Quantity, string Unit, Guid? CatalogItemId = null,
    decimal? NetPrice = null, decimal? TaxRate = null, bool Selected = true);

/// <param name="ServiceDate">Vorschlag für den Leistungszeitraum aus den Einsätzen.</param>
public sealed record SalesSuggestionsResponse(IReadOnlyList<SalesLineSuggestion> Lines, IReadOnlyList<string> Hints, DateOnly? ServiceDate = null,
    DateOnly? ServiceEndDate = null);

/// <summary>Vorschläge für Positionen aus der Bauakte: Fläche aus Aufmaß bzw. Kennwerten, Regiestunden aus Einsätzen.</summary>
public static class SalesSuggestions
{
    private static string Area(decimal value) => value.ToString("#,##0.##", CultureInfo.GetCultureInfo("de-DE")) + " m²";

    public static SalesSuggestionsResponse Build(Bauakte bauakte, IReadOnlyList<BauakteRoomArea> rooms, IReadOnlyList<CatalogItem> catalog,
        IReadOnlyList<BauakteAssignmentInfo>? assignments = null)
    {
        var lines = new List<SalesLineSuggestion>();
        var hints = new List<string>();
        var measured = rooms.Where(r => r.AreaSquareMeters is > 0).ToList();
        var project = bauakte.Project;
        var details = new List<string>();
        var screed = measured.Select(r => r.ScreedType).FirstOrDefault(t => t is not null);
        if (screed is not null)
        {
            details.Add(screed);
        }
        else if (project.StrengthClass is { } strength)
        {
            details.Add(strength);
        }

        var thickness = measured.Select(r => r.ThicknessMillimeters).FirstOrDefault(t => t is not null) ?? project.NominalThicknessMm;
        if (thickness is { } mm)
        {
            details.Add($"Nenndicke {mm.ToString(CultureInfo.InvariantCulture)} mm");
        }

        if (project.UnderfloorHeating == true)
        {
            details.Add("Fußbodenheizung");
        }

        // Ein passender Katalogeintrag (Leistung je m²), wenn es genau einen gibt.
        var perArea = catalog.Where(c => c.IsActive && c.Kind == CatalogItemKind.Service && c.Unit is { } u && (u == "m²" || u.Equals("qm", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var match = perArea.Count == 1 ? perArea[0] : null;
        if (measured.Count > 0)
        {
            var total = measured.Sum(r => r.AreaSquareMeters!.Value);
            var list = string.Join(", ", measured.Take(12).Select(r => $"{r.Name} {Area(r.AreaSquareMeters!.Value)}"));
            if (measured.Count > 12)
            {
                list += ", weitere";
            }

            lines.Add(new SalesLineSuggestion("rooms", $"Summe aus „Räume und Flächen“ ({measured.Count.ToString(CultureInfo.InvariantCulture)} Räume)",
                match?.Title ?? "Estrich liefern und einbauen", Join(details, "Räume: " + list), Math.Round(total, 2), "m²", match?.Id, match?.NetPrice, match?.TaxRate));
        }
        else if (project.TotalArea is > 0 and var area)
        {
            lines.Add(new SalesLineSuggestion("project", "Kennwert „Fläche gesamt“ der Bauakte", match?.Title ?? "Estrich liefern und einbauen",
                Join(details, null), Math.Round(area, 2), "m²", match?.Id, match?.NetPrice, match?.TaxRate));
        }
        else
        {
            hints.Add("In der Bauakte ist noch keine Fläche erfasst (Räume und Flächen oder Kennwert „Fläche gesamt“).");
        }

        // Regiestunden: Einsätze mit Beginn und Ende als Personenstunden (Vorschlag, standardmäßig nicht ausgewählt).
        DateOnly? serviceStart = null, serviceEnd = null;
        if (assignments is { Count: > 0 })
        {
            var timed = assignments.Where(a => a.Hours is > 0 && a.People > 0).ToList();
            if (timed.Count > 0)
            {
                var hours = timed.Sum(a => a.Hours!.Value * a.People);
                var hourly = catalog.Where(c => c.IsActive && c.Kind == CatalogItemKind.Service && c.Unit is { } u
                    && (u.Equals("Std.", StringComparison.OrdinalIgnoreCase) || u.Equals("h", StringComparison.OrdinalIgnoreCase)
                        || u.StartsWith("Stunde", StringComparison.OrdinalIgnoreCase))).ToList();
                var rate = hourly.Count == 1 ? hourly[0] : null;
                lines.Add(new SalesLineSuggestion("hours",
                    $"Regiestunden aus {timed.Count.ToString(CultureInfo.InvariantCulture)} Einsätzen mit Beginn und Ende (Personen mal Stunden)",
                    rate?.Title ?? "Regiestunden", string.Join(", ", timed.Take(10).Select(a => $"{SalesRules.Date(a.Day)} {a.Activity}".Trim())),
                    Math.Round(hours, 2), rate?.Unit ?? "Std.", rate?.Id, rate?.NetPrice, rate?.TaxRate, Selected: false));
            }

            if (SalesRules.ServicePeriod(assignments) is { } period)
            {
                (serviceStart, serviceEnd) = (period.Start, period.End == period.Start ? null : period.End);
            }
        }

        // Raumumfänge führt EstrichONE nicht: Ohne sie gibt es keinen Vorschlag für Randdämmstreifen.
        hints.Add("Randdämmstreifen werden nicht vorgeschlagen, weil in der Bauakte keine Raumumfänge erfasst sind. Bitte ergänzen Sie die Position bei Bedarf selbst.");
        return new SalesSuggestionsResponse(lines, hints, serviceStart, serviceEnd);
    }

    private static string? Join(List<string> details, string? rooms)
    {
        var parts = new List<string>(details);
        if (rooms is not null)
        {
            parts.Add(rooms);
        }

        var text = string.Join(". ", parts);
        return text.Length == 0 ? null : text.Length > 2000 ? text[..2000] : text;
    }
}

/// <summary>Ergebnis des Sendens an Lexware Office.</summary>
/// <param name="Adopted">Der Beleg war bereits in Lexware Office angelegt (vorheriger Versuch ohne eindeutige Antwort) und wurde übernommen statt erneut gesendet.</param>
public sealed record SalesSendResult(SalesVoucher Voucher, bool AlreadySent, bool Adopted);

/// <summary>
/// Verkaufsbelege an Lexware Office senden. Doppeltes Senden ist ausgeschlossen: Der Beleg wird vor dem Aufruf in der
/// Datenbank auf „wird gesendet“ gesetzt (nur aus „Entwurf“ oder „fehlgeschlagen“), der Aufruf wird nicht wiederholt, und
/// nach einem Abbruch ohne eindeutige Antwort sucht der nächste Versuch zuerst in Lexware Office nach dem Beleg. Danach ist
/// der Beleg hier schreibgeschützt. Festgeschrieben wird nur mit <paramref name="finalize"/> (Modul, Recht und Bestätigung
/// prüft der Aufrufer).
/// </summary>
public sealed class SalesVoucherService(LexwareService lexware, IAuditLog audit)
{
    /// <summary>Nach dieser Zeit gilt ein hängender Sendevorgang (Server neu gestartet) als abgebrochen ohne eindeutige Antwort.</summary>
    public static readonly TimeSpan StaleSending = TimeSpan.FromMinutes(10);

    public async Task<SalesSendResult> SendAsync(Guid voucherId, Guid userId, bool finalize, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var gate = lexware.QuotationGate;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var voucher = await db.Set<SalesVoucher>().SingleAsync(q => q.Id == voucherId, cancellationToken);
            var name = SalesTypes.Name(voucher.VoucherType, voucher.Purpose);
            if (voucher.State == SalesVoucherState.Sent)
            {
                return new SalesSendResult(voucher, true, false);
            }

            var now = lexware.Clock.GetUtcNow();
            var stale = now - StaleSending;
            if (voucher.State == SalesVoucherState.Sending && voucher.SendStartedAt > stale)
            {
                throw new LexwareException("SALES_SENDING", $"Dieser Beleg ({name}) wird gerade an Lexware Office gesendet. Bitte warten Sie einen Moment.");
            }

            var row = await lexware.SettingsAsync(cancellationToken);
            if (SalesRules.Problems(voucher, LexwareTaxTypes.State(row)) is { Count: > 0 } problems)
            {
                throw new LexwareException("SALES_INCOMPLETE", string.Join(" ", problems));
            }

            var lexwareContactId = await db.Set<LexwareContactLink>().AsNoTracking().Where(l => l.ContactId == voucher.ContactId).Select(l => l.LexwareContactId)
                .FirstOrDefaultAsync(cancellationToken) ?? throw NotLinked(await CustomerNameAsync(voucher.ContactId, cancellationToken));
            var connection = lexware.Connection(row);
            var preceding = voucher.PrecedingLexwareId;
            if (preceding is null && voucher.PrecedingId is { } precedingId)
            {
                preceding = await db.Set<SalesVoucher>().AsNoTracking().Where(v => v.Id == precedingId).Select(v => v.LexwareId).FirstOrDefaultAsync(cancellationToken);
            }

            // Sperre in der Datenbank: Nur wer den Beleg von „Entwurf“ bzw. „fehlgeschlagen“ auf „wird gesendet“ setzt, sendet.
            var uncertain = voucher.SendUncertain || voucher.State == SalesVoucherState.Sending;
            var claimed = await db.Set<SalesVoucher>().Where(q => q.Id == voucherId && q.Version == voucher.Version
                    && (q.State == SalesVoucherState.Draft || q.State == SalesVoucherState.Failed
                        || (q.State == SalesVoucherState.Sending && q.SendStartedAt < stale)))
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.State, SalesVoucherState.Sending).SetProperty(q => q.SendStartedAt, now), cancellationToken);
            if (claimed == 0)
            {
                db.ChangeTracker.Clear();
                var current = await db.Set<SalesVoucher>().SingleAsync(q => q.Id == voucherId, cancellationToken);
                return current.State == SalesVoucherState.Sent
                    ? new SalesSendResult(current, true, false)
                    : throw new LexwareException("SALES_SENDING", "Dieser Beleg wird gerade an Lexware Office gesendet oder wurde inzwischen geändert. Bitte laden Sie ihn neu.");
            }

            db.ChangeTracker.Clear();
            voucher = await db.Set<SalesVoucher>().SingleAsync(q => q.Id == voucherId, cancellationToken);
            string? lexwareId = null;
            var adopted = false;
            try
            {
                if (uncertain)
                {
                    lexwareId = await FindExistingAsync(connection, voucher, lexwareContactId, cancellationToken);
                    adopted = lexwareId is not null;
                }

                if (lexwareId is null)
                {
                    var catalogIds = voucher.Lines.Select(l => l.CatalogItemId).OfType<Guid>().Distinct().ToList();
                    var catalog = await db.Set<CatalogItem>().AsNoTracking().Where(c => catalogIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
                    voucher.Finalized = finalize;
                    lexwareId = (await lexware.Client.CreateSalesVoucherAsync(connection, SalesRules.ToLexware(voucher, lexwareContactId, lexware.Today(), catalog),
                        preceding, finalize, cancellationToken)).Id;
                }
            }
            catch (LexwareException exception)
            {
                var failure = exception;
                if (exception.Code == LexwareErrors.TaxTypeNotAllowed)
                {
                    // Abgelehnte Steuerart merken (je Belegart): Die Maske bietet sie danach nicht mehr an, bis sie erneut geprüft wird.
                    var taxType = exception.RejectedTaxType ?? voucher.TaxType;
                    await LexwareTaxTypeStore.RememberRejectionAsync(lexware, voucher.VoucherType, taxType, CancellationToken.None);
                    var text = LexwareErrors.TaxTypeText(taxType, SalesTypes.Accusative(voucher.VoucherType));
                    failure = new LexwareException(exception.Code, text, exception.Status)
                    {
                        Issues = exception.Issues.Select(i => i.Field == "taxType" ? i with { Text = text } : i).ToList(),
                        Technical = exception.Technical, RejectedTaxType = taxType,
                    };
                }

                // Ohne eindeutige Antwort kann Lexware Office den Beleg angelegt haben: Der nächste Versuch sucht zuerst danach.
                voucher.State = SalesVoucherState.Failed;
                voucher.Finalized = false;
                voucher.SendUncertain = exception.Code is "TIMEOUT" or "OFFLINE" or "UNAVAILABLE" || (voucher.SendUncertain && exception.Code is "RATE_LIMITED");
                voucher.SendError = LexwareService.Cut(failure.Message, 2000);
                voucher.SendErrorTechnical = LexwareService.Cut(failure.Technical, 1000);
                voucher.SendErrorFix = failure.Fix;
                await db.SaveChangesAsync(CancellationToken.None);
                if (failure != exception)
                {
                    throw failure;
                }

                throw;
            }

            voucher.State = SalesVoucherState.Sent;
            voucher.LexwareId = lexwareId;
            voucher.PrecedingLexwareId = preceding;
            voucher.SentAt = now;
            voucher.SentByUserId = userId;
            voucher.SendError = null;
            voucher.SendErrorTechnical = null;
            voucher.SendErrorFix = null;
            voucher.SendUncertain = false;
            voucher.LexwareStatus = finalize ? "open" : "draft";
            await RefreshAsync(connection, voucher, cancellationToken);
            await LinkToBauakteAsync(voucher, userId, cancellationToken);
            audit.Record(finalize ? "lexware.sales_finalized" : "lexware.sales_sent", userId, voucher.BauakteId is null ? "contact" : "bauakte",
                (voucher.BauakteId ?? voucher.ContactId).ToString(), new Dictionary<string, string?>
                {
                    ["type"] = voucher.VoucherType, ["title"] = voucher.Title, ["lexwareId"] = lexwareId, ["adopted"] = adopted ? "true" : null,
                    ["preceding"] = preceding,
                });
            await db.SaveChangesAsync(CancellationToken.None);
            lexware.ForgetContacts(); // Belegliste des Kunden neu laden
            return new SalesSendResult(voucher, false, adopted);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Nummer und Status aus Lexware Office lesen (Fehler übergehen: der Stand wird später abgeglichen).</summary>
    public async Task RefreshAsync(LexwareConnection connection, SalesVoucher voucher, CancellationToken cancellationToken, bool payments = false)
    {
        if (voucher.LexwareId is not { } id)
        {
            return;
        }

        try
        {
            var found = await lexware.Client.GetVoucherAsync(connection, voucher.VoucherType, id, cancellationToken);
            voucher.LexwareNumber = LexwareService.Cut(found.Number, 100) ?? voucher.LexwareNumber;
            voucher.LexwareStatus = LexwareService.Cut(found.Status, 20) ?? voucher.LexwareStatus;
            voucher.VoucherDate ??= found.VoucherDate;
            if (payments && voucher.VoucherType == SalesTypes.Invoice && voucher.LexwareStatus is not ("draft" or "voided"))
            {
                var payment = await lexware.Client.GetPaymentAsync(connection, id, cancellationToken);
                voucher.OpenAmount = payment.OpenAmount;
                voucher.PaymentStatus = LexwareService.Cut(payment.PaymentStatus, 30);
            }

            voucher.LexwareSyncedAt = lexware.Clock.GetUtcNow();
        }
        catch (LexwareException)
        {
            // Angelegt ist der Beleg; Nummer und Status kommen beim nächsten Abgleich.
        }
    }

    /// <summary>Nach einem Abbruch ohne Antwort: ein Beleg gleicher Art des Kunden mit gleichem Titel und Nettobetrag, angelegt seit dem ersten Versuch.</summary>
    private async Task<string?> FindExistingAsync(LexwareConnection connection, SalesVoucher voucher, string lexwareContactId, CancellationToken cancellationToken)
    {
        var since = DateOnly.FromDateTime((voucher.SendStartedAt ?? lexware.Clock.GetUtcNow()).UtcDateTime).AddDays(-1);
        var taken = await lexware.Db.Set<SalesVoucher>().AsNoTracking().Where(q => q.LexwareId != null).Select(q => q.LexwareId!).ToListAsync(cancellationToken);
        var vouchers = (await lexware.Client.ListVouchersAsync(connection, lexwareContactId, null, 100, cancellationToken))
            .Where(v => v.Type == voucher.VoucherType && (v.Status == "draft" || voucher.Finalized) && (v.VoucherDate is null || v.VoucherDate >= since)
                && !taken.Contains(v.Id))
            .Take(10).ToList();
        var hits = new List<string>();
        foreach (var found in vouchers)
        {
            if (SalesRules.SameAs(voucher, await lexware.Client.GetVoucherAsync(connection, found.Type, found.Id, cancellationToken)))
            {
                hits.Add(found.Id);
            }
        }

        return hits.Count == 1 ? hits[0] : null;
    }

    /// <summary>Gesendeten Beleg der Bauakte zuordnen (erscheint in der Belegliste der Bauakte, wie ein zugeordneter Beleg).</summary>
    private async Task LinkToBauakteAsync(SalesVoucher voucher, Guid userId, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        if (voucher.BauakteId is not { } bauakteId || await db.Set<LexwareVoucherLink>().AnyAsync(l => l.LexwareId == voucher.LexwareId, cancellationToken))
        {
            return;
        }

        var totals = SalesRules.Totals(voucher.Lines, voucher.TaxType);
        db.Add(new LexwareVoucherLink
        {
            Id = Guid.CreateVersion7(), BauakteId = bauakteId, LexwareId = voucher.LexwareId!, VoucherType = voucher.VoucherType,
            Status = voucher.LexwareStatus ?? "draft", Number = voucher.LexwareNumber, Title = LexwareService.Cut(voucher.Title, 300),
            VoucherDate = voucher.VoucherDate ?? lexware.Today(), DueDate = SalesRules.DueDate(voucher), NetAmount = totals.Net, GrossAmount = totals.Gross,
            ContactName = await CustomerNameAsync(voucher.ContactId, cancellationToken), LinkedByUserId = userId, LinkedAt = lexware.Clock.GetUtcNow(),
            SyncedAt = lexware.Clock.GetUtcNow(),
        });
    }

    private async Task<string> CustomerNameAsync(Guid contactId, CancellationToken cancellationToken) =>
        await lexware.Db.Set<Contact>().AsNoTracking().Where(c => c.Id == contactId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken) ?? "unbekannt";

    public static LexwareException NotLinked(string customerName) => new("CUSTOMER_NOT_LINKED",
        $"Der Kunde „{customerName}“ ist noch nicht mit Lexware Office verknüpft. Verknüpfen Sie ihn zuerst am Kunden im Abschnitt „Lexware Office“ (vorhandenen Kontakt verknüpfen oder in Lexware Office anlegen).");
}
