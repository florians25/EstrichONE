using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Helpdesk;

public sealed record HelpdeskGroupDto(Guid Id, string Name, string? Description, IReadOnlyList<Guid> MemberUserIds, bool IsActive, int SortOrder, long Version);

public sealed record SaveHelpdeskGroupRequest(
    [property: Required, MaxLength(100)] string Name, [property: MaxLength(500)] string? Description, IReadOnlyList<Guid>? MemberUserIds,
    bool IsActive = true, int SortOrder = 0);

public sealed record TicketSubcategoryDto(Guid Id, TicketCategory Category, string Name, bool IsActive, int SortOrder, long Version);

public sealed record SaveTicketSubcategoryRequest(TicketCategory Category, [property: Required, MaxLength(100)] string Name, bool IsActive = true, int SortOrder = 0);

public sealed record HelpdeskRuleDto(
    Guid Id, string Name, TicketCategory? Category, Guid? SubcategoryId, string? Keyword, Guid? GroupId, AssignmentStrategy Strategy, Guid? AssigneeUserId,
    TicketPriority? Priority, bool IsActive, int SortOrder, long Version);

public sealed record SaveHelpdeskRuleRequest(
    [property: Required, MaxLength(100)] string Name, TicketCategory? Category, Guid? SubcategoryId, [property: MaxLength(100)] string? Keyword, Guid? GroupId,
    AssignmentStrategy Strategy, Guid? AssigneeUserId, TicketPriority? Priority, bool IsActive = true, int SortOrder = 0);

public sealed record CannedResponseDto(Guid Id, string Name, string Text, TicketCategory? Category, bool IsPersonal, int SortOrder, long Version);

public sealed record SaveCannedResponseRequest(
    [property: Required, MaxLength(100)] string Name, [property: Required, MaxLength(8000)] string Text, TicketCategory? Category, bool IsPersonal = false,
    int SortOrder = 0);

public sealed record KnowledgeArticleDto(
    Guid Id, string Title, string Body, TicketCategory? Category, string? Keywords, bool IsPublished, string? AuthorName, string? UpdatedByName,
    int Views, int HelpfulCount, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version);

public sealed record SaveKnowledgeArticleRequest(
    [property: Required, MaxLength(200)] string Title, [property: Required, MaxLength(KnowledgeArticle.MaxBodyLength)] string Body, TicketCategory? Category,
    [property: MaxLength(500)] string? Keywords, bool IsPublished = true);

public sealed record HelpdeskAgentDto(Guid Id, string DisplayName, int OpenTickets);

public sealed record HelpdeskCategoryOption(TicketCategory Key, string Name, IReadOnlyList<TicketSubcategoryDto> Subcategories);

/// <summary>Was Web und Apps zum Melden brauchen: Kategorien mit Unterkategorien und Schalter.</summary>
public sealed record HelpdeskOptions(IReadOnlyList<HelpdeskCategoryOption> Categories, bool RatingEnabled, bool SuggestArticles, bool TimeTrackingEnabled,
    bool SlaEnabled);

/// <summary>Einrichtung des Helpdesks und Wissensdatenbank (/helpdesk).</summary>
internal static class HelpdeskEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/helpdesk").WithTags("Helpdesk").RequireModule(AppModules.Helpdesk);

        group.MapGet("/options", OptionsAsync).WithName("GetHelpdeskOptions").WithSummary("Kategorien und Unterkategorien zum Melden").AllowPendingDevice();
        group.MapGet("/settings", (HelpdeskSettingsStore store, CancellationToken ct) => store.GetAsync(ct)).WithName("GetHelpdeskSettings")
            .WithSummary("Reaktions- und Lösungsziele, Geschäftszeiten, Optionen").RequirePermission(Permissions.HelpdeskAgent);
        group.MapPut("/settings", SaveSettingsAsync).WithName("SaveHelpdeskSettings").WithSummary("Helpdesk-Einstellungen speichern")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HelpdeskManage);
        group.MapGet("/agents", AgentsAsync).WithName("ListHelpdeskAgents").WithSummary("Bearbeiter mit Anzahl offener Tickets (Zuweisen, Erwähnen)")
            .RequirePermission(Permissions.HelpdeskAgent);

        group.MapGet("/groups", GroupsAsync).WithName("ListHelpdeskGroups").WithSummary("Bearbeitungsgruppen").RequirePermission(Permissions.HelpdeskAgent);
        group.MapPost("/groups", CreateGroupAsync).WithName("CreateHelpdeskGroup").WithSummary("Gruppe anlegen")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HelpdeskManage);
        group.MapPut("/groups/{id:guid}", UpdateGroupAsync).WithName("UpdateHelpdeskGroup").WithSummary("Gruppe ändern")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HelpdeskManage);
        group.MapDelete("/groups/{id:guid}", DeleteAsync<HelpdeskGroup>).WithName("DeleteHelpdeskGroup").WithSummary("Gruppe löschen (Tickets behalten)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HelpdeskManage);

        group.MapGet("/subcategories", SubcategoriesAsync).WithName("ListTicketSubcategories").WithSummary("Unterkategorien")
            .RequirePermission(Permissions.HelpdeskAgent);
        group.MapPost("/subcategories", CreateSubcategoryAsync).WithName("CreateTicketSubcategory").WithSummary("Unterkategorie anlegen")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HelpdeskManage);
        group.MapPut("/subcategories/{id:guid}", UpdateSubcategoryAsync).WithName("UpdateTicketSubcategory").WithSummary("Unterkategorie ändern")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HelpdeskManage);
        group.MapDelete("/subcategories/{id:guid}", DeleteAsync<TicketSubcategory>).WithName("DeleteTicketSubcategory")
            .WithSummary("Unterkategorie löschen (Tickets behalten)").ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HelpdeskManage);

        group.MapGet("/rules", RulesAsync).WithName("ListHelpdeskRules").WithSummary("Regeln der automatischen Zuweisung")
            .RequirePermission(Permissions.HelpdeskAgent);
        group.MapPost("/rules", CreateRuleAsync).WithName("CreateHelpdeskRule").WithSummary("Zuweisungsregel anlegen")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HelpdeskManage);
        group.MapPut("/rules/{id:guid}", UpdateRuleAsync).WithName("UpdateHelpdeskRule").WithSummary("Zuweisungsregel ändern")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HelpdeskManage);
        group.MapDelete("/rules/{id:guid}", DeleteAsync<HelpdeskRule>).WithName("DeleteHelpdeskRule").WithSummary("Zuweisungsregel löschen")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HelpdeskManage);

        group.MapGet("/responses", ResponsesAsync).WithName("ListCannedResponses").WithSummary("Textbausteine (gemeinsame und eigene)")
            .RequirePermission(Permissions.HelpdeskAgent);
        group.MapPost("/responses", CreateResponseAsync).WithName("CreateCannedResponse").WithSummary("Textbaustein anlegen")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HelpdeskAgent);
        group.MapPut("/responses/{id:guid}", UpdateResponseAsync).WithName("UpdateCannedResponse").WithSummary("Textbaustein ändern")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HelpdeskAgent);
        group.MapDelete("/responses/{id:guid}", DeleteResponseAsync).WithName("DeleteCannedResponse").WithSummary("Textbaustein löschen")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HelpdeskAgent);

        group.MapGet("/articles", ArticlesAsync).WithName("ListKnowledgeArticles")
            .WithSummary("Wissensdatenbank: veröffentlichte Artikel (Bearbeitung sieht auch Entwürfe); q = Suche").AllowPendingDevice();
        group.MapGet("/articles/suggest", SuggestAsync).WithName("SuggestKnowledgeArticles")
            .WithSummary("Passende veröffentlichte Artikel zu Titel und Beschreibung (beim Melden)").AllowPendingDevice();
        group.MapGet("/articles/{id:guid}", ArticleAsync).WithName("GetKnowledgeArticle").WithSummary("Artikel lesen")
            .ProducesProblem(StatusCodes.Status404NotFound).AllowPendingDevice();
        group.MapPost("/articles/{id:guid}/helpful", HelpfulAsync).WithName("MarkKnowledgeArticleHelpful").WithSummary("„Hat geholfen“")
            .ProducesProblem(StatusCodes.Status404NotFound).AllowPendingDevice();
        group.MapPost("/articles", CreateArticleAsync).WithName("CreateKnowledgeArticle").WithSummary("Artikel anlegen")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HelpdeskAgent);
        group.MapPut("/articles/{id:guid}", UpdateArticleAsync).WithName("UpdateKnowledgeArticle").WithSummary("Artikel ändern")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HelpdeskAgent);
        group.MapDelete("/articles/{id:guid}", DeleteAsync<KnowledgeArticle>).WithName("DeleteKnowledgeArticle").WithSummary("Artikel löschen")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HelpdeskAgent);
    }

    private static ProblemHttpResult Invalid(string message) =>
        TicketEndpoints.Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Ungültige Angaben", message);

    private static ProblemHttpResult Missing() =>
        TicketEndpoints.Problem(StatusCodes.Status404NotFound, "NOT_FOUND", "Nicht gefunden", "Der Eintrag existiert nicht (mehr).");

    private static async Task<Ok<HelpdeskOptions>> OptionsAsync(EstrichOneDbContext db, HelpdeskSettingsStore store, CancellationToken cancellationToken)
    {
        var settings = await store.GetAsync(cancellationToken);
        var subs = await db.Set<TicketSubcategory>().AsNoTracking().Where(s => s.IsActive).OrderBy(s => s.SortOrder).ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);
        var categories = Enum.GetValues<TicketCategory>().Select(c => new HelpdeskCategoryOption(c, TicketService.CategoryName(c),
            subs.Where(s => s.Category == c).Select(ToDto).ToList())).ToList();
        return TypedResults.Ok(new HelpdeskOptions(categories, settings.RatingEnabled, settings.SuggestArticles, settings.TimeTrackingEnabled, settings.SlaEnabled));
    }

    private static async Task<Results<Ok<HelpdeskSettings>, ProblemHttpResult>> SaveSettingsAsync(
        HelpdeskSettings request, HelpdeskSettingsStore store, CancellationToken cancellationToken)
    {
        if (request.Hours is { } hours && (!TimeOnly.TryParse(hours.Start, System.Globalization.CultureInfo.InvariantCulture, out var start)
            || !TimeOnly.TryParse(hours.End, System.Globalization.CultureInfo.InvariantCulture, out var end) || end <= start))
        {
            return Invalid("Bitte gültige Geschäftszeiten angeben (Beginn vor Ende, z. B. 07:00 bis 17:00).");
        }

        if (request.Hours?.Days is { Count: 0 })
        {
            return Invalid("Bitte mindestens einen Arbeitstag wählen.");
        }

        return TypedResults.Ok(await store.SaveAsync(request, cancellationToken));
    }

    private static async Task<Ok<IReadOnlyList<HelpdeskAgentDto>>> AgentsAsync(EstrichOneDbContext db, IRoleAssignments roles, CancellationToken cancellationToken)
    {
        var ids = await roles.GetUserIdsWithPermissionAsync(Permissions.HelpdeskAgent, cancellationToken);
        var users = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id) && u.IsActive).OrderBy(u => u.DisplayName)
            .Select(u => new { u.Id, u.DisplayName }).ToListAsync(cancellationToken);
        var load = await db.Set<Ticket>().AsNoTracking()
            .Where(t => t.AssigneeUserId != null && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .GroupBy(t => t.AssigneeUserId!.Value).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return TypedResults.Ok<IReadOnlyList<HelpdeskAgentDto>>(users.Select(u => new HelpdeskAgentDto(u.Id, u.DisplayName, load.GetValueOrDefault(u.Id))).ToList());
    }

    // ---- Gruppen -------------------------------------------------------------------------------------------------

    private static HelpdeskGroupDto ToDto(HelpdeskGroup g) => new(g.Id, g.Name, g.Description, g.MemberUserIds, g.IsActive, g.SortOrder, g.Version);

    private static async Task<Ok<IReadOnlyList<HelpdeskGroupDto>>> GroupsAsync(EstrichOneDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok<IReadOnlyList<HelpdeskGroupDto>>((await db.Set<HelpdeskGroup>().AsNoTracking().OrderBy(g => g.SortOrder).ThenBy(g => g.Name)
            .ToListAsync(cancellationToken)).Select(ToDto).ToList());

    private static async Task<Results<Ok<HelpdeskGroupDto>, ProblemHttpResult>> CreateGroupAsync(SaveHelpdeskGroupRequest request, EstrichOneDbContext db,
        CancellationToken cancellationToken)
    {
        try
        {
            var group = HelpdeskGroup.Create(Guid.CreateVersion7(), request.Name, request.Description, request.MemberUserIds ?? [], request.IsActive,
                request.SortOrder);
            db.Add(group);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(ToDto(group));
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception.Message);
        }
    }

    private static async Task<Results<Ok<HelpdeskGroupDto>, ProblemHttpResult>> UpdateGroupAsync(Guid id, SaveHelpdeskGroupRequest request,
        EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var group = await db.Set<HelpdeskGroup>().SingleOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (group is null)
        {
            return Missing();
        }

        try
        {
            group.Update(request.Name, request.Description, request.MemberUserIds ?? [], request.IsActive, request.SortOrder);
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToDto(group));
    }

    // ---- Unterkategorien -----------------------------------------------------------------------------------------

    private static TicketSubcategoryDto ToDto(TicketSubcategory s) => new(s.Id, s.Category, s.Name, s.IsActive, s.SortOrder, s.Version);

    private static async Task<Ok<IReadOnlyList<TicketSubcategoryDto>>> SubcategoriesAsync(EstrichOneDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok<IReadOnlyList<TicketSubcategoryDto>>((await db.Set<TicketSubcategory>().AsNoTracking().OrderBy(s => s.Category).ThenBy(s => s.SortOrder)
            .ThenBy(s => s.Name).ToListAsync(cancellationToken)).Select(ToDto).ToList());

    private static async Task<Results<Ok<TicketSubcategoryDto>, ProblemHttpResult>> CreateSubcategoryAsync(SaveTicketSubcategoryRequest request,
        EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            var item = TicketSubcategory.Create(Guid.CreateVersion7(), request.Category, request.Name, request.IsActive, request.SortOrder);
            db.Add(item);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(ToDto(item));
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception.Message);
        }
    }

    private static async Task<Results<Ok<TicketSubcategoryDto>, ProblemHttpResult>> UpdateSubcategoryAsync(Guid id, SaveTicketSubcategoryRequest request,
        EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var item = await db.Set<TicketSubcategory>().SingleOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (item is null)
        {
            return Missing();
        }

        try
        {
            item.Update(request.Category, request.Name, request.IsActive, request.SortOrder);
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToDto(item));
    }

    // ---- Regeln --------------------------------------------------------------------------------------------------

    private static HelpdeskRuleDto ToDto(HelpdeskRule r) =>
        new(r.Id, r.Name, r.Category, r.SubcategoryId, r.Keyword, r.GroupId, r.Strategy, r.AssigneeUserId, r.Priority, r.IsActive, r.SortOrder, r.Version);

    private static async Task<Ok<IReadOnlyList<HelpdeskRuleDto>>> RulesAsync(EstrichOneDbContext db, CancellationToken cancellationToken) =>
        TypedResults.Ok<IReadOnlyList<HelpdeskRuleDto>>((await db.Set<HelpdeskRule>().AsNoTracking().OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
            .ToListAsync(cancellationToken)).Select(ToDto).ToList());

    private static async Task<string?> ValidateRuleAsync(SaveHelpdeskRuleRequest request, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (request.GroupId is { } g && !await db.Set<HelpdeskGroup>().AnyAsync(x => x.Id == g, cancellationToken))
        {
            return "Die Gruppe existiert nicht.";
        }

        if (request.SubcategoryId is { } s && !await db.Set<TicketSubcategory>().AnyAsync(x => x.Id == s && (request.Category == null || x.Category == request.Category),
                cancellationToken))
        {
            return "Die Unterkategorie existiert nicht oder passt nicht zur Kategorie.";
        }

        return request.AssigneeUserId is { } u && !await db.Set<User>().AnyAsync(x => x.Id == u && x.IsActive, cancellationToken)
            ? "Der Benutzer existiert nicht oder ist gesperrt."
            : null;
    }

    private static async Task<Results<Ok<HelpdeskRuleDto>, ProblemHttpResult>> CreateRuleAsync(SaveHelpdeskRuleRequest request, EstrichOneDbContext db,
        CancellationToken cancellationToken)
    {
        if (await ValidateRuleAsync(request, db, cancellationToken) is { } error)
        {
            return Invalid(error);
        }

        try
        {
            var rule = HelpdeskRule.Create(Guid.CreateVersion7(), request.Name, request.Category, request.SubcategoryId, request.Keyword, request.GroupId,
                request.Strategy, request.AssigneeUserId, request.Priority, request.IsActive, request.SortOrder);
            db.Add(rule);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(ToDto(rule));
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception.Message);
        }
    }

    private static async Task<Results<Ok<HelpdeskRuleDto>, ProblemHttpResult>> UpdateRuleAsync(Guid id, SaveHelpdeskRuleRequest request,
        EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var rule = await db.Set<HelpdeskRule>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rule is null)
        {
            return Missing();
        }

        if (await ValidateRuleAsync(request, db, cancellationToken) is { } error)
        {
            return Invalid(error);
        }

        try
        {
            rule.Update(request.Name, request.Category, request.SubcategoryId, request.Keyword, request.GroupId, request.Strategy, request.AssigneeUserId,
                request.Priority, request.IsActive, request.SortOrder);
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToDto(rule));
    }

    // ---- Textbausteine -------------------------------------------------------------------------------------------

    private static CannedResponseDto ToDto(TicketCannedResponse r) => new(r.Id, r.Name, r.Text, r.Category, r.OwnerUserId is not null, r.SortOrder, r.Version);

    private static async Task<Ok<IReadOnlyList<CannedResponseDto>>> ResponsesAsync(EstrichOneDbContext db, ICurrentUser currentUser,
        CancellationToken cancellationToken) =>
        TypedResults.Ok<IReadOnlyList<CannedResponseDto>>((await db.Set<TicketCannedResponse>().AsNoTracking()
            .Where(r => r.OwnerUserId == null || r.OwnerUserId == currentUser.UserId).OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
            .ToListAsync(cancellationToken)).Select(ToDto).ToList());

    private static async Task<Results<Ok<CannedResponseDto>, ProblemHttpResult>> CreateResponseAsync(SaveCannedResponseRequest request,
        EstrichOneDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        try
        {
            var item = TicketCannedResponse.Create(Guid.CreateVersion7(), request.Name, request.Text, request.Category,
                request.IsPersonal ? currentUser.UserId : null, request.SortOrder);
            db.Add(item);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(ToDto(item));
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception.Message);
        }
    }

    private static async Task<Results<Ok<CannedResponseDto>, ProblemHttpResult>> UpdateResponseAsync(Guid id, SaveCannedResponseRequest request,
        EstrichOneDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var item = await db.Set<TicketCannedResponse>().SingleOrDefaultAsync(r => r.Id == id && (r.OwnerUserId == null || r.OwnerUserId == currentUser.UserId),
            cancellationToken);
        if (item is null)
        {
            return Missing();
        }

        try
        {
            item.Update(request.Name, request.Text, request.Category, request.SortOrder);
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToDto(item));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteResponseAsync(Guid id, EstrichOneDbContext db, ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var item = await db.Set<TicketCannedResponse>().SingleOrDefaultAsync(r => r.Id == id && (r.OwnerUserId == null || r.OwnerUserId == currentUser.UserId),
            cancellationToken);
        if (item is null)
        {
            return Missing();
        }

        db.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    // ---- Wissensdatenbank ----------------------------------------------------------------------------------------

    private static async Task<Ok<IReadOnlyList<KnowledgeArticleSummary>>> ArticlesAsync(EstrichOneDbContext db, IUserPermissions permissions,
        CancellationToken cancellationToken, string? q = null, string? category = null)
    {
        var agent = await permissions.HasAsync(Permissions.HelpdeskAgent, cancellationToken);
        var articles = await db.Set<KnowledgeArticle>().AsNoTracking().Where(a => agent || a.IsPublished).ToListAsync(cancellationToken);
        TicketCategory? c = Enum.TryParse<TicketCategory>(category, ignoreCase: true, out var parsed) ? parsed : null;
        if (!string.IsNullOrWhiteSpace(q))
        {
            return TypedResults.Ok(KnowledgeSearch.Rank(articles, q, c, publishedOnly: !agent, take: 50));
        }

        return TypedResults.Ok<IReadOnlyList<KnowledgeArticleSummary>>(articles.Where(a => c is null || a.Category == c)
            .OrderByDescending(a => a.HelpfulCount).ThenBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase).Select(KnowledgeSearch.Summarize).ToList());
    }

    private static async Task<Ok<IReadOnlyList<KnowledgeArticleSummary>>> SuggestAsync(EstrichOneDbContext db, HelpdeskSettingsStore store,
        CancellationToken cancellationToken, string? q = null, string? category = null)
    {
        if (!(await store.GetAsync(cancellationToken)).SuggestArticles || string.IsNullOrWhiteSpace(q))
        {
            return TypedResults.Ok<IReadOnlyList<KnowledgeArticleSummary>>([]);
        }

        var articles = await db.Set<KnowledgeArticle>().AsNoTracking().Where(a => a.IsPublished).ToListAsync(cancellationToken);
        TicketCategory? c = Enum.TryParse<TicketCategory>(category, ignoreCase: true, out var parsed) ? parsed : null;
        return TypedResults.Ok(KnowledgeSearch.Rank(articles, q, c, publishedOnly: true, take: 3));
    }

    private static async Task<KnowledgeArticleDto> ArticleDtoAsync(KnowledgeArticle a, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var ids = new[] { a.AuthorUserId, a.UpdatedByUserId }.OfType<Guid>().Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        return new KnowledgeArticleDto(a.Id, a.Title, a.Body, a.Category, a.Keywords, a.IsPublished,
            a.AuthorUserId is { } author ? names.GetValueOrDefault(author) : null, a.UpdatedByUserId is { } editor ? names.GetValueOrDefault(editor) : null,
            a.Views, a.HelpfulCount, a.CreatedAt, a.UpdatedAt, a.Version);
    }

    private static async Task<Results<Ok<KnowledgeArticleDto>, ProblemHttpResult>> ArticleAsync(Guid id, EstrichOneDbContext db, IUserPermissions permissions,
        CancellationToken cancellationToken)
    {
        var agent = await permissions.HasAsync(Permissions.HelpdeskAgent, cancellationToken);
        var article = await db.Set<KnowledgeArticle>().SingleOrDefaultAsync(a => a.Id == id && (agent || a.IsPublished), cancellationToken);
        if (article is null)
        {
            return Missing();
        }

        // Aufrufe zählen ohne Versionswechsel (kein Konflikt mit gleichzeitiger Bearbeitung).
        await db.Database.ExecuteSqlAsync($"UPDATE knowledge_articles SET views = views + 1 WHERE id = {id}", cancellationToken);
        return TypedResults.Ok(await ArticleDtoAsync(article, db, cancellationToken));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HelpfulAsync(Guid id, EstrichOneDbContext db, IUserPermissions permissions,
        CancellationToken cancellationToken)
    {
        var agent = await permissions.HasAsync(Permissions.HelpdeskAgent, cancellationToken);
        var updated = await db.Database.ExecuteSqlAsync(
            $"UPDATE knowledge_articles SET helpful_count = helpful_count + 1 WHERE id = {id} AND (is_published OR {agent})", cancellationToken);
        return updated == 0 ? Missing() : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<KnowledgeArticleDto>, ProblemHttpResult>> CreateArticleAsync(SaveKnowledgeArticleRequest request,
        EstrichOneDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        try
        {
            var article = KnowledgeArticle.Create(Guid.CreateVersion7(), request.Title, request.Body, request.Category, request.Keywords, request.IsPublished,
                currentUser.UserId);
            db.Add(article);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(await ArticleDtoAsync(article, db, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception.Message);
        }
    }

    private static async Task<Results<Ok<KnowledgeArticleDto>, ProblemHttpResult>> UpdateArticleAsync(Guid id, SaveKnowledgeArticleRequest request,
        EstrichOneDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var article = await db.Set<KnowledgeArticle>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (article is null)
        {
            return Missing();
        }

        try
        {
            article.Update(request.Title, request.Body, request.Category, request.Keywords, request.IsPublished, currentUser.UserId);
        }
        catch (ArgumentException exception)
        {
            return Invalid(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await ArticleDtoAsync(article, db, cancellationToken));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync<T>(Guid id, EstrichOneDbContext db, CancellationToken cancellationToken)
        where T : class
    {
        var entity = await db.Set<T>().FindAsync([id], cancellationToken);
        if (entity is null)
        {
            return Missing();
        }

        db.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }
}
