namespace EstrichOne.Api.Modules.Documentation.Exports;

/// <summary>
/// Erweitert die Leseprüfung für Dateien ohne Bauakte: Andere Module (Personal, Helpdesk) geben Dateien frei, die an
/// ihren Datensätzen hängen. Eine Freigabe genügt.
/// </summary>
public interface IFileReadGrant
{
    Task<bool> CanReadAsync(Guid fileId, CancellationToken cancellationToken);
}

/// <summary>Kurzinfo zu einer gespeicherten Datei (für Listen in anderen Modulen).</summary>
public sealed record StoredFileInfo(Guid Id, string OriginalName, string ContentType, long SizeBytes, Guid UploadedByUserId, Guid? BauakteId, bool IsStored);

public interface IStoredFiles
{
    Task<IReadOnlyDictionary<Guid, StoredFileInfo>> FindAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}
