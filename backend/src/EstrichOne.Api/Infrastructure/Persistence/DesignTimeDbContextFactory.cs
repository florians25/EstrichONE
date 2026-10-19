using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EstrichOne.Api.Infrastructure.Persistence;

/// <summary>Nur für <c>dotnet ef migrations add</c>; verbindet sich dabei nicht mit einer Datenbank.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EstrichOneDbContext>
{
    public EstrichOneDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<EstrichOneDbContext>();
        PersistenceServiceCollectionExtensions.Configure(builder, "Host=localhost;Database=estrichone_design");
        return new EstrichOneDbContext(builder.Options);
    }
}
