using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Identity.Sessions;

internal sealed class SessionTermination(EstrichOneDbContext db, TimeProvider clock) : ISessionTermination
{
    public Task<int> RevokeAllAsync(string reason, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        return db.Set<Session>().Where(s => s.RevokedAt == null && s.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now).SetProperty(x => x.RevokeReason, reason), cancellationToken);
    }
}
