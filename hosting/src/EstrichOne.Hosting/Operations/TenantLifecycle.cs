using System.Globalization;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Docker;
using EstrichOne.Hosting.Domain;
using EstrichOne.Hosting.Infrastructure;
using EstrichOne.Hosting.Instances;
using EstrichOne.Hosting.Proxy;
using EstrichOne.Hosting.Recovery;
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
    TimeProvider clock,
    IInstanceProbe probe,
    AccessLetters letters,
    ConsoleSettings consoleSettings,
    CertificateService certificates)
{
    /// <summary>Dienste, die für das Zurückspielen einer Sicherung angehalten werden (Datenbank läuft weiter).</summary>
    private static readonly string[] RestoreStopServices = ["api", "caddy", "backup"];

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
                await UpdateAsync(tenant, plan, parameters.Version, parameters.BackupFirst, log, cancellationToken);
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
            case OperationKind.Restore:
                await RestoreAsync(tenant, plan, parameters.Snapshot, parameters.BackupFirst, log, cancellationToken);
                break;
            case OperationKind.Rollback:
                await RollbackAsync(tenant, plan, log, cancellationToken);
                break;
            case OperationKind.InitialSetup:
                await InitialSetupAsync(tenant, log, cancellationToken);
                break;
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
        // Zertifikat und DNS im Hintergrund prüfen (der TLS-Handshake darf den Vorgang nicht aufhalten).
        var domain = tenant.Domain;
        _ = Task.Run(() => certificates.CheckAsync(domain, CancellationToken.None), CancellationToken.None);
    }

    private async Task ProvisionAsync(Tenant tenant, Plan plan, OperationLog log, CancellationToken cancellationToken)
    {
        log.Write($"Bereitstellung von „{tenant.CompanyName}“ ({tenant.Slug}) mit Version {tenant.Version}.");
        await SetStateAsync(tenant, TenantState.Provisioning, cancellationToken);
        await PrepareAsync(tenant, plan, log, cancellationToken);
        log.Write("Images laden …");
        await ComposeAsync(tenant, ["pull", "--ignore-pull-failures"], PullTimeout, log, cancellationToken);
        await UpAsync(tenant, log, cancellationToken);
        await SetStateAsync(tenant, TenantState.Running, cancellationToken);
        log.Write($"Instanz läuft. Öffentliche Adresse: {InstanceFiles.PublicUrl(tenant.Domain, Settings)}");
        if (!string.IsNullOrEmpty(tenant.FirstAdminUserName))
        {
            await InitialSetupAsync(tenant, log, cancellationToken);
            return;
        }

        var code = await status.ReadSetupCodeAsync(tenant.Slug, cancellationToken);
        if (code is not null)
        {
            log.Write($"Einrichtungscode für den ersten Administrator: {code}");
        }
    }

    /// <summary>
    /// Ersteinrichtung der Suite über deren API: Firma und erste Admin-Person mit Startpasswort anlegen (Einrichtungscode
    /// aus dem API-Protokoll, wie ihn sonst der Kunde eingibt). Ergebnis ist der Zugangsbrief.
    /// </summary>
    private async Task InitialSetupAsync(Tenant tenant, OperationLog log, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tenant.FirstAdminUserName) || string.IsNullOrEmpty(tenant.FirstAdminName))
        {
            throw new InvalidOperationException("Für diesen Mandanten ist keine erste Admin-Person hinterlegt.");
        }

        log.Write($"Ersteinrichtung: erste Admin-Person „{tenant.FirstAdminName}“ ({tenant.FirstAdminUserName}) anlegen …");
        string? code = null;
        for (var attempt = 0; attempt < 30 && code is null; attempt++)
        {
            var state = await probe.ProbeAsync(tenant.Slug, cancellationToken);
            if (state.SetupRequired == false)
            {
                log.Write("Die Instanz ist bereits eingerichtet – nichts zu tun.");
                return;
            }

            code = await status.ReadSetupCodeAsync(tenant.Slug, cancellationToken);
            if (code is null)
            {
                await Task.Delay(Settings.IsFake ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromSeconds(2), clock, cancellationToken);
            }
        }

        if (code is null)
        {
            throw new InvalidOperationException("Kein Einrichtungscode im Protokoll der Instanz gefunden.");
        }

        var password = AccessLetters.GeneratePassword();
        var result = await probe.CompleteSetupAsync(tenant.Slug, new SuiteSetupRequest(code, tenant.CompanyName,
            new SuiteSetupAdministrator(tenant.FirstAdminName, tenant.FirstAdminUserName, tenant.FirstAdminEmail, password)), cancellationToken);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Ersteinrichtung der Instanz fehlgeschlagen: {result.Error}");
        }

        await letters.SaveAsync(tenant.Slug, new AccessLetter(tenant.CompanyName, InstanceFiles.PublicUrl(tenant.Domain, Settings),
            tenant.FirstAdminName, tenant.FirstAdminUserName, tenant.FirstAdminEmail, password, clock.GetUtcNow().UtcDateTime), cancellationToken);
        status.Forget(tenant.Slug);
        log.Write("Erste Admin-Person angelegt. Der Zugangsbrief liegt auf der Detailseite bereit (Startpasswort nur dort).");
    }

    /// <summary>Dateien schreiben (Vorlagen, Hosting-Ergänzung, .env), Datenverzeichnisse und Edge-Netz vorbereiten.</summary>
    private async Task PrepareAsync(Tenant tenant, Plan plan, OperationLog log, CancellationToken cancellationToken)
    {
        var created = await files.PrepareAsync(tenant, plan, cancellationToken);
        log.Write(created
            ? $"Verzeichnis {layout.Directory(tenant.Slug)} mit neuer .env (zufällige Passwörter) angelegt."
            : $"Konfiguration in {layout.Directory(tenant.Slug)} aktualisiert (Passwörter unverändert).");
        // Zweites Sicherungsziel (Einstellungen → Sicherung): nur verwalten, wenn in der Konsole festgelegt.
        if (await consoleSettings.GetAsync(ConsoleSettings.SecondaryRepositoryKey, cancellationToken) is { } secondary)
        {
            var value = string.IsNullOrWhiteSpace(secondary) ? string.Empty : $"{secondary.TrimEnd('/')}/{tenant.Slug}";
            await files.UpdateEnvAsync(tenant.Slug, new Dictionary<string, string> { ["BACKUP_REPOSITORY_2"] = value }, cancellationToken);
            if (value.Length > 0)
            {
                log.Write("Zweites Sicherungsziel eingetragen.");
            }
        }
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

    private async Task UpdateAsync(Tenant tenant, Plan plan, string? version, bool backupFirst, OperationLog log, CancellationToken cancellationToken)
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
            await ComposeAsync(tenant, ["pull", "--policy", "missing"], PullTimeout, log, cancellationToken);
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
        if (backupFirst && wasRunning)
        {
            try
            {
                log.Write("Vorab-Sicherung mit der bisherigen Version …");
                await BackupAsync(tenant, log, cancellationToken);
                tenant.RollbackSnapshot = SnapshotReader.Read(StatusDirectory(tenant)) is [var newest, ..] ? newest.Id : null;
                log.Write(tenant.RollbackSnapshot is null
                    ? "Vorab-Sicherung erstellt (Snapshot-Liste nicht lesbar – Zurücksetzen nur ohne Datenstand)."
                    : $"Vorab-Sicherung {tenant.RollbackSnapshot[..8]} – Grundlage für das Zurücksetzen.");
            }
            catch
            {
                tenant.Version = previous;
                await files.UpdateEnvAsync(tenant.Slug, InstanceFiles.EnvValuesFor(tenant, Settings), cancellationToken);
                log.Write($"Vorab-Sicherung fehlgeschlagen – Aktualisierung abgebrochen, {previous} bleibt aktiv.");
                throw;
            }
        }

        if (!backupFirst || !wasRunning)
        {
            tenant.RollbackSnapshot = null;
        }

        tenant.PreviousVersion = previous;
        await SaveAsync(tenant, cancellationToken);
        if (wasRunning)
        {
            try
            {
                await UpAsync(tenant, log, cancellationToken);
            }
            catch (DockerException exception)
            {
                // Neue Version startet nicht: automatisch zurück auf den bisherigen Stand (Version + Daten der Vorab-Sicherung).
                log.Write($"Start mit {version} fehlgeschlagen: {exception.Message}");
                log.Write("Automatische Rücknahme der Aktualisierung …");
                await RollbackAsync(tenant, plan, log, cancellationToken);
                throw new RolledBackException(
                    $"Aktualisierung auf {version} fehlgeschlagen ({exception.Message}). Automatisch auf {previous} zurückgesetzt – die Instanz läuft wieder.");
            }

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

    private string StatusDirectory(Tenant tenant) => Path.Combine(layout.DataDirectory(tenant.Slug), "backup-status");

    /// <summary>Letzte Aktualisierung zurücknehmen: vorherige Version, Daten der Vorab-Sicherung (falls vorhanden), starten.</summary>
    private async Task RollbackAsync(Tenant tenant, Plan plan, OperationLog log, CancellationToken cancellationToken)
    {
        var target = tenant.PreviousVersion ?? throw new InvalidOperationException("Keine vorherige Version zum Zurücksetzen bekannt.");
        var snapshot = tenant.RollbackSnapshot;
        log.Write($"Zurücksetzen {tenant.Version} → {target}{(snapshot is null ? " (ohne Datenstand – keine Vorab-Sicherung)" : $" mit Datenstand {snapshot[..8]}")}.");
        tenant.Version = target;
        await PrepareAsync(tenant, plan, log, cancellationToken);
        await ComposeAsync(tenant, ["pull", "--policy", "missing"], PullTimeout, log, cancellationToken);
        if (snapshot is not null)
        {
            await RestoreSnapshotAsync(tenant, snapshot, log, cancellationToken);
        }

        await UpAsync(tenant, log, cancellationToken);
        tenant.PreviousVersion = null;
        tenant.RollbackSnapshot = null;
        if (tenant.State != TenantState.Locked)
        {
            await SetStateAsync(tenant, TenantState.Running, cancellationToken);
        }

        await SaveAsync(tenant, cancellationToken);
        log.Write($"Version {target} läuft wieder.");
    }

    private async Task RestoreAsync(Tenant tenant, Plan plan, string? snapshot, bool backupFirst, OperationLog log, CancellationToken cancellationToken)
    {
        var available = SnapshotReader.Read(StatusDirectory(tenant));
        var chosen = snapshot is null or "latest" ? (available.Count > 0 ? available[0] : null) : available.FirstOrDefault(s => s.Id == snapshot || s.ShortId == snapshot);
        if (chosen is null)
        {
            throw new InvalidOperationException(snapshot is null or "latest" ? "Für diese Instanz gibt es noch keine Sicherung." : $"Sicherung {snapshot} nicht gefunden.");
        }

        log.Write($"Wiederherstellung des Stands vom {chosen.Time:yyyy-MM-dd HH:mm} UTC (Snapshot {chosen.ShortId}, {chosen.Repository}).");
        var running = (await docker.ListContainersAsync(InstanceLayout.ProjectName(tenant.Slug), cancellationToken))
            .Any(c => c.Service == "backup" && c.State == "running");
        if (backupFirst && running)
        {
            log.Write("Sicherheitskopie des aktuellen Stands vor dem Zurückspielen …");
            await BackupAsync(tenant, log, cancellationToken);
        }

        await PrepareAsync(tenant, plan, log, cancellationToken);
        await RestoreSnapshotAsync(tenant, chosen.Id, log, cancellationToken);
        await UpAsync(tenant, log, cancellationToken);
        if (tenant.State is not TenantState.Locked)
        {
            await SetStateAsync(tenant, TenantState.Running, cancellationToken);
        }

        log.Write("Wiederherstellung abgeschlossen – die Suite aktualisiert beim Start das Datenbankschema.");
    }

    /// <summary>Dienst „restore“ der Suite: Datenbank in neue Datenbank einspielen und tauschen, Dateien zurückspielen.</summary>
    private async Task RestoreSnapshotAsync(Tenant tenant, string snapshot, OperationLog log, CancellationToken cancellationToken)
    {
        if (!SnapshotReader.IsSnapshotId(snapshot))
        {
            throw new InvalidOperationException("Ungültige Snapshot-Kennung.");
        }

        log.Write("Dienste anhalten (Datenbank läuft weiter) …");
        await ComposeAsync(tenant, ["stop", .. RestoreStopServices], StopTimeout, log, cancellationToken);
        await ComposeAsync(tenant, ["up", "-d", "--wait", "postgres"], UpTimeout, log, cancellationToken);
        log.Write($"Sicherung {snapshot[..8]} zurückspielen …");
        await ComposeAsync(tenant, ["--profile", "restore", "run", "--rm", "-T", "restore", snapshot], BackupTimeout, log, cancellationToken);
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
            throw new DockerException($"docker compose {arguments.First(a => !a.StartsWith('-') && a != "restore")} fehlgeschlagen: {result.Summary}");
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

/// <summary>Vorgang fehlgeschlagen, Instanz aber automatisch auf den vorherigen Stand zurückgesetzt (läuft wieder).</summary>
public sealed class RolledBackException(string message) : Exception(message);
