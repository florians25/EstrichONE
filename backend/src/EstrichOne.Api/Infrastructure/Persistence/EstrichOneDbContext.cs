using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Infrastructure.Persistence;

/// <summary>
/// Eine Datenbank, ein Kontext. Jedes Modul liefert seine Tabellen über
/// <see cref="IEntityTypeConfiguration{TEntity}"/>-Klassen in seinem eigenen Ordner.
/// </summary>
public sealed class EstrichOneDbContext(DbContextOptions<EstrichOneDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EstrichOneDbContext).Assembly);
    }
}
