using EstrichOne.Api.Modules.Organization.Exports;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Devices.Exports;
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

    /// <summary>Gleicher Refresh-Token kurz zuvor schon eingelöst (paralleler Tab/Request) – kein Angriff, Cookies sind neu.</summary>
    Concurrent,
    Expired,
    Reused,
    SessionEnded,
    UserDisabled,
    DeviceProofRequired,
    DeviceLocked,
    DeviceRevoked,
}

/// <summary>Sitzungen anlegen, Tokens rotieren, Sitzungen beenden.</summary>
public sealed class SessionService(
    EstrichOneDbContext db,
    TimeProvider clock,
    IOptions<AuthenticationSettings> options,
    IAuditLog audit,
    ISystemSettings systemSettings)
{
    private AuthenticationSettings Settings => options.Value;

    /// <summary>Legt eine Sitzung an. Gespeichert wird mit dem nächsten <c>SaveChangesAsync</c> des Aufrufers.</summary>
    /// <param name="persistent">Web: „angemeldet bleiben“ – längere Leerlaufzeit (Einstellungen → Sitzungen).</param>
    public async Task<IssuedTokens> StartAsync(User user, ClientKind clientKind, string? clientName, bool persistent,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var session = new Session(user.Id, clientKind, clientName, now, Settings.SessionMaxLifetime, persistent);
        db.Add(session);
        return Issue(session, now, await RefreshLifetimeAsync(session, cancellationToken));
    }

    /// <summary>
    /// Gleitende Sitzung: Jede Erneuerung (bei Aktivität spätestens alle paar Minuten) verlängert um die Leerlaufzeit.
    /// Apps behalten die feste Laufzeit der Konfiguration.
    /// </summary>
    private async Task<TimeSpan> RefreshLifetimeAsync(Session session, CancellationToken cancellationToken)
    {
        if (session.ClientKind != ClientKind.Web)
        {
            return Settings.RefreshTokenLifetime;
        }

        var configured = (await systemSettings.GetAsync(cancellationToken)).Session;
        return session.Persistent ? TimeSpan.FromDays(configured.RememberDays) : TimeSpan.FromHours(configured.IdleHours);
    }

    /// <param name="deviceSignature">
    /// Pflicht für gerätegebundene Sitzungen: ECDSA-Signatur des Geräteschlüssels über
    /// <see cref="Device.RefreshProofPrefix"/> + Refresh-Token. Ein abgegriffener Refresh-Token ist ohne das Gerät wertlos.
    /// </param>
    public async Task<(IssuedTokens? Tokens, RefreshFailure? Failure)> RefreshAsync(string refreshToken, byte[]? deviceSignature,
        CancellationToken cancellationToken)
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
            if (now - stored.UsedAt.Value < Settings.RefreshReuseGracePeriod)
            {
                return (null, RefreshFailure.Concurrent);
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

        if (session.DeviceId is { } deviceId)
        {
            var device = await db.Set<Device>().SingleAsync(d => d.Id == deviceId, cancellationToken);
            if (deviceSignature is null || !device.VerifyRefreshProof(refreshToken, deviceSignature))
            {
                return (null, RefreshFailure.DeviceProofRequired);
            }

            if (device.Status == DeviceStatus.Locked)
            {
                return (null, RefreshFailure.DeviceLocked);
            }

            if (device.Status == DeviceStatus.Retired)
            {
                session.Revoke(now, "device_retired");
                await db.SaveChangesAsync(cancellationToken);
                return (null, RefreshFailure.DeviceRevoked);
            }
        }

        var user = await db.Set<User>().SingleAsync(u => u.Id == session.UserId, cancellationToken);
        if (!user.IsActive)
        {
            session.Revoke(now, "user_disabled");
            await db.SaveChangesAsync(cancellationToken);
            return (null, RefreshFailure.UserDisabled);
        }

        stored.MarkUsed(now);
        var tokens = Issue(session, now, await RefreshLifetimeAsync(session, cancellationToken));
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

    private IssuedTokens Issue(Session session, DateTimeOffset now, TimeSpan refreshLifetime)
    {
        var (accessToken, accessHash) = TokenFactory.Create(TokenFactory.AccessPrefix);
        session.IssueAccessToken(accessHash, now + Settings.AccessTokenLifetime);

        var (refreshToken, refreshHash) = TokenFactory.Create(TokenFactory.RefreshPrefix);
        var refreshExpires = now + refreshLifetime;
        if (refreshExpires > session.ExpiresAt)
        {
            refreshExpires = session.ExpiresAt;
        }

        db.Add(new RefreshToken(session.Id, refreshHash, now, refreshExpires));
        return new IssuedTokens(session.Id, accessToken, session.AccessTokenExpiresAt, refreshToken, refreshExpires);
    }
}
