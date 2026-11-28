using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using EstrichOne.Api.Modules.Planning.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Position eines Verkaufsbelegs.</summary>
/// <param name="Kind"><c>item</c> (Artikel, Leistung oder freie Position), <c>text</c> (Textposition), <c>subtotal</c> (Zwischensumme) oder <c>deduction</c> (Abzug einer Abschlagsrechnung).</param>
/// <param name="CatalogItemId">Eintrag aus „Artikel und Leistungen“; mit Lexware-Verknüpfung geht die Position als Artikel an Lexware Office.</param>
/// <param name="LineNet">Positionsbetrag netto bzw. bei Zwischensummen die Summe der Positionen darüber (berechnet).</param>
public sealed record SalesLineDto(
    [property: Required, MaxLength(10)] string Kind,
    [property: MaxLength(255)] string Name = "",
    [property: MaxLength(2000)] string? Description = null,
    Guid? CatalogItemId = null,
    [property: Range(0, 10_000_000)] decimal? Quantity = null,
    [property: MaxLength(50)] string? Unit = null,
    [property: Range(-100_000_000, 100_000_000)] decimal? NetPrice = null,
    [property: Range(0, 100)] decimal? TaxRate = null,
    [property: Range(0, 100)] decimal? DiscountPercent = null,
    decimal? LineNet = null,
    Guid? DeductionVoucherId = null);

/// <summary>Glied der Belegkette (Angebot, Auftragsbestätigung, Abschläge, Schlussrechnung, Gutschrift).</summary>
/// <param name="Id">Beleg in EstrichONE, falls hier erfasst.</param>
/// <param name="Status">Wie in der Übersicht: local, failed, draft, open, overdue, paid, accepted usw.</param>
public sealed record SalesChainItem(Guid? Id, string? LexwareId, string Type, string TypeName, string? Number, DateOnly? Date, string Status, decimal? Gross,
    bool Current, string? LexwareUrl);

/// <summary>Verkaufsbeleg mit Positionen, Summen, Belegkette und Stand.</summary>
/// <param name="Type">quotation, orderconfirmation, invoice oder creditnote.</param>
/// <param name="Purpose">Bei Rechnungen: standard, downPayment (Abschlag) oder final (Schlussrechnung).</param>
/// <param name="State"><c>draft</c>, <c>sending</c>, <c>sent</c> oder <c>failed</c>.</param>
/// <param name="Status">Stand wie in der Übersicht (local, failed, draft, open, overdue, paid, accepted, rejected, voided).</param>
/// <param name="Editable">Bearbeiten möglich (nur vor dem Senden). Lexware Office erlaubt keine Änderung eines dort angelegten Belegs über die API.</param>
/// <param name="CanEdit">Recht „erfassen“ der Belegart.</param>
/// <param name="CanFinalize">Festschreiben möglich: Modul „Verkauf: Festschreiben aus EstrichONE“ und Recht „festschreiben“ der Belegart.</param>
/// <param name="Followers">Belegarten, in die sich der Beleg umwandeln lässt (Belegkette über <c>precedingSalesVoucherId</c>).</param>
public sealed record SalesVoucherResponse(
    Guid Id, string Type, string TypeName, string Purpose, Guid ContactId, string? CustomerName, string? CustomerNumber, string? CustomerAddress,
    bool CustomerLinked, Guid? BauakteId, string? BauakteNumber, string? BauakteTitle, string Title, string? Introduction, string? Remark, DateOnly? VoucherDate,
    DateOnly? ValidUntil, DateOnly? ServiceDate, DateOnly? ServiceEndDate, string TaxType, string? PaymentConditionId, string? PaymentLabel, int? PaymentDuration,
    decimal? PaymentDiscountPercent, int? PaymentDiscountRange, string State, string Status, bool Editable, bool CanEdit, bool CanFinalize, string? LexwareId,
    string? LexwareUrl, string? LexwareNumber, bool Finalized, DateTimeOffset? SentAt, string? SendError, string? SendErrorTechnical, string? SendErrorFix,
    Guid? PrecedingId, decimal? DownPaymentPercent, decimal? OpenAmount, string? PaymentStatus, IReadOnlyList<SalesLineDto> Lines, SalesTotals Totals,
    IReadOnlyList<string> Problems, IReadOnlyList<SalesChainItem> Chain, IReadOnlyList<string> Followers, long Version, DateTimeOffset UpdatedAt);

/// <summary>Zeile der Übersicht „Verkauf“: Beleg aus Lexware Office oder noch nicht gesendeter Beleg aus EstrichONE.</summary>
/// <param name="Id">Beleg in EstrichONE (öffnet den Belegeditor); leer bei Belegen, die nur in Lexware Office erfasst wurden.</param>
/// <param name="Status">local (nur in EstrichONE), failed, draft, open, overdue, paid, paidoff, voided, accepted, rejected.</param>
/// <param name="OpenAmount">Offener Betrag (nur Rechnungen und Gutschriften).</param>
public sealed record SalesListItem(
    Guid? Id, string? LexwareId, string Type, string TypeName, string? Number, string? Title, Guid? ContactId, string? CustomerName, Guid? BauakteId,
    string? BauakteNumber, string? BauakteTitle, DateOnly? VoucherDate, DateOnly? DueDate, decimal? Net, decimal? Gross, decimal? OpenAmount, string Status,
    string? LexwareUrl);

/// <param name="LexwareError">Lexware Office war nicht erreichbar: Gezeigt werden die Belege aus EstrichONE und die zugeordneten Belege.</param>
public sealed record SalesListResponse(IReadOnlyList<SalesListItem> Items, DateTimeOffset LoadedAt, string? LexwareError);

public sealed record CreateSalesVoucherRequest(
    [property: Required] Guid Id, [property: Required, MaxLength(30)] string Type, Guid? ContactId = null, Guid? BauakteId = null);

public sealed record SaveSalesVoucherRequest(
    [property: Required] long Version,
    [property: Required, MaxLength(SalesVoucher.MaxTitle)] string Title,
    [property: MaxLength(SalesVoucher.MaxText)] string? Introduction = null,
    [property: MaxLength(SalesVoucher.MaxText)] string? Remark = null,
    DateOnly? VoucherDate = null,
    DateOnly? ValidUntil = null,
    DateOnly? ServiceDate = null,
    DateOnly? ServiceEndDate = null,
    [property: MaxLength(30)] string TaxType = SalesVoucher.TaxNet,
    [property: MaxLength(100)] string? PaymentConditionId = null,
    [property: MaxLength(300)] string? PaymentLabel = null,
    [property: Range(0, 365)] int? PaymentDuration = null,
    [property: Range(0, 100)] decimal? PaymentDiscountPercent = null,
    [property: Range(0, 365)] int? PaymentDiscountRange = null,
    [property: MaxLength(300)] IReadOnlyList<SalesLineDto>? Lines = null);

/// <param name="Finalize">In Lexware Office festschreiben (Nummer wird vergeben, danach nicht mehr änderbar). Nur mit Modul, Recht und <paramref name="Confirmed"/>.</param>
/// <param name="Confirmed">Ausdrückliche Bestätigung des Festschreibens.</param>
public sealed record SendSalesVoucherRequest(bool Finalize = false, bool Confirmed = false);

/// <summary>Neuer Beleg aus einem vorhandenen (Duplizieren oder Umwandeln in die nächste Belegart).</summary>
public sealed record CopySalesVoucherRequest([property: Required] Guid Id, [property: MaxLength(30)] string? Type = null);

/// <summary>Abschlagsrechnung: Prozent der Auftragssumme oder fester Betrag netto.</summary>
public sealed record DownPaymentRequest([property: Required] Guid Id, [property: Range(0.01, 100)] decimal? Percent = null,
    [property: Range(0.01, 100_000_000)] decimal? Amount = null);

public sealed record FinalInvoiceRequest([property: Required] Guid Id);

/// <summary>Eintrag aus „Artikel und Leistungen“ für die Vorschlagsliste im Beleg.</summary>
public sealed record SalesCatalogItem(
    Guid Id, string Title, string? Description, CatalogItemKind Kind, string? ArticleNumber, string? Unit, decimal NetPrice, decimal TaxRate, bool InLexware);

/// <summary>Auswahlwerte des Belegeditors.</summary>
/// <param name="PaymentConditionsError">Zahlungsbedingungen konnten nicht aus Lexware Office gelesen werden; die Maske bleibt nutzbar.</param>
/// <param name="TaxTypes">Steuerarten der Belegart mit Angabe, ob Lexware Office sie zulässt (Profil und gemerkte Ablehnungen).</param>
/// <param name="Types">Eingeschaltete Belegarten mit den Rechten des Benutzers.</param>
public sealed record SalesOptionsResponse(
    IReadOnlyList<LexwarePaymentCondition> PaymentConditions, string? PaymentConditionsError, IReadOnlyList<SalesCatalogItem> Catalog,
    IReadOnlyList<decimal> TaxRates, int DefaultValidDays, IReadOnlyList<LexwareTaxTypeOption> TaxTypes, bool SmallBusiness, IReadOnlyList<SalesTypeAccess> Types,
    bool FinalizeModule, bool PaymentsModule);

public sealed record SalesTypeAccess(string Type, string Name, bool Enabled, bool CanRead, bool CanEdit, bool CanFinalize);

/// <summary>Rechnungsplan der Bauakte. Beträge netto, „bezahlt“ und „offen“ brutto laut Lexware Office.</summary>
/// <param name="OrderNet">Auftragssumme netto: zuletzt festgeschriebene Auftragsbestätigung, sonst angenommenes Angebot.</param>
/// <param name="OrderSource">Herkunft der Auftragssumme als Text, z. B. „Auftragsbestätigung AB-2026-0007“.</param>
/// <param name="ProgressPercent">Fortschritt laut „Räume und Flächen“ (eingebrachte Fläche zur Gesamtfläche), falls erfasst.</param>
/// <param name="SuggestedPercent">Vorschlag für den nächsten Abschlag in Prozent der Auftragssumme (Fortschritt abzüglich bisheriger Abschläge).</param>
/// <param name="Text">Zusammenfassung als Textzeile.</param>
public sealed record SalesPlanResponse(
    Guid BauakteId, bool CustomerLinked, Guid? CustomerId, string? CustomerName, decimal? OrderNet, decimal? OrderGross, string? OrderSource, Guid? OrderVoucherId,
    decimal DownPaymentsNet, int DownPaymentCount, decimal InvoicedNet, decimal PaidGross, decimal OpenGross, decimal? NotBilledNet, decimal? ProgressPercent,
    decimal? SuggestedPercent, DateOnly? ServiceDate, DateOnly? ServiceEndDate, bool FinalInvoiceExists, string Text, IReadOnlyList<SalesListItem> Vouchers,
    IReadOnlyList<string> Hints);

/// <summary>
/// Modul „Verkauf“: Angebote, Auftragsbestätigungen, Rechnungen (auch Abschlags- und Schlussrechnung) und Gutschriften in
/// EstrichONE erfassen und an Lexware Office senden. Lexware Office bleibt Buchhaltung, Nummernkreis und Belegarchiv.
/// Module „Lexware Office“, „Verkauf“ und das Untermodul der Belegart; Rechte je Belegart ansehen, erfassen, festschreiben.
/// </summary>
internal static class SalesEndpoints
{
    private const string Tag = "Verkauf";
    public const int DefaultValidDays = 30;

    private static readonly string[] AnyRead =
        [Permissions.QuotationsRead, Permissions.QuotationsPrepare, Permissions.OrderConfirmationsRead, Permissions.SalesInvoicesRead, Permissions.CreditNotesRead];

    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/sales").WithTags(Tag).RequireModule(AppModules.Lexware).RequireModule(AppModules.Sales)
            .RequireAnyPermission(AnyRead).AddEndpointFilter(LexwareEndpoints.GuardAsync);
        group.MapGet("/vouchers", ListAsync).WithName("ListSalesVouchers")
            .WithSummary("Übersicht Verkauf: Belege aus Lexware Office (Belegliste) und noch nicht gesendete Belege aus EstrichONE, mit Bauakte, offenem Betrag und Status");
        group.MapGet("/options", OptionsAsync).WithName("GetSalesOptions")
            .WithSummary("Auswahlwerte des Belegeditors: Zahlungsbedingungen, Artikel und Leistungen, Steuersätze, erlaubte Steuerarten, Belegarten mit Rechten");
        group.MapGet("/suggestions", SuggestionsAsync).WithName("GetSalesSuggestions")
            .WithSummary("Positionsvorschläge aus der Bauakte (Fläche aus Räume und Flächen bzw. Kennwerten, Regiestunden aus Einsätzen) und Leistungszeitraum")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/vouchers", CreateAsync).WithName("CreateSalesVoucher")
            .WithSummary("Beleg anlegen (zur Bauakte oder zum Kunden); der Kunde muss mit Lexware Office verknüpft sein")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapGet("/vouchers/{id:guid}", GetAsync).WithName("GetSalesVoucher")
            .WithSummary("Beleg mit Positionen, Summen und Belegkette; refresh=true liest Nummer, Status und Zahlstand aus Lexware Office")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPut("/vouchers/{id:guid}", SaveAsync).WithName("SaveSalesVoucher")
            .WithSummary("Beleg speichern (nur vor dem Senden; Version aus dem letzten Abruf)")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapDelete("/vouchers/{id:guid}", DeleteAsync).WithName("DiscardSalesVoucher")
            .WithSummary("Beleg in EstrichONE verwerfen (nur vor dem Senden; in Lexware Office wird nie gelöscht)")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/vouchers/{id:guid}/send", SendAsync).WithName("SendSalesVoucherToLexware")
            .WithSummary("An Lexware Office senden: als Entwurf, mit finalize und Bestätigung festgeschrieben (Modul und Recht). Mehrfaches Senden legt keinen zweiten Beleg an")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status424FailedDependency)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        group.MapPost("/vouchers/{id:guid}/copy", CopyAsync).WithName("CopySalesVoucher")
            .WithSummary("Duplizieren (ohne type) oder in die nächste Belegart umwandeln (Belegkette über precedingSalesVoucherId)")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapGet("/bauakten/{id:guid}/plan", PlanAsync).WithName("GetBauakteBillingPlan")
            .WithSummary("Rechnungsplan der Bauakte: Auftragssumme, Abschläge, bezahlt, offen, noch nicht berechnet, Vorschlag für den nächsten Abschlag")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/bauakten/{id:guid}/down-payment", DownPaymentAsync).WithName("CreateDownPaymentInvoice")
            .WithSummary("Abschlagsrechnung als Beleg in EstrichONE anlegen (Prozent der Auftragssumme oder Betrag netto)")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapPost("/bauakten/{id:guid}/final-invoice", FinalInvoiceAsync).WithName("CreateFinalInvoice")
            .WithSummary("Schlussrechnung anlegen: Positionen des Auftrags, bisherige Abschläge als Abzugszeilen mit Nummer und Datum")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    // ---------- Rechte und Module ----------

    private sealed record Access(ModuleSettings Modules, IReadOnlySet<string> Granted)
    {
        public bool Enabled(string type) => Modules.IsEnabled(SalesTypes.Module(type));

        public bool CanRead(string type) => Enabled(type)
            && (Granted.Contains(SalesTypes.ReadPermission(type)) || Granted.Contains(SalesTypes.EditPermission(type)));

        public bool CanEdit(string type) => Enabled(type) && Granted.Contains(SalesTypes.EditPermission(type));

        public bool CanFinalize(string type) => CanEdit(type) && Modules.IsEnabled(AppModules.SalesFinalize) && Granted.Contains(SalesTypes.FinalizePermission(type));
    }

    private static async Task<Access> AccessAsync(ISystemSettings system, IUserPermissions permissions, CancellationToken cancellationToken) =>
        new((await system.GetAsync(cancellationToken)).Modules, await permissions.GetAsync(cancellationToken));

    private static ProblemHttpResult Denied(string type, string what) => TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Keine Berechtigung",
        detail: $"Ihnen fehlt das Recht, {what} ({SalesTypes.Name(type)}), oder das Untermodul der Belegart ist ausgeschaltet.", extensions: Code("PERMISSION_DENIED"));

    // ---------- Abbildung ----------

    private static string Key(SalesVoucherState state) => state switch
    {
        SalesVoucherState.Sending => "sending",
        SalesVoucherState.Sent => "sent",
        SalesVoucherState.Failed => "failed",
        _ => "draft",
    };

    private static async Task<bool> VisibleAsync(SalesVoucher voucher, IBauakteAccess access, CancellationToken cancellationToken) =>
        voucher.BauakteId is not { } bauakteId || await access.CanReadAsync(bauakteId, cancellationToken);

    private static string? Address(Contact c)
    {
        var a = c.BillingAddress;
        var street = a?.Street ?? c.Street;
        var place = string.Join(' ', new[] { a?.PostalCode ?? c.PostalCode, a?.City ?? c.City }.Where(p => !string.IsNullOrWhiteSpace(p)));
        var text = string.Join(", ", new[] { street, place }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return text.Length == 0 ? null : text;
    }

    private static SalesLineDto Line(SalesVoucherLine l, IReadOnlyList<SalesVoucherLine> ordered, int index) => new(l.Kind, l.Name, l.Description, l.CatalogItemId,
        l.Quantity, l.Unit, l.NetPrice, l.TaxRate, l.DiscountPercent,
        l.Kind == SalesVoucherLine.KindSubtotal ? SalesRules.Subtotal(ordered, index) : l.Priced ? SalesRules.LineNet(l) : null, l.DeductionVoucherId);

    private static async Task<SalesVoucherResponse> ResponseAsync(LexwareService lexware, SalesVoucher voucher, Access access, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var row = await lexware.SettingsAsync(cancellationToken);
        var customer = await db.Set<Contact>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == voucher.ContactId, cancellationToken);
        var linked = await db.Set<LexwareContactLink>().AnyAsync(l => l.ContactId == voucher.ContactId, cancellationToken);
        var bauakte = voucher.BauakteId is { } id
            ? await db.Set<Bauakte>().AsNoTracking().Where(b => b.Id == id).Select(b => new { b.Number, b.Title }).FirstOrDefaultAsync(cancellationToken)
            : null;
        var ordered = voucher.Lines.OrderBy(l => l.Position).ToList();
        var problems = voucher.Editable ? SalesRules.Problems(voucher, LexwareTaxTypes.State(row)) : [];
        if (voucher.Editable && !linked)
        {
            problems.Insert(0, SalesVoucherService.NotLinked(customer?.Name ?? "unbekannt").Message);
        }

        var type = voucher.VoucherType;
        return new SalesVoucherResponse(voucher.Id, type, SalesTypes.Name(type, voucher.Purpose), voucher.Purpose, voucher.ContactId, customer?.Name,
            customer?.CustomerNumber, customer is null ? null : Address(customer), linked, voucher.BauakteId, bauakte?.Number, bauakte?.Title, voucher.Title,
            voucher.Introduction, voucher.Remark, voucher.VoucherDate, voucher.ValidUntil, voucher.ServiceDate, voucher.ServiceEndDate, voucher.TaxType,
            voucher.PaymentConditionId, voucher.PaymentLabel, voucher.PaymentDuration, voucher.PaymentDiscountPercent, voucher.PaymentDiscountRange,
            Key(voucher.State), SalesRules.StatusKey(voucher, lexware.Today()), voucher.Editable, access.CanEdit(type), access.CanFinalize(type), voucher.LexwareId,
            voucher.LexwareId is null ? null : lexware.VoucherUrl(row, type, voucher.LexwareId, voucher.LexwareStatus ?? "draft"), voucher.LexwareNumber,
            voucher.Finalized, voucher.SentAt, voucher.SendError, voucher.SendErrorTechnical, voucher.SendErrorFix, voucher.PrecedingId, voucher.DownPaymentPercent,
            voucher.OpenAmount, voucher.PaymentStatus, ordered.Select((l, i) => Line(l, ordered, i)).ToList(), SalesRules.Totals(voucher.Lines, voucher.TaxType),
            problems, await ChainAsync(lexware, row, voucher, cancellationToken),
            SalesTypes.Followers(type).Where(access.CanEdit).ToList(), voucher.Version, voucher.UpdatedAt);
    }

    /// <summary>Belegkette: Vorgänger bis zum Anfang, dann alle Nachfolger (in EstrichONE erfasst), jeweils mit Stand.</summary>
    private static async Task<IReadOnlyList<SalesChainItem>> ChainAsync(LexwareService lexware, LexwareSettingsRow row, SalesVoucher voucher,
        CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var root = voucher;
        for (var guard = 0; root.PrecedingId is { } precedingId && guard < 10; guard++)
        {
            var preceding = await db.Set<SalesVoucher>().AsNoTracking().SingleOrDefaultAsync(v => v.Id == precedingId, cancellationToken);
            if (preceding is null)
            {
                break;
            }

            root = preceding;
        }

        var chain = new List<SalesVoucher> { root };
        var frontier = new List<Guid> { root.Id };
        for (var depth = 0; depth < 5 && frontier.Count > 0; depth++)
        {
            var next = await db.Set<SalesVoucher>().AsNoTracking().Where(v => v.PrecedingId != null && frontier.Contains(v.PrecedingId.Value))
                .OrderBy(v => v.CreatedAt).ToListAsync(cancellationToken);
            chain.AddRange(next.Where(n => chain.All(c => c.Id != n.Id)));
            frontier = next.Select(n => n.Id).ToList();
        }

        if (chain.Count == 1)
        {
            return [];
        }

        var today = lexware.Today();
        return chain.Select(v => new SalesChainItem(v.Id, v.LexwareId, v.VoucherType, SalesTypes.Name(v.VoucherType, v.Purpose), v.LexwareNumber, v.VoucherDate,
            SalesRules.StatusKey(v, today), SalesRules.Totals(v.Lines, v.TaxType).Gross, v.Id == voucher.Id,
            v.LexwareId is null ? null : lexware.VoucherUrl(row, v.VoucherType, v.LexwareId, v.LexwareStatus ?? "draft"))).ToList();
    }

    // ---------- Übersicht ----------

    private static async Task<Ok<SalesListResponse>> ListAsync(LexwareService lexware, IBauakteAccess bauakten, ISystemSettings system, IUserPermissions permissions,
        CancellationToken cancellationToken, [FromQuery] string? type = null, [FromQuery] Guid? bauakteId = null, [FromQuery] Guid? contactId = null,
        [FromQuery] bool refresh = false)
    {
        var access = await AccessAsync(system, permissions, cancellationToken);
        var types = SalesTypes.All.Where(access.CanRead).Where(t => type is null || t == type).ToList();
        var (items, error) = await ItemsAsync(lexware, bauakten, types, bauakteId, contactId, refresh, cancellationToken);
        return TypedResults.Ok(new SalesListResponse(items, lexware.Clock.GetUtcNow(), error));
    }

    /// <summary>
    /// Zeilen der Übersicht: Belegliste aus Lexware Office (kurz zwischengespeichert) plus Belege aus EstrichONE, die dort noch
    /// nicht angelegt sind. Bauakte aus der Zuordnung bzw. dem Beleg in EstrichONE; Belege unsichtbarer Bauakten entfallen.
    /// </summary>
    private static async Task<(List<SalesListItem> Items, string? Error)> ItemsAsync(LexwareService lexware, IBauakteAccess bauakten, IReadOnlyList<string> types,
        Guid? bauakteId, Guid? contactId, bool refresh, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var row = await lexware.SettingsAsync(cancellationToken);
        var local = await db.Set<SalesVoucher>().AsNoTracking().Where(v => types.Contains(v.VoucherType))
            .Where(v => bauakteId == null || v.BauakteId == bauakteId).Where(v => contactId == null || v.ContactId == contactId)
            .OrderByDescending(v => v.UpdatedAt).Take(500).ToListAsync(cancellationToken);
        var links = await db.Set<LexwareVoucherLink>().AsNoTracking().Where(l => types.Contains(l.VoucherType))
            .Where(l => bauakteId == null || l.BauakteId == bauakteId).ToListAsync(cancellationToken);
        var linkByLexware = links.ToDictionary(l => l.LexwareId);
        var localByLexware = local.Where(v => v.LexwareId != null).ToDictionary(v => v.LexwareId!);
        var bauakteIds = local.Select(v => v.BauakteId).OfType<Guid>().Concat(links.Select(l => l.BauakteId)).Distinct().ToList();
        var bauakteInfo = await db.Set<Bauakte>().AsNoTracking().Where(b => bauakteIds.Contains(b.Id))
            .Select(b => new { b.Id, b.Number, b.Title }).ToDictionaryAsync(b => b.Id, cancellationToken);
        var visible = new Dictionary<Guid, bool>();
        foreach (var id in bauakteIds)
        {
            visible[id] = await bauakten.CanReadAsync(id, cancellationToken);
        }

        var contactIds = local.Select(v => v.ContactId).Distinct().ToList();
        var names = await db.Set<Contact>().AsNoTracking().Where(c => contactIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        var contactByLexware = await db.Set<LexwareContactLink>().AsNoTracking().ToDictionaryAsync(l => l.LexwareContactId, l => l.ContactId, cancellationToken);
        var today = lexware.Today();
        var items = new List<SalesListItem>();
        string? error = null;

        IReadOnlyList<LexwareListVoucher> remote = [];
        if (row.Configured && bauakteId is null)
        {
            try
            {
                remote = await lexware.SalesListAsync(row, types, refresh, cancellationToken);
            }
            catch (LexwareException exception)
            {
                error = exception.Message;
            }
        }

        var lexwareContact = contactId is { } c
            ? await db.Set<LexwareContactLink>().AsNoTracking().Where(l => l.ContactId == c).Select(l => l.LexwareContactId).FirstOrDefaultAsync(cancellationToken)
            : null;
        var seen = new HashSet<string>();
        foreach (var v in remote)
        {
            if (contactId is not null && v.ContactId != lexwareContact)
            {
                continue;
            }

            seen.Add(v.Id);
            localByLexware.TryGetValue(v.Id, out var own);
            linkByLexware.TryGetValue(v.Id, out var link);
            var bauakte = own?.BauakteId ?? link?.BauakteId;
            if (bauakte is { } b && !visible.GetValueOrDefault(b))
            {
                continue;
            }

            var info = bauakte is { } bb ? bauakteInfo.GetValueOrDefault(bb) : null;
            var payable = LexwareVoucherTypes.Payable(v.Type);
            items.Add(new SalesListItem(own?.Id, v.Id, v.Type, SalesTypes.Name(v.Type, own?.Purpose), v.Number, own?.Title ?? link?.Title,
                v.ContactId is { } lc && contactByLexware.TryGetValue(lc, out var cid) ? cid : own?.ContactId, v.ContactName, bauakte, info?.Number, info?.Title,
                v.VoucherDate, v.DueDate, own is null ? link?.NetAmount : SalesRules.Totals(own.Lines, own.TaxType).Net, v.GrossAmount,
                payable ? v.OpenAmount : null, lexware.DisplayStatus(v.Type, v.Status, v.DueDate), lexware.VoucherUrl(row, v.Type, v.Id, v.Status)));
        }

        // Zugeordnete Belege, die die Belegliste nicht geliefert hat (Bauakte gefiltert oder Lexware nicht erreichbar).
        foreach (var link in links.Where(l => !seen.Contains(l.LexwareId) && visible.GetValueOrDefault(l.BauakteId)))
        {
            localByLexware.TryGetValue(link.LexwareId, out var own);
            if (contactId is not null && own?.ContactId != contactId)
            {
                continue;
            }

            seen.Add(link.LexwareId);
            var info = bauakteInfo.GetValueOrDefault(link.BauakteId);
            items.Add(new SalesListItem(own?.Id, link.LexwareId, link.VoucherType, SalesTypes.Name(link.VoucherType, own?.Purpose), link.Number, link.Title,
                own?.ContactId, link.ContactName, link.BauakteId, info?.Number, info?.Title, link.VoucherDate, link.DueDate, link.NetAmount, link.GrossAmount,
                LexwareVoucherTypes.Payable(link.VoucherType) ? own?.OpenAmount : null, lexware.DisplayStatus(link.VoucherType, link.Status, link.DueDate),
                lexware.VoucherUrl(row, link.VoucherType, link.LexwareId, link.Status)));
        }

        // Belege aus EstrichONE, die noch nicht in Lexware Office angelegt sind.
        foreach (var v in local.Where(v => v.LexwareId is null || !seen.Contains(v.LexwareId)))
        {
            if (v.BauakteId is { } b && !visible.GetValueOrDefault(b))
            {
                continue;
            }

            var info = v.BauakteId is { } bb ? bauakteInfo.GetValueOrDefault(bb) : null;
            var totals = SalesRules.Totals(v.Lines, v.TaxType);
            items.Add(new SalesListItem(v.Id, v.LexwareId, v.VoucherType, SalesTypes.Name(v.VoucherType, v.Purpose), v.LexwareNumber, v.Title, v.ContactId,
                names.GetValueOrDefault(v.ContactId), v.BauakteId, info?.Number, info?.Title, v.VoucherDate, SalesRules.DueDate(v), totals.Net, totals.Gross,
                null, SalesRules.StatusKey(v, today),
                v.LexwareId is null ? null : lexware.VoucherUrl(row, v.VoucherType, v.LexwareId, v.LexwareStatus ?? "draft")));
        }

        return (items.OrderByDescending(i => i.VoucherDate ?? DateOnly.MaxValue).ThenBy(i => i.Number).ToList(), error);
    }

    // ---------- Auswahlwerte und Vorschläge ----------

    private static async Task<Ok<SalesOptionsResponse>> OptionsAsync(LexwareService lexware, ISystemSettings system, IUserPermissions permissions,
        CancellationToken cancellationToken, [FromQuery] string? type = null)
    {
        var access = await AccessAsync(system, permissions, cancellationToken);
        var row = await lexware.SettingsAsync(cancellationToken);
        IReadOnlyList<LexwarePaymentCondition> conditions = [];
        string? error = null;
        try
        {
            conditions = await lexware.PaymentConditionsAsync(row, cancellationToken);
        }
        catch (LexwareException exception)
        {
            error = "Die Zahlungsbedingungen konnten nicht aus Lexware Office gelesen werden. Sie können den Text der Zahlungsbedingung selbst eingeben. " + exception.Message;
        }

        var catalog = await lexware.Db.Set<CatalogItem>().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.Title)
            .Select(c => new SalesCatalogItem(c.Id, c.Title, c.Description, c.Kind, c.ArticleNumber, c.Unit, c.NetPrice, c.TaxRate,
                c.LexwareId != null && c.RemoteState == null))
            .ToListAsync(cancellationToken);
        var taxTypes = await LexwareTaxTypeStore.StateAsync(lexware, cancellationToken);
        var voucherType = SalesTypes.Known(type) ? type! : SalesTypes.Quotation;
        return TypedResults.Ok(new SalesOptionsResponse(conditions, error, catalog, SalesRules.TaxRates, DefaultValidDays,
            LexwareTaxTypes.Options(voucherType, taxTypes), LexwareTaxTypes.IsSmallBusiness(taxTypes.SmallBusiness, taxTypes.ProfileTaxType),
            SalesTypes.All.Select(t => new SalesTypeAccess(t, SalesTypes.Name(t), access.Enabled(t), access.CanRead(t), access.CanEdit(t), access.CanFinalize(t)))
                .ToList(),
            access.Modules.IsEnabled(AppModules.SalesFinalize), access.Modules.IsEnabled(AppModules.SalesPayments)));
    }

    private static async Task<Results<Ok<SalesSuggestionsResponse>, ProblemHttpResult>> SuggestionsAsync([FromQuery] Guid bauakteId, LexwareService lexware,
        IBauakteAccess access, IBauakteRooms rooms, IBauakteAssignments assignments, CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(bauakteId, cancellationToken)
            || await lexware.Db.Set<Bauakte>().AsNoTracking().SingleOrDefaultAsync(b => b.Id == bauakteId, cancellationToken) is not { } bauakte)
        {
            return BauakteWriteProblems.NotFound();
        }

        var catalog = await lexware.Db.Set<CatalogItem>().AsNoTracking().Where(c => c.IsActive).ToListAsync(cancellationToken);
        return TypedResults.Ok(SalesSuggestions.Build(bauakte, await rooms.AreasAsync(bauakteId, cancellationToken), catalog,
            await assignments.ListAsync(bauakteId, cancellationToken)));
    }

    // ---------- Anlegen, Bearbeiten, Senden ----------

    private static string Intro(string type, Bauakte? bauakte)
    {
        var text = type switch
        {
            SalesTypes.OrderConfirmation => "Vielen Dank für Ihren Auftrag. Wir bestätigen Ihnen folgende Leistungen.",
            SalesTypes.Invoice => "Für unsere Leistungen erlauben wir uns, Ihnen Folgendes in Rechnung zu stellen.",
            SalesTypes.CreditNote => "Wir schreiben Ihnen folgenden Betrag gut.",
            _ => "Vielen Dank für Ihre Anfrage. Gerne bieten wir Ihnen folgende Leistungen an.",
        };
        if (bauakte is null)
        {
            return text;
        }

        var address = string.Join(' ', new[] { bauakte.Street, bauakte.PostalCode, bauakte.City }.Where(p => !string.IsNullOrWhiteSpace(p)));
        return $"Bauvorhaben: {string.Join(", ", new[] { bauakte.Number, bauakte.Title, address }.Where(p => !string.IsNullOrWhiteSpace(p)))}\n{text}";
    }

    private static async Task<Results<Created<SalesVoucherResponse>, Ok<SalesVoucherResponse>, ProblemHttpResult>> CreateAsync(CreateSalesVoucherRequest request,
        LexwareService lexware, IBauakteAccess bauakten, IBauakteAssignments assignments, ISystemSettings system, IUserPermissions permissions,
        ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var access = await AccessAsync(system, permissions, cancellationToken);
        if (!SalesTypes.Known(request.Type))
        {
            return Invalid("Die Belegart ist unbekannt.");
        }

        if (!access.CanEdit(request.Type))
        {
            return Denied(request.Type, "Belege zu erfassen");
        }

        var db = lexware.Db;
        if (await db.Set<SalesVoucher>().SingleOrDefaultAsync(q => q.Id == request.Id, cancellationToken) is { } existing)
        {
            return TypedResults.Ok(await ResponseAsync(lexware, existing, access, cancellationToken)); // gleiche ID: kein zweiter Beleg
        }

        var (bauakte, customer, problem) = await TargetAsync(lexware, bauakten, request.BauakteId, request.ContactId, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var voucher = await NewVoucherAsync(lexware, assignments, request.Id, request.Type, customer!, bauakte, currentUser.UserId, cancellationToken);
        db.Add(voucher);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/sales/vouchers/{voucher.Id}", await ResponseAsync(lexware, voucher, access, cancellationToken));
    }

    /// <summary>Bauakte (sichtbar) und Kunde (mit Lexware verknüpft) für einen neuen Beleg prüfen.</summary>
    private static async Task<(Bauakte? Bauakte, Contact? Customer, ProblemHttpResult? Problem)> TargetAsync(LexwareService lexware, IBauakteAccess bauakten,
        Guid? bauakteId, Guid? contactId, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        Bauakte? bauakte = null;
        if (bauakteId is { } id)
        {
            bauakte = await bauakten.CanReadAsync(id, cancellationToken)
                ? await db.Set<Bauakte>().AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken)
                : null;
            if (bauakte is null)
            {
                return (null, null, BauakteWriteProblems.NotFound());
            }
        }

        var customerId = contactId ?? bauakte?.CustomerId;
        if (customerId is null)
        {
            return (bauakte, null, TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: Tag,
                detail: "Der Bauakte ist noch kein Kunde zugeordnet. Ordnen Sie zuerst einen Kunden zu.", extensions: Code("SALES_NO_CUSTOMER")));
        }

        if (await db.Set<Contact>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == customerId, cancellationToken) is not { } customer)
        {
            return (bauakte, null, TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Kontakt nicht gefunden", extensions: Code("CONTACT_NOT_FOUND")));
        }

        if (!await db.Set<LexwareContactLink>().AnyAsync(l => l.ContactId == customer.Id, cancellationToken))
        {
            return (bauakte, customer, TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Tag,
                detail: SalesVoucherService.NotLinked(customer.Name).Message,
                extensions: new Dictionary<string, object?> { ["code"] = "LEXWARE_CUSTOMER_NOT_LINKED", ["contactId"] = customer.Id }));
        }

        return (bauakte, customer, null);
    }

    private static async Task<SalesVoucher> NewVoucherAsync(LexwareService lexware, IBauakteAssignments assignments, Guid id, string type, Contact customer,
        Bauakte? bauakte, Guid userId, CancellationToken cancellationToken)
    {
        var row = await lexware.SettingsAsync(cancellationToken);
        var voucher = new SalesVoucher
        {
            Id = id, VoucherType = type, ContactId = customer.Id, BauakteId = bauakte?.Id, Title = SalesTypes.Name(type),
            Introduction = LexwareService.Cut(Intro(type, bauakte), SalesVoucher.MaxText), CreatedByUserId = userId,
            TaxType = LexwareTaxTypes.Default(LexwareTaxTypes.State(row)),
            Remark = type == SalesTypes.Quotation ? "Wir freuen uns auf Ihren Auftrag." : null,
        };
        if (type == SalesTypes.Quotation)
        {
            voucher.ValidUntil = lexware.Today().AddDays(DefaultValidDays);
        }
        else if (bauakte is not null && SalesRules.ServicePeriod(await assignments.ListAsync(bauakte.Id, cancellationToken), lexware.Today()) is { } period)
        {
            // Leistungszeitraum aus den bisherigen Einsätzen der Bauakte (Vorschlag, in der Maske änderbar).
            voucher.ServiceDate = period.Start;
            voucher.ServiceEndDate = period.End == period.Start ? null : period.End;
        }
        else if (type == SalesTypes.Invoice)
        {
            voucher.ServiceDate = lexware.Today();
        }

        // Zahlungsbedingung: Vorgabe der Organisation in Lexware Office (ohne Fehler, falls nicht erreichbar).
        if (type != SalesTypes.CreditNote)
        {
            try
            {
                if ((await lexware.PaymentConditionsAsync(row, cancellationToken)).FirstOrDefault(c => c.IsDefault) is { } payment)
                {
                    voucher.PaymentConditionId = payment.Id;
                    voucher.PaymentLabel = payment.Label;
                    voucher.PaymentDuration = payment.Duration;
                    voucher.PaymentDiscountPercent = payment.DiscountPercent;
                    voucher.PaymentDiscountRange = payment.DiscountRange;
                }
            }
            catch (LexwareException)
            {
                // Die Maske bietet die Auswahl erneut an.
            }
        }

        return voucher;
    }

    private static async Task<SalesVoucher?> FindAsync(LexwareService lexware, IBauakteAccess access, Guid id, CancellationToken cancellationToken) =>
        await lexware.Db.Set<SalesVoucher>().SingleOrDefaultAsync(q => q.Id == id, cancellationToken) is { } voucher && await VisibleAsync(voucher, access, cancellationToken)
            ? voucher : null;

    private static async Task<Results<Ok<SalesVoucherResponse>, ProblemHttpResult>> GetAsync(Guid id, LexwareService lexware, SalesVoucherService sales,
        IBauakteAccess bauakten, ISystemSettings system, IUserPermissions permissions, CancellationToken cancellationToken, [FromQuery] bool refresh = false)
    {
        var access = await AccessAsync(system, permissions, cancellationToken);
        if (await FindAsync(lexware, bauakten, id, cancellationToken) is not { } voucher || !access.CanRead(voucher.VoucherType))
        {
            return NotFound();
        }

        if (refresh && voucher.LexwareId is not null)
        {
            var row = await lexware.SettingsAsync(cancellationToken);
            if (row.Configured)
            {
                await sales.RefreshAsync(lexware.Connection(row), voucher, cancellationToken, payments: access.Modules.IsEnabled(AppModules.SalesPayments));
                await lexware.Db.SaveChangesAsync(cancellationToken);
            }
        }

        return TypedResults.Ok(await ResponseAsync(lexware, voucher, access, cancellationToken));
    }

    private static async Task<Results<Ok<SalesVoucherResponse>, ProblemHttpResult>> SaveAsync(Guid id, SaveSalesVoucherRequest request, LexwareService lexware,
        IBauakteAccess bauakten, ISystemSettings system, IUserPermissions permissions, CancellationToken cancellationToken)
    {
        var access = await AccessAsync(system, permissions, cancellationToken);
        if (await FindAsync(lexware, bauakten, id, cancellationToken) is not { } voucher || !access.CanRead(voucher.VoucherType))
        {
            return NotFound();
        }

        if (!access.CanEdit(voucher.VoucherType))
        {
            return Denied(voucher.VoucherType, "Belege zu bearbeiten");
        }

        if (!voucher.Editable)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Tag, detail: SentText(voucher), extensions: Code("LEXWARE_SALES_SENT"));
        }

        if (request.Version != voucher.Version)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Geändert",
                detail: "Der Beleg wurde inzwischen geändert. Bitte laden Sie ihn neu.", extensions: Code("VERSION_CONFLICT"));
        }

        if (!LexwareTaxTypes.Supported.Contains(request.TaxType))
        {
            return Invalid("Die Steuerart ist unbekannt.");
        }

        var lines = request.Lines ?? [];
        if (lines.FirstOrDefault(l => !SalesVoucherLine.Kinds.Contains(l.Kind)) is not null)
        {
            return Invalid("Unbekannte Art einer Position.");
        }

        voucher.Title = string.IsNullOrWhiteSpace(request.Title) ? SalesTypes.Name(voucher.VoucherType, voucher.Purpose) : request.Title.Trim();
        voucher.Introduction = LexwareService.Cut(request.Introduction, SalesVoucher.MaxText);
        voucher.Remark = LexwareService.Cut(request.Remark, SalesVoucher.MaxText);
        voucher.VoucherDate = request.VoucherDate;
        voucher.ValidUntil = voucher.VoucherType == SalesTypes.Quotation ? request.ValidUntil : null;
        voucher.ServiceDate = voucher.VoucherType == SalesTypes.Quotation ? null : request.ServiceDate;
        voucher.ServiceEndDate = voucher.VoucherType == SalesTypes.Quotation ? null : request.ServiceEndDate;
        voucher.TaxType = request.TaxType;
        voucher.PaymentConditionId = LexwareService.Cut(request.PaymentConditionId, 100);
        voucher.PaymentLabel = LexwareService.Cut(request.PaymentLabel, 300);
        voucher.PaymentDuration = request.PaymentDuration;
        voucher.PaymentDiscountPercent = request.PaymentDiscountPercent;
        voucher.PaymentDiscountRange = request.PaymentDiscountRange;
        if (voucher.State == SalesVoucherState.Failed && !voucher.SendUncertain)
        {
            voucher.State = SalesVoucherState.Draft;
            voucher.SendError = null;
            voucher.SendErrorTechnical = null;
            voucher.SendErrorFix = null;
        }

        ReplaceLines(lexware, voucher, lines);
        lexware.Db.Entry(voucher).State = EntityState.Modified; // auch nur geänderte Positionen erhöhen die Version des Belegs
        await lexware.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ResponseAsync(lexware, voucher, access, cancellationToken));
    }

    private static void ReplaceLines(LexwareService lexware, SalesVoucher voucher, IEnumerable<SalesLineDto> lines)
    {
        lexware.Db.RemoveRange(voucher.Lines);
        voucher.Lines.Clear();
        var position = 0;
        foreach (var line in lines)
        {
            var kind = line.Kind;
            var priced = kind is SalesVoucherLine.KindItem or SalesVoucherLine.KindDeduction;
            voucher.Lines.Add(new SalesVoucherLine
            {
                Id = Guid.CreateVersion7(), DraftId = voucher.Id, Position = ++position, Kind = kind,
                CatalogItemId = kind == SalesVoucherLine.KindItem ? line.CatalogItemId : null,
                DeductionVoucherId = kind == SalesVoucherLine.KindDeduction ? line.DeductionVoucherId : null,
                Name = (string.IsNullOrWhiteSpace(line.Name) && kind == SalesVoucherLine.KindSubtotal ? "Zwischensumme" : line.Name ?? string.Empty).Trim(),
                Description = LexwareService.Cut(line.Description, 2000),
                Quantity = kind == SalesVoucherLine.KindItem ? line.Quantity : kind == SalesVoucherLine.KindDeduction ? 1m : null,
                Unit = kind == SalesVoucherLine.KindItem ? LexwareService.Cut(line.Unit, 50) : kind == SalesVoucherLine.KindDeduction ? "pauschal" : null,
                NetPrice = priced ? line.NetPrice : null, TaxRate = priced ? line.TaxRate ?? 19m : null,
                DiscountPercent = kind != SalesVoucherLine.KindItem || line.DiscountPercent is null or 0 ? null : line.DiscountPercent,
            });
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid id, LexwareService lexware, IBauakteAccess bauakten, IAuditLog audit,
        ISystemSettings system, IUserPermissions permissions, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var access = await AccessAsync(system, permissions, cancellationToken);
        if (await FindAsync(lexware, bauakten, id, cancellationToken) is not { } voucher || !access.CanRead(voucher.VoucherType))
        {
            return NotFound();
        }

        if (!access.CanEdit(voucher.VoucherType))
        {
            return Denied(voucher.VoucherType, "Belege zu verwerfen");
        }

        if (!voucher.Editable || voucher.SendUncertain)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Tag,
                detail: voucher.SendUncertain
                    ? "Der letzte Sendeversuch ist ohne eindeutige Antwort abgebrochen. Senden Sie den Beleg bitte erneut; EstrichONE prüft dabei, ob er in Lexware Office schon angelegt wurde."
                    : SentText(voucher),
                extensions: Code("LEXWARE_SALES_SENT"));
        }

        lexware.Db.Remove(voucher);
        audit.Record("lexware.sales_discarded", currentUser.UserId, voucher.BauakteId is null ? "contact" : "bauakte", (voucher.BauakteId ?? voucher.ContactId).ToString(),
            new Dictionary<string, string?> { ["type"] = voucher.VoucherType, ["title"] = voucher.Title });
        await lexware.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<SalesVoucherResponse>, ProblemHttpResult>> SendAsync(Guid id, SendSalesVoucherRequest? request, LexwareService lexware,
        SalesVoucherService sales, IBauakteAccess bauakten, ISystemSettings system, IUserPermissions permissions, ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var access = await AccessAsync(system, permissions, cancellationToken);
        if (await FindAsync(lexware, bauakten, id, cancellationToken) is not { } voucher || !access.CanRead(voucher.VoucherType))
        {
            return NotFound();
        }

        if (!access.CanEdit(voucher.VoucherType))
        {
            return Denied(voucher.VoucherType, "Belege an Lexware Office zu senden");
        }

        var finalize = request?.Finalize == true;
        if (finalize && !access.CanFinalize(voucher.VoucherType))
        {
            return Denied(voucher.VoucherType, "Belege festzuschreiben");
        }

        if (finalize && request?.Confirmed != true)
        {
            return Invalid("Bitte bestätigen Sie das Festschreiben. Danach vergibt Lexware Office die Nummer, und der Beleg lässt sich nicht mehr ändern.");
        }

        lexware.Db.ChangeTracker.Clear();
        try
        {
            var result = await sales.SendAsync(voucher.Id, currentUser.UserId, finalize, cancellationToken);
            return TypedResults.Ok(await ResponseAsync(lexware, result.Voucher, access, cancellationToken));
        }
        catch (LexwareException exception) when (exception.Code == "SALES_INCOMPLETE")
        {
            return Invalid(exception.Message);
        }
    }

    private static async Task<Results<Created<SalesVoucherResponse>, ProblemHttpResult>> CopyAsync(Guid id, CopySalesVoucherRequest request, LexwareService lexware,
        IBauakteAccess bauakten, IBauakteAssignments assignments, ISystemSettings system, IUserPermissions permissions, ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var access = await AccessAsync(system, permissions, cancellationToken);
        if (await FindAsync(lexware, bauakten, id, cancellationToken) is not { } source || !access.CanRead(source.VoucherType))
        {
            return NotFound();
        }

        var type = request.Type ?? source.VoucherType;
        if (!SalesTypes.Known(type) || (type != source.VoucherType && !SalesTypes.Followers(source.VoucherType).Contains(type)))
        {
            return Invalid($"Aus {SalesTypes.Accusative(source.VoucherType)} lässt sich kein Beleg dieser Art erstellen.");
        }

        if (!access.CanEdit(type))
        {
            return Denied(type, "Belege zu erfassen");
        }

        if (await lexware.Db.Set<SalesVoucher>().AnyAsync(v => v.Id == request.Id, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Tag, detail: "Diesen Beleg gibt es bereits.", extensions: Code("SALES_EXISTS"));
        }

        var convert = type != source.VoucherType;
        if (convert && source.LexwareId is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Tag,
                detail: $"Senden Sie {SalesTypes.Accusative(source.VoucherType)} zuerst an Lexware Office. Die Belegkette entsteht dort über den vorhergehenden Beleg.",
                extensions: Code("SALES_PRECEDING_NOT_SENT"));
        }

        var (bauakte, customer, problem) = await TargetAsync(lexware, bauakten, source.BauakteId, source.ContactId, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var copy = await NewVoucherAsync(lexware, assignments, request.Id, type, customer!, bauakte, currentUser.UserId, cancellationToken);
        copy.Title = convert ? SalesTypes.Name(type) : source.Title;
        copy.Introduction = convert ? copy.Introduction : source.Introduction;
        copy.Remark = convert ? copy.Remark : source.Remark;
        copy.TaxType = source.TaxType;
        copy.Purpose = convert ? SalesTypes.PurposeStandard : source.Purpose;
        if (!convert)
        {
            copy.ValidUntil = source.VoucherType == SalesTypes.Quotation ? copy.ValidUntil : null;
            copy.ServiceDate = source.ServiceDate ?? copy.ServiceDate;
            copy.ServiceEndDate = source.ServiceEndDate ?? copy.ServiceEndDate;
        }

        if (source.PaymentLabel is not null && type != SalesTypes.CreditNote)
        {
            (copy.PaymentConditionId, copy.PaymentLabel, copy.PaymentDuration, copy.PaymentDiscountPercent, copy.PaymentDiscountRange) =
                (source.PaymentConditionId, source.PaymentLabel, source.PaymentDuration, source.PaymentDiscountPercent, source.PaymentDiscountRange);
        }

        if (convert)
        {
            copy.PrecedingId = source.Id;
            copy.PrecedingLexwareId = source.LexwareId;
        }

        var ordered = source.Lines.OrderBy(l => l.Position).ToList();
        ReplaceLines(lexware, copy, ordered.Where(l => type == SalesTypes.Invoice || l.Kind != SalesVoucherLine.KindDeduction)
            .Select((l, i) => Line(l, ordered, i)));
        lexware.Db.Add(copy);
        await lexware.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/sales/vouchers/{copy.Id}", await ResponseAsync(lexware, copy, access, cancellationToken));
    }

    // ---------- Rechnungsplan der Bauakte ----------

    private static async Task<Results<Ok<SalesPlanResponse>, ProblemHttpResult>> PlanAsync(Guid id, LexwareService lexware, IBauakteAccess bauakten,
        IBauakteRooms rooms, IBauakteAssignments assignments, ISystemSettings system, IUserPermissions permissions, CancellationToken cancellationToken)
    {
        var access = await AccessAsync(system, permissions, cancellationToken);
        if (!await bauakten.CanReadAsync(id, cancellationToken)
            || await lexware.Db.Set<Bauakte>().AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken) is not { } bauakte)
        {
            return BauakteWriteProblems.NotFound();
        }

        return TypedResults.Ok(await SalesPlans.BuildAsync(lexware, bauakte, access.CanRead, await rooms.AreasAsync(id, cancellationToken),
            await assignments.ListAsync(id, cancellationToken), cancellationToken));
    }

    private static async Task<Results<Created<SalesVoucherResponse>, ProblemHttpResult>> DownPaymentAsync(Guid id, DownPaymentRequest request,
        LexwareService lexware, IBauakteAccess bauakten, IBauakteRooms rooms, IBauakteAssignments assignments, ISystemSettings system,
        IUserPermissions permissions, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var access = await AccessAsync(system, permissions, cancellationToken);
        if (!access.CanEdit(SalesTypes.Invoice))
        {
            return Denied(SalesTypes.Invoice, "Rechnungen zu erfassen");
        }

        var (bauakte, customer, problem) = await TargetAsync(lexware, bauakten, id, null, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        if (await lexware.Db.Set<SalesVoucher>().AnyAsync(v => v.Id == request.Id, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Tag, detail: "Diesen Beleg gibt es bereits.", extensions: Code("SALES_EXISTS"));
        }

        var plan = await SalesPlans.BuildAsync(lexware, bauakte!, access.CanRead, await rooms.AreasAsync(id, cancellationToken),
            await assignments.ListAsync(id, cancellationToken), cancellationToken);
        if (request.Amount is null && plan.OrderNet is null)
        {
            return Invalid("Für diese Bauakte gibt es noch keine Auftragssumme (angenommenes Angebot oder Auftragsbestätigung). Geben Sie den Betrag des Abschlags an.");
        }

        var percent = request.Amount is null ? request.Percent ?? plan.SuggestedPercent ?? 30m : (decimal?)null;
        var amount = request.Amount ?? Math.Round(plan.OrderNet!.Value * percent!.Value / 100m, 2, MidpointRounding.AwayFromZero);
        var order = plan.OrderVoucherId is { } orderId ? await lexware.Db.Set<SalesVoucher>().AsNoTracking().SingleOrDefaultAsync(v => v.Id == orderId, cancellationToken) : null;
        var number = plan.DownPaymentCount + 1;
        var voucher = await NewVoucherAsync(lexware, assignments, request.Id, SalesTypes.Invoice, customer!, bauakte, currentUser.UserId, cancellationToken);
        voucher.Purpose = SalesTypes.PurposeDownPayment;
        voucher.DownPaymentPercent = percent;
        voucher.Title = $"{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}. Abschlagsrechnung";
        voucher.TaxType = order?.TaxType ?? voucher.TaxType;
        voucher.PrecedingId = order?.LexwareId is null ? null : order.Id;
        voucher.PrecedingLexwareId = order?.LexwareId;
        var description = string.Join(". ", new[]
        {
            plan.OrderSource is null ? null : $"Grundlage: {plan.OrderSource}, Auftragssumme netto {(plan.OrderNet is { } net ? SalesRules.Euro(net) : "nicht bekannt")}",
            plan.ProgressPercent is { } progress ? $"Leistungsstand laut Räume und Flächen: {progress.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("de-DE"))} %" : null,
        }.Where(p => p is not null));
        var rate = order?.Lines.FirstOrDefault(l => l.Kind == SalesVoucherLine.KindItem)?.TaxRate ?? 19m;
        ReplaceLines(lexware, voucher,
        [
            new SalesLineDto(SalesVoucherLine.KindItem,
                percent is { } p ? $"{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}. Abschlag: {p.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("de-DE"))} % der Auftragssumme"
                    : $"{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}. Abschlag",
                description.Length == 0 ? null : description, null, 1m, "pauschal", amount, rate),
        ]);
        lexware.Db.Add(voucher);
        await lexware.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/sales/vouchers/{voucher.Id}", await ResponseAsync(lexware, voucher, access, cancellationToken));
    }

    private static async Task<Results<Created<SalesVoucherResponse>, ProblemHttpResult>> FinalInvoiceAsync(Guid id, FinalInvoiceRequest request,
        LexwareService lexware, IBauakteAccess bauakten, IBauakteRooms rooms, IBauakteAssignments assignments, ISystemSettings system,
        IUserPermissions permissions, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var access = await AccessAsync(system, permissions, cancellationToken);
        if (!access.CanEdit(SalesTypes.Invoice))
        {
            return Denied(SalesTypes.Invoice, "Rechnungen zu erfassen");
        }

        var (bauakte, customer, problem) = await TargetAsync(lexware, bauakten, id, null, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        if (await lexware.Db.Set<SalesVoucher>().AnyAsync(v => v.Id == request.Id, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Tag, detail: "Diesen Beleg gibt es bereits.", extensions: Code("SALES_EXISTS"));
        }

        var plan = await SalesPlans.BuildAsync(lexware, bauakte!, access.CanRead, await rooms.AreasAsync(id, cancellationToken),
            await assignments.ListAsync(id, cancellationToken), cancellationToken);
        var db = lexware.Db;
        var order = plan.OrderVoucherId is { } orderId ? await db.Set<SalesVoucher>().AsNoTracking().SingleOrDefaultAsync(v => v.Id == orderId, cancellationToken) : null;
        var voucher = await NewVoucherAsync(lexware, assignments, request.Id, SalesTypes.Invoice, customer!, bauakte, currentUser.UserId, cancellationToken);
        voucher.Purpose = SalesTypes.PurposeFinal;
        voucher.Title = "Schlussrechnung";
        voucher.TaxType = order?.TaxType ?? voucher.TaxType;
        voucher.PrecedingId = order?.LexwareId is null ? null : order.Id;
        voucher.PrecedingLexwareId = order?.LexwareId;
        var lines = new List<SalesLineDto>();
        if (order is not null)
        {
            var ordered = order.Lines.OrderBy(l => l.Position).Where(l => l.Kind != SalesVoucherLine.KindDeduction).ToList();
            lines.AddRange(ordered.Select((l, i) => Line(l, ordered, i)));
        }
        else if (plan.OrderNet is { } net)
        {
            lines.Add(new SalesLineDto(SalesVoucherLine.KindItem, $"Leistungen laut {plan.OrderSource}", null, null, 1m, "pauschal", net, 19m));
        }

        // Bisherige Abschläge als Abzugszeilen (je Abschlag und Steuersatz) mit Nummer und Datum.
        var downPayments = await db.Set<SalesVoucher>().AsNoTracking()
            .Where(v => v.BauakteId == id && v.VoucherType == SalesTypes.Invoice && v.Purpose == SalesTypes.PurposeDownPayment && v.State == SalesVoucherState.Sent
                && v.LexwareStatus != "voided")
            .OrderBy(v => v.SentAt).ToListAsync(cancellationToken);
        if (lines.Count > 0 && downPayments.Count > 0 && lines[^1].Kind != SalesVoucherLine.KindSubtotal)
        {
            lines.Add(new SalesLineDto(SalesVoucherLine.KindSubtotal, "Gesamtleistung"));
        }

        foreach (var down in downPayments)
        {
            foreach (var group in SalesRules.Totals(down.Lines, down.TaxType).TaxLines)
            {
                var when = down.VoucherDate ?? (down.SentAt is { } sent ? DateOnly.FromDateTime(sent.UtcDateTime) : null);
                lines.Add(new SalesLineDto(SalesVoucherLine.KindDeduction,
                    $"Abzüglich {down.Title} Nr. {down.LexwareNumber ?? "ohne Nummer"}{(when is { } d ? " vom " + SalesRules.Date(d) : string.Empty)}",
                    null, null, 1m, "pauschal", -group.Net, group.Rate, null, null, down.Id));
            }
        }

        if (lines.Count == 0)
        {
            return Invalid("Für diese Bauakte gibt es noch keinen Auftrag (angenommenes Angebot oder Auftragsbestätigung) und keine Abschläge. Legen Sie eine normale Rechnung an.");
        }

        voucher.Introduction = LexwareService.Cut(voucher.Introduction + (downPayments.Count > 0
            ? "\nBereits gestellte Abschlagsrechnungen sind unten abgezogen."
            : string.Empty), SalesVoucher.MaxText);
        ReplaceLines(lexware, voucher, lines);
        db.Add(voucher);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/sales/vouchers/{voucher.Id}", await ResponseAsync(lexware, voucher, access, cancellationToken));
    }

    // ---------- Texte ----------

    private static string SentText(SalesVoucher voucher) => voucher.State == SalesVoucherState.Sending
        ? "Dieser Beleg wird gerade an Lexware Office gesendet."
        : "Dieser Beleg ist bereits in Lexware Office angelegt und hier schreibgeschützt. Lexware Office erlaubt keine Änderung über die Schnittstelle; ändern Sie ihn bitte in Lexware Office (In Lexware öffnen).";

    private static Dictionary<string, object?> Code(string code) => new() { ["code"] = code };

    private static ProblemHttpResult Invalid(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Ungültige Angaben",
        detail: detail, extensions: Code("VALIDATION_FAILED"));

    private static ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht gefunden",
        detail: "Diesen Beleg gibt es nicht.", extensions: Code("SALES_NOT_FOUND"));
}

/// <summary>Rechnungsplan der Bauakte aus den Belegen in EstrichONE und den zugeordneten Belegen aus Lexware Office.</summary>
internal static class SalesPlans
{
    public static async Task<SalesPlanResponse> BuildAsync(LexwareService lexware, Bauakte bauakte, Func<string, bool> canRead,
        IReadOnlyList<BauakteRoomArea> rooms, IReadOnlyList<BauakteAssignmentInfo> assignments, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var local = await db.Set<SalesVoucher>().AsNoTracking().Where(v => v.BauakteId == bauakte.Id).ToListAsync(cancellationToken);
        var links = await db.Set<LexwareVoucherLink>().AsNoTracking().Where(l => l.BauakteId == bauakte.Id).ToListAsync(cancellationToken);
        var linked = bauakte.CustomerId is { } customerId && await db.Set<LexwareContactLink>().AnyAsync(l => l.ContactId == customerId, cancellationToken);
        var customerName = bauakte.CustomerId is { } cid
            ? await db.Set<Contact>().AsNoTracking().Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken)
            : null;
        var byLexware = local.Where(v => v.LexwareId != null).ToDictionary(v => v.LexwareId!);
        string Status(LexwareVoucherLink l) => lexware.DisplayStatus(l.VoucherType, l.Status, l.DueDate);

        // Auftragssumme: Auftragsbestätigung (festgeschrieben, nicht storniert), sonst angenommenes Angebot; jeweils die neueste.
        var order = links.Where(l => l.VoucherType == SalesTypes.OrderConfirmation && l.Status is not ("draft" or "voided")).OrderByDescending(l => l.VoucherDate).FirstOrDefault()
            ?? links.Where(l => l.VoucherType == SalesTypes.Quotation && l.Status == "accepted").OrderByDescending(l => l.VoucherDate).FirstOrDefault();
        SalesVoucher? orderVoucher = order is not null ? byLexware.GetValueOrDefault(order.LexwareId) : null;
        var orderNet = order?.NetAmount ?? (orderVoucher is null ? null : SalesRules.Totals(orderVoucher.Lines, orderVoucher.TaxType).Net);
        var orderSource = order is null ? null : $"{SalesTypes.Name(order.VoucherType)} {order.Number ?? "ohne Nummer"}";

        var invoices = links.Where(l => l.VoucherType == SalesTypes.Invoice && l.Status != "voided").ToList();
        var credits = links.Where(l => l.VoucherType == SalesTypes.CreditNote && l.Status != "voided").ToList();
        bool IsDown(LexwareVoucherLink l) => byLexware.GetValueOrDefault(l.LexwareId)?.Purpose == SalesTypes.PurposeDownPayment;
        var downLinks = invoices.Where(IsDown).ToList();
        var localDowns = local.Where(v => v.Purpose == SalesTypes.PurposeDownPayment && v.LexwareId is null).ToList();
        var downNet = downLinks.Sum(l => l.NetAmount ?? 0m);
        var invoicedNet = invoices.Sum(l => l.NetAmount ?? 0m) - credits.Sum(l => l.NetAmount ?? 0m);
        var paid = invoices.Where(l => l.Status is "paid" or "paidoff").Sum(l => l.GrossAmount ?? 0m);
        var open = invoices.Where(l => Status(l) is "open" or "overdue").Sum(l => byLexware.GetValueOrDefault(l.LexwareId)?.OpenAmount ?? l.GrossAmount ?? 0m);
        decimal? notBilled = orderNet is { } o ? Math.Max(0m, o - invoicedNet) : null;
        var finalExists = local.Any(v => v.Purpose == SalesTypes.PurposeFinal && v.LexwareStatus != "voided");

        // Fortschritt: eingebrachte Fläche laut „Räume und Flächen“ zur Gesamtfläche.
        var measured = rooms.Where(r => r.AreaSquareMeters is > 0).ToList();
        var total = measured.Sum(r => r.AreaSquareMeters!.Value);
        decimal? progress = total > 0 ? Math.Round(measured.Where(r => r.Laid).Sum(r => r.AreaSquareMeters!.Value) / total * 100m, 1) : null;
        decimal? suggested = null;
        if (orderNet is > 0 && progress is { } p)
        {
            var already = Math.Round(downNet / orderNet.Value * 100m, 1);
            suggested = Math.Max(0m, Math.Round(p - already, 0));
        }

        var period = SalesRules.ServicePeriod(assignments, lexware.Today());
        var hints = new List<string>();
        if (!linked)
        {
            hints.Add("Der Kunde der Bauakte ist nicht mit Lexware Office verknüpft. Verknüpfen Sie ihn am Kunden, dann lassen sich Belege erstellen.");
        }

        if (order is null)
        {
            hints.Add("Es gibt noch keine Auftragssumme: Sie ergibt sich aus einer Auftragsbestätigung oder einem angenommenen Angebot dieser Bauakte.");
        }

        if (progress is null)
        {
            hints.Add("Ohne Flächen in „Räume und Flächen“ schlägt EstrichONE keinen Abschlag nach Leistungsstand vor; Sie können den Prozentsatz selbst wählen.");
        }

        var parts = new List<string>();
        parts.Add(orderNet is { } on ? $"Auftragssumme {SalesRules.Euro(on)} netto" : "Auftragssumme noch offen");
        parts.Add($"{downLinks.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)} {(downLinks.Count == 1 ? "Abschlag" : "Abschläge")} {SalesRules.Euro(downNet)}");
        parts.Add($"bezahlt {SalesRules.Euro(paid)}");
        parts.Add($"offen {SalesRules.Euro(open)}");
        if (notBilled is { } nb)
        {
            parts.Add($"noch nicht berechnet {SalesRules.Euro(nb)}");
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        var today = lexware.Today();
        var vouchers = links.Where(l => canRead(l.VoucherType)).Select(l =>
        {
            var own = byLexware.GetValueOrDefault(l.LexwareId);
            return new SalesListItem(own?.Id, l.LexwareId, l.VoucherType, SalesTypes.Name(l.VoucherType, own?.Purpose), l.Number, l.Title, own?.ContactId, l.ContactName,
                l.BauakteId, bauakte.Number, bauakte.Title, l.VoucherDate, l.DueDate, l.NetAmount, l.GrossAmount,
                LexwareVoucherTypes.Payable(l.VoucherType) ? own?.OpenAmount : null, Status(l), lexware.VoucherUrl(row, l.VoucherType, l.LexwareId, l.Status));
        }).Concat(local.Where(v => v.LexwareId is null && canRead(v.VoucherType)).Select(v =>
        {
            var totals = SalesRules.Totals(v.Lines, v.TaxType);
            return new SalesListItem(v.Id, null, v.VoucherType, SalesTypes.Name(v.VoucherType, v.Purpose), null, v.Title, v.ContactId, customerName, v.BauakteId,
                bauakte.Number, bauakte.Title, v.VoucherDate, SalesRules.DueDate(v), totals.Net, totals.Gross, null, SalesRules.StatusKey(v, today), null);
        })).OrderBy(i => i.VoucherDate ?? DateOnly.MaxValue).ToList();

        return new SalesPlanResponse(bauakte.Id, linked, bauakte.CustomerId, customerName, orderNet, order?.GrossAmount, orderSource, orderVoucher?.Id, downNet,
            downLinks.Count + localDowns.Count, invoicedNet, paid, open, notBilled, progress, suggested, period?.Start, period?.End, finalExists,
            string.Join(", ", parts), vouchers, hints);
    }
}
