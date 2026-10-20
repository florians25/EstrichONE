using System.Globalization;
using System.Text;
using System.Text.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Audit;

/// <summary>Ein Eintrag im Audit-Log für die Administration. Die IP-Adresse ist nur hier sichtbar, nie im Verlauf einer Bauakte.</summary>
public sealed record AuditLogItem(
    Guid Id, DateTimeOffset OccurredAt, string Action, Guid? ActorUserId, string? ActorName, string? EntityType, string? EntityId,
    string? IpAddress, IReadOnlyDictionary<string, string?> Details);

public sealed record AuditLogPage(IReadOnlyList<AuditLogItem> Items, string? NextCursor);

/// <summary>Audit-Log lesen (docs/reengineering/17): filtern nach Ereignis, Person, Objekt und Zeitraum, neueste zuerst.</summary>
internal static class AuditEndpoints
{
    private const int DefaultPageSize = 100;
    private const int MaxPageSize = 500;

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/audit", ListAsync).WithName("ListAuditEvents").WithTags("Audit")
            .WithSummary("Audit-Ereignisse, neueste zuerst; action filtert per Präfix (z. B. „auth.“)")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(Permissions.AuditRead);
    }

    private static async Task<Results<Ok<AuditLogPage>, ProblemHttpResult>> ListAsync(
        EstrichOneDbContext db, CancellationToken cancellationToken,
        [FromQuery] string? action = null, [FromQuery] Guid? actorUserId = null, [FromQuery] string? entityType = null,
        [FromQuery] string? entityId = null, [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? cursor = null, [FromQuery] int? limit = null)
    {
        var pageSize = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var query = db.Set<AuditEvent>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(action))
        {
            var prefix = action.Trim();
            query = query.Where(e => e.Action.StartsWith(prefix));
        }

        if (actorUserId is { } actor)
        {
            query = query.Where(e => e.ActorUserId == actor);
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(e => e.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            query = query.Where(e => e.EntityId == entityId);
        }

        if (from is { } start)
        {
            query = query.Where(e => e.OccurredAt >= start);
        }

        if (to is { } end)
        {
            query = query.Where(e => e.OccurredAt < end);
        }

        if (cursor is not null)
        {
            if (!TryDecodeCursor(cursor, out var occurredAt, out var lastId))
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Ungültiger Cursor",
                    extensions: new Dictionary<string, object?> { ["code"] = "INVALID_CURSOR" });
            }

            query = query.Where(e => e.OccurredAt < occurredAt || (e.OccurredAt == occurredAt && e.Id.CompareTo(lastId) < 0));
        }

        var rows = await query.OrderByDescending(e => e.OccurredAt).ThenByDescending(e => e.Id).Take(pageSize + 1).ToListAsync(cancellationToken);
        var page = rows.Take(pageSize).ToList();
        var actorIds = page.Select(e => e.ActorUserId).OfType<Guid>().Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        var items = page.Select(e => new AuditLogItem(
            e.Id, e.OccurredAt, e.Action, e.ActorUserId,
            e.ActorUserId is { } id && names.TryGetValue(id, out var name) ? name : null,
            e.EntityType, e.EntityId, e.IpAddress,
            e.Details is null ? new Dictionary<string, string?>() : JsonSerializer.Deserialize<Dictionary<string, string?>>(e.Details)!)).ToList();
        var next = rows.Count > pageSize ? EncodeCursor(page[^1].OccurredAt, page[^1].Id) : null;
        return TypedResults.Ok(new AuditLogPage(items, next));
    }

    private static string EncodeCursor(DateTimeOffset occurredAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{occurredAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}|{id}"));

    private static bool TryDecodeCursor(string cursor, out DateTimeOffset occurredAt, out Guid id)
    {
        occurredAt = default;
        id = default;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (parts.Length != 2 || !long.TryParse(parts[0], CultureInfo.InvariantCulture, out var ticks) || !Guid.TryParse(parts[1], out id))
            {
                return false;
            }

            occurredAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
