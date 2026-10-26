using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Documentation.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Documentation;

internal sealed class ArchivableDocuments(EstrichOneDbContext db, LocalFileStorage storage, System.Threading.Channels.Channel<Guid> variants)
    : IArchivableDocuments
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

    private Task<FileRecord?> RecordAsync(Guid fileId, CancellationToken cancellationToken) =>
        db.Set<FileRecord>().SingleOrDefaultAsync(file => file.Id == fileId && file.State == FileState.Stored, cancellationToken);

    public async Task<bool> HasContentAsync(Guid fileId, CancellationToken cancellationToken) =>
        await RecordAsync(fileId, cancellationToken) is { StorageKey: { } key } && File.Exists(storage.PathOf(key));

    public async Task EvictAsync(Guid fileId, CancellationToken cancellationToken)
    {
        if (await RecordAsync(fileId, cancellationToken) is not { StorageKey: { } key })
        {
            return;
        }

        foreach (var path in VariantWorker.Sizes.Select(size => storage.VariantPath(fileId, size.Name)).Prepend(storage.PathOf(key)))
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    public async Task<bool> RestoreAsync(Guid fileId, Stream content, CancellationToken cancellationToken)
    {
        if (await RecordAsync(fileId, cancellationToken) is not { StorageKey: { } key } file)
        {
            return false;
        }

        var path = storage.PathOf(key);
        var temp = path + ".restore";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string sha;
        await using (var target = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        using (var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            sha = Convert.ToHexStringLower(hash.GetHashAndReset());
        }

        if (sha != file.Sha256)
        {
            File.Delete(temp);
            return false;
        }

        File.Move(temp, path, overwrite: true);
        if (file.Variants != VariantState.NotApplicable)
        {
            file.SetVariants(VariantState.Pending);
            await db.SaveChangesAsync(cancellationToken);
            await variants.Writer.WriteAsync(fileId, cancellationToken);
        }

        return true;
    }
}
