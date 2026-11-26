using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Ergebnis eines Laufs des Änderungsabgleichs.</summary>
/// <param name="Active">Modul eingeschaltet, Schlüssel hinterlegt und Änderungsabgleich nicht aus.</param>
/// <param name="Checked">Verknüpfungen, die verglichen wurden.</param>
/// <param name="Error">Lexware Office war nicht erreichbar oder hat den Zugriff abgelehnt; der Lauf wurde abgebrochen.</param>
public sealed record LexwareChangeSyncRun(bool Active, bool Full, int Checked, int Failed, string? Error);

/// <summary>
/// Änderungsabgleich verknüpfter Kunden mit Lexware Office: Dreiwege-Abgleich je Feld gegen den zuletzt abgeglichenen
/// Stand (<see cref="LexwareContactSync"/>). Abgeglichen werden nur Kunden mit Verknüpfung und eingeschaltetem „Mit
/// Lexware Office abgleichen“, die weder archiviert noch anonymisiert sind. In Lexware Office wird dabei ausschließlich
/// der Kontakt geändert (<c>PUT /v1/contacts/{id}</c>), nie angelegt, archiviert oder gelöscht.
/// </summary>
public sealed class LexwareChangeSyncService(
    LexwareService lexware, IAuditLog audit, ISystemSettings system, IServiceScopeFactory scopes, ILogger<LexwareChangeSyncService> logger)
{
    /// <summary>Verknüpfungen, die ein Lauf ohne Kontaktliste höchstens einzeln abruft; darüber wird die Liste gelesen.</summary>
    private const int SingleLimit = 25;

    /// <summary>Fehler, die nicht an einem einzelnen Kontakt liegen: Der Lauf wird abgebrochen und später wiederholt.</summary>
    internal static bool Global(LexwareException exception) => exception.Code is "OFFLINE" or "TIMEOUT" or "UNAVAILABLE" or "RATE_LIMITED" or "UNAUTHORIZED"
        or "NO_API_ACCESS" or "KEY_UNREADABLE" or "NOT_CONFIGURED" or "INVALID_BASE_URL";

    public static bool Eligible(Contact contact) => contact.LexwareSync && contact.ArchivedAt is null && contact.AnonymizedAt is null && contact.DeletedAt is null;

    /// <summary>
    /// Einen verknüpften Kunden abgleichen. <paramref name="known"/> ist der Stand aus der Kontaktliste; geschrieben wird
    /// in Lexware Office aber immer erst nach frischem Lesen. <paramref name="choices"/> sind Entscheidungen des Nutzers
    /// zu Abweichungen (Feld auf <c>estrichOne</c> oder <c>lexware</c>).
    /// </summary>
    public async Task SyncAsync(LexwareSettingsRow row, LexwareContactLink link, Contact contact, LexwareContact? known, Guid? actor,
        IReadOnlyDictionary<string, string>? choices, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var connection = lexware.Connection(row);
        var baseline = LexwareContactSync.Baseline(link.SyncedFields);
        LexwareMergeResult? merge = null;
        IReadOnlyList<LexwareContactChange> Decide(LexwareContact current)
        {
            if (current.Archived)
            {
                merge = null;
                return [];
            }

            var local = LexwareContactSync.Local(contact, current.IsCompany);
            var theirs = LexwareContactSync.Remote(current, local, baseline);
            merge = LexwareContactSync.Merge(baseline, local, theirs, row.ChangeSync, choices);
            return merge.ToLexware.Select(c => new LexwareContactChange(c.Key, c.Value, theirs.GetValueOrDefault(c.Key))).ToList();
        }

        LexwareContact remote;
        var written = false;
        if (known is not null && Decide(known).Count == 0)
        {
            remote = known;
        }
        else
        {
            try
            {
                var update = await lexware.Client.UpdateContactAsync(connection, link.LexwareContactId, Decide, cancellationToken);
                remote = update.Contact;
                written = update.Written;
            }
            catch (LexwareException exception) when (exception.Code == "NOT_FOUND")
            {
                Settle(link, contact, LexwareContactLink.RemoteMissing);
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
        }

        if (remote.Archived || merge is null)
        {
            link.LexwareVersion = remote.Version;
            link.LexwareUpdatedAt = remote.UpdatedAt;
            Settle(link, contact, LexwareContactLink.RemoteArchived);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        LexwareContactSync.ApplyToCustomer(contact, merge.ToEstrichOne);
        link.SyncedFields = LexwareContactSync.Serialize(merge.Baseline);
        link.Deviations = LexwareContactSync.Serialize(merge.Deviations);
        link.DeviationCount = merge.Deviations.Count;
        link.LexwareVersion = remote.Version;
        link.LexwareUpdatedAt = remote.UpdatedAt;
        link.LexwareName = LexwareService.Cut(remote.Name, LexwareContactLink.MaxName) ?? link.LexwareName;
        link.LexwareNumber = LexwareService.Cut(remote.CustomerNumber, 50) ?? link.LexwareNumber;
        link.SyncedAt = lexware.Clock.GetUtcNow();
        Settle(link, contact, null);
        if (written)
        {
            Audit(contact, actor, "EstrichONE nach Lexware Office", merge.ToLexware.Keys);
        }

        if (merge.ToEstrichOne.Count > 0)
        {
            Audit(contact, actor, "Lexware Office nach EstrichONE", merge.ToEstrichOne.Keys);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (link.LocalVersion != contact.Version)
        {
            // Die Übernahme aus Lexware Office hat die Version des Kunden erhöht; das ist keine neue Änderung in EstrichONE.
            link.LocalVersion = contact.Version;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static void Settle(LexwareContactLink link, Contact contact, string? remoteState)
    {
        link.RemoteState = remoteState;
        link.LocalVersion = contact.Version;
        link.SyncError = null;
        link.SyncAttempts = 0;
        link.NextSyncAt = null;
        if (remoteState is not null)
        {
            link.Deviations = null;
            link.DeviationCount = 0;
        }
    }

    private void Audit(Contact contact, Guid? actor, string direction, IEnumerable<string> fields) =>
        audit.Record("lexware.contact_synced", actor, "contact", contact.Id.ToString(), new Dictionary<string, string?>
        {
            ["name"] = contact.Name, ["direction"] = direction, ["fields"] = string.Join(", ", fields.Select(LexwareSyncFields.Label)),
            ["auto"] = actor is null ? "true" : null,
        });

    /// <summary>
    /// Kunde mit seiner Verknüpfung laden und abgleichen (ein Abgleich zur selben Zeit je Server). Liefert <c>false</c>,
    /// wenn es nichts abzugleichen gibt (nicht verknüpft, Abgleich aus, archiviert).
    /// </summary>
    public async Task<bool> SyncCustomerAsync(Guid contactId, LexwareContact? known, Guid? actor, IReadOnlyDictionary<string, string>? choices,
        CancellationToken cancellationToken)
    {
        var gate = lexware.SyncGate;
        await gate.WaitAsync(cancellationToken);
        try
        {
            var db = lexware.Db;
            var row = await lexware.SettingsAsync(cancellationToken);
            if (row.ChangeSync == LexwareChangeSync.Off
                || await db.Set<LexwareContactLink>().SingleOrDefaultAsync(l => l.ContactId == contactId, cancellationToken) is not { } link
                || await db.Set<Contact>().SingleOrDefaultAsync(c => c.Id == contactId, cancellationToken) is not { } contact || !Eligible(contact))
            {
                return false;
            }

            await SyncAsync(row, link, contact, known, actor, choices, cancellationToken);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Ein Lauf des Änderungsabgleichs. Ohne <paramref name="full"/> nur Kunden, die in EstrichONE seit dem letzten
    /// Abgleich geändert wurden (einzeln gegen Lexware Office). Mit <paramref name="full"/> wird die Kontaktliste aus
    /// Lexware Office gelesen und jede Verknüpfung verglichen, deren Kontakt dort eine andere Version hat.
    /// </summary>
    public async Task<LexwareChangeSyncRun> RunAsync(bool full, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var row = await lexware.SettingsAsync(cancellationToken);
        if (!(await system.GetAsync(cancellationToken)).Modules.IsEnabled(AppModules.Lexware) || !row.Configured || row.ChangeSync == LexwareChangeSync.Off)
        {
            return new LexwareChangeSyncRun(false, full, 0, 0, null);
        }

        var now = lexware.Clock.GetUtcNow();
        var links = await db.Set<LexwareContactLink>().AsNoTracking()
            .Join(db.Set<Contact>().AsNoTracking(), l => l.ContactId, c => c.Id, (l, c) => new
            {
                l.ContactId, l.LexwareContactId, l.LexwareVersion, l.LocalVersion, l.NextSyncAt, l.RemoteState, HasBaseline = l.SyncedFields != null,
                Version = c.Version, Eligible = c.LexwareSync && c.ArchivedAt == null && c.AnonymizedAt == null,
            }).ToListAsync(cancellationToken);

        // Solange der Abgleich für einen Kunden ruht (Schalter aus, archiviert), gilt kein abgeglichener Stand mehr: Beim
        // Wiedereinschalten werden vorhandene Unterschiede als Abweichungen gezeigt und nicht still übernommen.
        var resting = links.Where(l => !l.Eligible && (l.HasBaseline || l.LocalVersion != null)).Select(l => l.ContactId).ToList();
        if (resting.Count > 0)
        {
            await ForgetAsync(db.Set<LexwareContactLink>().Where(l => resting.Contains(l.ContactId)), cancellationToken);
        }

        var due = links.Where(l => l.Eligible && (l.NextSyncAt is null || l.NextSyncAt <= now)).ToList();
        var changed = due.Where(l => l.LocalVersion != l.Version).ToList();
        full = full || changed.Count > SingleLimit;
        var work = new List<(Guid ContactId, LexwareContact? Known)>();
        try
        {
            if (!full)
            {
                work.AddRange(changed.Select(l => (l.ContactId, (LexwareContact?)null)));
            }
            else
            {
                var (contacts, _) = await lexware.ContactsAsync(row, refresh: false, cancellationToken);
                var byId = contacts.ToDictionary(c => c.Id, StringComparer.Ordinal);
                foreach (var l in due)
                {
                    var known = byId.GetValueOrDefault(l.LexwareContactId);
                    if (known is null || known.Version is null || known.Version != l.LexwareVersion || l.LocalVersion != l.Version || !l.HasBaseline
                        || l.RemoteState is not null)
                    {
                        work.Add((l.ContactId, known));
                    }
                }
            }

            var failed = 0;
            foreach (var (contactId, known) in work)
            {
                using var scope = scopes.CreateScope();
                var worker = scope.ServiceProvider.GetRequiredService<LexwareChangeSyncService>();
                try
                {
                    await worker.SyncCustomerAsync(contactId, known, null, null, cancellationToken);
                }
                catch (LexwareException exception) when (!Global(exception))
                {
                    failed++;
                    await FailAsync(contactId, exception.Message, cancellationToken);
                }
                catch (DbUpdateConcurrencyException)
                {
                    failed++; // Der Kunde wurde gerade bearbeitet; der nächste Lauf gleicht ihn ab.
                }
            }

            if (full)
            {
                await db.Set<LexwareSettingsRow>().ExecuteUpdateAsync(s => s.SetProperty(r => r.LastChangeSyncAt, now)
                    .SetProperty(r => r.LastChangeSyncError, (string?)null), cancellationToken);
            }

            return new LexwareChangeSyncRun(true, full, work.Count, failed, null);
        }
        catch (LexwareException exception)
        {
            logger.LogWarning("Lexware-Änderungsabgleich abgebrochen: {Code}", exception.Code);
            var message = exception.Message.Length > 2000 ? exception.Message[..2000] : exception.Message;
            await db.Set<LexwareSettingsRow>().ExecuteUpdateAsync(s => s.SetProperty(r => r.LastChangeSyncError, message), cancellationToken);
            return new LexwareChangeSyncRun(true, full, work.Count, 0, exception.Message);
        }
    }

    /// <summary>Abgeglichenen Stand und Abweichungen verwerfen (der nächste Abgleich ist wieder ein Erstabgleich).</summary>
    public static Task ForgetAsync(IQueryable<LexwareContactLink> links, CancellationToken cancellationToken) =>
        links.ExecuteUpdateAsync(s => s.SetProperty(l => l.SyncedFields, (string?)null).SetProperty(l => l.LocalVersion, (long?)null)
            .SetProperty(l => l.LexwareVersion, (int?)null).SetProperty(l => l.Deviations, (string?)null).SetProperty(l => l.DeviationCount, 0)
            .SetProperty(l => l.SyncError, (string?)null).SetProperty(l => l.SyncAttempts, 0).SetProperty(l => l.NextSyncAt, (DateTimeOffset?)null),
            cancellationToken);

    /// <summary>Fehler an einem einzelnen Kontakt merken; der nächste Versuch folgt mit wachsendem Abstand (2, 4, 8 … höchstens 60 Minuten).</summary>
    private async Task FailAsync(Guid contactId, string message, CancellationToken cancellationToken)
    {
        var db = lexware.Db;
        var attempts = await db.Set<LexwareContactLink>().Where(l => l.ContactId == contactId).Select(l => l.SyncAttempts).FirstOrDefaultAsync(cancellationToken) + 1;
        var next = lexware.Clock.GetUtcNow() + TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, Math.Min(attempts, 6))));
        var text = message.Length > 2000 ? message[..2000] : message;
        await db.Set<LexwareContactLink>().Where(l => l.ContactId == contactId).ExecuteUpdateAsync(s => s.SetProperty(l => l.SyncError, text)
            .SetProperty(l => l.SyncAttempts, attempts).SetProperty(l => l.NextSyncAt, next), cancellationToken);
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Lexware-Änderungsabgleich für Kunde {ContactId} fehlgeschlagen (Versuch {Attempt})", contactId, attempts);
        }
    }
}

/// <summary>
/// Hintergrunddienst des Änderungsabgleichs: sieht in kurzem Abstand nach in EstrichONE geänderten, verknüpften Kunden und
/// gleicht in größerem Abstand alle Verknüpfungen gegen die Kontaktliste aus Lexware Office ab. Das Speichern eines Kunden
/// wartet nie auf Lexware Office; ist es nicht erreichbar, pausiert der Dienst und holt den Abgleich nach.
/// </summary>
internal sealed class LexwareChangeSyncJob(IServiceScopeFactory scopes, TimeProvider clock, IOptions<LexwareOptions> options, ILogger<LexwareChangeSyncJob> logger)
    : BackgroundService
{
    private DateTimeOffset _lastFull = DateTimeOffset.MinValue;
    private DateTimeOffset _pausedUntil = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.BackgroundChangeSync)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(20), clock, stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Lexware-Änderungsabgleich fehlgeschlagen");
            }

            await Task.Delay(options.Value.ChangePollInterval, clock, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task<LexwareChangeSyncRun?> RunOnceAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        if (now < _pausedUntil)
        {
            return null;
        }

        var full = now - _lastFull >= options.Value.ChangeSyncInterval;
        using var scope = scopes.CreateScope();
        var run = await scope.ServiceProvider.GetRequiredService<LexwareChangeSyncService>().RunAsync(full, cancellationToken);
        if (run.Error is not null)
        {
            _pausedUntil = now + options.Value.ChangeSyncPause;
        }
        else if (run.Full && run.Active)
        {
            _lastFull = now;
        }

        return run;
    }
}
