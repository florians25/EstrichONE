using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Access;

/// <summary>
/// Rolle = frei konfigurierbares Bündel von Rechten. Verhalten hängt nie am Rollennamen, nur an den Rechten
/// (Lehre aus Estrich Web, siehe docs/reengineering/01-bestandsaufnahme.md, Abschnitt 7).
/// </summary>
public sealed class Role : IVersionedEntity
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }

    /// <summary>Bei der Ersteinrichtung angelegt; darf umbenannt, aber nicht gelöscht werden.</summary>
    public bool IsSystem { get; private set; }

    public List<RoleGrant> Permissions { get; private set; } = [];

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private Role()
    {
    }

    public static Role Create(Guid id, string name, string? description, bool isSystem, IEnumerable<string> permissions)
    {
        var role = new Role { Id = id, Name = name.Trim(), Description = description, IsSystem = isSystem };
        role.SetPermissions(permissions);
        return role;
    }

    /// <summary>Gleicht die Rechte ab (entfernt fehlende, ergänzt neue), statt alle neu anzulegen.</summary>
    public void SetPermissions(IEnumerable<string> permissions)
    {
        var wanted = permissions.ToHashSet(StringComparer.Ordinal);
        Permissions.RemoveAll(grant => !wanted.Contains(grant.Permission));
        wanted.ExceptWith(Permissions.Select(grant => grant.Permission));
        Permissions.AddRange(wanted.Order(StringComparer.Ordinal).Select(code => new RoleGrant(Id, code)));
    }

    public void Rename(string name, string? description)
    {
        Name = name.Trim();
        Description = description;
    }
}

public sealed class RoleGrant(Guid roleId, string permission)
{
    public Guid RoleId { get; private set; } = roleId;
    public string Permission { get; private set; } = permission;
}

public sealed class UserRole(Guid userId, Guid roleId)
{
    public Guid UserId { get; private set; } = userId;
    public Guid RoleId { get; private set; } = roleId;
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.ConfigureVersioned();
        builder.Property(role => role.Id).ValueGeneratedNever();
        builder.Property(role => role.Name).HasMaxLength(100);
        builder.Property(role => role.Description).HasMaxLength(500);
        builder.HasIndex(role => role.Name).IsUnique();
        builder.HasMany(role => role.Permissions).WithOne().HasForeignKey(permission => permission.RoleId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RoleGrantConfiguration : IEntityTypeConfiguration<RoleGrant>
{
    public void Configure(EntityTypeBuilder<RoleGrant> builder)
    {
        builder.ToTable("role_permissions");
        builder.HasKey(permission => new { permission.RoleId, permission.Permission });
        builder.Property(permission => permission.Permission).HasMaxLength(100);
    }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");
        builder.HasKey(userRole => new { userRole.UserId, userRole.RoleId });
        builder.HasOne<User>().WithMany().HasForeignKey(userRole => userRole.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Role>().WithMany().HasForeignKey(userRole => userRole.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(userRole => userRole.RoleId);
    }
}
