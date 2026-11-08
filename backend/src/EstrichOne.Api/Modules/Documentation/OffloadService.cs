using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EstrichOne.Api.Infrastructure.Peers;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Backup.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Documentation;

/// <summary>Storage nicht erreichbar oder Objekt nicht lesbar – nichts ist verloren, das Original liegt weiterhin dort.</summary>
public sealed class OffloadUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record OffloadServerStatus(string Role, string Name, string Version, DateTimeOffset Now, long? DiskFreeBytes, long? DiskTotalBytes,
    OffloadServerStatistics? Offload);

public sealed record OffloadServerStatistics(int Objects, long Bytes, int TrashObjects, long TrashBytes, int TrashDays,
    OffloadServerVerification? LastVerification);

public sealed record OffloadServerVerification(DateTimeOffset At, bool Ok, int ObjectsChecked, long BytesChecked, IReadOnlyList<string> Errors);

internal sealed record OffloadObjectResponse(string Id, string Sha256, long Size, DateTimeOffset StoredAt, bool Deleted, DateTimeOffset? DeletedAt);

/// <summary>Fortschritt des laufenden Auslagerns bzw. Rückholens (für die Verwaltungsseite).</summary>
/// <param name="Kind"><c>offload</c> oder <c>recall</c>.</param>
public sealed record OffloadJobStatus(bool Running, string? Kind, int Total, int Processed, int Failed, long Bytes, DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt, string? Message);

/// <summary>Zustand der laufenden Aufgabe (Singleton) und Sperre gegen gleichzeitiges Auslagern/Rückholen.</summary>
public sealed class OffloadProgress : IDisposable
{
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private OffloadJobStatus _status = new(false, null, 0, 0, 0, 0, null, null, null);

    public SemaphoreSlim Gate { get; } = new(1, 1);

    /// <summary>Angeforderte manuelle Läufe (Bauakte bzw. null = nach Regeln).</summary>
    public ConcurrentQueue<OffloadRequest> Requests { get; } = new();

    public OffloadJobStatus Status
    {
        get
        {
            lock (_lock)
            {
                return _status;
            }
        }
    }

    public void Update(Func<OffloadJobStatus, OffloadJobStatus> change)
    {
        lock (_lock)
        {
            _status = change(_status);
        }
    }

    public void Wake()
    {
        try
        {
            if (_signal.CurrentCount == 0)
            {
                _signal.Release();
            }
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);

    public void Dispose()
    {
        _signal.Dispose();
        Gate.Dispose();
    }
}

/// <param name="Recall">Rückholen statt Auslagern.</param>
/// <param name="StorageKeys">Rückholen: diese Ablagen (leer + <paramref name="BauakteId"/> null = alle).</param>
/// <param name="IgnoreRules">„Jetzt auslagern“ für eine Bauakte: Alter und Abschluss egal.</param>
public sealed record OffloadRequest(bool Recall, Guid? BauakteId, IReadOnlyList<string>? StorageKeys, bool IgnoreRules, Guid? UserId);

/// <summary>
/// Speicherstufe Storage: Datei-Originale verschlüsselt auf den gekoppelten Storage auslagern, bei Bedarf (mit Zwischenspeicher)
/// wieder lesen, zurückholen und abgleichen. Reihenfolge beim Auslagern (nichts geht verloren):
/// 1. Original lokal gegen seine SHA-256 prüfen, verschlüsseln, übertragen (Storage prüft die Prüfsumme des Objekts).
/// 2. Objekt vom Storage zurücklesen, entschlüsseln und erneut gegen die SHA-256 des Originals prüfen.
/// 3. Sicherungskopie anfordern (eigener Snapshot im Backup) und deren Erfolg abwarten.
/// 4. Erst dann lokal löschen (Vorschaustufen bleiben lokal).
/// </summary>
internal sealed partial class OffloadService(
    EstrichOneDbContext db, LocalFileStorage storage, IDataProtectionProvider protection, TimeProvider clock, IOffloadBackup backup,
    OffloadProgress progress, OffloadCache cache, ILogger<OffloadService> logger)
{
    public const string ProtectorPurpose = "EstrichOne.Offload";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>So viel Platz über der Schwelle wird freigeräumt, damit nicht bei jeder neuen Datei erneut ausgelagert wird.</summary>
    public const int HysteresisPercent = 5;

    /// <summary>Höchstens so viele Originale je Durchlauf.</summary>
    public const int BatchSize = 200;

    private IDataProtector Protector => protection.CreateProtector(ProtectorPurpose);

    public async Task<OffloadConnection> ConnectionAsync(CancellationToken cancellationToken)
    {
        if (await db.Set<OffloadConnection>().SingleOrDefaultAsync(cancellationToken) is { } existing)
        {
            return existing;
        }

        var created = OffloadConnection.Create(Protector.Protect(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))));
        db.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        return created;
    }

    public Task<OffloadConnection?> FindConnectionAsync(CancellationToken cancellationToken) =>
        db.Set<OffloadConnection>().AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    private byte[] MasterKey(OffloadConnection connection) => Convert.FromBase64String(Protector.Unprotect(connection.ProtectedMasterKey));

    public async Task<OffloadConnection> PairAsync(string address, string code, string clientName, Guid userId, CancellationToken cancellationToken)
    {
        var uri = PeerClient.NormalizeAddress(address);
        var pairing = await PeerClient.PairAsync(uri, code, clientName, "offload", cancellationToken);
        var connection = await ConnectionAsync(cancellationToken);
        if (connection.Paired && await db.Set<OffloadedBlob>().AnyAsync(b => b.State != OffloadState.Recalled, cancellationToken)
            && connection.CaFingerprint != pairing.CaFingerprint)
        {
            throw new PeerException("OFFLOAD_STILL_IN_USE",
                "Auf dem bisher gekoppelten Storage liegen noch ausgelagerte Dateien. Bitte erst alles zurückholen, dann einen anderen Storage koppeln.");
        }

        connection.Pair(uri.ToString().TrimEnd('/'), pairing.Name, pairing.Version, pairing.CaCertificatePem, pairing.CaFingerprint, pairing.ClientId,
            Protector.Protect(pairing.Token), userId, clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return connection;
    }

    /// <summary>Entkoppeln nur, wenn nichts mehr ausgelagert ist (sonst wären die Originale nicht mehr erreichbar).</summary>
    public async Task<bool> UnpairAsync(CancellationToken cancellationToken)
    {
        var connection = await ConnectionAsync(cancellationToken);
        if (await db.Set<OffloadedBlob>().AnyAsync(cancellationToken))
        {
            return false;
        }

        if (connection.Paired)
        {
            try
            {
                using var http = Client(connection, TimeSpan.FromSeconds(10));
                using var _ = await http.DeleteAsync(new Uri("/api/v1/pairing", UriKind.Relative), cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                // Storage nicht erreichbar: lokal trotzdem entkoppeln.
            }
        }

        connection.Unpair();
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public HttpClient Client(OffloadConnection connection, TimeSpan timeout) =>
        PeerClient.CreateClient(new Uri(connection.Address!), connection.CaCertificatePem!, Protector.Unprotect(connection.ProtectedToken!), timeout);

    /// <summary>Gekoppelter Storage für die Fernaktualisierung (Backup-Modul, docs/backup.md).</summary>
    public async Task<IReadOnlyList<UpdatablePeer>> UpdatablePeersAsync(CancellationToken cancellationToken)
    {
        if (await FindConnectionAsync(cancellationToken) is not { Paired: true } connection)
        {
            return [];
        }

        var token = Protector.Unprotect(connection.ProtectedToken!);
        return [new UpdatablePeer("offload", "offload", connection.ServerName ?? "Storage", "offload", connection.ClientId!, PeerUpdateOrders.Sha256(token),
            timeout => Client(connection, timeout), connection.ServerVersion, "/system/auslagerung")];
    }

    public async Task<OffloadServerStatus?> ServerStatusAsync(OffloadConnection connection, CancellationToken cancellationToken)
    {
        if (!connection.Paired)
        {
            return null;
        }

        try
        {
            using var http = Client(connection, TimeSpan.FromSeconds(15));
            var status = await http.GetFromJsonAsync<OffloadServerStatus>("/api/v1/status", Json, cancellationToken);
            await ContactedAsync(null, status?.Version, cancellationToken);
            return status;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            await ContactedAsync(Describe(exception), null, cancellationToken);
            return null;
        }
    }

    private async Task ContactedAsync(string? error, string? version, CancellationToken cancellationToken)
    {
        var tracked = await db.Set<OffloadConnection>().SingleAsync(cancellationToken);
        tracked.Contacted(clock.GetUtcNow(), error, version);
        await db.SaveChangesAsync(cancellationToken);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Platz und Kandidaten

    public (long Free, long Total)? Disk()
    {
        try
        {
            Directory.CreateDirectory(storage.RootPath);
            var drive = new DriveInfo(Path.GetFullPath(storage.RootPath));
            return (drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Knapp = weniger als Schwelle % frei bzw. weniger als die feste Grenze in GB.</summary>
    public static bool IsLow(OffloadConnection settings, long free, long total) =>
        total > 0 && (free * 100.0 / total < settings.ThresholdPercent || (settings.ThresholdFreeGb is { } gb && free < gb * 1024L * 1024 * 1024));

    /// <summary>Wie viele Bytes ausgelagert werden müssen, um wieder (mit Abstand) über der Schwelle zu liegen.</summary>
    public static long BytesToFree(OffloadConnection settings, long free, long total)
    {
        var targetPercent = Math.Min(95, settings.ThresholdPercent + HysteresisPercent);
        var target = (long)(total * (targetPercent / 100.0));
        if (settings.ThresholdFreeGb is { } gb)
        {
            target = Math.Max(target, (gb + 2) * 1024L * 1024 * 1024);
        }

        return Math.Max(0, target - free);
    }

    /// <summary>Ablagen, die ausgelagert werden dürfen (älteste zuerst): gespeichert, Vorschauen fertig, noch nicht ausgelagert.</summary>
    public IQueryable<FileRecord> Candidates(OffloadConnection settings, Guid? bauakteId, bool ignoreRules)
    {
        var kinds = new List<FileKind>();
        if (settings.IncludePhotos || ignoreRules) kinds.Add(FileKind.Photo);
        if (settings.IncludeVideos || ignoreRules) kinds.Add(FileKind.Video);
        if (settings.IncludeDocuments || ignoreRules) kinds.Add(FileKind.Document);
        var query = db.Set<FileRecord>().AsNoTracking()
            .Where(f => f.State == FileState.Stored && f.StorageKey != null && f.DeletedAt == null && kinds.Contains(f.Kind)
                && f.Variants != VariantState.Pending && f.Variants != VariantState.Failed
                && !db.Set<OffloadedBlob>().Any(b => b.StorageKey == f.StorageKey));
        if (bauakteId is { } id)
        {
            query = query.Where(f => f.BauakteId == id);
        }

        if (!ignoreRules)
        {
            if (settings.MinAgeMonths > 0)
            {
                var cutoff = clock.GetUtcNow().AddMonths(-settings.MinAgeMonths);
                query = query.Where(f => f.CreatedAt <= cutoff);
            }

            if (settings.OnlyCompletedBauakten)
            {
                query = query.Where(f => f.BauakteId != null
                    && db.Set<Bauakte>().Any(b => b.Id == f.BauakteId && b.Status == BauakteStatus.Completed));
            }
        }

        return query;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Auslagern

    /// <summary>Lagert Originale aus, bis <paramref name="bytesWanted"/> erreicht ist (null = alle Kandidaten, höchstens ein Stapel).</summary>
    public async Task<int> OffloadAsync(OffloadConnection settings, Guid? bauakteId, bool ignoreRules, long? bytesWanted, CancellationToken cancellationToken)
    {
        if (!settings.Paired)
        {
            return 0;
        }

        var candidates = await Candidates(settings, bauakteId, ignoreRules).OrderBy(f => f.CreatedAt).ThenBy(f => f.Id).Take(BatchSize * 4)
            .Select(f => new { f.StorageKey, f.Sha256, f.SizeBytes }).ToListAsync(cancellationToken);
        var blobs = candidates.GroupBy(c => c.StorageKey!).Select(g => g.First()).ToList();
        long selected = 0;
        var batch = new List<(string Key, string Sha256, long Size)>();
        foreach (var blob in blobs)
        {
            if (batch.Count >= BatchSize || (bytesWanted is { } wanted && selected >= wanted))
            {
                break;
            }

            if (File.Exists(storage.PathOf(blob.StorageKey!)))
            {
                batch.Add((blob.StorageKey!, blob.Sha256, blob.SizeBytes));
                selected += blob.SizeBytes;
            }
        }

        if (batch.Count == 0)
        {
            return 0;
        }

        progress.Update(_ => new OffloadJobStatus(true, "offload", batch.Count, 0, 0, 0, clock.GetUtcNow(), null, "Originale werden übertragen und geprüft"));
        var master = MasterKey(settings);
        var done = 0;
        var failed = 0;
        long bytes = 0;
        using var http = Client(settings, TimeSpan.FromMinutes(30));
        foreach (var (key, sha, size) in batch)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var blob = await UploadAsync(http, master, key, sha, cancellationToken);
                db.Add(blob);
                await db.SaveChangesAsync(cancellationToken);
                done++;
                bytes += size;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or CryptographicException
                or OffloadUnavailableException or DbUpdateException)
            {
                failed++;
                LogUploadFailed(logger, key, exception.Message);
                db.ChangeTracker.Clear();
                if (exception is HttpRequestException or TaskCanceledException)
                {
                    await ContactedAsync(Describe(exception), null, cancellationToken);
                    break; // Storage weg: nicht jede Datei einzeln scheitern lassen
                }
            }

            progress.Update(s => s with { Processed = done + failed, Failed = failed, Bytes = bytes });
        }

        if (done > 0)
        {
            await ContactedAsync(null, null, cancellationToken);
        }

        await FinalizeAsync(settings, cancellationToken);
        progress.Update(s => s with
        {
            Running = false, FinishedAt = clock.GetUtcNow(),
            Message = failed == 0 ? $"{done} Originale übertragen und geprüft" : $"{done} übertragen, {failed} fehlgeschlagen (werden erneut versucht)",
        });
        return done;
    }

    private async Task<OffloadedBlob> UploadAsync(HttpClient http, byte[] master, string storageKey, string expectedSha, CancellationToken cancellationToken)
    {
        var path = storage.PathOf(storageKey);
        var objectId = Guid.CreateVersion7();
        var key = OffloadCrypto.DeriveKey(master, objectId);
        var prefix = RandomNumberGenerator.GetBytes(8);

        // 1. Original prüfen und Prüfsumme des verschlüsselten Objekts vorab bilden (gleiches Ergebnis wie beim Senden).
        string plainSha, cipherSha;
        long plainLength, cipherLength;
        await using (var source = new HashingSource(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)))
        await using (var sink = new HashingSink())
        {
            await OffloadCrypto.EncryptAsync(source, sink, key, prefix, cancellationToken);
            plainSha = source.Sha256();
            plainLength = source.ReadBytes;
            cipherSha = sink.Sha256();
            cipherLength = sink.Written;
        }

        if (plainSha != expectedSha)
        {
            throw new IOException($"Original weicht von seiner Prüfsumme ab ({storageKey}). Die Datei wird nicht ausgelagert.");
        }

        // 2. Übertragen: der Storage prüft die Prüfsumme und legt das Objekt erst danach (fsync) ab.
        using (var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/offload/objects/{objectId:N}"))
        {
            request.Content = new EncryptingContent(path, key, prefix, cipherLength);
            request.Headers.Add("X-Offload-Sha256", cipherSha);
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.OK))
            {
                throw new OffloadUnavailableException($"Storage lehnt das Objekt ab (HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}).");
            }

            var stored = await response.Content.ReadFromJsonAsync<OffloadObjectResponse>(Json, cancellationToken);
            if (stored is null || stored.Sha256 != cipherSha || stored.Size != cipherLength)
            {
                throw new OffloadUnavailableException("Quittung des Storage passt nicht zum gesendeten Objekt.");
            }
        }

        // 3. Zurücklesen, entschlüsseln, erneut prüfen – erst ein geprüftes Objekt zählt als ausgelagert.
        await using (var sink = new HashingSink())
        {
            await DownloadAsync(http, objectId, key, sink, cancellationToken);
            if (sink.Sha256() != expectedSha || sink.Written != plainLength)
            {
                throw new CryptographicException("Rückleseprüfung fehlgeschlagen. Das Original bleibt lokal gespeichert.");
            }
        }

        return OffloadedBlob.Uploaded(storageKey, objectId, expectedSha, plainLength, cipherSha, cipherLength, clock.GetUtcNow());
    }

    private static async Task DownloadAsync(HttpClient http, Guid objectId, byte[] key, Stream target, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri($"/api/v1/offload/objects/{objectId:N}", UriKind.Relative),
            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new OffloadUnavailableException("Das Objekt fehlt auf dem Storage.");
        }

        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        await OffloadCrypto.DecryptAsync(body, target, key, cancellationToken);
    }

    /// <summary>Wartende Originale lokal löschen, sobald die Sicherungskopie bestätigt ist (bzw. sofort, wenn keine verlangt wird).</summary>
    public async Task<int> FinalizeAsync(OffloadConnection settings, CancellationToken cancellationToken)
    {
        var pending = await db.Set<OffloadedBlob>().Where(b => b.State == OffloadState.Pending).OrderBy(b => b.UploadedAt).ToListAsync(cancellationToken);
        if (pending.Count == 0)
        {
            return 0;
        }

        var finalized = 0;
        if (settings.RequireBackup)
        {
            foreach (var group in pending.Where(b => b.BackupRequestId is not null).GroupBy(b => b.BackupRequestId!))
            {
                var result = backup.CopyResult(group.Key);
                if (result?.Status == "succeeded")
                {
                    foreach (var blob in group)
                    {
                        finalized += DeleteLocal(blob, result.At) ? 1 : 0;
                    }
                }
                else if (result?.Status == "failed" || (result is null && clock.GetUtcNow() - group.Min(b => b.UploadedAt) > TimeSpan.FromHours(12)))
                {
                    foreach (var blob in group)
                    {
                        blob.AwaitBackup(null);
                        blob.Failed(result?.Message is { } m ? $"Sicherungskopie fehlgeschlagen: {m}" : "Sicherungskopie nicht bestätigt. Sie wird erneut angefordert.");
                    }
                }
            }

            var waiting = pending.Where(b => b.State == OffloadState.Pending && b.BackupRequestId is null).ToList();
            if (waiting.Count > 0 && backup.Available)
            {
                var id = Guid.NewGuid().ToString("N");
                if (backup.RequestCopy(id, [.. waiting.Select(b => b.StorageKey)]))
                {
                    foreach (var blob in waiting)
                    {
                        blob.AwaitBackup(id);
                    }
                }
            }
            else if (waiting.Count > 0)
            {
                foreach (var blob in waiting)
                {
                    blob.Failed("Kein Backup-Dienst eingerichtet. Das Original bleibt lokal, bis eine Sicherungskopie möglich ist.");
                }
            }
        }
        else
        {
            foreach (var blob in pending)
            {
                finalized += DeleteLocal(blob, null) ? 1 : 0;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return finalized;
    }

    private bool DeleteLocal(OffloadedBlob blob, DateTimeOffset? backedUpAt)
    {
        var path = storage.PathOf(blob.StorageKey);
        var info = new FileInfo(path);
        if (info.Exists && info.Length != blob.SizeBytes)
        {
            blob.Failed("Das lokale Original wurde verändert und bleibt lokal gespeichert.");
            return false;
        }

        if (info.Exists)
        {
            info.Delete();
        }

        blob.MarkOffloaded(clock.GetUtcNow(), backedUpAt);
        return true;
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Lesen (mit Zwischenspeicher)

    /// <summary>
    /// Pfad des Originals: lokal, sonst aus dem Zwischenspeicher bzw. vom Storage geladen (entschlüsselt, SHA-256 geprüft).
    /// <c>null</c>, wenn die Ablage weder lokal noch ausgelagert ist.
    /// </summary>
    public async Task<string?> OriginalPathAsync(string storageKey, CancellationToken cancellationToken)
    {
        var local = storage.PathOf(storageKey);
        if (File.Exists(local))
        {
            return local;
        }

        var blob = await db.Set<OffloadedBlob>().AsNoTracking().SingleOrDefaultAsync(b => b.StorageKey == storageKey, cancellationToken);
        if (blob is null)
        {
            return null;
        }

        if (blob.State == OffloadState.Missing)
        {
            throw new OffloadUnavailableException("Das Original fehlt auf dem Storage. Die Verwaltung kann es aus der Sicherung zurückholen.");
        }

        var settings = await FindConnectionAsync(cancellationToken);
        if (settings is not { Paired: true })
        {
            throw new OffloadUnavailableException("Die Datei liegt auf dem Storage, der Storage ist aber nicht (mehr) gekoppelt.");
        }

        return await cache.GetAsync(blob.ObjectId, blob.Sha256, settings.CacheLimitMb * 1024L * 1024, async (target, ct) =>
        {
            try
            {
                using var http = Client(settings, TimeSpan.FromMinutes(30));
                await DownloadAsync(http, blob.ObjectId, OffloadCrypto.DeriveKey(MasterKey(settings), blob.ObjectId), target, ct);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                throw new OffloadUnavailableException(
                    $"Die Datei liegt auf dem Storage „{settings.ServerName}“, der derzeit nicht erreichbar ist. Es gehen keine Daten verloren. Bitte versuchen Sie es später erneut und informieren Sie die Verwaltung.",
                    exception);
            }
            catch (CryptographicException exception)
            {
                throw new OffloadUnavailableException("Das Objekt auf dem Storage ist beschädigt (Prüfung fehlgeschlagen). Die Verwaltung kann es aus der Sicherung zurückholen.", exception);
            }
        }, cancellationToken);
    }

    public Task<bool> IsOffloadedAsync(string storageKey, CancellationToken cancellationToken) =>
        db.Set<OffloadedBlob>().AnyAsync(b => b.StorageKey == storageKey && (b.State == OffloadState.Offloaded || b.State == OffloadState.Missing), cancellationToken);

    // ------------------------------------------------------------------------------------------------------------------
    // Rückholen

    public async Task<int> RecallAsync(OffloadConnection settings, IReadOnlyList<string>? keys, Guid? bauakteId, CancellationToken cancellationToken)
    {
        var query = db.Set<OffloadedBlob>().Where(b => b.State == OffloadState.Offloaded || b.State == OffloadState.Pending || b.State == OffloadState.Missing);
        if (keys is { Count: > 0 })
        {
            query = query.Where(b => keys.Contains(b.StorageKey));
        }

        if (bauakteId is { } id)
        {
            query = query.Where(b => db.Set<FileRecord>().Any(f => f.StorageKey == b.StorageKey && f.BauakteId == id));
        }

        var blobs = await query.OrderBy(b => b.StorageKey).ToListAsync(cancellationToken);
        progress.Update(_ => new OffloadJobStatus(true, "recall", blobs.Count, 0, 0, 0, clock.GetUtcNow(), null, "Originale werden zurückgeholt"));
        var done = 0;
        var failed = 0;
        long bytes = 0;
        var disk = Disk();
        long reserve = 512L * 1024 * 1024;
        foreach (var blob in blobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (disk is { } d && d.Free - bytes - blob.SizeBytes < reserve)
            {
                progress.Update(s => s with { Running = false, FinishedAt = clock.GetUtcNow(), Message = $"Abgebrochen: zu wenig Platz auf dem Server ({done} zurückgeholt)." });
                await db.SaveChangesAsync(cancellationToken);
                return done;
            }

            try
            {
                if (await RecallOneAsync(blob, cancellationToken))
                {
                    done++;
                    bytes += blob.SizeBytes;
                }
                else
                {
                    failed++;
                }
            }
            catch (OffloadUnavailableException exception)
            {
                failed++;
                blob.Failed(exception.Message);
            }

            await db.SaveChangesAsync(cancellationToken);
            progress.Update(s => s with { Processed = done + failed, Failed = failed, Bytes = bytes });
        }

        await CleanupRemoteAsync(settings, cancellationToken);
        progress.Update(s => s with
        {
            Running = false, FinishedAt = clock.GetUtcNow(),
            Message = failed == 0 ? $"{done} Originale wieder lokal gespeichert" : $"{done} zurückgeholt, {failed} fehlgeschlagen",
        });
        return done;
    }

    private async Task<bool> RecallOneAsync(OffloadedBlob blob, CancellationToken cancellationToken)
    {
        var path = storage.PathOf(blob.StorageKey);
        if (!File.Exists(path))
        {
            var source = await OriginalPathAsync(blob.StorageKey, cancellationToken) ?? throw new OffloadUnavailableException("Original nicht auffindbar.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".recall";
            File.Copy(source, temp, overwrite: true);
            await using (var check = File.OpenRead(temp))
            {
                if (Convert.ToHexStringLower(await SHA256.HashDataAsync(check, cancellationToken)) != blob.Sha256)
                {
                    File.Delete(temp);
                    blob.Failed("Prüfsumme nach dem Rückholen fehlerhaft. Bitte erneut versuchen.");
                    return false;
                }
            }

            File.Move(temp, path, overwrite: false);
        }

        blob.MarkRecalled();
        blob.Failed(null);
        return true;
    }

    /// <summary>Kopien zurückgeholter Originale auf dem Storage in den Papierkorb legen; danach ist der Eintrag erledigt.</summary>
    public async Task<int> CleanupRemoteAsync(OffloadConnection settings, CancellationToken cancellationToken)
    {
        var recalled = await db.Set<OffloadedBlob>().Where(b => b.State == OffloadState.Recalled).Take(500).ToListAsync(cancellationToken);
        if (recalled.Count == 0 || !settings.Paired)
        {
            return 0;
        }

        var removed = 0;
        try
        {
            using var http = Client(settings, TimeSpan.FromSeconds(30));
            foreach (var blob in recalled)
            {
                if (!File.Exists(storage.PathOf(blob.StorageKey)))
                {
                    blob.MarkMissing("Lokale Kopie fehlt nach dem Rückholen.");
                    continue;
                }

                using var response = await http.DeleteAsync(new Uri($"/api/v1/offload/objects/{blob.ObjectId:N}", UriKind.Relative), cancellationToken);
                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                {
                    db.Remove(blob);
                    cache.Remove(blob.ObjectId);
                    removed++;
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            LogCleanupFailed(logger, exception.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return removed;
    }

    /// <summary>Inhalt endgültig aus dem Produktivsystem entfernt (Aktenarchivierung): auch die Kopie auf dem Storage aufgeben.</summary>
    public async Task DiscardAsync(string storageKey, CancellationToken cancellationToken)
    {
        if (await db.Set<OffloadedBlob>().SingleOrDefaultAsync(b => b.StorageKey == storageKey, cancellationToken) is not { } blob)
        {
            return;
        }

        var settings = await FindConnectionAsync(cancellationToken);
        try
        {
            if (settings is { Paired: true })
            {
                using var http = Client(settings, TimeSpan.FromSeconds(30));
                using var _ = await http.DeleteAsync(new Uri($"/api/v1/offload/objects/{blob.ObjectId:N}", UriKind.Relative), cancellationToken);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            LogCleanupFailed(logger, exception.Message); // Objekt bleibt verwaist auf dem Storage (verschlüsselt) – kein Datenverlust.
        }

        cache.Remove(blob.ObjectId);
        db.Remove(blob);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Original wurde von außen (Archiv, Sicherung) lokal zurückgespielt: Eintrag als zurückgeholt behandeln.</summary>
    public async Task LocalRestoredAsync(string storageKey, CancellationToken cancellationToken)
    {
        if (await db.Set<OffloadedBlob>().SingleOrDefaultAsync(b => b.StorageKey == storageKey, cancellationToken) is { } blob)
        {
            blob.MarkRecalled();
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Abgleich (nach Wiederherstellung, Ausfall, Neustart)

    /// <summary>
    /// Prüft die Einträge gegen lokale Ablage und Storage: lokal (wieder) vorhanden und Prüfsumme richtig → zurückgeholt;
    /// auf dem Storage im Papierkorb → zurücklegen; nirgends → „fehlt“ (aus der Sicherung zurückholen).
    /// </summary>
    public async Task<int> ReconcileAsync(OffloadConnection settings, CancellationToken cancellationToken)
    {
        var rows = await db.Set<OffloadedBlob>().Where(b => b.State == OffloadState.Offloaded || b.State == OffloadState.Missing)
            .OrderBy(b => b.StorageKey).ToListAsync(cancellationToken);
        var changed = 0;
        HttpClient? http = null;
        try
        {
            foreach (var blob in rows)
            {
                var path = storage.PathOf(blob.StorageKey);
                if (File.Exists(path))
                {
                    await using var stream = File.OpenRead(path);
                    if (Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken)) == blob.Sha256)
                    {
                        blob.MarkRecalled();
                        blob.Failed(null);
                        changed++;
                        continue;
                    }
                }

                if (!settings.Paired)
                {
                    continue;
                }

                http ??= Client(settings, TimeSpan.FromSeconds(20));
                using var response = await http.GetAsync(new Uri($"/api/v1/offload/objects/{blob.ObjectId:N}/info", UriKind.Relative), cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    if (blob.State != OffloadState.Missing)
                    {
                        blob.MarkMissing("Objekt fehlt auf dem Storage. Bitte aus der Sicherung zurückholen.");
                        changed++;
                    }

                    continue;
                }

                response.EnsureSuccessStatusCode();
                var info = await response.Content.ReadFromJsonAsync<OffloadObjectResponse>(Json, cancellationToken);
                if (info is { Deleted: true })
                {
                    using var undelete = await http.PostAsync(new Uri($"/api/v1/offload/objects/{blob.ObjectId:N}/undelete", UriKind.Relative), null, cancellationToken);
                    undelete.EnsureSuccessStatusCode();
                }

                if (info is not null && info.Sha256 != blob.CipherSha256)
                {
                    blob.MarkMissing("Objekt auf dem Storage stimmt nicht mit der Prüfsumme überein. Bitte aus der Sicherung zurückholen.");
                    changed++;
                }
                else if (blob.State == OffloadState.Missing)
                {
                    blob.MarkOffloaded(blob.OffloadedAt ?? clock.GetUtcNow(), blob.BackedUpAt);
                    changed++;
                }
            }

            await ContactedAsync(null, null, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            await ContactedAsync(Describe(exception), null, cancellationToken);
        }
        finally
        {
            http?.Dispose();
        }

        await db.SaveChangesAsync(cancellationToken);
        return changed;
    }

    /// <summary>Fehlende Originale aus den Auslagerungs-Snapshots der Sicherung zurückholen (Backup-Dienst).</summary>
    public async Task<string?> RequestRecoveryFromBackupAsync(CancellationToken cancellationToken)
    {
        var keys = await db.Set<OffloadedBlob>().Where(b => b.State == OffloadState.Missing).Select(b => b.StorageKey).ToListAsync(cancellationToken);
        if (keys.Count == 0)
        {
            return null;
        }

        var id = Guid.NewGuid().ToString("N");
        return backup.RequestRecovery(id, keys) ? id : null;
    }

    internal static string Describe(Exception exception) => exception switch
    {
        TaskCanceledException => "Zeitüberschreitung: Storage nicht erreichbar.",
        HttpRequestException { InnerException: System.Security.Authentication.AuthenticationException } =>
            "Zertifikat des Storage stimmt nicht mehr (neu koppeln?).",
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => "Storage kennt diesen Server nicht mehr (Kopplung aufgehoben).",
        _ => exception.Message.Length > 300 ? exception.Message[..300] : exception.Message,
    };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Auslagerung von {Key} fehlgeschlagen: {Error}")]
    private static partial void LogUploadFailed(ILogger logger, string key, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Aufräumen auf dem Storage fehlgeschlagen: {Error}")]
    private static partial void LogCleanupFailed(ILogger logger, string error);
}

/// <summary>Verschlüsselt das Original beim Senden (Länge vorab bekannt, kein Zwischenstand auf der Platte).</summary>
internal sealed class EncryptingContent : HttpContent
{
    private readonly string _path;
    private readonly byte[] _key;
    private readonly byte[] _prefix;
    private readonly long _length;

    public EncryptingContent(string path, byte[] key, byte[] prefix, long length)
    {
        _path = path;
        _key = key;
        _prefix = prefix;
        _length = length;
        Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        await SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        await using var source = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await OffloadCrypto.EncryptAsync(source, stream, _key, _prefix, cancellationToken);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _length;
        return true;
    }
}

/// <summary>
/// Zwischenspeicher für vom Storage geladene Originale (<c>&lt;Ablage&gt;/.offload-cache</c>, nicht in der Sicherung):
/// entschlüsselt, gegen die SHA-256 geprüft, älteste zuerst verdrängt. Gleichzeitige Abrufe derselben Datei laden einmal.
/// </summary>
public sealed class OffloadCache(LocalFileStorage storage)
{
    private readonly ConcurrentDictionary<Guid, Lazy<Task<string>>> _loading = new();

    private string Dir => Path.Combine(storage.RootPath, ".offload-cache");

    private string PathOf(Guid objectId) => Path.Combine(Dir, objectId.ToString("N"));

    public long SizeBytes() => Directory.Exists(Dir) ? new DirectoryInfo(Dir).EnumerateFiles().Where(f => f.Extension != ".tmp").Sum(f => f.Length) : 0;

    public void Remove(Guid objectId)
    {
        try
        {
            File.Delete(PathOf(objectId));
        }
        catch (IOException)
        {
            // wird beim Verdrängen entfernt
        }
    }

    public void Clear()
    {
        if (Directory.Exists(Dir))
        {
            foreach (var file in Directory.EnumerateFiles(Dir))
            {
                File.Delete(file);
            }
        }
    }

    internal async Task<string> GetAsync(Guid objectId, string sha256, long limitBytes, Func<Stream, CancellationToken, Task> download,
        CancellationToken cancellationToken)
    {
        var path = PathOf(objectId);
        if (File.Exists(path))
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            return path;
        }

        var lazy = _loading.GetOrAdd(objectId, _ => new Lazy<Task<string>>(() => LoadAsync(path, sha256, limitBytes, download)));
        try
        {
            return await lazy.Value.WaitAsync(cancellationToken);
        }
        finally
        {
            if (lazy.IsValueCreated && lazy.Value.IsCompleted)
            {
                _loading.TryRemove(objectId, out _);
            }
        }
    }

    private async Task<string> LoadAsync(string path, string sha256, long limitBytes, Func<Stream, CancellationToken, Task> download)
    {
        Directory.CreateDirectory(Dir);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            await using (var sink = new HashingSink(file))
            {
                await download(sink, timeout.Token);
                if (sink.Sha256() != sha256)
                {
                    throw new OffloadUnavailableException("Das vom Storage geladene Original passt nicht zu seiner Prüfsumme.");
                }
            }

            File.Move(temp, path, overwrite: true);
            Evict(limitBytes, keep: path);
            return path;
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private void Evict(long limitBytes, string keep)
    {
        var files = new DirectoryInfo(Dir).EnumerateFiles().Where(f => f.Extension != ".tmp").OrderBy(f => f.LastWriteTimeUtc).ToList();
        var total = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (total <= limitBytes)
            {
                break;
            }

            if (file.FullName == keep)
            {
                continue;
            }

            total -= file.Length;
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
                // gerade in Benutzung (wird ausgeliefert) – beim nächsten Mal
            }
        }
    }
}
