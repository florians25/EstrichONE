using System.Globalization;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Summen eines Angebots: netto, Steuer je Satz, brutto (Vorschau; maßgeblich rechnet Lexware Office).</summary>
public sealed record QuotationTotals(decimal Net, decimal Tax, decimal Gross, IReadOnlyList<QuotationTaxLine> TaxLines);

/// <summary>Steuer zu einem Steuersatz.</summary>
public sealed record QuotationTaxLine(decimal Rate, decimal Net, decimal Tax);

/// <summary>Berechnungen und Prüfungen eines Angebotsentwurfs als reine Funktionen (mit Unit-Tests geprüft).</summary>
public static class QuotationRules
{
    public static readonly decimal[] TaxRates = [19m, 7m, 0m];

    /// <summary>Positionsbetrag netto: Menge mal Einzelpreis abzüglich Rabatt, auf Cent gerundet.</summary>
    public static decimal LineNet(decimal? quantity, decimal? price, decimal? discountPercent) =>
        Math.Round((quantity ?? 0m) * (price ?? 0m) * (1m - Math.Clamp(discountPercent ?? 0m, 0m, 100m) / 100m), 2, MidpointRounding.AwayFromZero);

    /// <summary>Summen: Steuer je Steuersatz auf die Summe der Positionen gerechnet; bei § 13b UStG keine Steuer (Steuerschuld beim Leistungsempfänger).</summary>
    public static QuotationTotals Totals(IEnumerable<QuotationDraftLine> lines, string taxType)
    {
        var items = lines.Where(l => l.Kind == QuotationDraftLine.KindItem).ToList();
        var reverse = LexwareTaxTypes.WithoutTax(taxType);
        var groups = items.GroupBy(l => reverse ? 0m : l.TaxRate ?? 19m).OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var net = g.Sum(l => LineNet(l.Quantity, l.NetPrice, l.DiscountPercent));
                return new QuotationTaxLine(g.Key, net, Math.Round(net * g.Key / 100m, 2, MidpointRounding.AwayFromZero));
            }).ToList();
        var totalNet = groups.Sum(g => g.Net);
        var totalTax = groups.Sum(g => g.Tax);
        return new QuotationTotals(totalNet, totalTax, totalNet + totalTax, groups);
    }

    /// <summary>Fehler, die das Senden verhindern (leer = sendebereit).</summary>
    public static List<string> Problems(QuotationDraft draft, LexwareTaxTypeState? taxTypes = null)
    {
        var problems = new List<string>();
        if (taxTypes is not null && LexwareTaxTypes.Problem(LexwareVoucherTypes.Quotation, draft.TaxType, taxTypes, "das Angebot") is { } taxProblem)
        {
            problems.Add(taxProblem);
        }

        if (string.IsNullOrWhiteSpace(draft.Title))
        {
            problems.Add("Bitte geben Sie einen Titel an.");
        }

        var items = draft.Lines.Where(l => l.Kind == QuotationDraftLine.KindItem).ToList();
        if (items.Count == 0)
        {
            problems.Add("Das Angebot braucht mindestens eine Position mit Menge und Preis.");
        }

        foreach (var line in draft.Lines.OrderBy(l => l.Position))
        {
            var label = $"Position {line.Position.ToString(CultureInfo.InvariantCulture)}";
            if (string.IsNullOrWhiteSpace(line.Name))
            {
                problems.Add($"{label}: Bitte geben Sie eine Bezeichnung an.");
            }

            if (line.Kind == QuotationDraftLine.KindItem)
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

                if (!LexwareTaxTypes.WithoutTax(draft.TaxType) && line.TaxRate is { } rate && !TaxRates.Contains(rate))
                {
                    problems.Add($"{label}: Der Steuersatz muss 19, 7 oder 0 Prozent betragen.");
                }
            }
        }

        if (draft.ValidUntil is null)
        {
            problems.Add("Bitte geben Sie an, bis wann das Angebot gültig ist.");
        }

        return problems;
    }

    /// <summary>Entwurf als Angebot für Lexware Office. Katalogeinträge mit Lexware-Verknüpfung gehen als Artikel, sonst als freie Position.</summary>
    public static LexwareQuotationDraft ToLexware(QuotationDraft draft, string lexwareContactId, DateOnly today,
        IReadOnlyDictionary<Guid, CatalogItem> catalog) => new(
        lexwareContactId, today, draft.ValidUntil, draft.Title, draft.Introduction, draft.Remark, draft.TaxType,
        draft.PaymentLabel is { Length: > 0 } label
            ? new LexwarePaymentCondition(draft.PaymentConditionId, label, draft.PaymentDuration, draft.PaymentDiscountPercent, draft.PaymentDiscountRange, false)
            : null,
        draft.Lines.OrderBy(l => l.Position).Select(l =>
        {
            if (l.Kind == QuotationDraftLine.KindText)
            {
                return new LexwareQuotationLine("text", null, l.Name, l.Description, null, null, null, null, null);
            }

            var article = l.CatalogItemId is { } id && catalog.TryGetValue(id, out var item) && item.LexwareId is not null && item.RemoteState is null ? item : null;
            var type = article is null ? "custom" : article.Kind == CatalogItemKind.Product ? "material" : "service";
            return new LexwareQuotationLine(type, article?.LexwareId, l.Name, l.Description, l.Quantity, l.Unit, l.NetPrice, l.TaxRate, l.DiscountPercent);
        }).ToList());

    /// <summary>Gleicher Beleg wie dieser Entwurf? Für die Suche nach einem trotz Fehlermeldung angelegten Angebot.</summary>
    public static bool SameAs(QuotationDraft draft, LexwareVoucher voucher) =>
        voucher.Type == LexwareVoucherTypes.Quotation && voucher.Status == "draft"
        && string.Equals(LexwareContactSync.Clean(voucher.Title), LexwareContactSync.Clean(draft.Title), StringComparison.Ordinal)
        && (voucher.NetAmount is null || voucher.NetAmount == Totals(draft.Lines, draft.TaxType).Net);
}

/// <summary>Vorschlag für eine Angebotsposition aus der Bauakte (nur Vorschlag; übernommen wird erst nach Bestätigung).</summary>
/// <param name="Source"><c>rooms</c> (Summe aus „Räume und Flächen“) oder <c>project</c> (Kennwert „Fläche gesamt“).</param>
public sealed record QuotationLineSuggestion(string Source, string Hint, string Name, string? Description, decimal Quantity, string Unit, Guid? CatalogItemId = null,
    decimal? NetPrice = null, decimal? TaxRate = null);

public sealed record QuotationSuggestionsResponse(IReadOnlyList<QuotationLineSuggestion> Lines, IReadOnlyList<string> Hints);

/// <summary>Vorschläge für Positionen aus der Bauakte.</summary>
public static class QuotationSuggestions
{
    private static string Area(decimal value) => value.ToString("#,##0.##", CultureInfo.GetCultureInfo("de-DE")) + " m²";

    public static QuotationSuggestionsResponse Build(Bauakte bauakte, IReadOnlyList<BauakteRoomArea> rooms, IReadOnlyList<CatalogItem> catalog)
    {
        var lines = new List<QuotationLineSuggestion>();
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

            lines.Add(new QuotationLineSuggestion("rooms", $"Summe aus „Räume und Flächen“ ({measured.Count.ToString(CultureInfo.InvariantCulture)} Räume)",
                match?.Title ?? "Estrich liefern und einbauen", Join(details, "Räume: " + list), Math.Round(total, 2), "m²", match?.Id, match?.NetPrice, match?.TaxRate));
        }
        else if (project.TotalArea is > 0 and var area)
        {
            lines.Add(new QuotationLineSuggestion("project", "Kennwert „Fläche gesamt“ der Bauakte", match?.Title ?? "Estrich liefern und einbauen",
                Join(details, null), Math.Round(area, 2), "m²", match?.Id, match?.NetPrice, match?.TaxRate));
        }
        else
        {
            hints.Add("In der Bauakte ist noch keine Fläche erfasst (Räume und Flächen oder Kennwert „Fläche gesamt“).");
        }

        // Raumumfänge führt EstrichONE nicht: Ohne sie gibt es keinen Vorschlag für Randdämmstreifen.
        hints.Add("Randdämmstreifen werden nicht vorgeschlagen, weil in der Bauakte keine Raumumfänge erfasst sind. Bitte ergänzen Sie die Position bei Bedarf selbst.");
        return new QuotationSuggestionsResponse(lines, hints);
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
/// <param name="Adopted">Das Angebot war bereits in Lexware Office angelegt (vorheriger Versuch ohne eindeutige Antwort) und wurde übernommen statt erneut gesendet.</param>
public sealed record QuotationSendResult(QuotationDraft Draft, bool AlreadySent, bool Adopted);

/// <summary>
/// Angebote (Beta): Entwürfe in EstrichONE vorbereiten und als Entwurf in Lexware Office anlegen. Doppeltes Senden ist
/// ausgeschlossen: Der Entwurf wird vor dem Aufruf in der Datenbank auf „wird gesendet“ gesetzt (nur aus „Entwurf“ oder
/// „fehlgeschlagen“), der Aufruf wird nicht wiederholt, und nach einem Abbruch ohne eindeutige Antwort sucht der nächste
/// Versuch zuerst in Lexware Office nach dem Angebot. Nach dem Senden ist der Entwurf schreibgeschützt.
/// </summary>
public sealed class LexwareQuotationService(LexwareService lexware, IAuditLog audit)
{
    /// <summary>Nach dieser Zeit gilt ein hängender Sendevorgang (Server neu gestartet) als abgebrochen ohne eindeutige Antwort.</summary>
    public static readonly TimeSpan StaleSending = TimeSpan.FromMinutes(10);

    public async Task<QuotationSendResult> SendAsync(Guid draftId, Guid userId, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var gate = lexware.QuotationGate;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var draft = await db.Set<QuotationDraft>().SingleAsync(q => q.Id == draftId, cancellationToken);
            if (draft.State == QuotationDraftState.Sent)
            {
                return new QuotationSendResult(draft, true, false);
            }

            var now = lexware.Clock.GetUtcNow();
            var stale = now - StaleSending;
            if (draft.State == QuotationDraftState.Sending && draft.SendStartedAt > stale)
            {
                throw new LexwareException("QUOTATION_SENDING", "Dieses Angebot wird gerade an Lexware Office gesendet. Bitte warten Sie einen Moment.");
            }

            if (QuotationRules.Problems(draft, LexwareTaxTypes.State(await lexware.SettingsAsync(cancellationToken))) is { Count: > 0 } problems)
            {
                throw new LexwareException("QUOTATION_INCOMPLETE", string.Join(" ", problems));
            }

            var lexwareContactId = await db.Set<LexwareContactLink>().AsNoTracking().Where(l => l.ContactId == draft.ContactId).Select(l => l.LexwareContactId)
                .FirstOrDefaultAsync(cancellationToken) ?? throw NotLinked(await CustomerNameAsync(draft.ContactId, cancellationToken));
            var row = await lexware.SettingsAsync(cancellationToken);
            var connection = lexware.Connection(row);

            // Sperre in der Datenbank: Nur wer den Entwurf von „Entwurf“ bzw. „fehlgeschlagen“ auf „wird gesendet“ setzt, sendet.
            var uncertain = draft.SendUncertain || draft.State == QuotationDraftState.Sending;
            var claimed = await db.Set<QuotationDraft>().Where(q => q.Id == draftId && q.Version == draft.Version
                    && (q.State == QuotationDraftState.Draft || q.State == QuotationDraftState.Failed
                        || (q.State == QuotationDraftState.Sending && q.SendStartedAt < stale)))
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.State, QuotationDraftState.Sending).SetProperty(q => q.SendStartedAt, now), cancellationToken);
            if (claimed == 0)
            {
                db.ChangeTracker.Clear();
                var current = await db.Set<QuotationDraft>().SingleAsync(q => q.Id == draftId, cancellationToken);
                return current.State == QuotationDraftState.Sent
                    ? new QuotationSendResult(current, true, false)
                    : throw new LexwareException("QUOTATION_SENDING", "Dieses Angebot wird gerade an Lexware Office gesendet oder wurde inzwischen geändert. Bitte laden Sie es neu.");
            }

            db.ChangeTracker.Clear();
            draft = await db.Set<QuotationDraft>().SingleAsync(q => q.Id == draftId, cancellationToken);
            string? lexwareId = null;
            var adopted = false;
            try
            {
                if (uncertain)
                {
                    lexwareId = await FindExistingAsync(connection, draft, lexwareContactId, cancellationToken);
                    adopted = lexwareId is not null;
                }

                if (lexwareId is null)
                {
                    var catalogIds = draft.Lines.Select(l => l.CatalogItemId).OfType<Guid>().Distinct().ToList();
                    var catalog = await db.Set<CatalogItem>().AsNoTracking().Where(c => catalogIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
                    lexwareId = (await lexware.Client.CreateQuotationDraftAsync(connection, QuotationRules.ToLexware(draft, lexwareContactId, lexware.Today(), catalog),
                        cancellationToken)).Id;
                }
            }
            catch (LexwareException exception)
            {
                var failure = exception;
                if (exception.Code == LexwareErrors.TaxTypeNotAllowed)
                {
                    // Abgelehnte Steuerart merken (je Belegart): Die Maske bietet sie danach nicht mehr an, bis sie erneut geprüft wird.
                    var taxType = exception.RejectedTaxType ?? draft.TaxType;
                    await LexwareTaxTypeStore.RememberRejectionAsync(lexware, LexwareVoucherTypes.Quotation, taxType, CancellationToken.None);
                    failure = new LexwareException(exception.Code, LexwareErrors.TaxTypeText(taxType, "das Angebot"), exception.Status)
                    {
                        Issues = exception.Issues.Select(i => i.Field == "taxType" ? i with { Text = LexwareErrors.TaxTypeText(taxType, "das Angebot") } : i).ToList(),
                        Technical = exception.Technical, RejectedTaxType = taxType,
                    };
                }

                // Ohne eindeutige Antwort kann Lexware Office das Angebot angelegt haben: Der nächste Versuch sucht zuerst danach.
                draft.State = QuotationDraftState.Failed;
                draft.SendUncertain = exception.Code is "TIMEOUT" or "OFFLINE" or "UNAVAILABLE" || (draft.SendUncertain && exception.Code is "RATE_LIMITED");
                draft.SendError = LexwareService.Cut(failure.Message, 2000);
                draft.SendErrorTechnical = LexwareService.Cut(failure.Technical, 1000);
                draft.SendErrorFix = failure.Fix;
                await db.SaveChangesAsync(CancellationToken.None);
                if (failure != exception)
                {
                    throw failure;
                }

                throw;
            }

            draft.State = QuotationDraftState.Sent;
            draft.LexwareId = lexwareId;
            draft.SentAt = now;
            draft.SentByUserId = userId;
            draft.SendError = null;
            draft.SendErrorTechnical = null;
            draft.SendErrorFix = null;
            draft.SendUncertain = false;
            await LinkToBauakteAsync(row, connection, draft, userId, cancellationToken);
            audit.Record("lexware.quotation_sent", userId, draft.BauakteId is null ? "contact" : "bauakte", (draft.BauakteId ?? draft.ContactId).ToString(),
                new Dictionary<string, string?> { ["title"] = draft.Title, ["lexwareId"] = lexwareId, ["adopted"] = adopted ? "true" : null });
            await db.SaveChangesAsync(CancellationToken.None);
            lexware.ForgetContacts(); // Belegliste des Kunden neu laden
            return new QuotationSendResult(draft, false, adopted);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Nach einem Abbruch ohne Antwort: ein Angebotsentwurf des Kunden mit gleichem Titel und Nettobetrag, angelegt seit dem ersten Versuch.</summary>
    private async Task<string?> FindExistingAsync(LexwareConnection connection, QuotationDraft draft, string lexwareContactId, CancellationToken cancellationToken)
    {
        var since = DateOnly.FromDateTime((draft.SendStartedAt ?? lexware.Clock.GetUtcNow()).UtcDateTime).AddDays(-1);
        var taken = await lexware.Db.Set<QuotationDraft>().AsNoTracking().Where(q => q.LexwareId != null).Select(q => q.LexwareId!).ToListAsync(cancellationToken);
        var vouchers = (await lexware.Client.ListVouchersAsync(connection, lexwareContactId, null, 100, cancellationToken))
            .Where(v => v.Type == LexwareVoucherTypes.Quotation && v.Status == "draft" && (v.VoucherDate is null || v.VoucherDate >= since) && !taken.Contains(v.Id))
            .Take(10).ToList();
        var hits = new List<string>();
        foreach (var voucher in vouchers)
        {
            if (QuotationRules.SameAs(draft, await lexware.Client.GetVoucherAsync(connection, voucher.Type, voucher.Id, cancellationToken)))
            {
                hits.Add(voucher.Id);
            }
        }

        return hits.Count == 1 ? hits[0] : null;
    }

    /// <summary>Gesendetes Angebot der Bauakte zuordnen (erscheint in der Belegliste der Bauakte, wie ein zugeordneter Beleg).</summary>
    private async Task LinkToBauakteAsync(LexwareSettingsRow row, LexwareConnection connection, QuotationDraft draft, Guid userId, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        if (draft.BauakteId is not { } bauakteId || await db.Set<LexwareVoucherLink>().AnyAsync(l => l.LexwareId == draft.LexwareId, cancellationToken))
        {
            return;
        }

        var totals = QuotationRules.Totals(draft.Lines, draft.TaxType);
        var link = new LexwareVoucherLink
        {
            Id = Guid.CreateVersion7(), BauakteId = bauakteId, LexwareId = draft.LexwareId!, VoucherType = LexwareVoucherTypes.Quotation, Status = "draft",
            Title = LexwareService.Cut(draft.Title, 300), VoucherDate = lexware.Today(), DueDate = draft.ValidUntil, NetAmount = totals.Net, GrossAmount = totals.Gross,
            ContactName = await CustomerNameAsync(draft.ContactId, cancellationToken), LinkedByUserId = userId, LinkedAt = lexware.Clock.GetUtcNow(),
        };
        try
        {
            lexware.Apply(link, await lexware.Client.GetVoucherAsync(connection, LexwareVoucherTypes.Quotation, link.LexwareId, cancellationToken));
        }
        catch (LexwareException)
        {
            // Angelegt ist das Angebot; Status und Beträge gleicht die Belegliste beim nächsten Öffnen ab.
        }

        db.Add(link);
    }

    private async Task<string> CustomerNameAsync(Guid contactId, CancellationToken cancellationToken) =>
        await lexware.Db.Set<Contact>().AsNoTracking().Where(c => c.Id == contactId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken) ?? "unbekannt";

    public static LexwareException NotLinked(string customerName) => new("CUSTOMER_NOT_LINKED",
        $"Der Kunde „{customerName}“ ist noch nicht mit Lexware Office verknüpft. Verknüpfen Sie ihn zuerst am Kunden im Abschnitt „Lexware Office“ (vorhandenen Kontakt verknüpfen oder in Lexware Office anlegen).");
}
