using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Documentation.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Documentation;

internal sealed class ArchivableDocuments(EstrichOneDbContext db, LocalFileStorage storage) : IArchivableDocuments
{
    public IQueryable<ArchivableDocument> Query() =>
        db.Set<FileRecord>().AsNoTracking()
            .Where(file => file.State == FileState.Stored && file.DeletedAt == null && file.StorageKey != null)
            .Select(file => new ArchivableDocument
            {
                FileId = file.Id,
                BauakteId = file.BauakteId,
                Kind = file.Kind == FileKind.Document ? "document" : file.Kind == FileKind.Photo ? "photo" : "video",
                Category = file.Category,
                OriginalName = file.OriginalName,
                ContentType = file.DetectedContentType ?? file.ContentType,
                SizeBytes = file.SizeBytes,
                Sha256 = file.Sha256,
                CreatedAt = file.CreatedAt,
                ApprovedAt = db.Set<FileApproval>()
                    .Where(approval => approval.FileId == file.Id && approval.Status == ApprovalStatus.Approved)
                    .Max(approval => approval.DecidedAt),
            });

    public async Task<Stream?> OpenAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var key = await db.Set<FileRecord>().AsNoTracking()
            .Where(file => file.Id == fileId && file.State == FileState.Stored && file.DeletedAt == null)
            .Select(file => file.StorageKey).FirstOrDefaultAsync(cancellationToken);
        var path = key is null ? null : storage.PathOf(key);
        return path is not null && File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
    }
}
