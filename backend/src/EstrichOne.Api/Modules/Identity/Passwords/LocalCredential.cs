using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Identity.Passwords;

/// <summary>Passwort eines Benutzers. Fehlt der Eintrag, kann sich der Benutzer nicht lokal anmelden (z. B. nur SSO).</summary>
public sealed class LocalCredential
{
    public Guid UserId { get; private set; }
    public string PasswordHash { get; private set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>Nach einem Zurücksetzen durch den Administrator muss der Benutzer ein eigenes Passwort wählen.</summary>
    public bool MustChange { get; private set; }

    private LocalCredential()
    {
    }

    public LocalCredential(Guid userId, string passwordHash, DateTimeOffset changedAt, bool mustChange)
    {
        UserId = userId;
        PasswordHash = passwordHash;
        ChangedAt = changedAt;
        MustChange = mustChange;
    }

    public void Change(string passwordHash, DateTimeOffset changedAt, bool mustChange)
    {
        PasswordHash = passwordHash;
        ChangedAt = changedAt;
        MustChange = mustChange;
    }

    /// <summary>Gleiches Passwort, stärkere Parameter – ändert weder Zeitpunkt noch Pflicht zum Wechsel.</summary>
    public void Rehash(string passwordHash) => PasswordHash = passwordHash;
}

/// <summary>Fehlgeschlagene Anmeldeversuche für die Drosselung je Kennung und je Adresse.</summary>
public sealed class FailedLogin
{
    public Guid Id { get; private set; }
    public string NormalizedLogin { get; private set; } = string.Empty;
    public string? IpAddress { get; private set; }
    public DateTimeOffset At { get; private set; }

    private FailedLogin()
    {
    }

    public FailedLogin(string normalizedLogin, string? ipAddress, DateTimeOffset at)
    {
        Id = Guid.CreateVersion7(at);
        NormalizedLogin = normalizedLogin.Length > 320 ? normalizedLogin[..320] : normalizedLogin;
        IpAddress = ipAddress;
        At = at;
    }
}

internal sealed class LocalCredentialConfiguration : IEntityTypeConfiguration<LocalCredential>
{
    public void Configure(EntityTypeBuilder<LocalCredential> builder)
    {
        builder.ToTable("local_credentials");
        builder.HasKey(credential => credential.UserId);
        builder.Property(credential => credential.PasswordHash).HasMaxLength(200);
        builder.HasOne<User>().WithOne().HasForeignKey<LocalCredential>(credential => credential.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class FailedLoginConfiguration : IEntityTypeConfiguration<FailedLogin>
{
    public void Configure(EntityTypeBuilder<FailedLogin> builder)
    {
        builder.ToTable("failed_logins");
        builder.Property(attempt => attempt.Id).ValueGeneratedNever();
        builder.Property(attempt => attempt.NormalizedLogin).HasMaxLength(320);
        builder.Property(attempt => attempt.IpAddress).HasMaxLength(45);
        builder.HasIndex(attempt => new { attempt.NormalizedLogin, attempt.At });
        builder.HasIndex(attempt => new { attempt.IpAddress, attempt.At });
    }
}
