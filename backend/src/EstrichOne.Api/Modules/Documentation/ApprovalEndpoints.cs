using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Mail.Exports;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Documentation;

public sealed record RequestApprovalRequest(
    [property: Required] Guid Id,
    Guid? AssigneeUserId,
    Guid? AssigneeRoleId,
    [property: MaxLength(FileApproval.MaxMessageLength)] string? Message = null,
    DateOnly? DueDate = null);

public sealed record DecideApprovalRequest([property: MaxLength(FileApproval.MaxMessageLength)] string? Comment = null);

public sealed record ApprovalResponse(
    Guid Id, Guid FileId, string FileName, Guid BauakteId, string? BauakteNumber, string BauakteTitle,
    ApprovalStatus Status, Guid RequestedByUserId, string RequestedByName, DateTimeOffset RequestedAt,
    Guid? AssigneeUserId, Guid? AssigneeRoleId, string AssigneeName, string? Message, DateOnly? DueDate,
    Guid? DecidedByUserId, string? DecidedByName, DateTimeOffset? DecidedAt, string? DecisionComment, bool CanDecide, long Version);

/// <summary>
/// Dokumentfreigaben: anfragen (bei Person oder Rolle), genehmigen oder ablehnen mit Kommentar, zurückziehen,
/// Verlauf je Dokument und Liste „Warten auf Genehmigung“ für das Dashboard.
/// Entscheiden darf, wer angefragt ist (Person oder Mitglied der Rolle) oder das Recht documents.approve hat.
/// </summary>
internal static class ApprovalEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var files = api.MapGroup("/files").WithTags("Freigaben");
        files.MapGet("/{fileId:guid}/approvals", HistoryAsync).WithName("ListFileApprovals")
            .WithSummary("Freigabeverlauf eines Dokuments (neueste zuerst)")
            .RequirePermission(Permissions.BauaktenRead);
        files.MapPost("/{fileId:guid}/approvals", RequestAsync).WithName("RequestFileApproval")
            .WithSummary("Freigabe für ein Dokument anfragen (bei einer Person oder Rolle)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.FilesUpload);

        var approvals = api.MapGroup("/approvals").WithTags("Freigaben");
        approvals.MapGet("/", ListAsync).WithName("ListApprovals")
            .WithSummary("Freigaben: scope=mine (an mich oder meine Rollen), requested (von mir angefragt), all; status=pending|done|all")
            .RequirePermission(Permissions.BauaktenRead);
        approvals.MapPost("/{id:guid}/approve", (Guid id, DecideApprovalRequest request, ApprovalService service, CancellationToken ct) =>
                service.DecideAsync(id, true, request.Comment, ct))
            .WithName("ApproveFile").ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.BauaktenRead);
        approvals.MapPost("/{id:guid}/reject", (Guid id, DecideApprovalRequest request, ApprovalService service, CancellationToken ct) =>
                service.DecideAsync(id, false, request.Comment, ct))
            .WithName("RejectFile").ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.BauaktenRead);
        approvals.MapPost("/{id:guid}/cancel", (Guid id, ApprovalService service, CancellationToken ct) => service.CancelAsync(id, ct))
            .WithName("CancelFileApproval").ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.BauaktenRead);
    }

    private static async Task<Results<Ok<List<ApprovalResponse>>, ProblemHttpResult>> HistoryAsync(
        Guid fileId, EstrichOneDbContext db, IBauakteAccess access, ApprovalService service, CancellationToken cancellationToken)
    {
        var file = await db.Set<FileRecord>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file?.BauakteId is not { } bauakteId || !await access.CanReadAsync(bauakteId, cancellationToken))
        {
            return NotFound();
        }

        var list = await db.Set<FileApproval>().AsNoTracking().Where(a => a.FileId == fileId).OrderByDescending(a => a.RequestedAt).ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(await service.ToResponsesAsync(list, cancellationToken));
    }

    private static async Task<Results<Created<ApprovalResponse>, Ok<ApprovalResponse>, ProblemHttpResult>> RequestAsync(
        Guid fileId, RequestApprovalRequest request, EstrichOneDbContext db, IBauakteAccess access, ICurrentUser currentUser,
        IAuditLog audit, TimeProvider clock, ApprovalService service, IRoleAssignments roles, CancellationToken cancellationToken)
    {
        var file = await db.Set<FileRecord>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file?.BauakteId is not { } bauakteId || !await access.CanReadAsync(bauakteId, cancellationToken))
        {
            return NotFound();
        }

        if (await db.Set<FileApproval>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == request.Id, cancellationToken) is { } existing)
        {
            return existing.FileId == fileId
                ? TypedResults.Ok((await service.ToResponsesAsync([existing], cancellationToken))[0])
                : Problem(StatusCodes.Status409Conflict, "ID bereits vergeben", "ID_CONFLICT");
        }

        if (file.Kind != FileKind.Document)
        {
            return Problem(StatusCodes.Status409Conflict, "Freigaben gibt es nur für Dokumente.", "NOT_A_DOCUMENT");
        }

        if (request.AssigneeUserId is { } userId && !await db.Set<User>().AnyAsync(u => u.Id == userId && u.IsActive, cancellationToken))
        {
            return Problem(StatusCodes.Status409Conflict, "Die gewählte Person ist nicht (mehr) aktiv.", "ASSIGNEE_UNKNOWN");
        }

        if (request.AssigneeRoleId is { } roleId && !(await roles.GetRoleNamesAsync(cancellationToken)).ContainsKey(roleId))
        {
            return Problem(StatusCodes.Status409Conflict, "Die gewählte Rolle gibt es nicht.", "ASSIGNEE_UNKNOWN");
        }

        if (await db.Set<FileApproval>().AnyAsync(a => a.FileId == fileId && a.Status == ApprovalStatus.Pending, cancellationToken))
        {
            return Problem(StatusCodes.Status409Conflict, "Für dieses Dokument läuft bereits eine Freigabe.", "APPROVAL_PENDING");
        }

        FileApproval approval;
        try
        {
            approval = FileApproval.Request(request.Id, fileId, bauakteId, currentUser.UserId, clock.GetUtcNow(), request.AssigneeUserId,
                request.AssigneeRoleId, request.Message, request.DueDate);
        }
        catch (ArgumentException exception)
        {
            return Problem(StatusCodes.Status400BadRequest, exception.Message, "VALIDATION");
        }

        db.Add(approval);
        audit.Record("documents.approval_requested", currentUser.UserId, "file", fileId.ToString(), new Dictionary<string, string?>
        {
            ["bauakteId"] = bauakteId.ToString(), ["name"] = file.OriginalName,
            ["assignee"] = (request.AssigneeUserId ?? request.AssigneeRoleId)?.ToString(),
        });
        await db.SaveChangesAsync(cancellationToken);
        await service.NotifyRequestedAsync(approval, file.OriginalName, cancellationToken);
        return TypedResults.Created($"/api/v1/files/{fileId}/approvals", (await service.ToResponsesAsync([approval], cancellationToken))[0]);
    }

    private static async Task<Ok<List<ApprovalResponse>>> ListAsync(EstrichOneDbContext db, IBauakteAccess access, ICurrentUser currentUser,
        IRoleAssignments roles, ApprovalService service, CancellationToken cancellationToken,
        [FromQuery] string? scope = null, [FromQuery] string? status = null, [FromQuery] int? limit = null)
    {
        var visible = (await access.VisibleAsync(db.Set<Bauakte>().AsNoTracking(), cancellationToken)).Select(b => b.Id);
        var query = db.Set<FileApproval>().AsNoTracking().Where(a => visible.Contains(a.BauakteId));
        var userId = currentUser.UserId;
        switch (scope?.ToLowerInvariant())
        {
            case "requested":
                query = query.Where(a => a.RequestedByUserId == userId);
                break;
            case "all":
                break;
            default:
                var roleIds = await roles.GetRoleIdsAsync(userId, cancellationToken);
                query = query.Where(a => a.AssigneeUserId == userId || (a.AssigneeRoleId != null && roleIds.Contains(a.AssigneeRoleId.Value)));
                break;
        }

        query = (status?.ToLowerInvariant()) switch
        {
            "done" => query.Where(a => a.Status != ApprovalStatus.Pending),
            "all" => query,
            _ => query.Where(a => a.Status == ApprovalStatus.Pending),
        };
        var list = await query.OrderByDescending(a => a.RequestedAt).ThenByDescending(a => a.Id).Take(Math.Clamp(limit ?? 100, 1, 500)).ToListAsync(cancellationToken);
        return TypedResults.Ok(await service.ToResponsesAsync(list, cancellationToken));
    }

    internal static ProblemHttpResult NotFound() => Problem(StatusCodes.Status404NotFound, "Dokument nicht gefunden", "FILE_NOT_FOUND");

    internal static ProblemHttpResult Problem(int status, string title, string code) =>
        TypedResults.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}

/// <summary>Entscheiden, zurückziehen und Antworten mit Namen aufbereiten.</summary>
internal sealed class ApprovalService(EstrichOneDbContext db, ICurrentUser currentUser, IUserPermissions permissions, IRoleAssignments roles,
    IBauakteAccess access, IAuditLog audit, TimeProvider clock, IMailService mail, IBauakteReportSource bauakten,
    Organization.Exports.ISystemSettings settings, ILogger<ApprovalService> logger)
{
    /// <summary>E-Mail an die Person bzw. alle Mitglieder der Rolle (wenn eingerichtet und eingeschaltet).</summary>
    public async Task NotifyRequestedAsync(FileApproval approval, string fileName, CancellationToken cancellationToken)
    {
        var recipients = approval.AssigneeUserId is { } user ? [user] : approval.AssigneeRoleId is { } role ? await roles.GetUserIdsInRoleAsync(role, cancellationToken) : [];
        var info = await bauakten.GetAsync(approval.BauakteId, cancellationToken);
        await SendAsync(MailTemplateKeys.ApprovalRequested, recipients.Where(r => r != currentUser.UserId), approval, new Dictionary<string, string?>
        {
            ["dokument"] = fileName, ["bauakte"] = info is null ? null : $"{info.Number} {info.Title}".Trim(), ["nachricht"] = approval.Message,
        }, cancellationToken);
    }

    private async Task NotifyDecidedAsync(FileApproval approval, CancellationToken cancellationToken)
    {
        var file = await db.Set<FileRecord>().AsNoTracking().Where(f => f.Id == approval.FileId).Select(f => f.OriginalName).SingleOrDefaultAsync(cancellationToken);
        var info = await bauakten.GetAsync(approval.BauakteId, cancellationToken);
        await SendAsync(MailTemplateKeys.ApprovalDecided, [approval.RequestedByUserId], approval, new Dictionary<string, string?>
        {
            ["dokument"] = file, ["bauakte"] = info is null ? null : $"{info.Number} {info.Title}".Trim(),
            ["entscheidung"] = approval.Status == ApprovalStatus.Approved ? "freigegeben" : "abgelehnt", ["kommentar"] = approval.DecisionComment,
        }, cancellationToken);
    }

    private async Task SendAsync(string template, IEnumerable<Guid> userIds, FileApproval approval, Dictionary<string, string?> values,
        CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToList();
        var people = await db.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id) && u.IsActive && u.Email != null)
            .Select(u => new { u.DisplayName, u.Email }).ToListAsync(cancellationToken);
        foreach (var person in people)
        {
            try
            {
                await mail.QueueTemplateAsync(template, person.Email!, new Dictionary<string, string?>(values) { ["name"] = person.DisplayName },
                    $"bauakte:{approval.BauakteId}", null, cancellationToken);
            }
            catch (ArgumentException exception)
            {
                logger.LogWarning(exception, "Freigabe-Benachrichtigung an {Name} nicht möglich.", person.DisplayName);
            }
        }
    }

    public async Task<Results<Ok<ApprovalResponse>, ProblemHttpResult>> DecideAsync(Guid id, bool approve, string? comment, CancellationToken cancellationToken)
    {
        var approval = await db.Set<FileApproval>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (approval is null || !await access.CanReadAsync(approval.BauakteId, cancellationToken))
        {
            return ApprovalEndpoints.Problem(StatusCodes.Status404NotFound, "Freigabe nicht gefunden", "APPROVAL_NOT_FOUND");
        }

        if (!await CanDecideAsync(approval, cancellationToken))
        {
            return ApprovalEndpoints.Problem(StatusCodes.Status403Forbidden, "Über diese Freigabe entscheidet jemand anderes.", "NOT_ASSIGNEE");
        }

        if (approve && string.IsNullOrWhiteSpace(comment) && (await settings.GetAsync(cancellationToken)).Approvals.RequireCommentOnApprove)
        {
            return ApprovalEndpoints.Problem(StatusCodes.Status400BadRequest, "Bitte einen Kommentar zur Freigabe angeben (Einstellungen).", "COMMENT_REQUIRED");
        }

        try
        {
            approval.Decide(approve, currentUser.UserId, clock.GetUtcNow(), comment);
        }
        catch (InvalidOperationException exception)
        {
            return ApprovalEndpoints.Problem(StatusCodes.Status409Conflict, exception.Message, "APPROVAL_DECIDED");
        }
        catch (ArgumentException exception)
        {
            return ApprovalEndpoints.Problem(StatusCodes.Status400BadRequest, exception.Message, "COMMENT_REQUIRED");
        }

        audit.Record(approve ? "documents.approved" : "documents.rejected", currentUser.UserId, "file", approval.FileId.ToString(),
            new Dictionary<string, string?> { ["bauakteId"] = approval.BauakteId.ToString(), ["comment"] = approval.DecisionComment });
        await db.SaveChangesAsync(cancellationToken);
        await NotifyDecidedAsync(approval, cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync([approval], cancellationToken))[0]);
    }

    public async Task<Results<Ok<ApprovalResponse>, ProblemHttpResult>> CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        var approval = await db.Set<FileApproval>().SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (approval is null || !await access.CanReadAsync(approval.BauakteId, cancellationToken))
        {
            return ApprovalEndpoints.Problem(StatusCodes.Status404NotFound, "Freigabe nicht gefunden", "APPROVAL_NOT_FOUND");
        }

        if (approval.RequestedByUserId != currentUser.UserId && !await permissions.HasAsync(Permissions.DocumentsApprove, cancellationToken))
        {
            return ApprovalEndpoints.Problem(StatusCodes.Status403Forbidden, "Nur wer angefragt hat, kann die Freigabe zurückziehen.", "NOT_REQUESTER");
        }

        try
        {
            approval.Cancel(currentUser.UserId, clock.GetUtcNow());
        }
        catch (InvalidOperationException exception)
        {
            return ApprovalEndpoints.Problem(StatusCodes.Status409Conflict, exception.Message, "APPROVAL_DECIDED");
        }

        audit.Record("documents.approval_cancelled", currentUser.UserId, "file", approval.FileId.ToString(),
            new Dictionary<string, string?> { ["bauakteId"] = approval.BauakteId.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok((await ToResponsesAsync([approval], cancellationToken))[0]);
    }

    private async Task<bool> CanDecideAsync(FileApproval approval, CancellationToken cancellationToken)
    {
        if (approval.AssigneeUserId == currentUser.UserId || await permissions.HasAsync(Permissions.DocumentsApprove, cancellationToken))
        {
            return true;
        }

        return approval.AssigneeRoleId is { } roleId && (await roles.GetRoleIdsAsync(currentUser.UserId, cancellationToken)).Contains(roleId);
    }

    public async Task<List<ApprovalResponse>> ToResponsesAsync(IReadOnlyList<FileApproval> list, CancellationToken cancellationToken)
    {
        if (list.Count == 0)
        {
            return [];
        }

        var fileIds = list.Select(a => a.FileId).Distinct().ToList();
        var files = await db.Set<FileRecord>().AsNoTracking().IgnoreQueryFilters().Where(f => fileIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, f => f.OriginalName, cancellationToken);
        var bauakteIds = list.Select(a => a.BauakteId).Distinct().ToList();
        var akten = await db.Set<Bauakte>().AsNoTracking().IgnoreQueryFilters().Where(b => bauakteIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => new { b.Number, b.Title }, cancellationToken);
        var userIds = list.SelectMany(a => new[] { a.RequestedByUserId, a.AssigneeUserId ?? Guid.Empty, a.DecidedByUserId ?? Guid.Empty }).Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var roleNames = await roles.GetRoleNamesAsync(cancellationToken);
        var myRoles = await roles.GetRoleIdsAsync(currentUser.UserId, cancellationToken);
        var approveAll = await permissions.HasAsync(Permissions.DocumentsApprove, cancellationToken);
        string Name(Guid? id) => id is { } value && users.TryGetValue(value, out var name) ? name : "?";

        return list.Select(a => new ApprovalResponse(
            a.Id, a.FileId, files.GetValueOrDefault(a.FileId) ?? "?", a.BauakteId, akten.GetValueOrDefault(a.BauakteId)?.Number,
            akten.GetValueOrDefault(a.BauakteId)?.Title ?? "?", a.Status, a.RequestedByUserId, Name(a.RequestedByUserId), a.RequestedAt,
            a.AssigneeUserId, a.AssigneeRoleId,
            a.AssigneeUserId is not null ? Name(a.AssigneeUserId) : $"Rolle {roleNames.GetValueOrDefault(a.AssigneeRoleId!.Value) ?? "?"}",
            a.Message, a.DueDate, a.DecidedByUserId, a.DecidedByUserId is null ? null : Name(a.DecidedByUserId), a.DecidedAt, a.DecisionComment,
            a.Status == ApprovalStatus.Pending && (approveAll || a.AssigneeUserId == currentUser.UserId
                || (a.AssigneeRoleId is { } r && myRoles.Contains(r))),
            a.Version)).ToList();
    }
}
