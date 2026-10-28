using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EstrichOne.Api.Modules.Backup;

/// <summary>Zusatzserver (Backup-Ziele, Ziele, die ziehen, Archivserver) per Fernaktualisierung auf die Version dieses Servers bringen.</summary>
internal static class PeerUpdateEndpoints
{
    public static void Map(RouteGroupBuilder backup)
    {
        var updates = backup.MapGroup("/peer-updates").WithTags("Backup Center");

        updates.MapGet("/", async (PeerUpdateService service, bool? live, CancellationToken ct) =>
                TypedResults.Ok(await service.ListAsync(live != false, ct)))
            .WithName("GetPeerUpdates")
            .WithSummary("Installierte und verfügbare Version je Zusatzserver, Stand des letzten Auftrags")
            .RequirePermission(Permissions.BackupManage);

        updates.MapPost("/{key}/update", async Task<Results<Accepted<PeerUpdateDto>, ProblemHttpResult>> (string key, RequestPeerUpdateRequest? request,
                PeerUpdateService service, ICurrentUser user, CancellationToken ct) =>
            {
                var (item, problem) = await service.RequestAsync(key, request?.Version, user.UserId, ct);
                return problem is not null ? Problem(problem) : TypedResults.Accepted("/api/v1/backup/peer-updates", item!);
            })
            .WithName("RequestPeerUpdate")
            .WithSummary("Zusatzserver aktualisieren (Standard: Version dieses Servers; nie neuer). Läuft auf dem Rechner mit Rücksprung bei Fehlern.")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.BackupManage);

        updates.MapPost("/update-all", async (PeerUpdateService service, ICurrentUser user, string? kind, CancellationToken ct) =>
                TypedResults.Ok(await service.RequestAllAsync(user.UserId, kind is { Length: > 0 } k ? k.Split(',') : null, ct)))
            .WithName("RequestAllPeerUpdates")
            .WithSummary("Alle veralteten Zusatzserver mit Updater aktualisieren (optional nur kind=backup-target,backup-mirror bzw. archive)")
            .RequirePermission(Permissions.BackupManage);

        updates.MapPut("/{key}/auto", async Task<Results<Ok<PeerUpdateDto>, NotFound>> (string key, PeerAutoUpdateRequest request,
                PeerUpdateService service, ICurrentUser user, CancellationToken ct) =>
                await service.SetAutoUpdateAsync(key, request.AutoUpdate, user.UserId, ct) is { } item ? TypedResults.Ok(item) : TypedResults.NotFound())
            .WithName("SetPeerAutoUpdate")
            .WithSummary("Nach einem Update dieses Servers automatisch mitaktualisieren (ein/aus)")
            .RequirePermission(Permissions.BackupManage);

        // Ziel, das zieht: meldet sich alle paar Minuten und holt dabei Aufträge ab (Bearer-Token des Spiegels statt Anmeldung).
        backup.MapPost("/mirror/checkin", async (MirrorCheckIn report, HttpContext http, BackupMirrorStore store, PeerUpdateService service,
                CancellationToken ct) =>
            {
                if (store.Authenticate(http.Request.Headers.Authorization.ToString()) is not { } id)
                {
                    return Results.Unauthorized();
                }

                store.Seen(id);
                return Results.Json(new MirrorCheckInAnswer(await service.CheckInAsync(id, report, ct)));
            })
            .WithName("CheckInBackupMirror").AllowAnonymous().ExcludeFromDescription();
    }

    private static ProblemHttpResult Problem(PeerUpdateProblem problem) =>
        TypedResults.Problem(statusCode: problem.Status, title: problem.Message,
            extensions: new Dictionary<string, object?> { ["code"] = problem.Code });
}
