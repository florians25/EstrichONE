using System.Security.Cryptography;
using System.Text.Json;

namespace EstrichOne.Archive;

/// <summary>Beschreibung eines ausgelagerten Objekts (verschlüsselter Inhalt – den Schlüssel kennt nur der EstrichONE-Server).</summary>
public sealed record OffloadObjectInfo(string Id, string Sha256, long Size, DateTimeOffset StoredAt, bool Deleted, DateTimeOffset? DeletedAt);

public sealed record OffloadStatistics(int Objects, long Bytes, int TrashObjects, long TrashBytes, int TrashDays, OffloadVerification? LastVerification);

public sealed record OffloadVerification(DateTimeOffset At, bool Ok, int ObjectsChecked, long BytesChecked, IReadOnlyList<string> Errors);

public enum OffloadPutOutcome
{
    Created,
    AlreadyStored,
    ChecksumMismatch,
    Conflict,
    TooLarge,
    DiskFull,
    Invalid,
}

/// <summary>
/// Speicherstufe „Storage“ (Auslagerung): Ablage verschlüsselter Datei-Originale des EstrichONE-Servers, getrennt vom
/// WORM-Archiv und vom Backup-Repository. Je gekoppeltem Server ein eigener Bereich (<c>offload/&lt;client&gt;/…</c>).
/// Schreiben nur mit vorab genannter SHA-256 (geprüft, erst danach sichtbar, fsync), Löschen verschiebt in den Papierkorb
/// (Frist <see cref="ArchiveOptions.OffloadTrashDays"/>) – ein Rückholen oder eine Wiederherstellung auf einen älteren Stand
/// findet das Objekt dort noch. Wöchentlich werden alle Prüfsummen nachgerechnet.
/// </summary>
public sealed class OffloadStore(ArchiveOptions options, TimeProvider clock) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _verify = new(1, 1);

    private string Root => options.PathOf("offload");

    public void Dispose() => _verify.Dispose();

    /// <summary>Nur UUIDs (32 Hex-Zeichen, Bindestriche erlaubt) – nie Pfadbestandteile vom Client.</summary>
    public static string? NormalizeId(string? id) => Guid.TryParse(id, out var guid) ? guid.ToString("N") : null;

    private static string SafeClient(string clientId) =>
        new string([.. clientId.Where(c => char.IsAsciiLetterOrDigit(c) || c == '-')]) is { Length: > 0 } safe ? safe : "unbekannt";

    private string ObjectPath(string clientId, string id, bool trash = false) =>
        Path.Combine(Root, SafeClient(clientId), trash ? "trash" : "objects", id[..2], id);

    private static string MetaPath(string objectPath) => objectPath + ".json";

    public OffloadObjectInfo? Info(string clientId, string id)
    {
        foreach (var trash in new[] { false, true })
        {
            var meta = MetaPath(ObjectPath(clientId, id, trash));
            if (File.Exists(meta) && File.Exists(ObjectPath(clientId, id, trash)))
            {
                try
                {
                    return JsonSerializer.Deserialize<OffloadObjectInfo>(File.ReadAllText(meta), Json);
                }
                catch (JsonException)
                {
                    return null;
                }
            }
        }

        return null;
    }

    /// <summary>Inhalt lesen (auch aus dem Papierkorb); <c>null</c>, wenn unbekannt.</summary>
    public Stream? Open(string clientId, string id)
    {
        foreach (var trash in new[] { false, true })
        {
            var path = ObjectPath(clientId, id, trash);
            if (File.Exists(path) && File.Exists(MetaPath(path)))
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            }
        }

        return null;
    }

    public async Task<(OffloadPutOutcome Outcome, OffloadObjectInfo? Info)> PutAsync(string clientId, string id, string sha256, long? length,
        Stream content, CancellationToken cancellationToken)
    {
        sha256 = (sha256 ?? string.Empty).Trim().ToLowerInvariant();
        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            return (OffloadPutOutcome.Invalid, null);
        }

        if (length > options.MaxObjectBytes)
        {
            return (OffloadPutOutcome.TooLarge, null);
        }

        if (Info(clientId, id) is { } existing)
        {
            if (existing.Sha256 != sha256)
            {
                return (OffloadPutOutcome.Conflict, existing);
            }

            if (existing.Deleted)
            {
                Undelete(clientId, id);
                existing = Info(clientId, id)!;
            }

            await DrainAsync(content, cancellationToken);
            return (OffloadPutOutcome.AlreadyStored, existing);
        }

        var target = ObjectPath(clientId, id);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = Path.Combine(Root, SafeClient(clientId), "incoming", $"{id}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        long size = 0;
        string actual;
        try
        {
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    size += read;
                    if (size > options.MaxObjectBytes)
                    {
                        file.Close();
                        File.Delete(temp);
                        return (OffloadPutOutcome.TooLarge, null);
                    }

                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await file.FlushAsync(cancellationToken);
                file.Flush(flushToDisk: true);
                actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            }
        }
        catch (IOException exception) when (IsDiskFull(exception))
        {
            File.Delete(temp);
            return (OffloadPutOutcome.DiskFull, null);
        }
        catch
        {
            File.Delete(temp);
            throw;
        }

        if (actual != sha256 || (length is { } expected && expected != size))
        {
            File.Delete(temp);
            return (OffloadPutOutcome.ChecksumMismatch, null);
        }

        var info = new OffloadObjectInfo(id, sha256, size, clock.GetUtcNow(), false, null);
        File.Move(temp, target, overwrite: false);
        await WriteMetaAsync(target, info, cancellationToken);
        return (OffloadPutOutcome.Created, info);
    }

    /// <summary>In den Papierkorb verschieben (nach <see cref="ArchiveOptions.OffloadTrashDays"/> Tagen endgültig gelöscht).</summary>
    public bool Delete(string clientId, string id)
    {
        var path = ObjectPath(clientId, id);
        if (!File.Exists(path))
        {
            return Info(clientId, id) is { Deleted: true };
        }

        var info = Info(clientId, id)!;
        var trash = ObjectPath(clientId, id, trash: true);
        Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
        File.Move(path, trash, overwrite: true);
        File.WriteAllText(MetaPath(trash), JsonSerializer.Serialize(info with { Deleted = true, DeletedAt = clock.GetUtcNow() }, Json));
        File.Delete(MetaPath(path));
        return true;
    }

    /// <summary>Aus dem Papierkorb zurückholen (z. B. nach einer Wiederherstellung auf einen älteren Stand).</summary>
    public bool Undelete(string clientId, string id)
    {
        var trash = ObjectPath(clientId, id, trash: true);
        if (!File.Exists(trash))
        {
            return File.Exists(ObjectPath(clientId, id));
        }

        var info = JsonSerializer.Deserialize<OffloadObjectInfo>(File.ReadAllText(MetaPath(trash)), Json)!;
        var path = ObjectPath(clientId, id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Move(trash, path, overwrite: true);
        File.WriteAllText(MetaPath(path), JsonSerializer.Serialize(info with { Deleted = false, DeletedAt = null }, Json));
        File.Delete(MetaPath(trash));
        return true;
    }

    public OffloadStatistics Statistics(string? clientId)
    {
        int objects = 0, trashObjects = 0;
        long bytes = 0, trashBytes = 0;
        foreach (var dir in ClientDirectories(clientId))
        {
            foreach (var (trash, file) in Files(dir))
            {
                if (trash)
                {
                    trashObjects++;
                    trashBytes += file.Length;
                }
                else
                {
                    objects++;
                    bytes += file.Length;
                }
            }
        }

        return new OffloadStatistics(objects, bytes, trashObjects, trashBytes, options.OffloadTrashDays, LatestVerification());
    }

    /// <summary>Papierkorb leeren (älter als die Frist) und abgebrochene Uploads entfernen.</summary>
    public int Purge()
    {
        var now = clock.GetUtcNow();
        var removed = 0;
        foreach (var dir in ClientDirectories(null))
        {
            foreach (var (trash, file) in Files(dir).ToList())
            {
                if (!trash)
                {
                    continue;
                }

                try
                {
                    var info = JsonSerializer.Deserialize<OffloadObjectInfo>(File.ReadAllText(MetaPath(file.FullName)), Json);
                    if (info?.DeletedAt is { } at && now - at > TimeSpan.FromDays(options.OffloadTrashDays))
                    {
                        File.Delete(file.FullName);
                        File.Delete(MetaPath(file.FullName));
                        removed++;
                    }
                }
                catch (Exception exception) when (exception is IOException or JsonException)
                {
                    // nächster Durchlauf
                }
            }

            var incoming = Path.Combine(dir, "incoming");
            if (Directory.Exists(incoming))
            {
                foreach (var stale in new DirectoryInfo(incoming).EnumerateFiles().Where(f => now - f.LastWriteTimeUtc > TimeSpan.FromDays(1)))
                {
                    stale.Delete();
                }
            }
        }

        return removed;
    }

    /// <summary>Alle Prüfsummen nachrechnen (Inhalt gegen Metadaten).</summary>
    public async Task<OffloadVerification> VerifyAsync(CancellationToken cancellationToken)
    {
        await _verify.WaitAsync(cancellationToken);
        try
        {
            var errors = new List<string>();
            var checkedObjects = 0;
            long checkedBytes = 0;
            foreach (var dir in ClientDirectories(null))
            {
                foreach (var (_, file) in Files(dir))
                {
                    OffloadObjectInfo? info;
                    try
                    {
                        info = JsonSerializer.Deserialize<OffloadObjectInfo>(await File.ReadAllTextAsync(MetaPath(file.FullName), cancellationToken), Json);
                    }
                    catch (Exception exception) when (exception is IOException or JsonException)
                    {
                        errors.Add($"{file.Name}: Metadaten fehlen oder sind beschädigt.");
                        continue;
                    }

                    await using var stream = file.OpenRead();
                    var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
                    checkedObjects++;
                    checkedBytes += file.Length;
                    if (info is null || info.Sha256 != sha || info.Size != file.Length)
                    {
                        errors.Add($"{file.Name}: Prüfsumme stimmt nicht.");
                    }
                }
            }

            var report = new OffloadVerification(clock.GetUtcNow(), errors.Count == 0, checkedObjects, checkedBytes, errors.Take(100).ToList());
            Directory.CreateDirectory(Root);
            await File.WriteAllTextAsync(Path.Combine(Root, "verification.json"), JsonSerializer.Serialize(report, Json), cancellationToken);
            return report;
        }
        finally
        {
            _verify.Release();
        }
    }

    public OffloadVerification? LatestVerification()
    {
        var path = Path.Combine(Root, "verification.json");
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<OffloadVerification>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            return null;
        }
    }

    private IEnumerable<string> ClientDirectories(string? clientId)
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        return clientId is null ? Directory.EnumerateDirectories(Root) : [Path.Combine(Root, SafeClient(clientId))];
    }

    private static IEnumerable<(bool Trash, FileInfo File)> Files(string clientDirectory)
    {
        foreach (var trash in new[] { false, true })
        {
            var dir = new DirectoryInfo(Path.Combine(clientDirectory, trash ? "trash" : "objects"));
            if (!dir.Exists)
            {
                continue;
            }

            foreach (var file in dir.EnumerateFiles("*", SearchOption.AllDirectories).Where(f => f.Extension != ".json"))
            {
                yield return (trash, file);
            }
        }
    }

    private static async Task WriteMetaAsync(string objectPath, OffloadObjectInfo info, CancellationToken cancellationToken)
    {
        var temp = MetaPath(objectPath) + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(info, Json), cancellationToken);
        File.Move(temp, MetaPath(objectPath), overwrite: true);
    }

    private static async Task DrainAsync(Stream content, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        while (await content.ReadAsync(buffer, cancellationToken) > 0)
        {
        }
    }

    private static bool IsDiskFull(IOException exception) => (exception.HResult & 0xFFFF) is 28 or 112 || exception.Message.Contains("space", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Speicherstufe Storage: Zertifikat erneuern, Papierkorb leeren (stündlich), alle Prüfsummen wöchentlich nachrechnen.</summary>
internal sealed class OffloadMaintenance(OffloadStore store, TlsCertificates tls, TimeProvider clock, ILogger<OffloadMaintenance> logger)
    : BackgroundService
{
    public static readonly TimeSpan VerificationInterval = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                tls.RenewIfDue(clock.GetUtcNow());
                var purged = store.Purge();
                if (purged > 0)
                {
                    logger.LogInformation("Papierkorb: {Count} Objekte nach Fristablauf endgültig gelöscht.", purged);
                }

                if (store.LatestVerification() is not { } last || clock.GetUtcNow() - last.At >= VerificationInterval)
                {
                    var report = await store.VerifyAsync(stoppingToken);
                    if (report.Ok)
                    {
                        logger.LogInformation("Prüfung in Ordnung: {Objects} Objekte, {Bytes} Bytes.", report.ObjectsChecked, report.BytesChecked);
                    }
                    else
                    {
                        logger.LogError("PRÜFUNG MIT FEHLERN: {Errors}", string.Join(" | ", report.Errors.Take(10)));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogError(exception, "Wartung der Auslagerung fehlgeschlagen.");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }
}
