using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Notifications.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Work;

public sealed record CreateWorkItemRequest(
    [property: Required] Guid Id,
    Guid? BauakteId,
    WorkItemKind Kind,
    [property: Required, MaxLength(200)] string Title,
    [property: MaxLength(4000)] string? Description,
    DateOnly? DueDate,
    Guid? AssigneeUserId);

/// <summary>Nur gesetzte Felder werden geändert; <c>ClearDueDate</c>/<c>Unassign</c> leeren ausdrücklich.</summary>
public sealed record UpdateWorkItemRequest(
    [property: MaxLength(200)] string? Title,
    [property: MaxLength(4000)] string? Description,
    DateOnly? DueDate,
    bool ClearDueDate,
    Guid? AssigneeUserId,
    bool Unassign,
    WorkItemStatus? Status);

/// <summary>Aufgaben und Mängel für Web und Desktop. Die Apps nutzen den Sync (gleiche Regeln).</summary>
internal static class WorkEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/bauakten/{id:guid}/work-items", ListForBauakteAsync).WithName("ListBauakteWorkItems").WithTags("Aufgaben")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.WorkRead);

        var group = api.MapGroup("/work-items").WithTags("Aufgaben");
        group.MapGet("/mine", MineAsync).WithName("ListMyWorkItems").WithSummary("Offene Aufgaben, die mir zugewiesen sind")
            .RequirePermission(Permissions.WorkRead);
        group.MapPost("/", CreateAsync).WithName("CreateWorkItem").WithSummary("Aufgabe oder Mangel anlegen (idempotent über die ID)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.WorkEdit);
        group.MapPatch("/{id:guid}", UpdateAsync).WithName("UpdateWorkItem")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.WorkEdit);
    }

    private static async Task<Results<Ok<List<WorkItemDto>>, ProblemHttpResult>> ListForBauakteAsync(
        Guid id, IBauakteAccess access, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(id, cancellationToken))
        {
            return BauakteNotFound();
        }

        var items = await db.Set<WorkItem>().AsNoTracking().Where(w => w.BauakteId == id)
            .OrderBy(w => w.Status == WorkItemStatus.Done).ThenBy(w => w.DueDate == null).ThenBy(w => w.DueDate).ThenBy(w => w.Title)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(items.Select(WorkItemDto.From).ToList());
    }

    private static async Task<Ok<List<WorkItemDto>>> MineAsync(EstrichOneDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var items = await db.Set<WorkItem>().AsNoTracking()
            .Where(w => w.AssigneeUserId == currentUser.UserId && w.Status != WorkItemStatus.Done)
            .OrderBy(w => w.DueDate == null).ThenBy(w => w.DueDate).Take(200).ToListAsync(cancellationToken);
        return TypedResults.Ok(items.Select(WorkItemDto.From).ToList());
    }

    private static async Task<Results<Created<WorkItemDto>, Ok<WorkItemDto>, ProblemHttpResult>> CreateAsync(
        CreateWorkItemRequest request, IBauakteAccess access, IBauakteHistory history, ICurrentUser currentUser, EstrichOneDbContext db,
        INotifier notifier, IBauakteReportSource bauakten, CancellationToken cancellationToken)
    {
        if (request.BauakteId is { } bauakteId && !await access.CanReadAsync(bauakteId, cancellationToken))
        {
            return BauakteNotFound();
        }

        var existing = await db.Set<WorkItem>().IgnoreQueryFilters([QueryFilters.NotDeleted]).AsNoTracking().SingleOrDefaultAsync(w => w.Id == request.Id, cancellationToken);
        if (existing is not null)
        {
            return existing.CreatedByUserId == currentUser.UserId
                ? TypedResults.Ok(WorkItemDto.From(existing))
                : TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "ID bereits vergeben", extensions: new Dictionary<string, object?> { ["code"] = "ID_CONFLICT" });
        }

        if (request.AssigneeUserId is { } assignee && !await db.Set<User>().AnyAsync(u => u.Id == assignee && u.IsActive, cancellationToken))
        {
            return Validation("Der zugewiesene Benutzer existiert nicht oder ist gesperrt.");
        }

        WorkItem item;
        try
        {
            item = WorkItem.Create(request.Id, request.Kind, request.Title, currentUser.UserId, request.BauakteId);
            item.Update(item.Title, Clean(request.Description), request.DueDate, request.AssigneeUserId);
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }

        db.Add(item);
        if (request.BauakteId is { } owner)
        {
            history.Record(owner, BauakteHistoryActions.WorkItemCreated, new Dictionary<string, string?> { ["kind"] = item.Kind.ToString() });
        }

        await db.SaveChangesAsync(cancellationToken);
        await NotifyAssignedAsync(item, null, notifier, bauakten, cancellationToken);
        return TypedResults.Created($"/api/v1/work-items/{item.Id}", WorkItemDto.From(item));
    }

    private static async Task<Results<Ok<WorkItemDto>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateWorkItemRequest request, IBauakteAccess access, IBauakteHistory history, EstrichOneDbContext db, TimeProvider clock,
        INotifier notifier, IBauakteReportSource bauakten, CancellationToken cancellationToken)
    {
        var item = await db.Set<WorkItem>().SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (item is null || (item.BauakteId is { } bauakteId && !await access.CanReadAsync(bauakteId, cancellationToken)))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Aufgabe nicht gefunden",
                extensions: new Dictionary<string, object?> { ["code"] = "NOT_FOUND" });
        }

        var previousAssignee = item.AssigneeUserId;
        var assignee = request.Unassign ? null : request.AssigneeUserId ?? item.AssigneeUserId;
        if (request.AssigneeUserId is { } newAssignee && !await db.Set<User>().AnyAsync(u => u.Id == newAssignee && u.IsActive, cancellationToken))
        {
            return Validation("Der zugewiesene Benutzer existiert nicht oder ist gesperrt.");
        }

        try
        {
            item.Update(request.Title ?? item.Title, request.Description is null ? item.Description : Clean(request.Description),
                request.ClearDueDate ? null : request.DueDate ?? item.DueDate, assignee);
            if (request.Status is { } status)
            {
                var wasDone = item.Status == WorkItemStatus.Done;
                item.ChangeStatus(status, clock.GetUtcNow());
                if (!wasDone && status == WorkItemStatus.Done && item.BauakteId is { } owner)
                {
                    history.Record(owner, BauakteHistoryActions.WorkItemDone, new Dictionary<string, string?> { ["kind"] = item.Kind.ToString() });
                }
            }
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        await NotifyAssignedAsync(item, previousAssignee, notifier, bauakten, cancellationToken);
        return TypedResults.Ok(WorkItemDto.From(item));
    }

    /// <summary>Neu zugewiesene Person benachrichtigen (nur Titel und Bauakte, nie die Beschreibung).</summary>
    private static async Task NotifyAssignedAsync(WorkItem item, Guid? previousAssignee, INotifier notifier, IBauakteReportSource bauakten,
        CancellationToken cancellationToken)
    {
        if (item.AssigneeUserId is not { } assignee || assignee == previousAssignee || item.Status == WorkItemStatus.Done)
        {
            return;
        }

        var info = item.BauakteId is { } id ? await bauakten.GetAsync(id, cancellationToken) : null;
        var defect = item.Kind == WorkItemKind.Defect;
        await notifier.NotifyAsync([assignee], new NotificationRequest(NotificationCategory.Work,
            defect ? "Mangel zugewiesen" : "Aufgabe zugewiesen",
            info is null ? item.Title : $"{item.Title} · {info.Number} {info.Title}".Trim(),
            item.BauakteId is { } b ? $"/bauakten/{b}" : "/start", "workItem", item.Id, defect ? NotificationUrgency.High : NotificationUrgency.Normal,
            Event: NotificationEvents.WorkAssigned), cancellationToken);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static ProblemHttpResult BauakteNotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound,
        title: "Bauakte nicht gefunden", extensions: new Dictionary<string, object?> { ["code"] = "BAUAKTE_NOT_FOUND" });

    private static ProblemHttpResult Validation(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Ungültige Angaben", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
}
