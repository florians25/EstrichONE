using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Platform;

/// <summary>Legt nach den Migrationen den Systemzustand an, falls er noch fehlt (erste Installation).</summary>
public sealed class SystemStateInitializer(EstrichOneDbContext db, TimeProvider clock) : IDataInitializer
{
    public int Order => 0;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (await db.Set<SystemState>().AnyAsync(cancellationToken))
        {
            return;
        }

        db.Set<SystemState>().Add(SystemState.Create(clock.GetUtcNow()));
        await db.SaveChangesAsync(cancellationToken);
    }
}
