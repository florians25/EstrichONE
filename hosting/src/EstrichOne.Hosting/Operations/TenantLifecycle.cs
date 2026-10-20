using System.Globalization;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Docker;
using EstrichOne.Hosting.Domain;
using EstrichOne.Hosting.Infrastructure;
using EstrichOne.Hosting.Instances;
using EstrichOne.Hosting.Proxy;
using EstrichOne.Hosting.Status;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Operations;

/// <summary>
/// Führt die Lebenszyklus-Vorgänge einer Instanz aus: bereitstellen, starten, stoppen, neu starten, aktualisieren,
/// sperren/entsperren, sichern, löschen, neu konfigurieren. Jeder Schritt wird protokolliert.
/// </summary>
public sealed class TenantLifecycle(
    IDockerHost docker,
    InstanceLayout layout,
    InstanceFiles files,
    ProxyManager proxy,
    StatusService status,
    HostingDbContext db,
    IOptions<HostingOptions> options,
    TimeProvider clock)
{
    private static readonly TimeSpan PullTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BackupTimeout = TimeSpan.FromHours(3);

    private HostingOptions Settings => options.Value;

    private TimeSpan UpTimeout => TimeSpan.FromSeconds(Settings.ComposeWaitSeconds + 120);

    public async Task ExecuteAsync(Operation operation, Tenant tenant, OperationLog log, CancellationToken cancellationToken)
    {
        var plan = tenant.Plan ?? await db.Plans.FindAsync([tenant.PlanId], cancellationToken)
            ?? throw new InvalidOperationException("Der Tarif des Mandanten fehlt.");
        var parameters = OperationParameters.Parse(operation.Parameters);

        switch (operation.Kind)
        {
            case OperationKind.Provision:
                await ProvisionAsync(tenant, plan, log, cancellationToken);
                break;
            case OperationKind.Start:
                await PrepareAsync(tenant, plan, log, cancellationToken);
                await UpAsync(tenant, log, cancellationToken);
                if (tenant.State == TenantState.Locked)
                {
                    // Gesperrt bleibt gesperrt; nach dem Entsperren läuft die Instanz.
                    tenant.StateBeforeLock = TenantState.Running;
                    await SaveAsync(tenant, cancellationToken);
                }
                else
                {
                    await SetStateAsync(tenant, TenantState.Running, cancellationToken);
                }

                break;
            case OperationKind.Stop:
                await ComposeAsync(tenant, ["stop"], StopTimeout, log, cancellationToken);
                if (tenant.State == TenantState.Locked)
                {
                    tenant.StateBeforeLock = TenantState.Stopped;
                    await SaveAsync(tenant, cancellationToken);
                }
                else
                {
                    await SetStateAsync(tenant, TenantState.Stopped, cancellationToken);
                }

                break;
            case OperationKind.Restart:
                await ComposeAsync(tenant, ["restart"], StopTimeout, log, cancellationToken);
                await UpAsync(tenant, log, cancellationToken);
                if (tenant.State != TenantState.Locked)
                {
                    await SetStateAsync(tenant, TenantState.Running, cancellationToken);
                }

                break;
            case OperationKind.Update:
                await UpdateAsync(tenant, plan, parameters.Version, log, cancellationToken);
                break;
            case OperationKind.Lock:
                await LockAsync(tenant, parameters, log, cancellationToken);
                break;
            case OperationKind.Unlock:
                await UnlockAsync(tenant, plan, log, cancellationToken);
                break;
            case OperationKind.Backup:
                await BackupAsync(tenant, log, cancellationToken);
                break;
            case OperationKind.Delete:
                await DeleteAsync(tenant, parameters.FinalBackup, log, cancellationToken);
                return;
            case OperationKind.Reconfigure:
                await PrepareAsync(tenant, plan, log, cancellationToken);
                if (tenant.State is TenantState.Running or TenantState.Failed
                    || (tenant.State == TenantState.Locked && tenant.StateBeforeLock == TenantState.Running))
                {
                    await UpAsync(tenant, log, cancellationToken);
                    if (tenant.State == TenantState.Failed)
                    {
                        await SetStateAsync(tenant, TenantState.Running, cancellationToken);
                    }
                }

                break;
            default:
                throw new InvalidOperationException($"Unbekannter Vorgang {operation.Kind}.");
        }

        tenant.LastError = null;
        await SaveAsync(tenant, cancellationToken);
        await ApplyProxyAsync(log, cancellationToken);
        await status.CollectAsync(tenant, plan, forceUsage: true, cancellationToken);
    }

    private async Task ProvisionAsync(Tenant tenant, Plan plan, OperationLog log, CancellationToken cancellationToken)
    {
        log.Write($"Bereitstellung von „{tenant.CompanyName}“ ({tenant.Slug}) mit Version {tenant.Version}.");
        await SetStateAsync(tenant, TenantState.Provisioning, cancellationToken);
        await PrepareAsync(tenant, plan, log, cancellationToken);
        log.Write("Images laden …");
        await ComposeAsync(tenant, ["pull"], PullTimeout, log, cancellationToken);
        await UpAsync(tenant, log, cancellationToken);
        await SetStateAsync(tenant, TenantState.Running, cancellationToken);
        log.Write($"Instanz läuft. Öffentliche Adresse: {InstanceFiles.PublicUrl(tenant.Domain, Settings)}");
        var code = await status.ReadSetupCodeAsync(tenant.Slug, cancellationToken);
        if (code is not null)
        {
            log.Write($"Einrichtungscode für den ersten Administrator: {code}");
        }
    }

    /// <summary>Dateien schreiben (Vorlagen, Hosting-Ergänzung, .env), Datenverzeichnisse und Edge-Netz vorbereiten.</summary>
    private async Task PrepareAsync(Tenant tenant, Plan plan, OperationLog log, CancellationToken cancellationToken)
    {
        var created = await files.PrepareAsync(tenant, plan, cancellationToken);
        log.Write(created
            ? $"Verzeichnis {layout.Directory(tenant.Slug)} mit neuer .env (zufällige Passwörter) angelegt."
            : $"Konfiguration in {layout.Directory(tenant.Slug)} aktualisiert (Passwörter unverändert).");
        await proxy.AttachAsync(tenant.Slug, cancellationToken);
        log.Write($"Netz {InstanceLayout.EdgeNetwork(tenant.Slug)} bereit, Proxy verbunden.");
    }

    private Task UpAsync(Tenant tenant, OperationLog log, CancellationToken cancellationToken)
    {
        log.Write("Dienste starten und auf Bereitschaft warten …");
        return ComposeAsync(tenant,
            ["up", "-d", "--wait", "--wait-timeout", Settings.ComposeWaitSeconds.ToString(CultureInfo.InvariantCulture), "--remove-orphans"],
            UpTimeout, log, cancellationToken);
    }

    private async Task UpdateAsync(Tenant tenant, Plan plan, string? version, OperationLog log, CancellationToken cancellationToken)
    {
        if (Validation.CheckVersion(version) is { } error)
        {
            throw new InvalidOperationException(error);
        }

        var previous = tenant.Version;
        log.Write($"Aktualisierung {previous} → {version}.");
        tenant.Version = version!;
        try
        {
            await PrepareAsync(tenant, plan, log, cancellationToken);
            log.Write("Images laden …");
            await ComposeAsync(tenant, ["pull"], PullTimeout, log, cancellationToken);
        }
        catch
        {
            // Images nicht ladbar: Version in der .env zurücksetzen, die Instanz läuft unverändert weiter.
            tenant.Version = previous;
            await files.UpdateEnvAsync(tenant.Slug, InstanceFiles.EnvValuesFor(tenant, Settings), cancellationToken);
            log.Write($"Version {version} nicht verfügbar – {previous} bleibt aktiv.");
            throw;
        }

        var wasRunning = tenant.State is TenantState.Running or TenantState.Failed
            || (tenant.State == TenantState.Locked && tenant.StateBeforeLock == TenantState.Running);
        await SaveAsync(tenant, cancellationToken);
        if (wasRunning)
        {
            await UpAsync(tenant, log, cancellationToken);
            if (tenant.State == TenantState.Failed)
            {
                await SetStateAsync(tenant, TenantState.Running, cancellationToken);
            }
        }
        else
        {
            log.Write("Instanz ist angehalten – die neue Version wird beim nächsten Start aktiv.");
        }
    }

    private async Task LockAsync(Tenant tenant, OperationParameters parameters, OperationLog log, CancellationToken cancellationToken)
    {
        if (tenant.State != TenantState.Locked)
        {
            tenant.StateBeforeLock = tenant.State == TenantState.Failed ? TenantState.Running : tenant.State;
        }

        tenant.LockReason = parameters.Reason;
        tenant.LockedAt = clock.GetUtcNow().UtcDateTime;
        await SetStateAsync(tenant, TenantState.Locked, cancellationToken);
        log.Write($"Gesperrt: {parameters.Reason}. Der Proxy zeigt die Wartungsseite.");
        if (parameters.StopInstance)
        {
            await ComposeAsync(tenant, ["stop"], StopTimeout, log, cancellationToken);
            tenant.StateBeforeLock = TenantState.Stopped;
            log.Write("Instanz zusätzlich angehalten (Sicherungen ruhen ebenfalls).");
        }
        else if (!Settings.ManagesProxy)
        {
            // Externer Proxy: Wartungsseite nicht möglich – Weboberfläche anhalten, Datenbank und Sicherung laufen weiter.
            await ComposeAsync(tenant, ["stop", "caddy"], StopTimeout, log, cancellationToken);
            log.Write("Externer Proxy: Weboberfläche (caddy) angehalten.");
        }
    }

    private async Task UnlockAsync(Tenant tenant, Plan plan, OperationLog log, CancellationToken cancellationToken)
    {
        var target = tenant.StateBeforeLock ?? TenantState.Running;
        if (target == TenantState.Running)
        {
            await PrepareAsync(tenant, plan, log, cancellationToken);
            await UpAsync(tenant, log, cancellationToken);
        }

        tenant.LockReason = null;
        tenant.LockedAt = null;
        tenant.StateBeforeLock = null;
        await SetStateAsync(tenant, target, cancellationToken);
        log.Write("Sperre aufgehoben.");
    }

    private Task BackupAsync(Tenant tenant, OperationLog log, CancellationToken cancellationToken)
    {
        log.Write("Sicherung jetzt ausführen (restic: Datenbank-Dump und Dateien) …");
        return ComposeAsync(tenant, ["exec", "-T", "backup", "estrichone-backup", "run"], BackupTimeout, log, cancellationToken);
    }

    private async Task DeleteAsync(Tenant tenant, bool finalBackup, OperationLog log, CancellationToken cancellationToken)
    {
        await SetStateAsync(tenant, TenantState.Deleting, cancellationToken);
        await ApplyProxyAsync(log, cancellationToken);
        if (finalBackup && Directory.Exists(layout.Directory(tenant.Slug)))
        {
            log.Write("Endsicherung: Datenbank und Dateien sichern …");
            var containers = await docker.ListContainersAsync(InstanceLayout.ProjectName(tenant.Slug), cancellationToken);
            if (!containers.Any(c => c.Service == "backup" && c.State == "running"))
            {
                await UpAsync(tenant, log, cancellationToken);
            }

            await BackupAsync(tenant, log, cancellationToken);
            await ComposeAsync(tenant, ["stop"], StopTimeout, log, cancellationToken);
            var archive = files.Archive(tenant.Slug, clock.GetUtcNow().UtcDateTime);
            log.Write($"Endsicherung mit Schlüssel (instanz.env) archiviert unter {archive}.");
        }

        if (Directory.Exists(layout.Directory(tenant.Slug)))
        {
            log.Write("Container, Netze und Volumes entfernen …");
            await ComposeAsync(tenant, ["down", "--volumes", "--remove-orphans", "--timeout", "30"], StopTimeout, log, cancellationToken);
        }

        await proxy.DetachAsync(tenant.Slug, cancellationToken);
        files.DeleteDirectory(tenant.Slug);
        log.Write("Instanzverzeichnis gelöscht.");
        status.Forget(tenant.Slug);

        tenant.State = TenantState.Deleted;
        tenant.DeletedAt = clock.GetUtcNow().UtcDateTime;
        // Kürzel und Domain wieder freigeben; das Ereignisprotokoll behält den Mandanten.
        tenant.Slug = $"geloescht-{tenant.Id:N}"[..32];
        tenant.Domain = $"{tenant.Id:N}.geloescht.invalid";
        await SaveAsync(tenant, cancellationToken);
        await ApplyProxyAsync(log, cancellationToken);
        log.Write("Mandant gelöscht.");
    }

    private async Task ComposeAsync(Tenant tenant, IReadOnlyList<string> arguments, TimeSpan timeout, OperationLog log, CancellationToken cancellationToken)
    {
        log.Write("$ docker compose " + string.Join(' ', arguments));
        var result = await docker.ComposeAsync(layout.Project(tenant.Slug), arguments, timeout, line => log.Write("  " + line), cancellationToken);
        if (!result.Success)
        {
            throw new DockerException($"docker compose {arguments[0]} fehlgeschlagen: {result.Summary}");
        }
    }

    private async Task ApplyProxyAsync(OperationLog log, CancellationToken cancellationToken)
    {
        if (!Settings.ManagesProxy)
        {
            return;
        }

        await proxy.ApplyAsync(cancellationToken);
        log.Write("Reverse Proxy neu geladen.");
    }

    private async Task SetStateAsync(Tenant tenant, TenantState state, CancellationToken cancellationToken)
    {
        tenant.State = state;
        await SaveAsync(tenant, cancellationToken);
    }

    private async Task SaveAsync(Tenant tenant, CancellationToken cancellationToken)
    {
        tenant.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
    }
}
