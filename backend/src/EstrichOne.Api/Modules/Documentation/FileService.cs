using System.Security.Cryptography;
using System.Threading.Channels;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Helpdesk.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Documentation;

public enum FileFailure
{
    NotFound,
    IdConflict,
    TooLarge,
    OffsetMismatch,
    ChecksumMismatch,
    TypeNotAllowed,
    BauakteNotFound,
}

/// <summary>Ankündigen, Hochladen (auch in Teilen), Prüfen und Freigeben von Dateien.</summary>
public sealed class FileService(
    EstrichOneDbContext db,
    LocalFileStorage storage,
    IBauakteAccess bauakten,
    ICurrentUser currentUser,
    IOptions<FileStorageSettings> settings,
    Channel<Guid> variantQueue,
    IBauakteHistory history,
    IUserPermissions permissions,
    ITicketAttachments tickets,
    IEnumerable<Exports.IFileReadGrant> grants,
    IBauakteAutoStatus autoStatus)
{
    public async Task<(FileRecord? File, bool Created, FileFailure? Failure)> AnnounceAsync(
        Guid id, Guid? bauakteId, FileKind kind, string originalName, string contentType, long sizeBytes, string sha256,
        DateTimeOffset? capturedAt, double? latitude, double? longitude, string? caption, CancellationToken cancellationToken)
    {
        if (sizeBytes > settings.Value.MaxBytesFor(kind))
        {
            return (null, false, FileFailure.TooLarge);
        }

        if (bauakteId is { } target && !await bauakten.CanReadAsync(target, cancellationToken))
        {
            return (null, false, FileFailure.BauakteNotFound);
        }

        var existing = await db.Set<FileRecord>().IgnoreQueryFilters([QueryFilters.NotDeleted])
            .SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (existing is not null)
        {
            return existing.UploadedByUserId == currentUser.UserId && existing.IsSameAnnouncement(bauakteId, kind, sha256, sizeBytes)
                ? (existing, false, null)
                : (null, false, FileFailure.IdConflict);
        }

        var file = FileRecord.Announce(id, bauakteId, kind, Path.GetFileName(originalName), contentType, sizeBytes, sha256,
            currentUser.UserId, capturedAt);
        file.SetLocation(latitude, longitude);
        file.Describe(caption);
        db.Add(file);
        if (bauakteId is { } owner)
        {
            history.Record(owner, BauakteHistoryActions.FileAdded, new Dictionary<string, string?> { ["kind"] = kind.ToString() });
        }
        await db.SaveChangesAsync(cancellationToken);
        return (file, true, null);
    }

    /// <summary>
    /// Vom Server erzeugte Datei (z. B. Abnahmeprotokoll als PDF) direkt als fertiges Dokument der Bauakte ablegen.
    /// Gespeichert wird mit dem nächsten <c>SaveChanges</c>; die Datei liegt dann schon am Zielort.
    /// </summary>
    public async Task<FileRecord> StoreGeneratedAsync(Guid bauakteId, Guid uploadedBy, string originalName, string contentType, byte[] content,
        string? category, string? folder, string? caption, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7(now);
        var sha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(content));
        var file = FileRecord.Announce(id, bauakteId, FileKind.Document, originalName, contentType, content.Length, sha, uploadedBy);
        file.Categorize(category);
        file.MoveTo(folder);
        file.Describe(caption);

        // Gleiches Dokument erneut erzeugt (z. B. Bericht nach Änderungen): als neue Fassung anhängen.
        var normalized = FileRecord.NormalizeFolder(folder);
        var candidates = await db.Set<FileRecord>().AsNoTracking()
            .Where(f => f.BauakteId == bauakteId && f.Kind == FileKind.Document && f.OriginalName == originalName && f.Category == category && f.Folder == normalized)
            .Select(f => f.Id).ToListAsync(cancellationToken);
        if (candidates.Count > 0)
        {
            var superseded = await db.Set<FileRecord>().AsNoTracking().Where(f => f.PreviousVersionId != null && candidates.Contains(f.PreviousVersionId.Value))
                .Select(f => f.PreviousVersionId!.Value).ToListAsync(cancellationToken);
            var latest = candidates.Except(superseded).Order().LastOrDefault();
            if (latest != Guid.Empty)
            {
                file.Supersede(latest);
            }
        }

        var key = LocalFileStorage.OriginalKey(id, now);
        var path = storage.PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content, cancellationToken);
        file.MarkStored(key, contentType, needsVariants: false);
        db.Add(file);
        return file;
    }

    /// <summary>
    /// Nimmt Inhalt an. Ohne <paramref name="offset"/> ist es die ganze Datei, sonst ein Teilstück ab dieser Position.
    /// Liegt alles vor, werden Größe, SHA-256 und Typ geprüft und die Datei freigegeben.
    /// </summary>
    public async Task<(FileRecord? File, long ReceivedBytes, FileFailure? Failure)> UploadAsync(
        Guid id, Stream content, long? offset, CancellationToken cancellationToken)
    {
        var file = await FindOwnAsync(id, cancellationToken);
        if (file is null)
        {
            return (null, 0, FileFailure.NotFound);
        }

        if (file.State == FileState.Stored)
        {
            return (file, file.SizeBytes, null);
        }

        var current = storage.PartialLength(id);
        if (offset is null && current > 0)
        {
            // Vollständiger Neuversuch: alten Teilstand verwerfen.
            storage.DiscardPartial(id);
            current = 0;
        }

        if ((offset ?? 0) != current)
        {
            return (file, current, FileFailure.OffsetMismatch);
        }

        long received;
        try
        {
            received = await storage.AppendAsync(id, content, file.SizeBytes, cancellationToken);
        }
        catch (FileTooLargeException)
        {
            storage.DiscardPartial(id);
            return (file, 0, FileFailure.TooLarge);
        }

        if (received < file.SizeBytes)
        {
            return (file, received, null);
        }

        var (hash, head) = await HashAndHeadAsync(storage.PartialPath(id), cancellationToken);
        if (!string.Equals(hash, file.Sha256, StringComparison.Ordinal))
        {
            storage.DiscardPartial(id);
            return (file, 0, FileFailure.ChecksumMismatch);
        }

        var detected = ContentSniffer.Detect(head);
        if (detected is null || !ContentSniffer.IsAllowed(file.Kind, detected))
        {
            storage.DiscardPartial(id);
            return (file, 0, FileFailure.TypeNotAllowed);
        }

        var key = LocalFileStorage.OriginalKey(file.Id, file.CreatedAt);
        storage.Commit(id, key);
        file.MarkStored(key, detected, ContentSniffer.HasVariants(detected));
        if (file.Kind == FileKind.Photo && file.BauakteId is { } photoBauakte)
        {
            // Erstes Foto vor Ort: „Geplant“ wird ggf. automatisch „In Ausführung“ (Einstellungen).
            await autoStatus.TryStartAsync(photoBauakte, BauakteAutoTrigger.Photo, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (file.Variants == VariantState.Pending)
        {
            variantQueue.Writer.TryWrite(file.Id);
        }

        return (file, file.SizeBytes, null);
    }

    public async Task<long?> ReceivedBytesAsync(Guid id, CancellationToken cancellationToken)
    {
        var file = await FindOwnAsync(id, cancellationToken);
        return file is null ? null : file.State == FileState.Stored ? file.SizeBytes : storage.PartialLength(id);
    }

    /// <summary>Datei, die der Benutzer sehen darf: über die Bauakte bzw. – ohne Bauakte – nur eigene Dateien.</summary>
    public async Task<FileRecord?> FindReadableAsync(Guid id, CancellationToken cancellationToken)
    {
        var file = await db.Set<FileRecord>().AsNoTracking().SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (file is null)
        {
            return null;
        }

        if (file.BauakteId is { } bauakteId)
        {
            return await bauakten.CanReadAsync(bauakteId, cancellationToken) ? file : null;
        }

        // Ohne Bauakte: eigene Dateien; Anhänge von Tickets zusätzlich für die Bearbeitung im Helpdesk.
        if (file.UploadedByUserId == currentUser.UserId
            || (await permissions.HasAsync(Permissions.HelpdeskAgent, cancellationToken) && await tickets.IsAttachedToTicketAsync(id, cancellationToken)))
        {
            return file;
        }

        // Freigaben anderer Module (Personaldokumente, Antworten im Helpdesk).
        foreach (var grant in grants)
        {
            if (await grant.CanReadAsync(id, cancellationToken))
            {
                return file;
            }
        }

        return null;
    }

    private Task<FileRecord?> FindOwnAsync(Guid id, CancellationToken cancellationToken) =>
        db.Set<FileRecord>().SingleOrDefaultAsync(f => f.Id == id && f.UploadedByUserId == currentUser.UserId, cancellationToken);

    private static async Task<(string Hash, byte[] Head)> HashAndHeadAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var head = new byte[16];
        var read = await stream.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
        stream.Position = 0;
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return (Convert.ToHexStringLower(hash), head[..read]);
    }
}
