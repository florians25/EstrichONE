using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Work.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Work;

internal sealed class WorkItemReferences(EstrichOneDbContext db) : IWorkItemReferences
{
    public Task<bool> BelongsToAsync(Guid bauakteId, Guid workItemId, CancellationToken cancellationToken) =>
        db.Set<WorkItem>().AnyAsync(item => item.Id == workItemId && item.BauakteId == bauakteId, cancellationToken);
}
