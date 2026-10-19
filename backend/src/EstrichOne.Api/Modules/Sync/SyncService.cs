using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Sync.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Sync;

public sealed record PushResponse(IReadOnlyList<SyncOperationResult> Results);

public sealed record SyncChange(string EntityType, Guid Id, bool Deleted, JsonNode? Data);

public sealed record PullResponse(IReadOnlyList<SyncChange> Changes, string Cursor, bool HasMore, IReadOnlyList<Guid>? VisibleBauakteIds);

/// <summary>
/// Verarbeitet Outbox-Operationen und liefert Änderungen (docs/sync-protokoll.md, docs/reengineering/15).
/// </summary>
internal sealed partial class SyncService(
    IServiceScopeFactory scopes,
    EstrichOneDbContext db,
    IEnumerable<ISyncOperationHandler> handlers,
    IEnumerable<ISyncEntitySource> sources,
    ICurrentUser currentUser,
    ISessionDevices sessionDevices,
    IBauakteAccess bauakten,
    IUserPermissions permissions,
    TimeProvider clock,
    ILogger<SyncService> logger)
{
    public const int MaxOperationsPerPush = 100;
    public const int MaxChangesPerPull = 500;

    private readonly Dictionary<string, ISyncOperationHandler> _handlers = handlers
        .SelectMany(handler => handler.OperationTypes.Select(type => (type, handler)))
        .ToDictionary(pair => pair.type, pair => pair.handler, StringComparer.Ordinal);

    private readonly Dictionary<string, ISyncEntitySource> _sources = sources.ToDictionary(source => source.EntityType, StringComparer.Ordinal);

    public async Task<PushResponse> PushAsync(IReadOnlyList<SyncOperation> operations, CancellationToken cancellationToken)
    {
        var deviceId = await sessionDevices.GetCurrentSessionDeviceAsync(cancellationToken);
        var results = new List<SyncOperationResult>(operations.Count);
        foreach (var operation in operations)
        {
            results.Add(await ApplyOneAsync(operation, deviceId, cancellationToken));
        }

        return new PushResponse(results);
    }

    private async Task<SyncOperationResult> ApplyOneAsync(SyncOperation operation, Guid? deviceId, CancellationToken cancellationToken)
    {
        var earlier = await db.Set<ProcessedOperation>().AsNoTracking().SingleOrDefaultAsync(op => op.OpId == operation.OpId, cancellationToken);
        if (earlier is not null)
        {
            return earlier.UserId == currentUser.UserId
                ? Restore(earlier) with { Status = SyncOperationStatus.Duplicate }
                : SyncOperationResult.Rejected(operation.OpId, "OP_ID_CONFLICT", "Diese Operations-ID wurde bereits von einem anderen Benutzer verwendet.");
        }

        if (!_handlers.TryGetValue(operation.Type, out var handler))
        {
            return SyncOperationResult.Rejected(operation.OpId, "UNKNOWN_OPERATION", $"Unbekannte Operation „{operation.Type}“.");
        }

        // Jede Operation in eigenem Scope und eigener Transaktion: Eine abgelehnte Operation blockiert die übrigen nicht.
        await using var scope = scopes.CreateAsyncScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var scopedHandler = scope.ServiceProvider.GetServices<ISyncOperationHandler>().First(h => h.GetType() == handler.GetType());
        await using var transaction = await scopedDb.Database.BeginTransactionAsync(cancellationToken);
        SyncOperationResult result;
        try
        {
            result = await scopedHandler.ApplyAsync(operation, cancellationToken);
        }
        catch (SyncValidationException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await StoreRejectionAsync(operation, deviceId, SyncOperationResult.Rejected(operation.OpId, "VALIDATION_FAILED", exception.Message), cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Parallel geändert – nicht speichern, der Client sendet die Operation erneut.
            await transaction.RollbackAsync(cancellationToken);
            return SyncOperationResult.Rejected(operation.OpId, "RETRY_LATER", "Der Datensatz wurde gerade parallel geändert. Bitte erneut senden.");
        }

        if (result.Status == SyncOperationStatus.Rejected)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await StoreRejectionAsync(operation, deviceId, result, cancellationToken);
        }

        scopedDb.ChangeTracker.Clear();
        scopedDb.Add(new ProcessedOperation(operation, currentUser.UserId, deviceId, result.Status, Serialize(result), clock.GetUtcNow()));
        try
        {
            await scopedDb.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (!await IsProcessedAsync(operation.OpId, cancellationToken))
            {
                throw;
            }

            // Dieselbe Operation kam parallel doppelt an; die andere Anfrage hat gewonnen.
            return await ApplyOneAsync(operation, deviceId, cancellationToken);
        }

        return result;
    }

    private async Task<SyncOperationResult> StoreRejectionAsync(SyncOperation operation, Guid? deviceId, SyncOperationResult result, CancellationToken cancellationToken)
    {
        // Abgelehnte Operationen bleiben (mit Nutzdaten) nachvollziehbar, z. B. Fotos zu einer gelöschten Bauakte.
        var stored = result with { Entity = new JsonObject { ["payload"] = operation.Payload.DeepClone() } };
        await using var scope = scopes.CreateAsyncScope();
        var scopedDb = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        scopedDb.Add(new ProcessedOperation(operation, currentUser.UserId, deviceId, SyncOperationStatus.Rejected, Serialize(stored), clock.GetUtcNow()));
        try
        {
            await scopedDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            LogRejectionNotStored(logger, operation.OpId, exception.GetType().Name);
        }

        return result;
    }

    private async Task<bool> IsProcessedAsync(Guid opId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<ProcessedOperation>().AnyAsync(op => op.OpId == opId, cancellationToken);
    }

    public async Task<(PullResponse? Response, bool ResyncRequired)> PullAsync(string? cursor, int limit, bool includeScope, CancellationToken cancellationToken)
    {
        var epoch = await db.Database.SqlQuery<Guid>($"SELECT instance_epoch AS \"Value\" FROM system_state").SingleAsync(cancellationToken);
        var (txid, seq) = (0UL, 0L);
        if (cursor is not null)
        {
            if (!TryDecode(cursor, out var cursorEpoch, out txid, out seq) || cursorEpoch != epoch)
            {
                return (null, true);
            }
        }

        var take = Math.Clamp(limit, 1, MaxChangesPerPull);
        var rows = await db.Database.SqlQuery<ChangeRow>($"""
            SELECT entity_type, entity_id, bauakte_id, deleted, txid::text::numeric(20,0) AS txid, seq
            FROM change_log
            WHERE (txid, seq) > ({txid.ToString(CultureInfo.InvariantCulture)}::xid8, {seq})
              AND txid < pg_snapshot_xmin(pg_current_snapshot())
            ORDER BY txid, seq
            LIMIT {take + 1}
            """).ToListAsync(cancellationToken);

        var hasMore = rows.Count > take;
        rows = rows.Take(take).ToList();

        var visible = (await (await bauakten.VisibleAsync(db.Set<Bauakte>().IgnoreQueryFilters([QueryFilters.NotDeleted]), cancellationToken))
            .Select(b => b.Id).ToListAsync(cancellationToken)).ToHashSet();
        var granted = await permissions.GetAsync(cancellationToken);

        var visibleRows = rows.Where(row => _sources.TryGetValue(row.EntityType, out var source)
            && (row.BauakteId is { } bauakteId ? visible.Contains(bauakteId)
                : source.RequiredPermission is null || granted.Contains(source.RequiredPermission))).ToList();

        var changes = new List<SyncChange>(visibleRows.Count);
        foreach (var group in visibleRows.GroupBy(row => row.EntityType))
        {
            var live = group.Where(row => !row.Deleted).Select(row => row.EntityId).ToList();
            var data = live.Count > 0 ? await _sources[group.Key].LoadAsync(live, cancellationToken) : new Dictionary<Guid, JsonNode>();
            changes.AddRange(group.Select(row => new SyncChange(row.EntityType, row.EntityId, row.Deleted || !data.ContainsKey(row.EntityId),
                data.GetValueOrDefault(row.EntityId))));
        }

        var last = rows.LastOrDefault();
        var nextCursor = last is null ? cursor ?? Encode(epoch, 0, 0) : Encode(epoch, (ulong)last.Txid, last.Seq);
        var ordered = changes.OrderBy(change => rows.FindIndex(row => row.EntityId == change.Id && row.EntityType == change.EntityType)).ToList();
        return (new PullResponse(ordered, nextCursor, hasMore, includeScope ? visible.Order().ToList() : null), false);
    }

    private static SyncOperationResult Restore(ProcessedOperation earlier) =>
        JsonSerializer.Deserialize<SyncOperationResult>(earlier.Result, SyncPayload.Json)!;

    private static string Serialize(SyncOperationResult result) => JsonSerializer.Serialize(result, SyncPayload.Json);

    private static string Encode(Guid epoch, ulong txid, long seq) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{epoch:N}:{txid}:{seq}")));

    private static bool TryDecode(string cursor, out Guid epoch, out ulong txid, out long seq)
    {
        epoch = default;
        txid = 0;
        seq = 0;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(':');
            return parts.Length == 3 && Guid.TryParseExact(parts[0], "N", out epoch)
                && ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out txid)
                && long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out seq);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private sealed record ChangeRow(string EntityType, Guid EntityId, Guid? BauakteId, bool Deleted, decimal Txid, long Seq);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Abgelehnte Sync-Operation {OpId} konnte nicht protokolliert werden ({Reason})")]
    private static partial void LogRejectionNotStored(ILogger logger, Guid opId, string reason);
}
