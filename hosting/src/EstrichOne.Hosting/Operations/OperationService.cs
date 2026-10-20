using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Hosting.Operations;

public sealed class OperationConflictException(string message) : Exception(message);

/// <summary>Nimmt Vorgänge entgegen: je Mandant höchstens ein offener Vorgang.</summary>
public sealed class OperationService(HostingDbContext db, OperationDispatcher queue, AuditLog audit, TimeProvider clock)
{
    public static readonly IReadOnlyDictionary<OperationKind, string> Labels = new Dictionary<OperationKind, string>
    {
        [OperationKind.Provision] = "Bereitstellen",
        [OperationKind.Start] = "Starten",
        [OperationKind.Stop] = "Stoppen",
        [OperationKind.Restart] = "Neu starten",
        [OperationKind.Update] = "Aktualisieren",
        [OperationKind.Lock] = "Sperren",
        [OperationKind.Unlock] = "Entsperren",
        [OperationKind.Backup] = "Sicherung",
        [OperationKind.Delete] = "Löschen",
        [OperationKind.Reconfigure] = "Konfiguration anwenden",
    };

    public Task<bool> HasOpenOperationAsync(Guid tenantId, CancellationToken cancellationToken) =>
        db.Operations.AnyAsync(o => o.TenantId == tenantId && (o.Status == OperationStatus.Queued || o.Status == OperationStatus.Running), cancellationToken);

    public async Task<Operation> EnqueueAsync(Tenant tenant, OperationKind kind, OperationParameters? parameters, CancellationToken cancellationToken)
    {
        if (await HasOpenOperationAsync(tenant.Id, cancellationToken))
        {
            throw new OperationConflictException($"Für „{tenant.CompanyName}“ läuft bereits ein Vorgang. Bitte warten, bis er abgeschlossen ist.");
        }

        var operation = new Operation
        {
            TenantId = tenant.Id,
            Kind = kind,
            Parameters = parameters?.Serialize(),
            RequestedBy = audit.CurrentActor,
            QueuedAt = clock.GetUtcNow().UtcDateTime,
        };
        db.Operations.Add(operation);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync($"{Labels[kind]} angefordert", tenant, Describe(kind, parameters), cancellationToken: cancellationToken);
        queue.Enqueue(operation.Id);
        return operation;
    }

    private static string? Describe(OperationKind kind, OperationParameters? parameters) => kind switch
    {
        OperationKind.Update => $"Zielversion {parameters?.Version}",
        OperationKind.Lock => $"Grund: {parameters?.Reason}{(parameters?.StopInstance == true ? "; Instanz wird angehalten" : string.Empty)}",
        OperationKind.Delete => parameters?.FinalBackup == true ? "mit Endsicherung" : "ohne Endsicherung",
        _ => null,
    };
}
