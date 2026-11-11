using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Feedback;

/// <param name="Id">Vom Client erzeugt (UUIDv7); wiederholtes Senden legt keine zweite Meldung an.</param>
/// <param name="Area">Bereich bzw. Seite, z. B. „Bauakten“.</param>
/// <param name="Impact">Fehler: Auswirkung; Funktionswunsch: Wichtigkeit.</param>
/// <param name="AttachmentFileIds">Eigene, vorher hochgeladene Dateien ohne Bauakte (POST /files), z. B. Bildschirmfotos.</param>
public sealed record CreateFeedbackRequest(
    [property: Required] Guid Id,
    FeedbackKind Kind,
    [property: Required, MaxLength(FeedbackItem.MaxTitleLength)] string Title,
    [property: MaxLength(FeedbackItem.MaxTextLength)] string? Description,
    [property: Required, MaxLength(FeedbackItem.MaxAreaLength)] string Area,
    [property: MaxLength(FeedbackItem.MaxTextLength)] string? StepsToReproduce,
    [property: MaxLength(FeedbackItem.MaxTextLength)] string? ExpectedResult,
    [property: MaxLength(FeedbackItem.MaxTextLength)] string? ActualResult,
    FeedbackFrequency? Frequency,
    [property: MaxLength(FeedbackItem.MaxTextLength)] string? Problem,
    [property: MaxLength(FeedbackItem.MaxTextLength)] string? DesiredSolution,
    FeedbackImpact Impact,
    FeedbackContext? Context,
    IReadOnlyList<Guid>? AttachmentFileIds);

/// <param name="DuplicateOfNumber">Nummer der ursprünglichen Meldung (Pflicht bei Status Duplikat), z. B. F-2026-0003.</param>
public sealed record UpdateFeedbackRequest(
    FeedbackStatus? Status,
    FeedbackPriority? Priority,
    Guid? AssigneeUserId,
    bool Unassign,
    [property: MaxLength(20)] string? DuplicateOfNumber,
    FeedbackKind? Kind,
    [property: MaxLength(FeedbackItem.MaxAreaLength)] string? Area);

/// <param name="IsInternal">Interne Notiz (nur Verwaltung, für den Einreicher unsichtbar).</param>
public sealed record AddFeedbackCommentRequest(
    [property: MaxLength(FeedbackItem.MaxTextLength)] string? Text, bool IsInternal, IReadOnlyList<Guid>? AttachmentFileIds = null);

internal static class FeedbackEndpoints
{
    private const string EntityType = "feedback";

    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/feedback").WithTags("Feedback").RequireModule(AppModules.Feedback);

        group.MapPost("/", CreateAsync).WithName("CreateFeedback")
            .WithSummary("Fehler melden oder Funktion wünschen (jeder angemeldete Benutzer; idempotent über die ID)")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapGet("/mine", MineAsync).WithName("ListMyFeedback").WithSummary("Eigene Meldungen mit Status");
        group.MapGet("/", ListAsync).WithName("ListFeedback")
            .WithSummary("Alle Meldungen für die IT – Filter Art, Status (open, closed, all oder ein Status), Bereich, Priorität, Suche; mit Zählern")
            .RequirePermission(Permissions.FeedbackManage);
        group.MapGet("/{id:guid}", GetAsync).WithName("GetFeedback")
            .WithSummary("Meldung mit Verlauf (interne Notizen nur für die IT); fremde Meldungen: 404")
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPatch("/{id:guid}", UpdateAsync).WithName("UpdateFeedback")
            .WithSummary("Status, Priorität, Zuständigkeit, Duplikat-Verweis, Art und Bereich setzen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.FeedbackManage);
        group.MapPost("/{id:guid}/comments", CommentAsync).WithName("AddFeedbackComment")
            .WithSummary("Antwort (für den Einreicher sichtbar) oder interne Notiz der IT")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<Results<Created<FeedbackDetail>, Ok<FeedbackDetail>, ProblemHttpResult>> CreateAsync(
        CreateFeedbackRequest request, FeedbackService service, FeedbackNotifier notifier, EstrichOneDbContext db, ICurrentUser currentUser,
        IAuditLog audit, TimeProvider clock, INumberRanges numberRanges, CancellationToken cancellationToken)
    {
        var existing = await db.Set<FeedbackItem>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == request.Id, cancellationToken);
        if (existing is not null)
        {
            // Gleiche ID vom selben Benutzer: Wiederholung nach Verbindungsabbruch.
            return existing.ReporterUserId == currentUser.UserId && existing.Title == request.Title.Trim()
                ? TypedResults.Ok(await service.DetailAsync(existing, includeInternal: false, cancellationToken))
                : Problem(StatusCodes.Status409Conflict, "ID_CONFLICT", "ID bereits vergeben", "Unter dieser ID existiert bereits eine andere Meldung.");
        }

        var attachments = request.AttachmentFileIds ?? [];
        if (!await service.AreOwnLooseFilesAsync(attachments, currentUser.UserId, cancellationToken))
        {
            return Validation("Anhänge müssen eigene Dateien ohne Bauakte sein.");
        }

        var now = clock.GetUtcNow();
        FeedbackItem item;
        try
        {
            var context = request.Context is { } c ? JsonSerializer.Serialize(c.Sanitized(), FeedbackService.ContextJson) : null;
            item = FeedbackItem.Create(request.Id, string.Empty, request.Kind, request.Title, request.Description,
                request.Area, request.StepsToReproduce, request.ExpectedResult, request.ActualResult, request.Frequency, request.Problem,
                request.DesiredSolution, request.Impact, context, attachments, currentUser.UserId, now);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }

        item.SetNumber(await numberRanges.NextAsync(NumberRangeKeys.Feedback, cancellationToken));
        db.Add(item);
        db.Add(FeedbackEvent.Create(item.Id, currentUser.UserId, "created", null, nameof(FeedbackStatus.New), now));
        audit.Record("feedback.submitted", currentUser.UserId, EntityType, item.Id.ToString(), new Dictionary<string, string?>
        {
            ["number"] = item.Number, ["kind"] = item.Kind.ToString(), ["area"] = item.Area,
        });
        await db.SaveChangesAsync(cancellationToken);
        await notifier.SubmittedAsync(item, cancellationToken);
        return TypedResults.Created($"/api/v1/feedback/{item.Id}", await service.DetailAsync(item, includeInternal: false, cancellationToken));
    }

    private static async Task<Ok<IReadOnlyList<FeedbackSummary>>> MineAsync(
        FeedbackService service, EstrichOneDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.SummariesAsync(
            db.Set<FeedbackItem>().Where(f => f.ReporterUserId == currentUser.UserId).OrderByDescending(f => f.CreatedAt).Take(200), cancellationToken));

    private static async Task<Ok<FeedbackListResponse>> ListAsync(
        FeedbackService service, EstrichOneDbContext db, CancellationToken cancellationToken,
        string? kind = null, string? status = null, string? area = null, string? priority = null, string? q = null)
    {
        var query = db.Set<FeedbackItem>().AsQueryable();
        if (Enum.TryParse<FeedbackKind>(kind, ignoreCase: true, out var k))
        {
            query = query.Where(f => f.Kind == k);
        }

        query = (status ?? "open").ToLowerInvariant() switch
        {
            "all" => query,
            "closed" => query.Where(f => f.Status == FeedbackStatus.Done || f.Status == FeedbackStatus.Rejected || f.Status == FeedbackStatus.Duplicate),
            _ when Enum.TryParse<FeedbackStatus>(status, ignoreCase: true, out var s) => query.Where(f => f.Status == s),
            _ => query.Where(f => f.Status != FeedbackStatus.Done && f.Status != FeedbackStatus.Rejected && f.Status != FeedbackStatus.Duplicate),
        };
        if (!string.IsNullOrWhiteSpace(area))
        {
            var wanted = Escape(area.Trim());
            query = query.Where(f => EF.Functions.ILike(f.Area, wanted));
        }

        if (Enum.TryParse<FeedbackPriority>(priority, ignoreCase: true, out var p))
        {
            query = query.Where(f => f.Priority == p);
        }

        foreach (var word in (q ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(5))
        {
            var pattern = "%" + Escape(word) + "%";
            query = query.Where(f => EF.Functions.ILike(f.Number, pattern) || EF.Functions.ILike(f.Title, pattern) || EF.Functions.ILike(f.Area, pattern)
                || (f.Description != null && EF.Functions.ILike(f.Description, pattern)) || (f.ActualResult != null && EF.Functions.ILike(f.ActualResult, pattern))
                || (f.Problem != null && EF.Functions.ILike(f.Problem, pattern)));
        }

        var items = await service.SummariesAsync(query.OrderByDescending(f => f.LastActivityAt).Take(500), cancellationToken);
        var areas = await db.Set<FeedbackItem>().AsNoTracking().Select(f => f.Area).Distinct().OrderBy(a => a).ToListAsync(cancellationToken);
        return TypedResults.Ok(new FeedbackListResponse(items, await service.CountsAsync(cancellationToken), areas));
    }

    private static async Task<Results<Ok<FeedbackDetail>, ProblemHttpResult>> GetAsync(
        Guid id, FeedbackService service, EstrichOneDbContext db, ICurrentUser currentUser, IUserPermissions permissions, CancellationToken cancellationToken)
    {
        var (item, manager) = await FindVisibleAsync(id, db, currentUser, permissions, cancellationToken);
        return item is null ? NotFound() : TypedResults.Ok(await service.DetailAsync(item, includeInternal: manager, cancellationToken));
    }

    private static async Task<Results<Ok<FeedbackDetail>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateFeedbackRequest request, FeedbackService service, FeedbackNotifier notifier, EstrichOneDbContext db, ICurrentUser currentUser,
        IAuditLog audit, TimeProvider clock, CancellationToken cancellationToken)
    {
        var item = await db.Set<FeedbackItem>().SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (item is null)
        {
            return NotFound();
        }

        var now = clock.GetUtcNow();
        var changes = new Dictionary<string, string?> { ["number"] = item.Number };
        void Event(string kind, object? from, object? to)
        {
            if (!Equals(from, to))
            {
                db.Add(FeedbackEvent.Create(item.Id, currentUser.UserId, kind, from?.ToString(), to?.ToString(), now));
                changes[kind] = to?.ToString();
            }
        }

        var statusChanged = false;
        try
        {
            if (request.AssigneeUserId is not null || request.Unassign)
            {
                var target = request.Unassign ? null : request.AssigneeUserId;
                if (target is { } userId && !await db.Set<User>().AnyAsync(u => u.Id == userId && u.IsActive, cancellationToken))
                {
                    return Problem(StatusCodes.Status422UnprocessableEntity, "UNKNOWN_USER", "Unbekannter Benutzer", "Der Benutzer existiert nicht oder ist gesperrt.");
                }

                Event("assignee", item.AssigneeUserId, target);
                item.Assign(target);
            }

            if (request.Priority is { } priority)
            {
                Event("priority", item.Priority, priority);
                item.Prioritize(priority);
            }

            if (request.Kind is not null || request.Area is not null)
            {
                var kind = request.Kind ?? item.Kind;
                var area = request.Area?.Trim() ?? item.Area;
                var from = $"{FeedbackService.KindName(item.Kind)} · {item.Area}";
                var to = $"{FeedbackService.KindName(kind)} · {area}";
                item.Classify(kind, area);
                Event("classify", from, to);
            }

            if (request.Status is { } status)
            {
                Guid? duplicateOf = null;
                if (status == FeedbackStatus.Duplicate)
                {
                    var number = request.DuplicateOfNumber?.Trim().ToUpperInvariant();
                    duplicateOf = string.IsNullOrEmpty(number)
                        ? item.DuplicateOfId
                        : await db.Set<FeedbackItem>().Where(f => f.Number == number).Select(f => (Guid?)f.Id).SingleOrDefaultAsync(cancellationToken);
                    if (duplicateOf is null)
                    {
                        return Validation(string.IsNullOrEmpty(number)
                            ? "Bitte die Nummer der ursprünglichen Meldung angeben."
                            : $"Die Meldung {number} gibt es nicht.");
                    }
                }

                statusChanged = item.Status != status || item.DuplicateOfId != duplicateOf;
                Event("status", item.Status, status);
                item.ChangeStatus(status, duplicateOf, now);
            }
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }

        item.Touch(now);
        if (changes.Count > 1)
        {
            audit.Record("feedback.updated", currentUser.UserId, EntityType, item.Id.ToString(), changes);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (statusChanged)
        {
            await notifier.ReporterUpdatedAsync(item, reply: false, cancellationToken);
        }

        return TypedResults.Ok(await service.DetailAsync(item, includeInternal: true, cancellationToken));
    }

    private static async Task<Results<Ok<FeedbackDetail>, ProblemHttpResult>> CommentAsync(
        Guid id, AddFeedbackCommentRequest request, FeedbackService service, FeedbackNotifier notifier, EstrichOneDbContext db, ICurrentUser currentUser,
        IUserPermissions permissions, IAuditLog audit, TimeProvider clock, CancellationToken cancellationToken)
    {
        var (item, manager) = await FindVisibleAsync(id, db, currentUser, permissions, cancellationToken, tracking: true);
        if (item is null)
        {
            return NotFound();
        }

        if (request.IsInternal && !manager)
        {
            return Problem(StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "Keine Berechtigung", "Interne Notizen schreibt nur die IT.");
        }

        var attachments = request.AttachmentFileIds ?? [];
        if (!await service.AreOwnLooseFilesAsync(attachments, currentUser.UserId, cancellationToken))
        {
            return Validation("Anhänge müssen eigene Dateien ohne Bauakte sein.");
        }

        var now = clock.GetUtcNow();
        try
        {
            db.Add(FeedbackComment.Create(item.Id, currentUser.UserId, request.Text ?? string.Empty, request.IsInternal, attachments, now));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }

        item.Touch(now);
        audit.Record(request.IsInternal ? "feedback.note_added" : "feedback.commented", currentUser.UserId, EntityType, item.Id.ToString(),
            new Dictionary<string, string?> { ["number"] = item.Number });
        await db.SaveChangesAsync(cancellationToken);
        if (!request.IsInternal)
        {
            if (item.ReporterUserId == currentUser.UserId)
            {
                await notifier.ReporterRepliedAsync(item, cancellationToken);
            }
            else
            {
                await notifier.ReporterUpdatedAsync(item, reply: true, cancellationToken);
            }
        }

        return TypedResults.Ok(await service.DetailAsync(item, includeInternal: manager, cancellationToken));
    }

    /// <summary>Sichtbar für den Einreicher und die IT; sonst 404 (verrät keine fremden Meldungen).</summary>
    private static async Task<(FeedbackItem? Item, bool Manager)> FindVisibleAsync(
        Guid id, EstrichOneDbContext db, ICurrentUser currentUser, IUserPermissions permissions, CancellationToken cancellationToken, bool tracking = false)
    {
        var query = tracking ? db.Set<FeedbackItem>() : db.Set<FeedbackItem>().AsNoTracking();
        var item = await query.SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        var manager = await permissions.HasAsync(Permissions.FeedbackManage, cancellationToken);
        return item is not null && (manager || item.ReporterUserId == currentUser.UserId) ? (item, manager) : (null, false);
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private static ProblemHttpResult NotFound() =>
        Problem(StatusCodes.Status404NotFound, "FEEDBACK_NOT_FOUND", "Meldung nicht gefunden", "Die Meldung existiert nicht oder ist nicht sichtbar.");

    private static ProblemHttpResult Validation(string message) =>
        Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Ungültige Angaben", message);

    private static ProblemHttpResult Problem(int status, string code, string title, string detail) =>
        TypedResults.Problem(statusCode: status, title: title, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });
}
