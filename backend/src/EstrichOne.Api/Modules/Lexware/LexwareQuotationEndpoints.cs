using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Position eines Angebotsentwurfs.</summary>
/// <param name="Kind"><c>item</c> (Artikel, Leistung oder freie Position) oder <c>text</c> (Textposition ohne Betrag).</param>
/// <param name="CatalogItemId">Eintrag aus „Artikel und Leistungen“; mit Lexware-Verknüpfung geht die Position als Artikel an Lexware Office.</param>
public sealed record QuotationLineDto(
    [property: Required, MaxLength(10)] string Kind,
    [property: Required, MaxLength(255)] string Name,
    [property: MaxLength(2000)] string? Description = null,
    Guid? CatalogItemId = null,
    [property: Range(0, 10_000_000)] decimal? Quantity = null,
    [property: MaxLength(50)] string? Unit = null,
    [property: Range(-10_000_000, 10_000_000)] decimal? NetPrice = null,
    [property: Range(0, 100)] decimal? TaxRate = null,
    [property: Range(0, 100)] decimal? DiscountPercent = null,
    decimal? LineNet = null);

/// <summary>Angebotsentwurf mit Positionen, Summen und Stand.</summary>
/// <param name="State"><c>draft</c>, <c>sending</c>, <c>sent</c> oder <c>failed</c>.</param>
/// <param name="Editable">Bearbeiten möglich (nur vor dem Senden).</param>
/// <param name="LexwareUrl">Nach dem Senden: der Entwurf in der Lexware-Oberfläche.</param>
/// <param name="CustomerLinked">Der Kunde ist mit Lexware Office verknüpft (Voraussetzung für das Senden).</param>
/// <param name="SendErrorTechnical">Originaltext von Lexware Office zum letzten Fehler (englisch), für „Technische Einzelheiten“.</param>
/// <param name="SendErrorFix">Bekannte Behebung des letzten Fehlers, z. B. <c>taxTypeNet</c> („Steuerart Netto übernehmen und erneut senden“).</param>
public sealed record QuotationResponse(
    Guid Id, Guid ContactId, string? CustomerName, bool CustomerLinked, Guid? BauakteId, string? BauakteNumber, string? BauakteTitle, string Title,
    string? Introduction, string? Remark, DateOnly? ValidUntil, string TaxType, string? PaymentConditionId, string? PaymentLabel, int? PaymentDuration,
    decimal? PaymentDiscountPercent, int? PaymentDiscountRange, string State, bool Editable, string? LexwareId, string? LexwareUrl, DateTimeOffset? SentAt,
    string? SendError, IReadOnlyList<QuotationLineDto> Lines, QuotationTotals Totals, IReadOnlyList<string> Problems, long Version, DateTimeOffset UpdatedAt,
    string? SendErrorTechnical = null, string? SendErrorFix = null);

/// <summary>Angebotsentwurf in Listen (Bauakte, Kunde).</summary>
public sealed record QuotationSummary(
    Guid Id, string Title, Guid ContactId, string? CustomerName, Guid? BauakteId, string State, decimal Net, decimal Gross, DateOnly? ValidUntil,
    string? LexwareUrl, DateTimeOffset UpdatedAt);

public sealed record CreateQuotationRequest([property: Required] Guid Id, Guid? ContactId = null, Guid? BauakteId = null);

public sealed record SaveQuotationRequest(
    [property: Required] long Version,
    [property: Required, MaxLength(QuotationDraft.MaxTitle)] string Title,
    [property: MaxLength(QuotationDraft.MaxText)] string? Introduction = null,
    [property: MaxLength(QuotationDraft.MaxText)] string? Remark = null,
    DateOnly? ValidUntil = null,
    [property: MaxLength(30)] string TaxType = QuotationDraft.TaxNet,
    [property: MaxLength(100)] string? PaymentConditionId = null,
    [property: MaxLength(300)] string? PaymentLabel = null,
    [property: Range(0, 365)] int? PaymentDuration = null,
    [property: Range(0, 100)] decimal? PaymentDiscountPercent = null,
    [property: Range(0, 365)] int? PaymentDiscountRange = null,
    [property: MaxLength(200)] IReadOnlyList<QuotationLineDto>? Lines = null);

/// <summary>Eintrag aus „Artikel und Leistungen“ für die Auswahl im Angebot.</summary>
public sealed record QuotationCatalogItem(
    Guid Id, string Title, string? Description, CatalogItemKind Kind, string? ArticleNumber, string? Unit, decimal NetPrice, decimal TaxRate, bool InLexware);

/// <summary>Auswahlwerte der Angebotsmaske.</summary>
/// <param name="PaymentConditionsError">Zahlungsbedingungen konnten nicht aus Lexware Office gelesen werden; die Maske bleibt nutzbar.</param>
/// <param name="TaxTypes">Steuerarten mit Angabe, ob Lexware Office sie für Angebote zulässt (Profil und gemerkte Ablehnungen).</param>
public sealed record QuotationOptionsResponse(
    IReadOnlyList<LexwarePaymentCondition> PaymentConditions, string? PaymentConditionsError, IReadOnlyList<QuotationCatalogItem> Catalog,
    IReadOnlyList<decimal> TaxRates, int DefaultValidDays, IReadOnlyList<LexwareTaxTypeOption>? TaxTypes = null, bool SmallBusiness = false);

/// <summary>
/// Angebote (Beta): Entwürfe in EstrichONE vorbereiten und als Entwurf in Lexware Office anlegen. Recht <c>quotations.prepare</c>,
/// Module „Lexware Office“ und „Angebote (Beta)“. Nummer, Festschreiben und Versand bleiben in Lexware Office; Rechnungen
/// werden nicht erstellt.
/// </summary>
internal static class LexwareQuotationEndpoints
{
    private const string Tag = "Angebote (Beta)";
    public const int DefaultValidDays = 30;

    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/lexware/quotations").WithTags(Tag).RequireModule(AppModules.Lexware).RequireModule(AppModules.Quotations)
            .RequirePermission(Permissions.QuotationsPrepare).AddEndpointFilter(LexwareEndpoints.GuardAsync);
        group.MapGet("/", ListAsync).WithName("ListQuotationDrafts").WithSummary("Angebotsentwürfe einer Bauakte oder eines Kunden");
        group.MapGet("/options", OptionsAsync).WithName("GetQuotationOptions")
            .WithSummary("Auswahlwerte: Zahlungsbedingungen aus Lexware Office (falls verfügbar), Artikel und Leistungen, Steuersätze");
        group.MapGet("/suggestions", SuggestionsAsync).WithName("GetQuotationSuggestions")
            .WithSummary("Positionsvorschläge aus der Bauakte (Fläche aus Räume und Flächen bzw. Kennwerten); nur Vorschläge")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/", CreateAsync).WithName("CreateQuotationDraft")
            .WithSummary("Angebotsentwurf anlegen (zur Bauakte oder zum Kunden); der Kunde muss mit Lexware Office verknüpft sein")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapGet("/{id:guid}", GetAsync).WithName("GetQuotationDraft").ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPut("/{id:guid}", SaveAsync).WithName("SaveQuotationDraft")
            .WithSummary("Entwurf speichern (nur vor dem Senden; Version aus dem letzten Abruf)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteQuotationDraft")
            .WithSummary("Entwurf in EstrichONE verwerfen (nur vor dem Senden; in Lexware Office wird nichts gelöscht)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPost("/{id:guid}/send", SendAsync).WithName("SendQuotationDraftToLexware")
            .WithSummary("Als Entwurf an Lexware Office senden (POST /v1/quotations ohne finalize). Mehrfaches Senden legt kein zweites Angebot an")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status424FailedDependency)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static string Key(QuotationDraftState state) => state switch
    {
        QuotationDraftState.Sending => "sending",
        QuotationDraftState.Sent => "sent",
        QuotationDraftState.Failed => "failed",
        _ => "draft",
    };

    private static async Task<bool> VisibleAsync(QuotationDraft draft, IBauakteAccess access, CancellationToken cancellationToken) =>
        draft.BauakteId is not { } bauakteId || await access.CanReadAsync(bauakteId, cancellationToken);

    private static async Task<QuotationResponse> ResponseAsync(LexwareService lexware, QuotationDraft draft, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var row = await lexware.SettingsAsync(cancellationToken);
        var customer = await db.Set<Contact>().AsNoTracking().Where(c => c.Id == draft.ContactId).Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);
        var linked = await db.Set<LexwareContactLink>().AnyAsync(l => l.ContactId == draft.ContactId, cancellationToken);
        var bauakte = draft.BauakteId is { } id
            ? await db.Set<Bauakte>().AsNoTracking().Where(b => b.Id == id).Select(b => new { b.Number, b.Title }).FirstOrDefaultAsync(cancellationToken)
            : null;
        var lines = draft.Lines.OrderBy(l => l.Position).Select(l => new QuotationLineDto(l.Kind, l.Name, l.Description, l.CatalogItemId, l.Quantity, l.Unit,
            l.NetPrice, l.TaxRate, l.DiscountPercent, l.Kind == QuotationDraftLine.KindItem ? QuotationRules.LineNet(l.Quantity, l.NetPrice, l.DiscountPercent) : null))
            .ToList();
        var problems = draft.Editable ? QuotationRules.Problems(draft, LexwareTaxTypes.State(row)) : [];
        if (draft.Editable && !linked)
        {
            problems.Insert(0, QuotationService.NotLinkedText(customer ?? "unbekannt"));
        }

        return new QuotationResponse(draft.Id, draft.ContactId, customer, linked, draft.BauakteId, bauakte?.Number, bauakte?.Title, draft.Title, draft.Introduction,
            draft.Remark, draft.ValidUntil, draft.TaxType, draft.PaymentConditionId, draft.PaymentLabel, draft.PaymentDuration, draft.PaymentDiscountPercent,
            draft.PaymentDiscountRange, Key(draft.State), draft.Editable, draft.LexwareId,
            draft.LexwareId is null ? null : lexware.VoucherUrl(row, LexwareVoucherTypes.Quotation, draft.LexwareId, "draft"), draft.SentAt, draft.SendError, lines,
            QuotationRules.Totals(draft.Lines, draft.TaxType), problems, draft.Version, draft.UpdatedAt, draft.SendErrorTechnical, draft.SendErrorFix);
    }

    private static async Task<Ok<List<QuotationSummary>>> ListAsync(LexwareService lexware, IBauakteAccess access, CancellationToken cancellationToken,
        [FromQuery] Guid? bauakteId = null, [FromQuery] Guid? contactId = null)
    {
        var db = lexware.Db;
        var query = db.Set<QuotationDraft>().AsNoTracking();
        if (bauakteId is { } b)
        {
            if (!await access.CanReadAsync(b, cancellationToken))
            {
                return TypedResults.Ok(new List<QuotationSummary>());
            }

            query = query.Where(q => q.BauakteId == b);
        }

        if (contactId is { } c)
        {
            query = query.Where(q => q.ContactId == c);
        }

        if (bauakteId is null && contactId is null)
        {
            query = query.Where(q => q.State != QuotationDraftState.Sent);
        }

        var drafts = await query.OrderByDescending(q => q.UpdatedAt).Take(200).ToListAsync(cancellationToken);
        var visible = new List<QuotationDraft>();
        foreach (var draft in drafts)
        {
            if (bauakteId is not null || await VisibleAsync(draft, access, cancellationToken))
            {
                visible.Add(draft);
            }
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        var ids = visible.Select(d => d.ContactId).Distinct().ToList();
        var names = await db.Set<Contact>().AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
        return TypedResults.Ok(visible.Select(d =>
        {
            var totals = QuotationRules.Totals(d.Lines, d.TaxType);
            return new QuotationSummary(d.Id, d.Title, d.ContactId, names.GetValueOrDefault(d.ContactId), d.BauakteId, Key(d.State), totals.Net, totals.Gross,
                d.ValidUntil, d.LexwareId is null ? null : lexware.VoucherUrl(row, LexwareVoucherTypes.Quotation, d.LexwareId, "draft"), d.UpdatedAt);
        }).ToList());
    }

    private static async Task<Ok<QuotationOptionsResponse>> OptionsAsync(LexwareService lexware, CancellationToken cancellationToken)
    {
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
            .Select(c => new QuotationCatalogItem(c.Id, c.Title, c.Description, c.Kind, c.ArticleNumber, c.Unit, c.NetPrice, c.TaxRate,
                c.LexwareId != null && c.RemoteState == null))
            .ToListAsync(cancellationToken);
        var taxTypes = await LexwareTaxTypeStore.StateAsync(lexware, cancellationToken);
        return TypedResults.Ok(new QuotationOptionsResponse(conditions, error, catalog, QuotationRules.TaxRates, DefaultValidDays,
            LexwareTaxTypes.Options(LexwareVoucherTypes.Quotation, taxTypes), LexwareTaxTypes.IsSmallBusiness(taxTypes.SmallBusiness, taxTypes.ProfileTaxType)));
    }

    private static async Task<Results<Ok<QuotationSuggestionsResponse>, ProblemHttpResult>> SuggestionsAsync([FromQuery] Guid bauakteId, LexwareService lexware,
        IBauakteAccess access, IBauakteRooms rooms, CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(bauakteId, cancellationToken)
            || await lexware.Db.Set<Bauakte>().AsNoTracking().SingleOrDefaultAsync(b => b.Id == bauakteId, cancellationToken) is not { } bauakte)
        {
            return BauakteWriteProblems.NotFound();
        }

        var catalog = await lexware.Db.Set<CatalogItem>().AsNoTracking().Where(c => c.IsActive).ToListAsync(cancellationToken);
        return TypedResults.Ok(QuotationSuggestions.Build(bauakte, await rooms.AreasAsync(bauakteId, cancellationToken), catalog));
    }

    private static async Task<Results<Created<QuotationResponse>, Ok<QuotationResponse>, ProblemHttpResult>> CreateAsync(CreateQuotationRequest request,
        LexwareService lexware, IBauakteAccess access, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        if (await db.Set<QuotationDraft>().SingleOrDefaultAsync(q => q.Id == request.Id, cancellationToken) is { } existing)
        {
            return TypedResults.Ok(await ResponseAsync(lexware, existing, cancellationToken)); // gleiche ID: kein zweiter Entwurf
        }

        Bauakte? bauakte = null;
        if (request.BauakteId is { } bauakteId)
        {
            bauakte = await access.CanReadAsync(bauakteId, cancellationToken)
                ? await db.Set<Bauakte>().AsNoTracking().SingleOrDefaultAsync(b => b.Id == bauakteId, cancellationToken)
                : null;
            if (bauakte is null)
            {
                return BauakteWriteProblems.NotFound();
            }
        }

        var contactId = request.ContactId ?? bauakte?.CustomerId;
        if (contactId is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: Tag,
                detail: "Der Bauakte ist noch kein Kunde zugeordnet. Ordnen Sie zuerst einen Kunden zu.", extensions: Code("QUOTATION_NO_CUSTOMER"));
        }

        if (await db.Set<Contact>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == contactId, cancellationToken) is not { } customer)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Kontakt nicht gefunden", extensions: Code("CONTACT_NOT_FOUND"));
        }

        if (!await db.Set<LexwareContactLink>().AnyAsync(l => l.ContactId == customer.Id, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Tag, detail: QuotationService.NotLinkedText(customer.Name),
                extensions: new Dictionary<string, object?> { ["code"] = "LEXWARE_CUSTOMER_NOT_LINKED", ["contactId"] = customer.Id });
        }

        var intro = bauakte is null
            ? "Vielen Dank für Ihre Anfrage. Gerne bieten wir Ihnen folgende Leistungen an."
            : $"Bauvorhaben: {string.Join(", ", new[] { bauakte.Number, bauakte.Title, string.Join(' ', new[] { bauakte.Street, bauakte.PostalCode, bauakte.City }.Where(p => !string.IsNullOrWhiteSpace(p))) }.Where(p => !string.IsNullOrWhiteSpace(p)))}\n"
              + "Vielen Dank für Ihre Anfrage. Gerne bieten wir Ihnen folgende Leistungen an.";
        var draft = new QuotationDraft
        {
            Id = request.Id, ContactId = customer.Id, BauakteId = bauakte?.Id, Title = "Angebot", Introduction = LexwareService.Cut(intro, QuotationDraft.MaxText),
            ValidUntil = lexware.Today().AddDays(DefaultValidDays), CreatedByUserId = currentUser.UserId,
            TaxType = LexwareTaxTypes.Default(LexwareTaxTypes.State(await lexware.SettingsAsync(cancellationToken))),
        };
        db.Add(draft);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/lexware/quotations/{draft.Id}", await ResponseAsync(lexware, draft, cancellationToken));
    }

    private static async Task<QuotationDraft?> FindAsync(LexwareService lexware, IBauakteAccess access, Guid id, CancellationToken cancellationToken) =>
        await lexware.Db.Set<QuotationDraft>().SingleOrDefaultAsync(q => q.Id == id, cancellationToken) is { } draft && await VisibleAsync(draft, access, cancellationToken)
            ? draft : null;

    private static async Task<Results<Ok<QuotationResponse>, ProblemHttpResult>> GetAsync(Guid id, LexwareService lexware, IBauakteAccess access,
        CancellationToken cancellationToken) =>
        await FindAsync(lexware, access, id, cancellationToken) is { } draft ? TypedResults.Ok(await ResponseAsync(lexware, draft, cancellationToken)) : NotFound();

    private static async Task<Results<Ok<QuotationResponse>, ProblemHttpResult>> SaveAsync(Guid id, SaveQuotationRequest request, LexwareService lexware,
        IBauakteAccess access, CancellationToken cancellationToken)
    {
        if (await FindAsync(lexware, access, id, cancellationToken) is not { } draft)
        {
            return NotFound();
        }

        if (!draft.Editable)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Tag, detail: SentText(draft), extensions: Code("LEXWARE_QUOTATION_SENT"));
        }

        if (request.Version != draft.Version)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Geändert",
                detail: "Der Entwurf wurde inzwischen geändert. Bitte laden Sie ihn neu.", extensions: Code("VERSION_CONFLICT"));
        }

        if (!LexwareTaxTypes.Supported.Contains(request.TaxType))
        {
            return Invalid("Die Steuerart ist unbekannt.");
        }

        var lines = request.Lines ?? [];
        if (lines.FirstOrDefault(l => l.Kind is not (QuotationDraftLine.KindItem or QuotationDraftLine.KindText)) is not null)
        {
            return Invalid("Unbekannte Art einer Position.");
        }

        draft.Title = string.IsNullOrWhiteSpace(request.Title) ? "Angebot" : request.Title.Trim();
        draft.Introduction = LexwareService.Cut(request.Introduction, QuotationDraft.MaxText);
        draft.Remark = LexwareService.Cut(request.Remark, QuotationDraft.MaxText);
        draft.ValidUntil = request.ValidUntil;
        draft.TaxType = request.TaxType;
        draft.PaymentConditionId = LexwareService.Cut(request.PaymentConditionId, 100);
        draft.PaymentLabel = LexwareService.Cut(request.PaymentLabel, 300);
        draft.PaymentDuration = request.PaymentDuration;
        draft.PaymentDiscountPercent = request.PaymentDiscountPercent;
        draft.PaymentDiscountRange = request.PaymentDiscountRange;
        if (draft.State == QuotationDraftState.Failed && !draft.SendUncertain)
        {
            draft.State = QuotationDraftState.Draft;
            draft.SendError = null;
            draft.SendErrorTechnical = null;
            draft.SendErrorFix = null;
        }

        var db = lexware.Db;
        db.RemoveRange(draft.Lines);
        draft.Lines.Clear();
        var position = 0;
        foreach (var line in lines)
        {
            var text = line.Kind == QuotationDraftLine.KindText;
            draft.Lines.Add(new QuotationDraftLine
            {
                Id = Guid.CreateVersion7(), DraftId = draft.Id, Position = ++position, Kind = line.Kind, CatalogItemId = text ? null : line.CatalogItemId,
                Name = line.Name.Trim(), Description = LexwareService.Cut(line.Description, 2000), Quantity = text ? null : line.Quantity,
                Unit = text ? null : LexwareService.Cut(line.Unit, 50), NetPrice = text ? null : line.NetPrice, TaxRate = text ? null : line.TaxRate ?? 19m,
                DiscountPercent = text || line.DiscountPercent is null or 0 ? null : line.DiscountPercent,
            });
        }

        db.Entry(draft).State = EntityState.Modified; // auch nur geänderte Positionen erhöhen die Version des Entwurfs
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ResponseAsync(lexware, draft, cancellationToken));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid id, LexwareService lexware, IBauakteAccess access, IAuditLog audit,
        ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (await FindAsync(lexware, access, id, cancellationToken) is not { } draft)
        {
            return NotFound();
        }

        if (!draft.Editable || draft.SendUncertain)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Tag,
                detail: draft.SendUncertain
                    ? "Der letzte Sendeversuch ist ohne eindeutige Antwort abgebrochen. Senden Sie den Entwurf bitte erneut; EstrichONE prüft dabei, ob das Angebot in Lexware Office schon angelegt wurde."
                    : SentText(draft),
                extensions: Code("LEXWARE_QUOTATION_SENT"));
        }

        lexware.Db.Remove(draft);
        audit.Record("lexware.quotation_discarded", currentUser.UserId, draft.BauakteId is null ? "contact" : "bauakte", (draft.BauakteId ?? draft.ContactId).ToString(),
            new Dictionary<string, string?> { ["title"] = draft.Title });
        await lexware.Db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<QuotationResponse>, ProblemHttpResult>> SendAsync(Guid id, LexwareService lexware, LexwareQuotationService quotations,
        IBauakteAccess access, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (await FindAsync(lexware, access, id, cancellationToken) is not { } draft)
        {
            return NotFound();
        }

        lexware.Db.ChangeTracker.Clear();
        try
        {
            var result = await quotations.SendAsync(draft.Id, currentUser.UserId, cancellationToken);
            return TypedResults.Ok(await ResponseAsync(lexware, result.Draft, cancellationToken));
        }
        catch (LexwareException exception) when (exception.Code == "QUOTATION_INCOMPLETE")
        {
            return Invalid(exception.Message);
        }
    }

    private static string SentText(QuotationDraft draft) => draft.State == QuotationDraftState.Sending
        ? "Dieses Angebot wird gerade an Lexware Office gesendet."
        : "Dieses Angebot wurde bereits als Entwurf an Lexware Office gesendet und ist hier schreibgeschützt. Änderungen nehmen Sie bitte in Lexware Office vor.";

    private static Dictionary<string, object?> Code(string code) => new() { ["code"] = code };

    private static ProblemHttpResult Invalid(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Ungültige Angaben",
        detail: detail, extensions: Code("VALIDATION_FAILED"));

    private static ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht gefunden",
        detail: "Diesen Angebotsentwurf gibt es nicht.", extensions: Code("QUOTATION_NOT_FOUND"));
}

/// <summary>Texte der Angebote, die Endpunkte und Dienst teilen.</summary>
internal static class QuotationService
{
    public static string NotLinkedText(string customerName) => LexwareQuotationService.NotLinked(customerName).Message;
}
