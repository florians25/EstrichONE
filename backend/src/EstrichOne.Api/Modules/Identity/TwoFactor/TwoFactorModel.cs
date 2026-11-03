using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Identity.TwoFactor;

/// <summary>
/// TOTP-Geheimnis eines Benutzers, mit ASP.NET Data Protection (AES-256-GCM) verschlüsselt. Solange
/// <see cref="EnabledAt"/> leer ist, läuft die Einrichtung noch (Code aus der App noch nicht bestätigt).
/// </summary>
public sealed class UserTwoFactor
{
    public Guid UserId { get; private set; }
    public string ProtectedSecret { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? EnabledAt { get; private set; }

    /// <summary>Zuletzt eingelöster Zeitschritt – derselbe Code gilt nur einmal (Schutz vor Wiederverwendung).</summary>
    public long LastUsedStep { get; private set; }

    private UserTwoFactor()
    {
    }

    public UserTwoFactor(Guid userId, string protectedSecret, DateTimeOffset now)
    {
        UserId = userId;
        ProtectedSecret = protectedSecret;
        CreatedAt = now;
    }

    public bool IsEnabled => EnabledAt is not null;

    public void Restart(string protectedSecret, DateTimeOffset now)
    {
        ProtectedSecret = protectedSecret;
        CreatedAt = now;
        EnabledAt = null;
        LastUsedStep = 0;
    }

    public void Enable(DateTimeOffset now, long step)
    {
        EnabledAt = now;
        LastUsedStep = step;
    }

    public void Used(long step) => LastUsedStep = step;
}

/// <summary>Wiederherstellungscode (einmal verwendbar), gespeichert nur als SHA-256.</summary>
public sealed class UserRecoveryCode
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public byte[] CodeHash { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    private UserRecoveryCode()
    {
    }

    public UserRecoveryCode(Guid userId, byte[] codeHash, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        UserId = userId;
        CodeHash = codeHash;
        CreatedAt = now;
    }

    public void MarkUsed(DateTimeOffset now) => UsedAt = now;
}

internal sealed class UserTwoFactorConfiguration : IEntityTypeConfiguration<UserTwoFactor>
{
    public void Configure(EntityTypeBuilder<UserTwoFactor> builder)
    {
        builder.ToTable("user_two_factor");
        builder.HasKey(t => t.UserId);
        builder.Property(t => t.ProtectedSecret).HasMaxLength(1000);
        builder.HasOne<User>().WithOne().HasForeignKey<UserTwoFactor>(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class UserRecoveryCodeConfiguration : IEntityTypeConfiguration<UserRecoveryCode>
{
    public void Configure(EntityTypeBuilder<UserRecoveryCode> builder)
    {
        builder.ToTable("user_recovery_codes");
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.CodeHash).HasMaxLength(32);
        builder.HasIndex(c => c.UserId);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
