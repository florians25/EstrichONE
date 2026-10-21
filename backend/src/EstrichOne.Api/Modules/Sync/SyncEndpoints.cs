using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using EstrichOne.Api.Modules.Sync.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace EstrichOne.Api.Modules.Sync;

public sealed record PushOperation(
    [property: Required] Guid OpId,
    [property: Required, MaxLength(50)] string Type,
    [property: Required] Guid EntityId,
    long? BaseVersion,
    [property: Required] JsonObject Payload);

public sealed record SnapshotRequest([property: Required, MaxLength(SyncService.MaxBauaktenPerSnapshot)] IReadOnlyList<Guid> BauakteIds);

public sealed record SnapshotResponse(IReadOnlyList<SyncChange> Changes, IReadOnlyList<Guid> Missing);

public sealed record PushRequest([property: Required, MaxLength(SyncService.MaxOperationsPerPush)] IReadOnlyList<PushOperation> Operations);

internal static class SyncEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var sync = api.MapGroup("/sync").WithTags("Synchronisation");

        sync.MapPost("/push", PushAsync).WithName("SyncPush")
            .WithSummary("Outbox-Operationen in Reihenfolge anwenden (max. 100); idempotent über opId")
            .ProducesValidationProblem();

        sync.MapGet("/pull", PullAsync).WithName("SyncPull")
            .WithSummary("Änderungen seit dem Cursor; ohne Cursor = vollständiger Abgleich. 409 RESYNC_REQUIRED nach Wiederherstellung")
            .ProducesProblem(StatusCodes.Status409Conflict);

        sync.MapPost("/snapshot", SnapshotAsync).WithName("SyncSnapshot")
            .WithSummary("Aktueller Stand ganzer Bauakten samt Inhalt (max. 50) – schließt Lücken, wenn eine Bauakte neu sichtbar wird")
            .ProducesValidationProblem();
    }

    private static async Task<Results<Ok<SnapshotResponse>, ValidationProblem>> SnapshotAsync(
        SnapshotRequest request, SyncService service, CancellationToken cancellationToken)
    {
        if (request.BauakteIds.Count > SyncService.MaxBauaktenPerSnapshot)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["bauakteIds"] = [$"Höchstens {SyncService.MaxBauaktenPerSnapshot} Bauakten je Abruf."] },
                extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
        }

        return TypedResults.Ok(await service.SnapshotAsync(request.BauakteIds.Distinct().ToList(), cancellationToken));
    }

    private static async Task<Results<Ok<PushResponse>, ValidationProblem>> PushAsync(
        PushRequest request, SyncService service, CancellationToken cancellationToken)
    {
        if (request.Operations.Count > SyncService.MaxOperationsPerPush || request.Operations.Select(op => op.OpId).Distinct().Count() != request.Operations.Count)
        {
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["operations"] = [$"Höchstens {SyncService.MaxOperationsPerPush} Operationen mit eindeutiger opId."] },
                extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
        }

        var operations = request.Operations
            .Select(op => new SyncOperation(op.OpId, op.Type, op.EntityId, op.BaseVersion, op.Payload))
            .ToList();
        return TypedResults.Ok(await service.PushAsync(operations, cancellationToken));
    }

    private static async Task<Results<Ok<PullResponse>, ProblemHttpResult>> PullAsync(
        SyncService service, CancellationToken cancellationToken,
        [FromQuery] string? cursor = null, [FromQuery] int limit = 200, [FromQuery] bool includeScope = false)
    {
        var (response, resync) = await service.PullAsync(cursor, limit, includeScope, cancellationToken);
        return resync
            ? TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Vollständiger Abgleich nötig",
                detail: "Der Server wurde wiederhergestellt oder der Cursor ist ungültig. Bitte ohne Cursor neu abgleichen; nicht gesendete Änderungen bleiben erhalten.",
                extensions: new Dictionary<string, object?> { ["code"] = "RESYNC_REQUIRED" })
            : TypedResults.Ok(response!);
    }
}
