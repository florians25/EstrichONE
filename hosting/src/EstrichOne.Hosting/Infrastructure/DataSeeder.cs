using EstrichOne.Hosting.Data;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Hosting.Infrastructure;

public static class DataSeeder
{
    /// <summary>Schema anlegen/aktualisieren und beim ersten Start drei Beispieltarife anlegen.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HostingDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        if (!await db.Plans.AnyAsync(cancellationToken))
        {
            db.Plans.AddRange(
                new Plan { Name = "Starter", Description = "Kleine Betriebe bis 5 Benutzer", MaxUsers = 5, StorageGb = 20, MemoryMb = 1024, CpuLimit = 1, MonthlyPrice = 49 },
                new Plan { Name = "Business", Description = "Mittlere Betriebe bis 20 Benutzer", MaxUsers = 20, StorageGb = 100, MemoryMb = 2048, CpuLimit = 2, MonthlyPrice = 129 },
                new Plan { Name = "Enterprise", Description = "Unbegrenzte Benutzer, großer Speicher", MaxUsers = 0, StorageGb = 500, MemoryMb = 4096, CpuLimit = 4, MonthlyPrice = 299 });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
