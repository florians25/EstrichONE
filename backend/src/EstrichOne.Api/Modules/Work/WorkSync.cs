using System.Text.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Sync.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Work;

public sealed record WorkItemDto(
    Guid Id, Guid? BauakteId, WorkItemKind Kind, WorkItemStatus Status, string Title, string? Description, DateOnly? DueDate,
    Guid? AssigneeUserId, Guid CreatedByUserId, DateTimeOffset? CompletedAt, DateTimeOffset UpdatedAt, long Version)
{
    public static WorkItemDto From(WorkItem item) => new(item.Id, item.BauakteId, item.Kind, item.Status, item.Title,
        item.Description, item.DueDate, item.AssigneeUserId, item.CreatedByUserId, item.CompletedAt, item.UpdatedAt, item.Version);
}

/// <summary>Aufgaben und Mängel: Anlage idempotent, Änderungen feldweise zusammengeführt.</summary>
internal sealed class WorkSync(EstrichOneDbContext db, IBauakteAccess access, IUserPermissions permissions, ICurrentUser currentUser,
    TimeProvider clock, IBauakteHistory history) : ISyncOperationHandler
{
    private static readonly HashSet<string> EditableFields = ["title", "description", "dueDate", "assigneeUserId", "status"];

    public IReadOnlyCollection<string> OperationTypes { get; } = ["workitem.create", "workitem.update"];

    public async Task<SyncOperationResult> ApplyAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(Permissions.WorkEdit, cancellationToken))
        {
            return SyncOperationResult.Rejected(op.OpId, "PERMISSION_DENIED", "Für diese Aktion fehlt die Berechtigung.");
        }

        return op.Type == "workitem.create" ? await CreateAsync(op, cancellationToken) : await UpdateAsync(op, cancellationToken);
    }

    private async Task<SyncOperationResult> CreateAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        var bauakteId = SyncPayload.Read<Guid?>(op.Payload, "bauakteId");
        if (bauakteId is { } id && !await access.CanReadAsync(id, cancellationToken))
        {
            return SyncOperationResult.Rejected(op.OpId, "BAUAKTE_NOT_FOUND", "Die Bauakte existiert nicht (mehr) oder ist nicht sichtbar.");
        }

        var existing = await db.Set<WorkItem>().IgnoreQueryFilters([QueryFilters.NotDeleted]).SingleOrDefaultAsync(w => w.Id == op.EntityId, cancellationToken);
        if (existing is not null)
        {
            return existing.CreatedByUserId == currentUser.UserId
                ? new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: existing.Version, Entity: SyncPayload.ToNode(WorkItemDto.From(existing)))
                : SyncOperationResult.Rejected(op.OpId, "ID_CONFLICT", "Unter dieser ID existiert bereits eine andere Aufgabe.");
        }

        WorkItem item;
        try
        {
            item = WorkItem.Create(op.EntityId, SyncPayload.Read<WorkItemKind>(op.Payload, "kind"),
                SyncPayload.Read<string>(op.Payload, "title") ?? string.Empty, currentUser.UserId, bauakteId);
        }
        catch (ArgumentException exception)
        {
            throw new SyncValidationException(exception.Message);
        }

        var assignee = SyncPayload.Read<Guid?>(op.Payload, "assigneeUserId");
        await EnsureUserExistsAsync(assignee, cancellationToken);
        item.Update(item.Title, SyncPayload.Read<string>(op.Payload, "description"), SyncPayload.Read<DateOnly?>(op.Payload, "dueDate"), assignee);
        db.Add(item);
        if (bauakteId is { } owner)
        {
            history.Record(owner, BauakteHistoryActions.WorkItemCreated, new Dictionary<string, string?> { ["kind"] = item.Kind.ToString() });
        }
        await db.SaveChangesAsync(cancellationToken);
        return new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: item.Version, Entity: SyncPayload.ToNode(WorkItemDto.From(item)));
    }

    private async Task<SyncOperationResult> UpdateAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        var item = await db.Set<WorkItem>().SingleOrDefaultAsync(w => w.Id == op.EntityId, cancellationToken);
        if (item is null || (item.BauakteId is { } bauakteId && !await access.CanReadAsync(bauakteId, cancellationToken))
            || (item.BauakteId is null && item.CreatedByUserId != currentUser.UserId && item.AssigneeUserId != currentUser.UserId))
        {
            return SyncOperationResult.Rejected(op.OpId, "NOT_FOUND", "Die Aufgabe existiert nicht (mehr) oder ist nicht sichtbar.");
        }

        var current = (JsonObject)SyncPayload.ToNode(WorkItemDto.From(item))!;
        var (accepted, conflicts) = SyncPayload.Merge(op.Payload, current.ToDictionary(pair => pair.Key, pair => pair.Value), EditableFields);
        var changes = (JsonObject)op.Payload["changes"]!;

        var title = item.Title;
        var description = item.Description;
        var dueDate = item.DueDate;
        var assignee = item.AssigneeUserId;
        foreach (var field in accepted)
        {
            var value = changes[field];
            switch (field)
            {
                case "title": title = value?.GetValue<string>() ?? string.Empty; break;
                case "description": description = value?.GetValue<string>(); break;
                case "dueDate": dueDate = value.Deserialize<DateOnly?>(SyncPayload.Json); break;
                case "assigneeUserId":
                    assignee = value.Deserialize<Guid?>(SyncPayload.Json);
                    await EnsureUserExistsAsync(assignee, cancellationToken);
                    break;
                case "status":
                    var wasDone = item.Status == WorkItemStatus.Done;
                    item.ChangeStatus(value.Deserialize<WorkItemStatus>(SyncPayload.Json), clock.GetUtcNow());
                    if (!wasDone && item.Status == WorkItemStatus.Done && item.BauakteId is { } owner)
                    {
                        history.Record(owner, BauakteHistoryActions.WorkItemDone, new Dictionary<string, string?> { ["kind"] = item.Kind.ToString() });
                    }

                    break;
            }
        }

        try
        {
            item.Update(title, description, dueDate, assignee);
        }
        catch (ArgumentException exception)
        {
            throw new SyncValidationException(exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SyncOperationResult(op.OpId, conflicts.Count == 0 ? SyncOperationStatus.Applied : SyncOperationStatus.Conflict,
            Code: conflicts.Count == 0 ? null : "FIELD_CONFLICT", Version: item.Version,
            Conflicts: conflicts.Count == 0 ? null : conflicts, Entity: SyncPayload.ToNode(WorkItemDto.From(item)));
    }

    private async Task EnsureUserExistsAsync(Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is { } id && !await db.Set<User>().AnyAsync(u => u.Id == id, cancellationToken))
        {
            throw new SyncValidationException("Der zugewiesene Benutzer existiert nicht.");
        }
    }
}

internal sealed class WorkItemSource(EstrichOneDbContext db) : ISyncEntitySource
{
    public string EntityType => "workItem";

    public string? RequiredPermission => Permissions.WorkRead;

    public async Task<IReadOnlyDictionary<Guid, JsonNode>> LoadAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Set<WorkItem>().AsNoTracking().Where(w => ids.Contains(w.Id)).ToListAsync(cancellationToken))
            .ToDictionary(w => w.Id, w => SyncPayload.ToNode(WorkItemDto.From(w))!);
}
