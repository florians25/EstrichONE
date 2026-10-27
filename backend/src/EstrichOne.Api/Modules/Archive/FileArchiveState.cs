using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Documentation.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Archive;

internal sealed class FileArchiveState(EstrichOneDbContext db) : IFileArchiveState
{
    public async Task<IReadOnlySet<Guid>> ArchivedOrQueuedAsync(IReadOnlyCollection<Guid> fileIds, CancellationToken cancellationToken) =>
        (await db.Set<ArchiveRecord>().AsNoTracking().Where(r => fileIds.Contains(r.FileId)).Select(r => r.FileId).Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();
}
