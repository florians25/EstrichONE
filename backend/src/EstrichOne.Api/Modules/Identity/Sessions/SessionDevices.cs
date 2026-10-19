using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Identity.Sessions;

internal sealed class SessionDevices(EstrichOneDbContext db, ICurrentUser currentUser, TimeProvider clock) : ISessionDevices
{
    public Task<Guid?> GetCurrentSessionDeviceAsync(CancellationToken cancellationToken) =>
        db.Set<Session>().Where(s => s.Id == currentUser.SessionId).Select(s => s.DeviceId).SingleAsync(cancellationToken);

    public async Task BindCurrentSessionAsync(Guid deviceId, CancellationToken cancellationToken)
    {
        var session = await db.Set<Session>().SingleAsync(s => s.Id == currentUser.SessionId, cancellationToken);
        session.BindDevice(deviceId);
    }

    public async Task RevokeDeviceSessionsAsync(Guid deviceId, string reason, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var session in await db.Set<Session>().Where(s => s.DeviceId == deviceId && s.RevokedAt == null).ToListAsync(cancellationToken))
        {
            session.Revoke(now, reason);
        }
    }
}
