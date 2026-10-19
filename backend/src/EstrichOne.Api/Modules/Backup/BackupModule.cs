using EstrichOne.Api.Infrastructure.Modules;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EstrichOne.Api.Modules.Backup;

/// <summary>Backup Center (docs/reengineering/16). Die Sicherung selbst läuft im eigenen Container.</summary>
public sealed class BackupModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BackupSettings>().Bind(configuration.GetSection(BackupSettings.SectionName));
        services.AddSingleton<BackupStatusReader>();
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
    }
}
