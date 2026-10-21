using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EstrichOne.Archive;

/// <summary>Vom Client mitgegebene Beschreibung eines Dokuments (Kopfzeile <c>X-Archive-Metadata</c>, JSON in Base64).</summary>
public sealed record ObjectDescription(
    string Name, string? ContentType, string? DocumentType, int RetentionYears, DateOnly? DocumentDate,
    Dictionary<string, string>? Source);

/// <summary>Unveränderliche Metadaten je Objekt (eigene Datei, schreibgeschützt, Prüfsumme im Journal).</summary>
public sealed record ObjectMetadata(
    string ObjectId, string ClientId, string Name, string ContentType, string? DocumentType, int RetentionYears,
    DateOnly DocumentDate, DateOnly RetainUntil, string Sha256, long Size, DateTimeOffset ArchivedAt,
    Dictionary<string, string>? Source);

/// <summary>
/// Eintrag im Journal (<c>ledger.jsonl</c>, nur anhängen). <see cref="Hash"/> = SHA-256 über die JSON-Zeile mit leerem
/// <c>hash</c>; jede Zeile enthält den Hash der vorigen – nachträgliche Änderungen fallen bei der Prüfung auf.
/// </summary>
public sealed record LedgerEntry(
    long Seq, DateTimeOffset At, string Type, string? ClientId, string? ObjectId, string? Sha256, long? Size,
    string? MetadataSha256, DateOnly? RetainUntil, string? Note, string PreviousHash, string Hash);

public sealed record ArchiveReceipt(
    string ObjectId, string Sha256, long Size, DateTimeOffset ArchivedAt, DateOnly RetainUntil, long LedgerSeq,
    string LedgerHash, bool Deleted, string? Name, string? DocumentType);

public sealed record VerificationReport(
    DateTimeOffset At, bool Ok, long LedgerEntries, string LedgerHead, int ObjectsChecked, long BytesChecked, int Deleted,
    IReadOnlyList<string> Errors);

public sealed record ArchiveStatistics(int Objects, long Bytes, int Deleted, long LedgerEntries, string LedgerHead);

public enum StoreOutcome
{
    Created,
    AlreadyArchived,
    ChecksumMismatch,
    Immutable,
    TooLarge,
    RetentionTooShort,
    Invalid,
}

public enum DeleteOutcome
{
    Deleted,
    NotFound,
    RetentionActive,
    AlreadyDeleted,
}

/// <summary>
/// WORM-Ablage: Objekte werden genau einmal geschrieben (Prüfsumme vorab angegeben und beim Schreiben geprüft),
/// danach schreibgeschützt. Es gibt keine Schnittstelle zum Überschreiben; Löschen erst nach Fristende und nur mit
/// Journaleintrag (Metadaten bleiben). Jede Aktion steht im verketteten Journal.
/// </summary>
public sealed class ArchiveStore : IDisposable
{
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    internal static readonly JsonSerializerOptions Indented = new(Json) { WriteIndented = true };

    private static readonly UnixFileMode ReadOnly = UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
    private readonly ArchiveOptions _options;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _write = new(1, 1);
    private readonly Dictionary<string, ObjectState> _objects = new(StringComparer.Ordinal);
    private long _seq;
    private string _head = GenesisHash;

    private sealed class ObjectState
    {
        public required LedgerEntry Stored { get; init; }
        public DateOnly RetainUntil { get; set; }
        public LedgerEntry? DeletedBy { get; set; }
    }

    public ArchiveStore(ArchiveOptions options, TimeProvider clock)
    {
        _options = options;
        _clock = clock;
        Directory.CreateDirectory(_options.PathOf("objects"));
        Directory.CreateDirectory(_options.PathOf("tmp"));
        Directory.CreateDirectory(_options.PathOf("reports"));
        foreach (var stale in Directory.EnumerateFiles(_options.PathOf("tmp")))
        {
            File.Delete(stale);
        }

        if (File.Exists(LedgerPath))
        {
            foreach (var line in File.ReadLines(LedgerPath))
            {
                if (line.Length > 0 && JsonSerializer.Deserialize<LedgerEntry>(line, Json) is { } entry)
                {
                    Apply(entry);
                }
            }
        }
    }

    public void Dispose() => _write.Dispose();

    public string LedgerPath => _options.PathOf("ledger.jsonl");

    public static string? NormalizeId(string id) => Guid.TryParse(id, out var guid) ? guid.ToString("D") : null;

    /// <summary>Ende der Aufbewahrung: Die Frist beginnt mit dem Schluss des Kalenderjahres (§ 147 Abs. 4 AO).</summary>
    public static DateOnly RetainUntilFor(DateOnly documentDate, int years) => new(documentDate.Year + years, 12, 31);

    public static string HashEntry(LedgerEntry entry) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry with { Hash = string.Empty }, Json))));

    public async Task<(StoreOutcome Outcome, ArchiveReceipt? Receipt)> StoreAsync(
        string clientId, string objectId, string declaredSha256, ObjectDescription description, Stream content,
        CancellationToken cancellationToken)
    {
        declaredSha256 = declaredSha256.Trim().ToLowerInvariant();
        if (declaredSha256.Length != 64 || !declaredSha256.All(char.IsAsciiHexDigitLower) || string.IsNullOrWhiteSpace(description.Name)
            || description.RetentionYears is < 1 or > 30)
        {
            return (StoreOutcome.Invalid, null);
        }

        if (description.RetentionYears < _options.MinRetentionYears)
        {
            return (StoreOutcome.RetentionTooShort, null);
        }

        var existing = Receipt(clientId, objectId);
        if (existing is not null)
        {
            return existing.Sha256 == declaredSha256 ? (StoreOutcome.AlreadyArchived, existing) : (StoreOutcome.Immutable, existing);
        }

        var temp = _options.PathOf("tmp", Guid.NewGuid().ToString("N"));
        long size = 0;
        string actual;
        try
        {
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    size += read;
                    if (size > _options.MaxObjectBytes)
                    {
                        return (StoreOutcome.TooLarge, null);
                    }

                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await file.FlushAsync(cancellationToken);
                file.Flush(flushToDisk: true);
                actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            }

            if (actual != declaredSha256)
            {
                return (StoreOutcome.ChecksumMismatch, null);
            }

            await _write.WaitAsync(cancellationToken);
            try
            {
                existing = Receipt(clientId, objectId);
                if (existing is not null)
                {
                    return existing.Sha256 == actual ? (StoreOutcome.AlreadyArchived, existing) : (StoreOutcome.Immutable, existing);
                }

                var now = _clock.GetUtcNow();
                var documentDate = description.DocumentDate ?? DateOnly.FromDateTime(now.UtcDateTime);
                var metadata = new ObjectMetadata(objectId, clientId, Path.GetFileName(description.Name.Trim()),
                    string.IsNullOrWhiteSpace(description.ContentType) ? "application/octet-stream" : description.ContentType.Trim(),
                    description.DocumentType, description.RetentionYears, documentDate,
                    RetainUntilFor(documentDate, description.RetentionYears), actual, size, now, description.Source);
                var contentPath = ContentPath(clientId, objectId);
                Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
                File.Move(temp, contentPath, overwrite: false);
                MakeReadOnly(contentPath);
                var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata, Indented);
                await using (var metadataFile = new FileStream(MetadataPath(clientId, objectId), FileMode.CreateNew, FileAccess.Write))
                {
                    await metadataFile.WriteAsync(metadataBytes, cancellationToken);
                    metadataFile.Flush(flushToDisk: true);
                }

                MakeReadOnly(MetadataPath(clientId, objectId));
                var entry = await AppendAsync("store", clientId, objectId, actual, size, Convert.ToHexStringLower(SHA256.HashData(metadataBytes)),
                    metadata.RetainUntil, metadata.DocumentType, cancellationToken);
                return (StoreOutcome.Created, ToReceipt(entry, metadata.RetainUntil, deleted: false, metadata));
            }
            finally
            {
                _write.Release();
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    public ArchiveReceipt? Receipt(string clientId, string objectId)
    {
        lock (_objects)
        {
            if (!_objects.TryGetValue(Key(clientId, objectId), out var state))
            {
                return null;
            }

            return ToReceipt(state.Stored, state.RetainUntil, state.DeletedBy is not null, ReadMetadata(clientId, objectId));
        }
    }

    public Stream? OpenContent(string clientId, string objectId)
    {
        var path = ContentPath(clientId, objectId);
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null;
    }

    /// <summary>Frist verlängern ist erlaubt (z. B. laufende Betriebsprüfung), verkürzen nie.</summary>
    public async Task<ArchiveReceipt?> ExtendRetentionAsync(string clientId, string objectId, DateOnly retainUntil, string? reason,
        CancellationToken cancellationToken)
    {
        await _write.WaitAsync(cancellationToken);
        try
        {
            ObjectState? state;
            lock (_objects)
            {
                _objects.TryGetValue(Key(clientId, objectId), out state);
            }

            if (state is null || state.DeletedBy is not null)
            {
                return null;
            }

            if (retainUntil > state.RetainUntil)
            {
                await AppendAsync("retention", clientId, objectId, state.Stored.Sha256, null, null, retainUntil, Limit(reason), cancellationToken);
            }

            return Receipt(clientId, objectId);
        }
        finally
        {
            _write.Release();
        }
    }

    public async Task<(DeleteOutcome Outcome, DateOnly? RetainUntil)> DeleteAsync(string clientId, string objectId, string? reason,
        CancellationToken cancellationToken)
    {
        await _write.WaitAsync(cancellationToken);
        try
        {
            ObjectState? state;
            lock (_objects)
            {
                _objects.TryGetValue(Key(clientId, objectId), out state);
            }

            if (state is null)
            {
                return (DeleteOutcome.NotFound, null);
            }

            if (state.DeletedBy is not null)
            {
                return (DeleteOutcome.AlreadyDeleted, state.RetainUntil);
            }

            if (DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime) <= state.RetainUntil)
            {
                return (DeleteOutcome.RetentionActive, state.RetainUntil);
            }

            File.Delete(ContentPath(clientId, objectId));
            await AppendAsync("delete", clientId, objectId, state.Stored.Sha256, null, null, state.RetainUntil, Limit(reason), cancellationToken);
            return (DeleteOutcome.Deleted, state.RetainUntil);
        }
        finally
        {
            _write.Release();
        }
    }

    public ArchiveStatistics Statistics(string? clientId = null)
    {
        lock (_objects)
        {
            var states = _objects.Values.Where(state => clientId is null || state.Stored.ClientId == clientId).ToList();
            return new ArchiveStatistics(states.Count(state => state.DeletedBy is null),
                states.Where(state => state.DeletedBy is null).Sum(state => state.Stored.Size ?? 0),
                states.Count(state => state.DeletedBy is not null), _seq, _head);
        }
    }

    public VerificationReport? LatestVerification()
    {
        var latest = Directory.EnumerateFiles(_options.PathOf("reports"), "verify-*.json").Order(StringComparer.Ordinal).LastOrDefault();
        return latest is null ? null : JsonSerializer.Deserialize<VerificationReport>(File.ReadAllText(latest), Json);
    }

    /// <summary>
    /// Prüflauf: Journal-Kette neu berechnen, jede Datei gegen ihre SHA-256 prüfen, Metadaten gegen ihre Prüfsumme,
    /// unbekannte Dateien melden. Ergebnis als Prüfprotokoll (Datei + Journaleintrag).
    /// </summary>
    public async Task<VerificationReport> VerifyAsync(CancellationToken cancellationToken)
    {
        await _write.WaitAsync(cancellationToken);
        try
        {
            var errors = new List<string>();
            var previous = GenesisHash;
            long expectedSeq = 1, entries = 0;
            var stored = new Dictionary<string, LedgerEntry>(StringComparer.Ordinal);
            var deleted = new HashSet<string>(StringComparer.Ordinal);
            if (File.Exists(LedgerPath))
            {
                var lineNumber = 0;
                foreach (var line in File.ReadLines(LedgerPath))
                {
                    lineNumber++;
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    LedgerEntry? entry;
                    try
                    {
                        entry = JsonSerializer.Deserialize<LedgerEntry>(line, Json);
                    }
                    catch (JsonException)
                    {
                        errors.Add($"Journal Zeile {lineNumber}: nicht lesbar.");
                        continue;
                    }

                    if (entry is null)
                    {
                        continue;
                    }

                    entries++;
                    if (entry.Seq != expectedSeq)
                    {
                        errors.Add($"Journal Zeile {lineNumber}: Laufnummer {entry.Seq} statt {expectedSeq} (Eintrag fehlt oder eingefügt).");
                    }

                    if (entry.PreviousHash != previous)
                    {
                        errors.Add($"Journal Zeile {lineNumber}: Verkettung unterbrochen.");
                    }

                    if (HashEntry(entry) != entry.Hash)
                    {
                        errors.Add($"Journal Zeile {lineNumber}: Inhalt verändert (Hash stimmt nicht).");
                    }

                    previous = entry.Hash;
                    expectedSeq = entry.Seq + 1;
                    if (entry.ObjectId is not null && entry.ClientId is not null)
                    {
                        var key = Key(entry.ClientId, entry.ObjectId);
                        if (entry.Type == "store")
                        {
                            stored[key] = entry;
                        }
                        else if (entry.Type == "delete")
                        {
                            deleted.Add(key);
                        }
                    }
                }
            }

            var checkedObjects = 0;
            long bytes = 0;
            foreach (var (key, entry) in stored)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var contentPath = ContentPath(entry.ClientId!, entry.ObjectId!);
                var metadataPath = MetadataPath(entry.ClientId!, entry.ObjectId!);
                if (!File.Exists(metadataPath))
                {
                    errors.Add($"Objekt {entry.ObjectId}: Metadaten fehlen.");
                }
                else if (Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(metadataPath, cancellationToken))) != entry.MetadataSha256)
                {
                    errors.Add($"Objekt {entry.ObjectId}: Metadaten verändert.");
                }

                if (deleted.Contains(key))
                {
                    if (File.Exists(contentPath))
                    {
                        errors.Add($"Objekt {entry.ObjectId}: als gelöscht verzeichnet, Datei aber vorhanden.");
                    }

                    continue;
                }

                if (!File.Exists(contentPath))
                {
                    errors.Add($"Objekt {entry.ObjectId}: Datei fehlt.");
                    continue;
                }

                await using var stream = File.OpenRead(contentPath);
                var actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
                checkedObjects++;
                bytes += stream.Length;
                if (actual != entry.Sha256 || stream.Length != entry.Size)
                {
                    errors.Add($"Objekt {entry.ObjectId}: Inhalt verändert (SHA-256 stimmt nicht).");
                }
            }

            foreach (var file in Directory.EnumerateFiles(_options.PathOf("objects"), "*.bin", SearchOption.AllDirectories))
            {
                var clientDir = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(file)));
                if (!stored.ContainsKey(Key(clientDir ?? string.Empty, Path.GetFileNameWithoutExtension(file))))
                {
                    errors.Add($"Unbekannte Datei ohne Journaleintrag: {Path.GetRelativePath(_options.DataPath, file)}");
                }
            }

            var report = new VerificationReport(_clock.GetUtcNow(), errors.Count == 0, entries, previous, checkedObjects, bytes,
                deleted.Count, errors);
            var reportBytes = JsonSerializer.SerializeToUtf8Bytes(report, Indented);
            var reportPath = _options.PathOf("reports",
                $"verify-{report.At.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture)}.json");
            await File.WriteAllBytesAsync(reportPath, reportBytes, cancellationToken);
            MakeReadOnly(reportPath);
            await AppendAsync("verify", null, null, Convert.ToHexStringLower(SHA256.HashData(reportBytes)), null, null, null,
                errors.Count == 0 ? "ok" : $"{errors.Count} Fehler", cancellationToken);
            return report;
        }
        finally
        {
            _write.Release();
        }
    }

    /// <summary>
    /// Export für Betriebsprüfer (Datenträgerüberlassung): Dokumente, Metadaten, Journal, Prüfsummenliste,
    /// aktuelles Prüfprotokoll und ein Prüfskript – lesbar ohne EstrichONE.
    /// </summary>
    public async Task<string> ExportAsync(string? clientId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var report = await VerifyAsync(cancellationToken);
        var target = _options.PathOf("tmp", $"export-{Guid.NewGuid():N}.zip");
        List<(LedgerEntry Entry, bool Deleted, DateOnly RetainUntil)> selected;
        lock (_objects)
        {
            selected = [.. _objects.Values
                .Where(state => clientId is null || state.Stored.ClientId == clientId)
                .Where(state => from is null || DateOnly.FromDateTime(state.Stored.At.UtcDateTime) >= from)
                .Where(state => to is null || DateOnly.FromDateTime(state.Stored.At.UtcDateTime) <= to)
                .OrderBy(state => state.Stored.Seq)
                .Select(state => (state.Stored, state.DeletedBy is not null, state.RetainUntil))];
        }

        await using (var file = new FileStream(target, FileMode.CreateNew))
        await using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false))
        {
            var manifest = new StringBuilder("objekt_id;datei;dokumentart;sha256;bytes;archiviert_am;aufbewahren_bis;journal_nr;journal_hash;geloescht;quelle\n");
            var sums = new StringBuilder();
            foreach (var (entry, isDeleted, retainUntil) in selected)
            {
                var metadata = ReadMetadata(entry.ClientId!, entry.ObjectId!);
                var name = $"{entry.ObjectId}_{Safe(metadata?.Name ?? "dokument")}";
                if (!isDeleted)
                {
                    zip.CreateEntryFromFile(ContentPath(entry.ClientId!, entry.ObjectId!), $"dokumente/{name}", CompressionLevel.Optimal);
                    sums.Append(CultureInfo.InvariantCulture, $"{entry.Sha256}  dokumente/{name}\n");
                }

                zip.CreateEntryFromFile(MetadataPath(entry.ClientId!, entry.ObjectId!), $"metadaten/{entry.ObjectId}.json");
                var source = metadata?.Source is null ? string.Empty : string.Join(", ", metadata.Source.Select(pair => $"{pair.Key}={pair.Value}"));
                manifest.Append(CultureInfo.InvariantCulture,
                    $"{entry.ObjectId};{Csv(metadata?.Name)};{Csv(metadata?.DocumentType)};{entry.Sha256};{entry.Size};{entry.At:yyyy-MM-dd'T'HH:mm:ss'Z'};{retainUntil:yyyy-MM-dd};{entry.Seq};{entry.Hash};{(isDeleted ? "ja" : "nein")};{Csv(source)}\n");
            }

            await WriteTextAsync(zip, "verzeichnis.csv", manifest.ToString(), cancellationToken);
            await WriteTextAsync(zip, "sha256sums.txt", sums.ToString(), cancellationToken);
            zip.CreateEntryFromFile(LedgerPath, "journal.jsonl");
            await WriteTextAsync(zip, "pruefprotokoll.json", JsonSerializer.Serialize(report, Indented), cancellationToken);
            await WriteTextAsync(zip, "journal_pruefen.py", ExportTexts.VerifyScript, cancellationToken);
            await WriteTextAsync(zip, "LIESMICH.txt", ExportTexts.Readme(_options.Name, selected.Count, report, from, to), cancellationToken);
        }

        return target;
    }

    private static async Task WriteTextAsync(ZipArchive zip, string name, string content, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(name);
        await using var stream = entry.Open();
        await stream.WriteAsync(Encoding.UTF8.GetBytes(content), cancellationToken);
    }

    private async Task<LedgerEntry> AppendAsync(string type, string? clientId, string? objectId, string? sha256, long? size,
        string? metadataSha256, DateOnly? retainUntil, string? note, CancellationToken cancellationToken)
    {
        var entry = new LedgerEntry(_seq + 1, _clock.GetUtcNow(), type, clientId, objectId, sha256, size, metadataSha256, retainUntil,
            note, _head, string.Empty);
        entry = entry with { Hash = HashEntry(entry) };
        var line = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entry, Json) + "\n");
        await using (var ledger = new FileStream(LedgerPath, FileMode.Append, FileAccess.Write, FileShare.Read))
        {
            await ledger.WriteAsync(line, cancellationToken);
            ledger.Flush(flushToDisk: true);
        }

        Apply(entry);
        return entry;
    }

    private void Apply(LedgerEntry entry)
    {
        lock (_objects)
        {
            _seq = entry.Seq;
            _head = entry.Hash;
            if (entry.ClientId is null || entry.ObjectId is null)
            {
                return;
            }

            var key = Key(entry.ClientId, entry.ObjectId);
            switch (entry.Type)
            {
                case "store":
                    _objects[key] = new ObjectState { Stored = entry, RetainUntil = entry.RetainUntil ?? DateOnly.MaxValue };
                    break;
                case "retention" when _objects.TryGetValue(key, out var state) && entry.RetainUntil > state.RetainUntil:
                    state.RetainUntil = entry.RetainUntil.Value;
                    break;
                case "delete" when _objects.TryGetValue(key, out var state):
                    state.DeletedBy = entry;
                    break;
            }
        }
    }

    private ObjectMetadata? ReadMetadata(string clientId, string objectId)
    {
        var path = MetadataPath(clientId, objectId);
        return File.Exists(path) ? JsonSerializer.Deserialize<ObjectMetadata>(File.ReadAllBytes(path), Json) : null;
    }

    private static ArchiveReceipt ToReceipt(LedgerEntry entry, DateOnly retainUntil, bool deleted, ObjectMetadata? metadata) =>
        new(entry.ObjectId!, entry.Sha256!, entry.Size ?? 0, entry.At, retainUntil, entry.Seq, entry.Hash, deleted, metadata?.Name,
            metadata?.DocumentType);

    private static string Key(string clientId, string objectId) => clientId + "/" + objectId;

    private string ContentPath(string clientId, string objectId) => _options.PathOf("objects", clientId, objectId[..2], objectId + ".bin");

    private string MetadataPath(string clientId, string objectId) => _options.PathOf("objects", clientId, objectId[..2], objectId + ".json");

    private static void MakeReadOnly(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, ReadOnly);
        }
    }

    private static string? Limit(string? text) => text is null ? null : text.Length > 500 ? text[..500] : text;

    private static string Safe(string name) =>
        new([.. name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_')]);

    private static string Csv(string? value) => value is null ? string.Empty : value.Replace(';', ',').Replace('\n', ' ');
}
