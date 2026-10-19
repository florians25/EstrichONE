using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Identity.Sessions;

public enum ClientKind
{
    Web,
    Desktop,
    Ios,
    Android,
    Other,
}

/// <summary>
/// Angemeldete Sitzung eines Benutzers auf einem Client. Das aktuelle Zugriffstoken liegt nur als SHA-256-Hash vor
/// (ADR 0011). Eine Sitzung bündelt alle rotierten Refresh-Tokens („Familie“).
/// </summary>
public sealed class Session
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public ClientKind ClientKind { get; private set; }
    public string? ClientName { get; private set; }
    public byte[] AccessTokenHash { get; private set; } = [];
    public DateTimeOffset AccessTokenExpiresAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? RevokeReason { get; private set; }

    private Session()
    {
    }

    public Session(Guid userId, ClientKind clientKind, string? clientName, DateTimeOffset now, TimeSpan maxLifetime)
    {
        Id = Guid.CreateVersion7(now);
        UserId = userId;
        ClientKind = clientKind;
        ClientName = clientName;
        CreatedAt = now;
        LastSeenAt = now;
        ExpiresAt = now + maxLifetime;
    }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;

    public void IssueAccessToken(byte[] hash, DateTimeOffset expiresAt)
    {
        AccessTokenHash = hash;
        AccessTokenExpiresAt = expiresAt < ExpiresAt ? expiresAt : ExpiresAt;
    }

    public void Touch(DateTimeOffset now) => LastSeenAt = now;

    public void Revoke(DateTimeOffset now, string reason)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokeReason = reason;
        AccessTokenExpiresAt = now;
    }
}

/// <summary>Refresh-Token einer Sitzung. Jeder Token ist genau einmal verwendbar (Rotation).</summary>
public sealed class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public byte[] TokenHash { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? UsedAt { get; private set; }

    private RefreshToken()
    {
    }

    public RefreshToken(Guid sessionId, byte[] tokenHash, DateTimeOffset now, DateTimeOffset expiresAt)
    {
        Id = Guid.CreateVersion7(now);
        SessionId = sessionId;
        TokenHash = tokenHash;
        CreatedAt = now;
        ExpiresAt = expiresAt;
    }

    public void MarkUsed(DateTimeOffset now) => UsedAt = now;
}

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");
        builder.Property(session => session.Id).ValueGeneratedNever();
        builder.Property(session => session.ClientKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(session => session.ClientName).HasMaxLength(100);
        builder.Property(session => session.AccessTokenHash).HasMaxLength(32);
        builder.Property(session => session.RevokeReason).HasMaxLength(50);
        builder.HasIndex(session => session.AccessTokenHash).IsUnique();
        builder.HasIndex(session => session.UserId);
        builder.HasOne<User>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.Property(token => token.Id).ValueGeneratedNever();
        builder.Property(token => token.TokenHash).HasMaxLength(32);
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => token.SessionId);
        builder.HasOne<Session>().WithMany().HasForeignKey(token => token.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}
