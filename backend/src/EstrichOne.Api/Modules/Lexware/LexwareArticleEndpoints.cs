using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Materials.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Abgleichstand eines Eintrags in „Artikel und Leistungen“.</summary>
/// <param name="State">
/// <c>local</c> (nur in EstrichONE), <c>synced</c> (abgeglichen), <c>deviations</c> (offene Abweichungen), <c>error</c>,
/// <c>missing</c> bzw. <c>archived</c> (in Lexware Office nicht mehr vorhanden bzw. archiviert) oder <c>off</c> (Abgleich aus).
/// </param>
public sealed record CatalogItemSyncState(string State, DateTimeOffset? SyncedAt, IReadOnlyList<LexwareDeviationField> Deviations, string? Error);

/// <summary>Eintrag in „Artikel und Leistungen“.</summary>
/// <param name="Kind"><c>product</c> (Material, Ware) oder <c>service</c> (Leistung).</param>
/// <param name="LexwareUrl">Artikel in der Lexware-Oberfläche, wenn verknüpft.</param>
public sealed record CatalogItemResponse(
    Guid Id, string Title, string? Description, CatalogItemKind Kind, string? ArticleNumber, string? Gtin, string? Unit, decimal NetPrice, decimal? GrossPrice,
    decimal TaxRate, Guid? MaterialId, string? MaterialName, bool IsActive, string Origin, string? LexwareId, string? LexwareUrl, CatalogItemSyncState Sync,
    long Version);

/// <summary>Liste „Artikel und Leistungen“ mit dem Stand des Artikelabgleichs.</summary>
/// <param name="SyncEnabled">Modul „Lexware Office: Artikel“ eingeschaltet und Schlüssel hinterlegt.</param>
/// <param name="CanPush">Richtung „beide“: Einträge aus EstrichONE werden in Lexware Office angelegt und geändert.</param>
public sealed record CatalogListResponse(
    IReadOnlyList<CatalogItemResponse> Items, bool SyncEnabled, LexwareArticleSync Direction, bool CanPush, DateTimeOffset? LastSyncAt, string? LastSyncError,
    IReadOnlyList<MaterialSummary> Materials);

public sealed record SaveCatalogItemRequest(
    [property: Required] Guid Id,
    [property: Required, MaxLength(CatalogItem.MaxTitle)] string Title,
    CatalogItemKind Kind = CatalogItemKind.Service,
    [property: MaxLength(CatalogItem.MaxDescription)] string? Description = null,
    [property: MaxLength(50)] string? ArticleNumber = null,
    [property: MaxLength(20)] string? Gtin = null,
    [property: MaxLength(50)] string? Unit = null,
    [property: Range(-1_000_000, 10_000_000)] decimal NetPrice = 0m,
    [property: Range(0, 100)] decimal TaxRate = 19m,
    Guid? MaterialId = null,
    bool IsActive = true,
    long? Version = null);

/// <summary>Artikel in Lexware Office, der in EstrichONE noch fehlt, mit möglichen Treffern.</summary>
public sealed record LexwareArticleSummary(
    string Id, string Title, string? Description, string Type, string? ArticleNumber, string? UnitName, decimal? NetPrice, decimal? TaxRate, bool Archived, string Url);

public sealed record LexwareArticleCandidate(CatalogItemResponse Item, IReadOnlyList<string> Reasons);

public sealed record LexwareArticleOnlyRemote(LexwareArticleSummary Article, IReadOnlyList<LexwareArticleCandidate> Candidates);

/// <summary>Eintrag nur in EstrichONE mit möglichen Treffern in Lexware Office.</summary>
public sealed record LexwareArticleOnlyLocal(CatalogItemResponse Item, IReadOnlyList<LexwareArticleSummary> Candidates);

/// <summary>Abgleichübersicht der Artikel (liest die Artikel aus Lexware Office, kurz zwischengespeichert).</summary>
public sealed record LexwareArticleReconciliationResponse(
    DateTimeOffset LoadedAt, IReadOnlyList<LexwareArticleOnlyRemote> OnlyLexware, IReadOnlyList<LexwareArticleOnlyLocal> OnlyEstrichOne);

public sealed record LinkCatalogItemRequest([property: Required, MaxLength(100)] string LexwareId);

/// <param name="CreateAnyway">Bewusst übernehmen bzw. anlegen, obwohl es einen möglichen Treffer gibt.</param>
public sealed record ImportLexwareArticleRequest([property: Required, MaxLength(100)] string LexwareId, bool CreateAnyway = false);

public sealed record CreateLexwareArticleRequest(bool CreateAnyway = false);

/// <summary>Artikelabgleich jetzt ausführen: Ergebnis des Laufs.</summary>
public sealed record LexwareArticleSyncResponse(int Imported, int Created, int Updated, int Deviations, int Duplicates, DateTimeOffset At);

/// <summary>
/// „Artikel und Leistungen“ und Artikelabgleich mit Lexware Office. Lesen mit <c>materials.read</c>, Pflegen und Abgleichen
/// mit <c>materials.manage</c>. Der Katalog steht mit „Lexware Office: Artikel“ oder „Angebote (Beta)“ bereit; der Abgleich nur
/// mit „Lexware Office: Artikel“. In Lexware Office wird nie gelöscht.
/// </summary>
internal static class LexwareArticleEndpoints
{
    private const string Tag = "Lexware Office";

    public static void Map(IEndpointRouteBuilder api)
    {
        var catalog = api.MapGroup("/lexware/articles").WithTags(Tag).RequireModule(AppModules.Lexware)
            .RequireModule(AppModules.LexwareArticles, AppModules.Quotations).AddEndpointFilter(GuardAsync);
        catalog.MapGet("/", ListAsync).WithName("ListCatalogItems")
            .WithSummary("Artikel und Leistungen mit Abgleichstand (ohne Aufruf bei Lexware Office)").RequirePermission(Permissions.MaterialsRead);
        catalog.MapPut("/{id:guid}", SaveAsync).WithName("SaveCatalogItem")
            .WithSummary("Eintrag anlegen oder ändern (nur in EstrichONE; der Abgleich überträgt Änderungen, wenn die Richtung es erlaubt)")
            .ProducesProblem(StatusCodes.Status412PreconditionFailed).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.MaterialsManage);

        var sync = catalog.MapGroup("/").RequireModule(AppModules.LexwareArticles);
        sync.MapPost("/sync", SyncAsync).WithName("RunLexwareArticleSync")
            .WithSummary("Artikelabgleich jetzt ausführen (liest alle Artikel aus Lexware Office)")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status424FailedDependency)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable).RequirePermission(Permissions.MaterialsManage);
        sync.MapGet("/reconciliation", ReconciliationAsync).WithName("GetLexwareArticleReconciliation")
            .WithSummary("Nicht verknüpfte Artikel beider Seiten mit möglichen Treffern (Artikelnummer, Bezeichnung)")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status424FailedDependency)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable).RequirePermission(Permissions.MaterialsManage);
        sync.MapPost("/import", ImportAsync).WithName("ImportLexwareArticle")
            .WithSummary("Artikel aus Lexware Office übernehmen; bei möglichen Treffern nur mit createAnyway")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.MaterialsManage);
        sync.MapPost("/{id:guid}/link", LinkAsync).WithName("LinkCatalogItemToLexware")
            .WithSummary("Eintrag mit einem vorhandenen Artikel in Lexware Office verknüpfen; der erste Abgleich zeigt Unterschiede als Abweichungen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.MaterialsManage);
        sync.MapPost("/{id:guid}/create", CreateAsync).WithName("CreateLexwareArticle")
            .WithSummary("Eintrag in Lexware Office anlegen (nur bei Richtung „beide“); bei möglichen Treffern nur mit createAnyway")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status424FailedDependency).ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .RequirePermission(Permissions.MaterialsManage);
        sync.MapPost("/{id:guid}/resolve", ResolveAsync).WithName("ResolveCatalogItemDeviations")
            .WithSummary("Abweichungen entscheiden: je Feld oder gesamt EstrichONE oder Lexware übernehmen (EstrichONE nur bei Richtung „beide“)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).ProducesProblem(StatusCodes.Status424FailedDependency)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable).RequirePermission(Permissions.MaterialsManage);
    }

    internal static string ArticleUrl(LexwareService lexware, LexwareSettingsRow row, string id) =>
        $"{lexware.AppBaseUrl(row)}/permalink/articles/view/{Uri.EscapeDataString(id)}";

    private static CatalogItemSyncState State(CatalogItem item, bool syncOn) =>
        item.LexwareId is null ? new CatalogItemSyncState(syncOn ? "local" : "off", null, [], item.SyncError)
        : !syncOn ? new CatalogItemSyncState("off", item.SyncedAt, [], null)
        : item.RemoteState is { } remote ? new CatalogItemSyncState(remote, item.SyncedAt, [], null)
        : item.SyncError is not null ? new CatalogItemSyncState("error", item.SyncedAt, Fields(item), item.SyncError)
        : new CatalogItemSyncState(item.DeviationCount > 0 ? "deviations" : "synced", item.SyncedAt, Fields(item), null);

    private static List<LexwareDeviationField> Fields(CatalogItem item) =>
        LexwareArticleRules.Deviations(item.Deviations).Select(d => new LexwareDeviationField(d.Field, LexwareArticleFields.Label(d.Field), d.EstrichOne, d.Lexware)).ToList();

    internal static CatalogItemResponse ToResponse(LexwareService lexware, LexwareSettingsRow row, CatalogItem item, bool syncOn,
        IReadOnlyDictionary<Guid, MaterialSummary>? materials = null) => new(
        item.Id, item.Title, item.Description, item.Kind, item.ArticleNumber, item.Gtin, item.Unit, item.NetPrice, item.GrossPrice, item.TaxRate, item.MaterialId,
        item.MaterialId is { } m ? materials?.GetValueOrDefault(m)?.Name : null, item.IsActive, item.Origin, item.LexwareId,
        item.LexwareId is null ? null : ArticleUrl(lexware, row, item.LexwareId), State(item, syncOn), item.Version);

    private static LexwareArticleSummary Summary(LexwareService lexware, LexwareSettingsRow row, LexwareArticle a) => new(
        a.Id, a.Title, a.Description, a.Type, a.ArticleNumber, a.UnitName, a.NetPrice, a.TaxRate, a.Archived, ArticleUrl(lexware, row, a.Id));

    private static async Task<bool> SyncOnAsync(LexwareArticleSyncService sync, LexwareSettingsRow row, CancellationToken cancellationToken) =>
        await sync.ActiveAsync(row, cancellationToken);

    private static async Task<Ok<CatalogListResponse>> ListAsync(LexwareService lexware, LexwareArticleSyncService sync, IMaterialDirectory materials,
        CancellationToken cancellationToken, [FromQuery] bool includeInactive = false)
    {
        var row = await lexware.SettingsAsync(cancellationToken);
        var on = await SyncOnAsync(sync, row, cancellationToken);
        var query = lexware.Db.Set<CatalogItem>().AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        var items = await query.OrderBy(c => c.Title).ToListAsync(cancellationToken);
        var linked = await materials.ListAsync(items.Select(i => i.MaterialId).OfType<Guid>().Distinct().ToList(), cancellationToken);
        var byId = linked.ToDictionary(m => m.Id);
        return TypedResults.Ok(new CatalogListResponse(items.Select(i => ToResponse(lexware, row, i, on, byId)).ToList(), on, row.ArticleSync,
            on && row.ArticleSync == LexwareArticleSync.Both, row.LastArticleSyncAt, row.LastArticleSyncError, await materials.ListAsync(null, cancellationToken)));
    }

    private static async Task<Results<Ok<CatalogItemResponse>, ProblemHttpResult>> SaveAsync(Guid id, SaveCatalogItemRequest request, LexwareService lexware,
        LexwareArticleSyncService sync, IMaterialDirectory materials, CancellationToken cancellationToken)
    {
        if (request.Id != id || string.IsNullOrWhiteSpace(request.Title))
        {
            return Invalid("Bitte geben Sie eine Bezeichnung an.");
        }

        if (!Enum.IsDefined(request.Kind))
        {
            return Invalid("Die Art ist unbekannt.");
        }

        if (request.MaterialId is { } materialId && (await materials.ListAsync([materialId], cancellationToken)).Count == 0)
        {
            return Invalid("Der verknüpfte Artikel des Materialstamms existiert nicht.");
        }

        var db = lexware.Db;
        var item = await db.Set<CatalogItem>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (item is null)
        {
            item = new CatalogItem { Id = id };
            db.Add(item);
        }
        else if (request.Version is { } version && version != item.Version)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Geändert",
                detail: "Der Eintrag wurde inzwischen geändert, zum Beispiel durch den Abgleich mit Lexware Office. Bitte laden Sie die Seite neu.",
                extensions: new Dictionary<string, object?> { ["code"] = "VERSION_CONFLICT" });
        }

        item.Title = request.Title.Trim();
        item.Description = LexwareService.Cut(request.Description, CatalogItem.MaxDescription);
        item.Kind = request.Kind;
        item.ArticleNumber = LexwareService.Cut(request.ArticleNumber, 50);
        item.Gtin = LexwareService.Cut(request.Gtin, 20);
        item.Unit = LexwareService.Cut(request.Unit, 50);
        item.NetPrice = Math.Round(request.NetPrice, 4);
        item.TaxRate = request.TaxRate;
        item.MaterialId = request.MaterialId;
        item.IsActive = request.IsActive;
        await db.SaveChangesAsync(cancellationToken);
        var row = await lexware.SettingsAsync(cancellationToken);
        var linked = await materials.ListAsync(item.MaterialId is { } m ? [m] : [], cancellationToken);
        return TypedResults.Ok(ToResponse(lexware, row, item, await SyncOnAsync(sync, row, cancellationToken), linked.ToDictionary(x => x.Id)));
    }

    private static async Task<Results<Ok<LexwareArticleSyncResponse>, ProblemHttpResult>> SyncAsync(LexwareService lexware, LexwareArticleSyncService sync,
        ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var row = await lexware.SettingsAsync(cancellationToken);
        lexware.Connection(row); // ohne Schlüssel: klare Meldung
        if (row.ArticleSync == LexwareArticleSync.Off)
        {
            return LexwareEndpoints.Problem(SyncOff());
        }

        var run = await sync.RunAsync(currentUser.UserId, cancellationToken);
        if (run.Error is not null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Lexware Office ist nicht erreichbar", detail: run.Error,
                extensions: new Dictionary<string, object?> { ["code"] = "LEXWARE_ARTICLE_SYNC_FAILED" });
        }

        return TypedResults.Ok(new LexwareArticleSyncResponse(run.Imported, run.Created, run.Updated, run.Deviations, run.Duplicates, lexware.Clock.GetUtcNow()));
    }

    private static LexwareException SyncOff() => new("ARTICLE_SYNC_OFF",
        "Der Artikelabgleich ist ausgeschaltet. Wählen Sie unter Verwaltung › Integrationen › Lexware Office eine Richtung für den Artikelabgleich.");

    private static LexwareException NoPush() => new("ARTICLE_SYNC_DIRECTION",
        "Die Richtung des Artikelabgleichs ist „Aus Lexware Office nach EstrichONE“. In Lexware Office wird deshalb nichts angelegt oder geändert. Stellen Sie die Richtung auf „Beide Richtungen“, um Artikel aus EstrichONE zu übertragen.");

    private static async Task<Results<Ok<LexwareArticleReconciliationResponse>, ProblemHttpResult>> ReconciliationAsync(LexwareService lexware,
        LexwareArticleSyncService sync, CancellationToken cancellationToken, [FromQuery] bool refresh = false)
    {
        var row = await lexware.SettingsAsync(cancellationToken);
        var on = await SyncOnAsync(sync, row, cancellationToken);
        var (remote, loadedAt) = await lexware.ArticlesAsync(row, refresh, cancellationToken);
        var items = await lexware.Db.Set<CatalogItem>().AsNoTracking().OrderBy(c => c.Title).ToListAsync(cancellationToken);
        var linked = items.Where(i => i.LexwareId is not null).Select(i => i.LexwareId!).ToHashSet(StringComparer.Ordinal);
        var local = items.Where(i => i.LexwareId is null).ToList();
        var onlyRemote = remote.Where(a => !a.Archived && !linked.Contains(a.Id)).OrderBy(a => a.Title).Take(500).Select(a => new LexwareArticleOnlyRemote(
            Summary(lexware, row, a), local.Select(i => (Item: i, Reasons: LexwareArticleRules.Reasons(i, a))).Where(x => x.Reasons.Count > 0)
                .Select(x => new LexwareArticleCandidate(ToResponse(lexware, row, x.Item, on), x.Reasons)).ToList())).ToList();
        var unlinkedRemote = remote.Where(a => !linked.Contains(a.Id)).ToList();
        var onlyLocal = local.Where(i => i.IsActive).Take(500).Select(i => new LexwareArticleOnlyLocal(ToResponse(lexware, row, i, on),
            unlinkedRemote.Where(a => LexwareArticleRules.Reasons(i, a).Count > 0).Select(a => Summary(lexware, row, a)).ToList())).ToList();
        return TypedResults.Ok(new LexwareArticleReconciliationResponse(loadedAt, onlyRemote, onlyLocal));
    }

    private static async Task<Results<Ok<CatalogItemResponse>, ProblemHttpResult>> ImportAsync(ImportLexwareArticleRequest request, LexwareService lexware,
        LexwareArticleSyncService sync, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var row = await lexware.SettingsAsync(cancellationToken);
        var db = lexware.Db;
        var lexwareId = request.LexwareId.Trim();
        if (await db.Set<CatalogItem>().AnyAsync(c => c.LexwareId == lexwareId, cancellationToken))
        {
            return LexwareEndpoints.Problem(new LexwareException("ALREADY_LINKED", "Dieser Artikel ist bereits mit einem Eintrag in EstrichONE verknüpft."));
        }

        var article = await lexware.Client.GetArticleAsync(lexware.Connection(row), lexwareId, cancellationToken);
        if (!request.CreateAnyway)
        {
            var matches = (await db.Set<CatalogItem>().AsNoTracking().Where(c => c.LexwareId == null).ToListAsync(cancellationToken))
                .Where(i => LexwareArticleRules.Reasons(i, article).Count > 0).ToList();
            if (matches.Count > 0)
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Tag,
                    detail: $"In EstrichONE gibt es bereits einen ähnlichen Eintrag („{matches[0].Title}“). Verknüpfen Sie ihn oder übernehmen Sie den Artikel bewusst als neuen Eintrag.",
                    extensions: new Dictionary<string, object?> { ["code"] = "LEXWARE_ARTICLE_DUPLICATE" });
            }
        }

        var item = LexwareArticleRules.Import(article, Guid.CreateVersion7());
        LexwareArticleSyncService.Settle(item, LexwareArticleRules.Remote(article), article, lexware.Clock.GetUtcNow());
        db.Add(item);
        audit.Record("lexware.article_imported", currentUser.UserId, "catalog_item", item.Id.ToString(), new Dictionary<string, string?> { ["title"] = item.Title });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(lexware, row, item, await SyncOnAsync(sync, row, cancellationToken)));
    }

    private static async Task<Results<Ok<CatalogItemResponse>, ProblemHttpResult>> LinkAsync(Guid id, LinkCatalogItemRequest request, LexwareService lexware,
        LexwareArticleSyncService sync, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        if (await db.Set<CatalogItem>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken) is not { } item)
        {
            return NotFound();
        }

        var lexwareId = request.LexwareId.Trim();
        if (item.LexwareId is not null || await db.Set<CatalogItem>().AnyAsync(c => c.LexwareId == lexwareId, cancellationToken))
        {
            return LexwareEndpoints.Problem(new LexwareException("ALREADY_LINKED",
                "Der Eintrag oder der Artikel ist bereits verknüpft. Ein Artikel in Lexware Office gehört zu höchstens einem Eintrag in EstrichONE."));
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        var connection = lexware.Connection(row);
        var article = await lexware.Client.GetArticleAsync(connection, lexwareId, cancellationToken);
        item.LexwareId = article.Id;
        item.SyncedFields = null; // Erstabgleich: Unterschiede werden Abweichungen, überschrieben wird nichts.
        await sync.SyncItemAsync(row, connection, item, article, row.ArticleSync, null, cancellationToken);
        audit.Record("lexware.article_linked", currentUser.UserId, "catalog_item", item.Id.ToString(), new Dictionary<string, string?> { ["title"] = item.Title });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(lexware, row, item, await SyncOnAsync(sync, row, cancellationToken)));
    }

    private static async Task<Results<Ok<CatalogItemResponse>, ProblemHttpResult>> CreateAsync(Guid id, CreateLexwareArticleRequest request, LexwareService lexware,
        LexwareArticleSyncService sync, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        if (await db.Set<CatalogItem>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken) is not { } item)
        {
            return NotFound();
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        var connection = lexware.Connection(row);
        if (row.ArticleSync != LexwareArticleSync.Both)
        {
            return LexwareEndpoints.Problem(row.ArticleSync == LexwareArticleSync.Off ? SyncOff() : NoPush());
        }

        if (item.LexwareId is not null)
        {
            return LexwareEndpoints.Problem(new LexwareException("ALREADY_LINKED", "Dieser Eintrag ist bereits mit einem Artikel in Lexware Office verknüpft."));
        }

        if (!request.CreateAnyway)
        {
            var (remote, _) = await lexware.ArticlesAsync(row, refresh: true, cancellationToken);
            var linked = (await db.Set<CatalogItem>().AsNoTracking().Where(c => c.LexwareId != null).Select(c => c.LexwareId!).ToListAsync(cancellationToken)).ToHashSet();
            if (remote.FirstOrDefault(a => !linked.Contains(a.Id) && LexwareArticleRules.Reasons(item, a).Count > 0) is { } match)
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: Tag,
                    detail: $"In Lexware Office gibt es bereits einen ähnlichen Artikel („{match.Title}“). Verknüpfen Sie ihn oder legen Sie den Artikel bewusst neu an.",
                    extensions: new Dictionary<string, object?> { ["code"] = "LEXWARE_ARTICLE_DUPLICATE" });
            }
        }

        await sync.CreateInLexwareAsync(row, connection, item, currentUser.UserId, cancellationToken);
        return TypedResults.Ok(ToResponse(lexware, row, item, await SyncOnAsync(sync, row, cancellationToken)));
    }

    private static async Task<Results<Ok<CatalogItemResponse>, ProblemHttpResult>> ResolveAsync(Guid id, ResolveLexwareDeviationsRequest request,
        LexwareService lexware, LexwareArticleSyncService sync, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        if (await db.Set<CatalogItem>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken) is not { } item || item.LexwareId is null)
        {
            return NotFound();
        }

        var choices = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.Source is not null)
        {
            foreach (var field in LexwareArticleFields.All)
            {
                choices[field] = request.Source;
            }
        }

        foreach (var (field, source) in request.Fields ?? new Dictionary<string, string>())
        {
            choices[field] = source;
        }

        if (choices.Count == 0 || choices.Any(c => !LexwareArticleFields.All.Contains(c.Key) || c.Value is not (LexwareContactSync.EstrichOne or LexwareContactSync.Lexware)))
        {
            return Invalid("Bitte wählen Sie je Feld EstrichONE oder Lexware Office.");
        }

        var row = await lexware.SettingsAsync(cancellationToken);
        if (choices.ContainsValue(LexwareContactSync.EstrichOne) && row.ArticleSync != LexwareArticleSync.Both)
        {
            return LexwareEndpoints.Problem(NoPush());
        }

        var connection = lexware.Connection(row);
        var gate = lexware.ArticleGate;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var article = await lexware.Client.GetArticleAsync(connection, item.LexwareId, cancellationToken);
            await sync.SyncItemAsync(row, connection, item, article, row.ArticleSync, choices, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }

        return TypedResults.Ok(ToResponse(lexware, row, item, await SyncOnAsync(sync, row, cancellationToken)));
    }

    private static ProblemHttpResult Invalid(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Ungültige Angaben",
        detail: detail, extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });

    private static ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht gefunden",
        detail: "Diesen Eintrag gibt es in „Artikel und Leistungen“ nicht.", extensions: new Dictionary<string, object?> { ["code"] = "CATALOG_ITEM_NOT_FOUND" });

    private static ValueTask<object?> GuardAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next) => LexwareEndpoints.GuardAsync(context, next);
}
