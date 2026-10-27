using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Backup;

/// <summary>Backup Center: Zeitplan, Sicherungen, Wiederherstellung, Notfallblatt, Backup-Ziele, die ziehen (docs/backup.md).</summary>
internal static class BackupCenterEndpoints
{
    public static void Map(RouteGroupBuilder backup)
    {
        backup.MapGet("/settings", (BackupOperations ops) => TypedResults.Ok(ops.Preferences()))
            .WithName("GetBackupSettings").WithSummary("Zeitplan (tägliche Vollsicherung, inkrementell), Aufbewahrung, Meldungen")
            .RequirePermission(Permissions.BackupManage);

        backup.MapPut("/settings", async Task<Results<Ok<BackupPreferences>, ValidationProblem, ProblemHttpResult>> (BackupPreferences request,
                BackupOperations ops, IAuditLog audit, ICurrentUser user, EstrichOneDbContext db, CancellationToken ct) =>
            {
                var errors = BackupOperations.Validate(request);
                if (errors.Count > 0)
                {
                    return TypedResults.ValidationProblem(errors);
                }

                if (!ops.SavePreferences(request))
                {
                    return NotConfigured();
                }

                audit.Record("backup.settings_changed", user.UserId, "backup", "settings", new Dictionary<string, string?>
                {
                    ["dailyTime"] = request.DailyTime, ["incrementalEveryHours"] = request.IncrementalEveryHours.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["keep"] = $"{request.KeepDaily}/{request.KeepWeekly}/{request.KeepMonthly}/{request.KeepYearly}", ["notifyAdmins"] = request.NotifyAdmins.ToString(),
                });
                await db.SaveChangesAsync(ct);
                return TypedResults.Ok(request);
            })
            .WithName("SaveBackupSettings").ProducesValidationProblem().ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.BackupManage);

        backup.MapGet("/snapshots", (BackupOperations ops) => TypedResults.Ok(ops.Snapshots()))
            .WithName("GetBackupSnapshots").WithSummary("Sicherungen je Ziel (vom Backup-Dienst nach jedem Lauf aktualisiert)")
            .RequirePermission(Permissions.BackupManage);

        backup.MapGet("/restore", Results<Ok<RestoreProgress>, NoContent> (BackupOperations ops) =>
                ops.Restore() is { } p ? TypedResults.Ok(p) : TypedResults.NoContent())
            .WithName("GetRestoreProgress").WithSummary("Fortschritt der laufenden bzw. letzten Wiederherstellung")
            .RequirePermission(Permissions.BackupManage);

        backup.MapPost("/restore", async Task<Results<Accepted<RestoreProgress>, ProblemHttpResult>> (RestoreRequest request, BackupOperations ops,
                IAuditLog audit, ICurrentUser user, EstrichOneDbContext db, CancellationToken ct) =>
            {
                if (!ops.Configured)
                {
                    return NotConfigured();
                }

                if (ops.RequestRestore(request, user.UserId.ToString()) is { } error)
                {
                    return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Wiederherstellung nicht möglich", detail: error,
                        extensions: new Dictionary<string, object?> { ["code"] = "RESTORE_REJECTED" });
                }

                audit.Record("backup.restore_requested", user.UserId, "backup", request.Snapshot, new Dictionary<string, string?>
                {
                    ["scope"] = request.Scope, ["source"] = request.Source, ["fileId"] = request.FileId?.ToString(),
                });
                await db.SaveChangesAsync(ct);
                return TypedResults.Accepted("/api/v1/backup/restore", ops.Restore()!);
            })
            .WithName("RequestRestore")
            .WithSummary("Wiederherstellen: ganzes System (Wartungsmodus, Bestätigung „WIEDERHERSTELLEN“) oder eine einzelne Datei")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.BackupManage);

        backup.MapGet("/emergency-sheet", async (EmergencySheetService sheet, HttpContext http, IOptions<EstrichOneOptions> options, IAuditLog audit,
                ICurrentUser user, EstrichOneDbContext db, CancellationToken ct) =>
            {
                var url = (options.Value.PublicUrl?.ToString() ?? $"{http.Request.Scheme}://{http.Request.Host}").TrimEnd('/');
                var pdf = await sheet.RenderAsync(url, ct);
                audit.Record("backup.emergency_sheet_downloaded", user.UserId, "backup", "emergency-sheet");
                await db.SaveChangesAsync(ct);
                return TypedResults.File(pdf, "application/pdf", "estrichone-notfallblatt.pdf");
            })
            .WithName("GetEmergencySheet").WithSummary("Notfallblatt (PDF): Wiederherstellungsschlüssel, Sicherungsziele, Befehl für die Neuinstallation")
            .RequirePermission(Permissions.BackupManage);

        // Backup-Ziele, die selbst ziehen (zuhause hinter NAT)
        backup.MapGet("/mirrors", (BackupMirrorStore store) => TypedResults.Ok(new BackupMirrorsResponse(store.Available, store.List())))
            .WithName("GetBackupMirrors").RequirePermission(Permissions.BackupManage);

        backup.MapPost("/mirrors", async Task<Results<Ok<CreateBackupMirrorResponse>, ProblemHttpResult>> (CreateBackupMirrorRequest request,
                BackupMirrorStore store, HttpContext http, IOptions<EstrichOneOptions> options, IOptions<BackupSettings> settings, IAuditLog audit,
                ICurrentUser user, EstrichOneDbContext db, CancellationToken ct) =>
            {
                if (store.Create(request.Name ?? string.Empty) is not { } created)
                {
                    return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Nicht verfügbar",
                        detail: "Backup-Ziele, die ziehen, brauchen ein lokales Hauptziel (BACKUP_REPOSITORY=/repository) und den Backup-Dienst.",
                        extensions: new Dictionary<string, object?> { ["code"] = "MIRROR_UNAVAILABLE" });
                }

                var url = (options.Value.PublicUrl?.ToString() ?? $"{http.Request.Scheme}://{http.Request.Host}").TrimEnd('/');
                var version = ApplicationVersion.Current.Split('+')[0];
                var command = $"sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock -e ESTRICHONE_PULL_FROM={url} "
                    + $"-e ESTRICHONE_PULL_CODE={created.Code} {settings.Value.InstallerImage}:{version} backup-ziel";
                audit.Record("backup.mirror.created", user.UserId, "backup_mirror", created.Mirror.Id);
                await db.SaveChangesAsync(ct);
                return TypedResults.Ok(new CreateBackupMirrorResponse(created.Mirror, created.Code, url, command));
            })
            .WithName("CreateBackupMirror").WithSummary("Backup-Ziel, das zieht, anlegen: Einmal-Code (24 h) und Befehl für den Rechner zuhause")
            .ProducesProblem(StatusCodes.Status409Conflict).RequirePermission(Permissions.BackupManage);

        backup.MapDelete("/mirrors/{id}", async Task<Results<NoContent, NotFound>> (string id, BackupMirrorStore store, IAuditLog audit, ICurrentUser user,
                EstrichOneDbContext db, CancellationToken ct) =>
            {
                if (!store.Remove(id))
                {
                    return TypedResults.NotFound();
                }

                audit.Record("backup.mirror.removed", user.UserId, "backup_mirror", id);
                await db.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveBackupMirror").RequirePermission(Permissions.BackupManage);

        // Protokoll für das ziehende Backup-Ziel (Gegenstück: backend/src/EstrichOne.Archive/MirrorPuller.cs). Bearer-Token statt Anmeldung.
        var mirror = backup.MapGroup("/mirror").WithTags("Backup-Spiegel");
        mirror.MapPost("/pair", Results<Ok<MirrorPairResponse>, ProblemHttpResult> (MirrorPairRequest request, BackupMirrorStore store, IAuditLog audit) =>
            {
                if (store.Pair(request.Code, request.Name) is not { } paired)
                {
                    return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Kopplung fehlgeschlagen",
                        detail: "Code ungültig, abgelaufen oder bereits verwendet.", extensions: new Dictionary<string, object?> { ["code"] = "MIRROR_CODE_INVALID" });
                }

                audit.Record("backup.mirror.paired", null, "backup_mirror", paired.Id, new Dictionary<string, string?> { ["name"] = request.Name });
                return TypedResults.Ok(new MirrorPairResponse(paired.Id, paired.Token));
            })
            .WithName("PairBackupMirror").AllowAnonymous().RequireRateLimiting("auth");

        mirror.MapGet("/repo/config", (HttpContext http, BackupMirrorStore store) =>
                Authorized(http, store) is null ? Results.Unauthorized() : store.ConfigPath is { } c ? Results.File(c, "application/octet-stream") : Results.NotFound())
            .WithName("GetMirrorConfig").AllowAnonymous().ExcludeFromDescription();
        mirror.MapGet("/repo/{type}/", (string type, HttpContext http, BackupMirrorStore store) =>
                Authorized(http, store) is null ? Results.Unauthorized() : store.ListFiles(type) is { } files ? Results.Json(files) : Results.NotFound())
            .WithName("ListMirrorFiles").AllowAnonymous().ExcludeFromDescription();
        mirror.MapGet("/repo/{type}/{name}", (string type, string name, HttpContext http, BackupMirrorStore store) =>
                Authorized(http, store) is null ? Results.Unauthorized()
                : store.FilePath(type, name) is { } path ? Results.File(path, "application/octet-stream", enableRangeProcessing: true) : Results.NotFound())
            .WithName("GetMirrorFile").AllowAnonymous().ExcludeFromDescription();
        mirror.MapPost("/report", (BackupMirrorReport report, HttpContext http, BackupMirrorStore store) =>
            {
                if (Authorized(http, store) is not { } id)
                {
                    return Results.Unauthorized();
                }

                store.Report(id, report);
                return Results.NoContent();
            })
            .WithName("ReportBackupMirror").AllowAnonymous().ExcludeFromDescription();
    }

    private static string? Authorized(HttpContext http, BackupMirrorStore store)
    {
        var id = store.Authenticate(http.Request.Headers.Authorization.ToString());
        if (id is not null)
        {
            store.Seen(id);
        }

        return id;
    }

    private static ProblemHttpResult NotConfigured() =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Backup nicht eingerichtet",
            extensions: new Dictionary<string, object?> { ["code"] = "BACKUP_NOT_CONFIGURED" });
}

public sealed record BackupMirrorsResponse(bool Available, IReadOnlyList<BackupMirrorDto> Mirrors);
