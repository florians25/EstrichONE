using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Identity.Sessions;

public sealed record IssuedTokens(
    Guid SessionId,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public enum RefreshFailure
{
    Invalid,
    Expired,
    Reused,
    SessionEnded,
    UserDisabled,
}

/// <summary>Sitzungen anlegen, Tokens rotieren, Sitzungen beenden.</summary>
public sealed class SessionService(
    EstrichOneDbContext db,
    TimeProvider clock,
    IOptions<AuthenticationSettings> options,
    IAuditLog audit)
{
    private AuthenticationSettings Settings => options.Value;

    /// <summary>Legt eine Sitzung an. Gespeichert wird mit dem nächsten <c>SaveChangesAsync</c> des Aufrufers.</summary>
    public IssuedTokens Start(User user, ClientKind clientKind, string? clientName)
    {
        var now = clock.GetUtcNow();
        var session = new Session(user.Id, clientKind, clientName, now, Settings.SessionMaxLifetime);
        db.Add(session);
        return Issue(session, now);
    }

    public async Task<(IssuedTokens? Tokens, RefreshFailure? Failure)> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var hash = TokenFactory.Hash(refreshToken);
        var stored = await db.Set<RefreshToken>().SingleOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);
        if (stored is null)
        {
            return (null, RefreshFailure.Invalid);
        }

        var session = await db.Set<Session>().SingleAsync(s => s.Id == stored.SessionId, cancellationToken);
        if (stored.UsedAt is not null)
        {
            // Innerhalb der Gnadenfrist: vermutlich zwei parallele Anfragen desselben Geräts – ablehnen, nicht bestrafen.
            if (now - stored.UsedAt.Value <= Settings.RefreshReuseGracePeriod)
            {
                return (null, RefreshFailure.Invalid);
            }

            session.Revoke(now, "refresh_token_reused");
            audit.Record(AuditActions.RefreshTokenReused, session.UserId, "session", session.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);
            return (null, RefreshFailure.Reused);
        }

        if (!session.IsActive(now))
        {
            return (null, RefreshFailure.SessionEnded);
        }

        if (stored.ExpiresAt <= now)
        {
            return (null, RefreshFailure.Expired);
        }

        var user = await db.Set<User>().SingleAsync(u => u.Id == session.UserId, cancellationToken);
        if (!user.IsActive)
        {
            session.Revoke(now, "user_disabled");
            await db.SaveChangesAsync(cancellationToken);
            return (null, RefreshFailure.UserDisabled);
        }

        stored.MarkUsed(now);
        var tokens = Issue(session, now);
        await db.SaveChangesAsync(cancellationToken);
        return (tokens, null);
    }

    public async Task RevokeAsync(Guid sessionId, string reason, Guid? actorUserId, CancellationToken cancellationToken)
    {
        var session = await db.Set<Session>().SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return;
        }

        session.Revoke(clock.GetUtcNow(), reason);
        audit.Record(AuditActions.SessionRevoked, actorUserId, "session", sessionId.ToString(),
            new Dictionary<string, string?> { ["reason"] = reason });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Beendet alle Sitzungen eines Benutzers außer <paramref name="exceptSessionId"/>.</summary>
    public async Task RevokeAllAsync(Guid userId, Guid? exceptSessionId, string reason, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var sessions = await db.Set<Session>()
            .Where(s => s.UserId == userId && s.RevokedAt == null && s.Id != exceptSessionId)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions)
        {
            session.Revoke(now, reason);
        }
    }

    private IssuedTokens Issue(Session session, DateTimeOffset now)
    {
        var (accessToken, accessHash) = TokenFactory.Create(TokenFactory.AccessPrefix);
        session.IssueAccessToken(accessHash, now + Settings.AccessTokenLifetime);

        var (refreshToken, refreshHash) = TokenFactory.Create(TokenFactory.RefreshPrefix);
        var refreshExpires = now + Settings.RefreshTokenLifetime;
        if (refreshExpires > session.ExpiresAt)
        {
            refreshExpires = session.ExpiresAt;
        }

        db.Add(new RefreshToken(session.Id, refreshHash, now, refreshExpires));
        return new IssuedTokens(session.Id, accessToken, session.AccessTokenExpiresAt, refreshToken, refreshExpires);
    }
}
