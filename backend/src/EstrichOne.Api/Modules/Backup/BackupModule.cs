using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Infrastructure.Peers;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Backup;

/// <summary>Backup Center (docs/reengineering/16). Die Sicherung selbst läuft im eigenen Container.</summary>
public sealed class BackupModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BackupSettings>().Bind(configuration.GetSection(BackupSettings.SectionName));
        services.AddSingleton<BackupStatusReader>();
        services.AddScoped<BackupTargetService>();
        services.AddSingleton<BackupOperations>();
        services.AddSingleton<BackupMirrorStore>();
        services.AddScoped<EmergencySheetService>();
        services.AddScoped<Exports.IBackupMonitoring, BackupMonitoring>();
        services.AddHostedService<RestoreFinisher>();
        services.AddScoped<BackupPeerSource>();
        services.AddScoped<Exports.IUpdatablePeerSource>(provider => provider.GetRequiredService<BackupPeerSource>());
        services.AddScoped<PeerUpdateService>();
        // EstrichOne:Backup:PeerUpdateWorker=false schaltet die Nachverfolgung ab (Tests steuern den Ablauf selbst).
        if (!string.Equals(configuration["EstrichOne:Backup:PeerUpdateWorker"], "false", StringComparison.OrdinalIgnoreCase))
        {
            services.AddHostedService<PeerUpdateWorker>();
        }
        services.AddHealthChecks().AddCheck<BackupHealthCheck>("backup", tags: ["ready"]);
    }

    public void MapEndpoints(IEndpointRouteBuilder api)
    {
        var backup = api.MapGroup("/backup").WithTags("Backup Center");

        backup.MapGet("/status", (BackupStatusReader reader) => TypedResults.Ok(reader.Read()))
            .WithName("GetBackupStatus").RequirePermission(Permissions.BackupManage);

        backup.MapPost("/run", Results<Accepted, ProblemHttpResult> (BackupStatusReader reader, IAuditLog audit, ICurrentUser user) =>
            {
                if (!reader.RequestRun())
                {
                    return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Backup nicht eingerichtet",
                        extensions: new Dictionary<string, object?> { ["code"] = "BACKUP_NOT_CONFIGURED" });
                }

                audit.Record("backup.run_requested", user.UserId);
                return TypedResults.Accepted("/api/v1/backup/status");
            })
            .WithName("RequestBackupRun").WithSummary("Sicherung anfordern; der Backup-Dienst startet sie innerhalb von 30 Sekunden")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.BackupManage);

        // Backup-Ziele auf anderer Hardware (Kopplungscode, gepinntes Zertifikat, nur anhängen).
        backup.MapGet("/targets", async (BackupTargetService targets, bool? live, CancellationToken ct) =>
                TypedResults.Ok(await targets.ListAsync(live != false, ct)))
            .WithName("GetBackupTargets").RequirePermission(Permissions.BackupManage);

        backup.MapPost("/targets", async Task<Results<Ok<BackupTargetDto>, ProblemHttpResult>> (PairBackupTargetRequest request,
                BackupTargetService targets, IAuditLog audit, ICurrentUser user, EstrichOneDbContext db, CancellationToken ct) =>
            {
                try
                {
                    var target = await targets.PairAsync(request.Address, request.Code, user.UserId, ct);
                    audit.Record("backup.target.paired", user.UserId, "backup_target", target.Id.ToString(),
                        new Dictionary<string, string?> { ["address"] = target.Address, ["fingerprint"] = target.CaFingerprint });
                    await db.SaveChangesAsync(ct);
                    var list = await targets.ListAsync(live: true, ct);
                    return TypedResults.Ok(list.Single(t => t.Id == target.Id));
                }
                catch (PeerException exception)
                {
                    return TypedResults.Problem(statusCode: exception.Code == "PEER_UNREACHABLE" ? 502 : 422, title: "Kopplung fehlgeschlagen",
                        detail: exception.Message, extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
                }
            })
            .WithName("PairBackupTarget").WithSummary("Backup-Ziel per Adresse und Kopplungscode verbinden")
            .RequirePermission(Permissions.BackupManage);

        backup.MapDelete("/targets/{id:guid}", async Task<Results<NoContent, NotFound>> (Guid id, BackupTargetService targets, IAuditLog audit,
                ICurrentUser user, EstrichOneDbContext db, CancellationToken ct) =>
            {
                if (!await targets.RemoveAsync(id, ct))
                {
                    return TypedResults.NotFound();
                }

                audit.Record("backup.target.removed", user.UserId, "backup_target", id.ToString());
                await db.SaveChangesAsync(ct);
                return TypedResults.NoContent();
            })
            .WithName("RemoveBackupTarget").RequirePermission(Permissions.BackupManage);
        backup.MapGet("/targets/{id:guid}/recovery", async Task<Results<FileContentHttpResult, NotFound>> (Guid id, BackupTargetService targets,
                IAuditLog audit, ICurrentUser user, EstrichOneDbContext db, CancellationToken ct) =>
            {
                var text = await targets.RecoveryInfoAsync(id, ct);
                if (text is null)
                {
                    return TypedResults.NotFound();
                }

                audit.Record("backup.target.recovery_downloaded", user.UserId, "backup_target", id.ToString());
                await db.SaveChangesAsync(ct);
                return TypedResults.File(System.Text.Encoding.UTF8.GetBytes(text), "text/plain; charset=utf-8", "estrichone-notfallzugang-backup-ziel.txt");
            })
            .WithName("GetBackupTargetRecovery").WithSummary("Notfallzugang (Repository, Zertifikat, Anleitung) zum Aufbewahren")
            .RequirePermission(Permissions.BackupManage);

        BackupCenterEndpoints.Map(backup);
        PeerUpdateEndpoints.Map(backup);
    }
}
