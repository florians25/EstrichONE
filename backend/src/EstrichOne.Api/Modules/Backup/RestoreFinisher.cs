using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Backup;

/// <summary>
/// Schließt eine Wiederherstellung aus dem Backup Center ab: Der Backup-Dienst spielt Datenbank und Dateien zurück und
/// meldet <c>restored</c>; die API bringt danach das Datenbankschema auf den Stand dieser Version (die Sicherung kann
/// älter sein) und gibt das System wieder frei (<c>completed</c>). Bis dahin sperrt das Wartungs-Gate alle Aufrufe.
/// </summary>
internal sealed partial class RestoreFinisher(BackupOperations operations, IServiceScopeFactory scopes, ILogger<RestoreFinisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await Tick(timer, stoppingToken))
        {
            if (operations.Restore() is not { Status: "restored" } progress)
            {
                continue;
            }

            try
            {
                await FinishAsync(progress, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception.Message);
            }
        }
    }

    private static async Task<bool> Tick(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    internal async Task FinishAsync(RestoreProgress progress, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        if (progress.Scope == "full")
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        scope.ServiceProvider.GetRequiredService<IAuditLog>().Record("backup.restore_completed", null, "backup", progress.Snapshot,
            new Dictionary<string, string?> { ["scope"] = progress.Scope });
        await db.SaveChangesAsync(cancellationToken);
        operations.CompleteRestore(progress, "Wiederherstellung abgeschlossen. Bitte neu anmelden; die Apps gleichen automatisch neu ab.");
        LogCompleted(logger, progress.Snapshot);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Wiederherstellung aus Snapshot {Snapshot} abgeschlossen (Schema aktualisiert).")]
    private static partial void LogCompleted(ILogger logger, string snapshot);

    [LoggerMessage(Level = LogLevel.Error, Message = "Wiederherstellung konnte nicht abgeschlossen werden: {Error}")]
    private static partial void LogFailed(ILogger logger, string error);
}
