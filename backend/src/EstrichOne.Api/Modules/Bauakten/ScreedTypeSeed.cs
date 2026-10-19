using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>Grundliste gängiger Estricharten (DIN EN 13813) für eine frische Installation; danach frei änderbar.</summary>
internal sealed class ScreedTypeSeed(EstrichOneDbContext db) : IDataInitializer
{
    public int Order => 20;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (await db.Set<ScreedType>().AnyAsync(cancellationToken))
        {
            return;
        }

        db.AddRange(
            ScreedType.Create(Guid.CreateVersion7(), "Zementestrich", "CT-C25-F4", ScreedBinder.Cement),
            ScreedType.Create(Guid.CreateVersion7(), "Zementestrich (Heizestrich)", "CT-C30-F5", ScreedBinder.Cement),
            ScreedType.Create(Guid.CreateVersion7(), "Calciumsulfat-Fließestrich", "CA-C25-F5", ScreedBinder.CalciumSulfate),
            ScreedType.Create(Guid.CreateVersion7(), "Calciumsulfatestrich", "CA-C20-F4", ScreedBinder.CalciumSulfate),
            ScreedType.Create(Guid.CreateVersion7(), "Gussasphaltestrich", "AS-IC10", ScreedBinder.MasticAsphalt));
        await db.SaveChangesAsync(cancellationToken);
    }
}
