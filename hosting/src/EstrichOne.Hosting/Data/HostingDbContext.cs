using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EstrichOne.Hosting.Data;

/// <summary>Eigene kleine SQLite-Datenbank der Konsole (Mandanten, Tarife, Vorgänge, Ereignisprotokoll).</summary>
public sealed class HostingDbContext(DbContextOptions<HostingDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<AdminUser> Admins => Set<AdminUser>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Operation> Operations => Set<Operation>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<Notice> Notices => Set<Notice>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite kennt keine Zeitzonen: Zeitpunkte werden als UTC gespeichert und als UTC zurückgegeben.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcConverter>();
        configurationBuilder.Properties<decimal>().HaveConversion<double>();
        configurationBuilder.Properties<decimal?>().HaveConversion<double?>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Plan>(plan =>
        {
            plan.Property(p => p.Name).HasMaxLength(80);
            plan.HasIndex(p => p.Name).IsUnique();
        });

        modelBuilder.Entity<Tenant>(tenant =>
        {
            tenant.Property(t => t.Slug).HasMaxLength(32);
            tenant.Property(t => t.Domain).HasMaxLength(253);
            tenant.Property(t => t.State).HasConversion<string>().HasMaxLength(20);
            tenant.Property(t => t.StateBeforeLock).HasConversion<string>().HasMaxLength(20);
            tenant.HasIndex(t => t.Slug).IsUnique();
            tenant.HasIndex(t => t.Domain).IsUnique();
            tenant.HasOne(t => t.Plan).WithMany().HasForeignKey(t => t.PlanId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AdminUser>(admin =>
        {
            admin.Property(a => a.UserName).HasMaxLength(32);
            admin.HasIndex(a => a.UserName).IsUnique();
        });

        modelBuilder.Entity<AuditEvent>(audit =>
        {
            audit.HasIndex(a => a.At);
            audit.HasIndex(a => a.TenantId);
        });

        modelBuilder.Entity<Operation>(operation =>
        {
            operation.Property(o => o.Kind).HasConversion<string>().HasMaxLength(20);
            operation.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
            operation.HasIndex(o => new { o.TenantId, o.QueuedAt });
            operation.HasIndex(o => o.Status);
        });

        modelBuilder.Entity<Setting>().HasKey(s => s.Key);

        modelBuilder.Entity<Notice>(notice =>
        {
            notice.Property(n => n.Kind).HasConversion<string>().HasMaxLength(20);
            notice.Property(n => n.Title).HasMaxLength(120);
            notice.Property(n => n.Severity).HasMaxLength(10);
            notice.HasIndex(n => n.EndsAt);
        });
    }

    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        value => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime(),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
