using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Documentation;

public sealed record DocumentListItem(
    FileResponse File, string? BauakteNumber, string BauakteTitle, string UploadedByName, int VersionCount,
    ApprovalStatus? ApprovalStatus, string? ApprovalAssignee);

public sealed record DocumentFacets(int Total, IReadOnlyDictionary<string, int> Categories, int PendingApprovals);

public sealed record DocumentSearchResult(IReadOnlyList<DocumentListItem> Items, DocumentFacets Facets);

/// <summary>
/// Zentrale Dokumentverwaltung über alle sichtbaren Bauakten: jeweils die aktuelle Fassung eines Dokuments mit
/// Bauakte, Ablage (Dokumentart/Unterordner), Anzahl Fassungen und Freigabestand – durchsuch- und filterbar.
/// </summary>
internal static class DocumentEndpoints
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/documents", SearchAsync).WithTags("Dokumente").WithName("SearchDocuments")
            .WithSummary("Dokumente aller sichtbaren Bauakten (aktuelle Fassungen); Filter: search, category, bauakteId, approval (pending|approved|rejected|none), sort (newest|name|bauakte)")
            .RequirePermission(Permissions.BauaktenRead);

    private static async Task<Ok<DocumentSearchResult>> SearchAsync(EstrichOneDbContext db, IBauakteAccess access, IRoleAssignments roles, CancellationToken cancellationToken,
        [FromQuery] string? search = null, [FromQuery] string? category = null, [FromQuery] Guid? bauakteId = null,
        [FromQuery] string? approval = null, [FromQuery] string? sort = null, [FromQuery] int? limit = null)
    {
        var visible = (await access.VisibleAsync(db.Set<Bauakte>().AsNoTracking(), cancellationToken)).Select(b => b.Id);
        var files = db.Set<FileRecord>().AsNoTracking()
            .Where(f => f.Kind == FileKind.Document && f.State == FileState.Stored && f.BauakteId != null && visible.Contains(f.BauakteId.Value));
        // Nur die aktuelle Fassung: keine andere Datei verweist auf diese als Vorgänger.
        var current = files.Where(f => !db.Set<FileRecord>().Any(n => n.PreviousVersionId == f.Id));
        if (bauakteId is { } id)
        {
            current = current.Where(f => f.BauakteId == id);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal) + "%";
            var matchingAkten = db.Set<Bauakte>().Where(b => EF.Functions.ILike(b.Title, pattern, "\\") || (b.Number != null && EF.Functions.ILike(b.Number, pattern, "\\")))
                .Select(b => b.Id);
            current = current.Where(f => EF.Functions.ILike(f.OriginalName, pattern, "\\")
                || (f.Caption != null && EF.Functions.ILike(f.Caption, pattern, "\\"))
                || (f.Folder != null && EF.Functions.ILike(f.Folder, pattern, "\\"))
                || matchingAkten.Contains(f.BauakteId!.Value));
        }

        var categoryCounts = await current.GroupBy(f => f.Category ?? "other").Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var pending = await db.Set<FileApproval>().CountAsync(a => a.Status == ApprovalStatus.Pending && current.Any(f => f.Id == a.FileId), cancellationToken);
        var total = categoryCounts.Values.Sum();

        if (!string.IsNullOrWhiteSpace(category))
        {
            current = category == "other" ? current.Where(f => f.Category == null || f.Category == "other") : current.Where(f => f.Category == category);
        }

        var latestApproval = db.Set<FileApproval>();
        current = approval?.ToLowerInvariant() switch
        {
            "pending" => current.Where(f => latestApproval.Any(a => a.FileId == f.Id && a.Status == ApprovalStatus.Pending)),
            "approved" => current.Where(f => latestApproval.Where(a => a.FileId == f.Id).OrderByDescending(a => a.RequestedAt).ThenByDescending(a => a.Id).Select(a => (ApprovalStatus?)a.Status).FirstOrDefault() == ApprovalStatus.Approved),
            "rejected" => current.Where(f => latestApproval.Where(a => a.FileId == f.Id).OrderByDescending(a => a.RequestedAt).ThenByDescending(a => a.Id).Select(a => (ApprovalStatus?)a.Status).FirstOrDefault() == ApprovalStatus.Rejected),
            "none" => current.Where(f => !latestApproval.Any(a => a.FileId == f.Id)),
            _ => current,
        };

        current = sort?.ToLowerInvariant() switch
        {
            "name" => current.OrderBy(f => f.OriginalName),
            "bauakte" => current.OrderBy(f => f.BauakteId).ThenByDescending(f => f.CreatedAt),
            _ => current.OrderByDescending(f => f.CreatedAt),
        };
        var page = await current.Take(Math.Clamp(limit ?? 200, 1, 1000)).ToListAsync(cancellationToken);

        var ids = page.Select(f => f.Id).ToList();
        var akteIds = page.Select(f => f.BauakteId!.Value).Distinct().ToList();
        var akten = await db.Set<Bauakte>().AsNoTracking().Where(b => akteIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => new { b.Number, b.Title }, cancellationToken);
        var userIds = page.Select(f => f.UploadedByUserId).Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var approvals = (await db.Set<FileApproval>().AsNoTracking().Where(a => ids.Contains(a.FileId)).ToListAsync(cancellationToken))
            .GroupBy(a => a.FileId).ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.RequestedAt).ThenByDescending(a => a.Id).First());
        var roleNames = approvals.Count > 0 ? await roles.GetRoleNamesAsync(cancellationToken) : new Dictionary<Guid, string>();
        var assigneeUsers = approvals.Values.Where(a => a.AssigneeUserId != null).Select(a => a.AssigneeUserId!.Value).Distinct().ToList();
        var assigneeNames = await db.Set<User>().AsNoTracking().Where(u => assigneeUsers.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        // Fassungen je Kette zählen (Vorgänger rückwärts verfolgen).
        var all = await db.Set<FileRecord>().AsNoTracking().Where(f => akteIds.Contains(f.BauakteId!.Value) && f.Kind == FileKind.Document)
            .Select(f => new { f.Id, f.PreviousVersionId }).ToListAsync(cancellationToken);
        var previous = all.ToDictionary(f => f.Id, f => f.PreviousVersionId);
        int Versions(Guid fileId)
        {
            var count = 1;
            var cursor = previous.GetValueOrDefault(fileId);
            while (cursor is { } prev && count < 1000)
            {
                count++;
                cursor = previous.GetValueOrDefault(prev);
            }

            return count;
        }

        var items = page.Select(f =>
        {
            var a = approvals.GetValueOrDefault(f.Id);
            var assignee = a is null ? null : a.AssigneeUserId is { } u ? assigneeNames.GetValueOrDefault(u) : $"Rolle {roleNames.GetValueOrDefault(a.AssigneeRoleId!.Value)}";
            var akte = akten.GetValueOrDefault(f.BauakteId!.Value);
            return new DocumentListItem(FileEndpoints.ToResponse(f), akte?.Number, akte?.Title ?? "?", users.GetValueOrDefault(f.UploadedByUserId) ?? "?",
                Versions(f.Id), a?.Status, assignee);
        }).ToList();
        return TypedResults.Ok(new DocumentSearchResult(items, new DocumentFacets(total, categoryCounts, pending)));
    }

}
