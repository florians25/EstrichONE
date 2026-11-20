using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Documentation.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Documentation;

internal sealed class StoredFiles(EstrichOneDbContext db) : IStoredFiles
{
    public async Task<IReadOnlyDictionary<Guid, StoredFileInfo>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Set<FileRecord>().AsNoTracking().Where(f => ids.Contains(f.Id))
            .Select(f => new StoredFileInfo(f.Id, f.OriginalName, f.ContentType, f.SizeBytes, f.UploadedByUserId, f.BauakteId, f.State == FileState.Stored))
            .ToDictionaryAsync(f => f.Id, cancellationToken);

    public async Task<bool> ClassifyOwnAsync(Guid fileId, Guid ownerUserId, string category, CancellationToken cancellationToken)
    {
        var file = await db.Set<FileRecord>().SingleOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is null || file.BauakteId is not null || file.UploadedByUserId != ownerUserId)
        {
            return false;
        }

        file.Categorize(category);
        return true;
    }
}
