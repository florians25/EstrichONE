using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Infrastructure.Persistence;

/// <summary>Datensatz mit Anlage- und Änderungszeitpunkt (setzt <see cref="AuditingInterceptor"/>).</summary>
public interface IAuditedEntity
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Synchronisierbarer Datensatz: <see cref="Version"/> steigt bei jeder Änderung um 1 und dient als
/// optimistische Sperre (<c>If-Match</c>, <c>baseVersion</c> im Sync-Protokoll).
/// </summary>
public interface IVersionedEntity : IAuditedEntity
{
    long Version { get; set; }
}

/// <summary>Gelöschte Datensätze bleiben als Tombstone erhalten, bis alle Geräte sie abgeglichen haben.</summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; }
}

public static class QueryFilters
{
    /// <summary>Name des Filters, der Tombstones ausblendet. Die Synchronisation schaltet ihn gezielt ab.</summary>
    public const string NotDeleted = "NotDeleted";
}

public static class EntityTypeBuilderExtensions
{
    /// <summary>Standardspalten für versionierte, löschbare Datensätze.</summary>
    public static EntityTypeBuilder<T> ConfigureVersioned<T>(this EntityTypeBuilder<T> builder)
        where T : class, IVersionedEntity
    {
        builder.Property(entity => entity.Version).IsConcurrencyToken();
        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
        {
            builder.HasQueryFilter(QueryFilters.NotDeleted, entity => ((ISoftDeletable)entity).DeletedAt == null);
        }

        return builder;
    }
}

/// <summary>Setzt Zeitstempel und erhöht Versionen zentral, damit kein Modul es vergessen kann.</summary>
public sealed class AuditingInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        foreach (var entry in context.ChangeTracker.Entries<IAuditedEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    if (entry.Entity is IVersionedEntity added)
                    {
                        added.Version = 1;
                    }

                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Property(entity => entity.CreatedAt).IsModified = false;
                    if (entry.Entity is IVersionedEntity modified)
                    {
                        // Der Originalwert bleibt in der WHERE-Bedingung (optimistische Sperre).
                        modified.Version = (long)entry.Property(nameof(IVersionedEntity.Version)).OriginalValue! + 1;
                    }

                    break;
            }
        }
    }
}
