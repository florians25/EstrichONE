using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Identity.Sessions;

/// <param name="Current">Die Sitzung dieser Anfrage.</param>
public sealed record SessionInfo(Guid Id, ClientKind Client, string? ClientName, bool DeviceBound, DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt, DateTimeOffset ExpiresAt, bool Current);

public sealed record RevokedSessions(int Count);

/// <summary>Eigene Anmeldungen ansehen und beenden („überall abmelden“).</summary>
internal static class SessionEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/auth/sessions").WithTags("Anmeldung");
        group.MapGet("/", ListAsync).WithName("ListMySessions").WithSummary("Aktive Anmeldungen des eigenen Kontos (Browser, Apps, Desktop)");
        group.MapDelete("/{id:guid}", RevokeAsync).WithName("RevokeMySession")
            .WithSummary("Eine eigene Anmeldung beenden (wirkt sofort, auch für Apps)").ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/revoke-others", RevokeOthersAsync).WithName("RevokeMyOtherSessions")
            .WithSummary("Überall abmelden: alle eigenen Anmeldungen außer dieser beenden");
    }

    private static async Task<Ok<List<SessionInfo>>> ListAsync(ICurrentUser current, EstrichOneDbContext db, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var sessions = await db.Set<Session>().AsNoTracking()
            .Where(s => s.UserId == current.UserId && s.RevokedAt == null && s.ExpiresAt > now)
            .OrderByDescending(s => s.LastSeenAt).Take(200).ToListAsync(cancellationToken);
        return TypedResults.Ok(sessions.Select(s => new SessionInfo(s.Id, s.ClientKind, s.ClientName, s.DeviceId is not null, s.CreatedAt,
            s.LastSeenAt, s.ExpiresAt, s.Id == current.SessionId)).ToList());
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeAsync(Guid id, ICurrentUser current, EstrichOneDbContext db,
        SessionService sessions, IAuditLog audit, CancellationToken cancellationToken)
    {
        // Nur eigene Sitzungen; fremde sehen aus wie nicht vorhanden.
        if (!await db.Set<Session>().AnyAsync(s => s.Id == id && s.UserId == current.UserId && s.RevokedAt == null, cancellationToken))
        {
            return AuthEndpoints.Problem(StatusCodes.Status404NotFound, "SESSION_NOT_FOUND", "Nicht gefunden", "Diese Anmeldung gibt es nicht (mehr).");
        }

        await sessions.RevokeAsync(id, "revoked_by_user", current.UserId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<RevokedSessions>> RevokeOthersAsync(ICurrentUser current, EstrichOneDbContext db, SessionService sessions,
        IAuditLog audit, CancellationToken cancellationToken)
    {
        var count = await db.Set<Session>().CountAsync(s => s.UserId == current.UserId && s.RevokedAt == null && s.Id != current.SessionId, cancellationToken);
        await sessions.RevokeAllAsync(current.UserId, current.SessionId, "revoked_everywhere", cancellationToken);
        audit.Record(AuditActions.SessionsRevokedAll, current.UserId, "user", current.UserId.ToString(),
            new Dictionary<string, string?> { ["count"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new RevokedSessions(count));
    }
}
