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

    /// <summary>Sitzung beendet, weil das Passwort geändert bzw. zurückgesetzt wurde.</summary>
    PasswordChanged,

    /// <summary>Sitzung abgemeldet bzw. im IT Center beendet.</summary>
    Revoked,
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
        var lifetime = await RefreshLifetimeAsync(session, cancellationToken);
        if (session.RequiresDevice)
        {
            // Gerätesitzung: keine feste Höchstdauer, nur die Leerlaufzeit (gleitend, siehe RefreshAsync).
            session.ExtendUntil(now + lifetime);
        }

        return Issue(session, now, lifetime);
    }

    /// <summary>
    /// Gleitende Sitzung: Jede Erneuerung (bei Aktivität spätestens alle paar Minuten) verlängert um die Leerlaufzeit.
    /// Web: Leerlauf bzw. „angemeldet bleiben“; iOS/Android: Gerätesitzung (Einstellungen → Sitzungen, Standard 90 Tage);
    /// Desktop: feste Laufzeit der Konfiguration.
    /// </summary>
    private async Task<TimeSpan> RefreshLifetimeAsync(Session session, CancellationToken cancellationToken)
    {
        if (session.ClientKind is not (ClientKind.Web or ClientKind.Ios or ClientKind.Android))
        {
            return Settings.RefreshTokenLifetime;
        }

        var configured = (await systemSettings.GetAsync(cancellationToken)).Session.Normalize();
        if (session.RequiresDevice)
        {
            return TimeSpan.FromDays(configured.DeviceIdleDays);
        }

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
        if (!session.IsActive(now))
        {
            return (null, EndedFailure(session));
        }

        if (stored.UsedAt is not null && !await TryRecoverAsync(session, stored, refreshToken, deviceSignature, now, cancellationToken))
        {
            // Innerhalb der Gnadenfrist: vermutlich zwei parallele Anfragen desselben Clients – ablehnen, nicht bestrafen.
            if (now - stored.UsedAt.Value < Settings.RefreshReuseGracePeriod)
            {
                return (null, RefreshFailure.Concurrent);
            }

            session.Revoke(now, "refresh_token_reused");
            audit.Record(AuditActions.RefreshTokenReused, session.UserId, "session", session.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);
            return (null, RefreshFailure.Reused);
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
        var lifetime = await RefreshLifetimeAsync(session, cancellationToken);
        if (session.RequiresDevice)
        {
            session.ExtendUntil(now + lifetime);
        }

        var tokens = Issue(session, now, lifetime);
        await db.SaveChangesAsync(cancellationToken);
        return (tokens, null);
    }

    /// <summary>
    /// Gerätesitzung, deren letzte Antwort verloren ging (Funkloch, App im Hintergrund beendet) oder zwei Anfragen
    /// desselben Geräts gleichzeitig: Der vorgelegte Token ist schon eingelöst, sein Nachfolger aber noch von niemandem
    /// verwendet. Mit gültiger Signatur des Geräteschlüssels (Secure Enclave, nicht kopierbar) ist das kein Diebstahl –
    /// der unbenutzte Nachfolger wird verworfen und neu ausgestellt, statt die Sitzung zu beenden.
    /// Wurde der Nachfolger dagegen schon eingelöst, gilt weiter die Wiederverwendungserkennung.
    /// </summary>
    private async Task<bool> TryRecoverAsync(Session session, RefreshToken stored, string refreshToken, byte[]? deviceSignature,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (session.DeviceId is not { } deviceId || deviceSignature is null || stored.ExpiresAt <= now)
        {
            return false;
        }

        var newer = await db.Set<RefreshToken>()
            .Where(token => token.SessionId == session.Id && token.CreatedAt >= stored.CreatedAt && token.Id != stored.Id)
            .ToListAsync(cancellationToken);
        if (newer.Count == 0 || newer.Any(token => token.UsedAt is not null))
        {
            return false;
        }

        var device = await db.Set<Device>().SingleAsync(d => d.Id == deviceId, cancellationToken);
        if (!device.VerifyRefreshProof(refreshToken, deviceSignature))
        {
            return false;
        }

        foreach (var token in newer)
        {
            token.MarkUsed(now);
        }

        audit.Record(AuditActions.RefreshTokenRecovered, session.UserId, "session", session.Id.ToString());
        return true;
    }

    /// <summary>Warum eine Sitzung nicht mehr gilt – damit die App den Grund nennen kann.</summary>
    private static RefreshFailure EndedFailure(Session session) => session.RevokeReason switch
    {
        null => RefreshFailure.Expired,
        "password_changed" or "password_reset" => RefreshFailure.PasswordChanged,
        "user_disabled" => RefreshFailure.UserDisabled,
        "device_retired" => RefreshFailure.DeviceRevoked,
        "refresh_token_reused" => RefreshFailure.Reused,
        _ => RefreshFailure.Revoked,
    };

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
