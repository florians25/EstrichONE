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

    /// <summary>
    /// Eigene Datei ohne Bauakte als Beleg einordnen (z. B. <see cref="StoredFileCategories.FuelReceipt"/>): Die Archivregel
    /// der Belegart gilt dann für sie (GoBD-Aufbewahrung). Gespeichert wird mit dem nächsten <c>SaveChanges</c>.
    /// </summary>
    /// <returns><c>false</c>, wenn die Datei nicht existiert, zu einer Bauakte gehört oder von jemand anderem stammt.</returns>
    Task<bool> ClassifyOwnAsync(Guid fileId, Guid ownerUserId, string category, CancellationToken cancellationToken);
}

/// <summary>Belegarten von Dateien ohne Bauakte (Archivregeln).</summary>
public static class StoredFileCategories
{
    public const string FuelReceipt = "fuelReceipt";
}
