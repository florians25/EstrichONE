using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Operations;

/// <summary>Arbeitet die Vorgänge ab (mehrere Mandanten parallel, je Mandant nacheinander).</summary>
public sealed class OperationWorker(
    OperationDispatcher queue,
    IServiceScopeFactory scopes,
    IOptions<HostingOptions> options,
    TimeProvider clock,
    ILogger<OperationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);
        using var slots = new SemaphoreSlim(Math.Max(1, options.Value.MaxParallelOperations));
        var running = new List<Task>();
        await foreach (var id in queue.ReadAllAsync(stoppingToken))
        {
            await slots.WaitAsync(stoppingToken);
            running.RemoveAll(t => t.IsCompleted);
            running.Add(Task.Run(async () =>
            {
                try
                {
                    await RunAsync(id, stoppingToken);
                }
                finally
                {
                    slots.Release();
                }
            }, stoppingToken));
        }

        await Task.WhenAll(running);
    }

    /// <summary>Nach einem Neustart: unterbrochene Vorgänge als fehlgeschlagen markieren, wartende erneut einreihen.</summary>
    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HostingDbContext>();
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Operations.Where(o => o.Status == OperationStatus.Running).ExecuteUpdateAsync(s => s
            .SetProperty(o => o.Status, OperationStatus.Failed)
            .SetProperty(o => o.FinishedAt, now)
            .SetProperty(o => o.Error, "Unterbrochen: Die Konsole wurde während des Vorgangs neu gestartet."), cancellationToken);
        foreach (var id in await db.Operations.Where(o => o.Status == OperationStatus.Queued).OrderBy(o => o.QueuedAt).Select(o => o.Id).ToListAsync(cancellationToken))
        {
            queue.Enqueue(id);
        }
    }

    public async Task RunAsync(Guid operationId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HostingDbContext>();
        var operation = await db.Operations.FirstOrDefaultAsync(o => o.Id == operationId, cancellationToken);
        if (operation is null || operation.Status != OperationStatus.Queued)
        {
            return;
        }

        var tenant = await db.Tenants.Include(t => t.Plan).FirstOrDefaultAsync(t => t.Id == operation.TenantId, cancellationToken);
        var log = new OperationLog(operation.Id, scopes, clock);
        operation.Status = OperationStatus.Running;
        operation.StartedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);

        using var pumpStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var pump = log.PumpAsync(pumpStop.Token);
        var audit = scope.ServiceProvider.GetRequiredService<AuditLog>();
        var label = OperationService.Labels[operation.Kind];
        try
        {
            if (tenant is null || tenant.State == TenantState.Deleted)
            {
                throw new InvalidOperationException("Der Mandant existiert nicht mehr.");
            }

            var slug = tenant.Slug;
            await scope.ServiceProvider.GetRequiredService<TenantLifecycle>().ExecuteAsync(operation, tenant, log, cancellationToken);
            operation.Status = OperationStatus.Succeeded;
            log.Write("Abgeschlossen.");
            await audit.WriteAsync($"{label} abgeschlossen", tenant.State == TenantState.Deleted ? null : tenant,
                tenant.State == TenantState.Deleted ? $"Mandant {slug}" : null, actor: operation.RequestedBy, cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(exception, "Vorgang {Kind} für {Tenant} fehlgeschlagen.", operation.Kind, tenant?.Slug);
            log.Write("FEHLER: " + exception.Message);
            operation.Status = OperationStatus.Failed;
            operation.Error = exception.Message;
            if (tenant is not null && tenant.State != TenantState.Deleted)
            {
                db.ChangeTracker.Entries().Where(e => e.Entity is not Operation && e.Entity is not Tenant).ToList()
                    .ForEach(e => e.State = EntityState.Unchanged);
                tenant.LastError = $"{label}: {exception.Message}";
                // Sperre bleibt bestehen; sonst gilt die Instanz als fehlerhaft.
                if (tenant.State != TenantState.Locked && operation.Kind != OperationKind.Backup)
                {
                    tenant.State = TenantState.Failed;
                }

                tenant.UpdatedAt = clock.GetUtcNow().UtcDateTime;
            }

            await audit.WriteAsync($"{label} fehlgeschlagen", tenant, exception.Message, success: false, actor: operation.RequestedBy,
                cancellationToken: CancellationToken.None);
        }
        finally
        {
            await pumpStop.CancelAsync();
            await pump;
            operation.FinishedAt = clock.GetUtcNow().UtcDateTime;
            operation.Log = log.Text;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
}
