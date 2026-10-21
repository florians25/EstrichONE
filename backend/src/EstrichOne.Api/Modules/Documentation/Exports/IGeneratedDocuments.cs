namespace EstrichOne.Api.Modules.Documentation.Exports;

/// <summary>
/// Vom Server erzeugte PDFs (Berichte, Protokolle, Bestellscheine …) automatisch als Dokument der Bauakte ablegen –
/// mit Dokumentart, Unterordner und Fassungen (gleicher Name an gleicher Stelle = neue Fassung).
/// </summary>
public interface IGeneratedDocuments
{
    /// <summary>Speichert mit dem nächsten SaveChanges; liefert die Datei-ID.</summary>
    Task<Guid> StoreAsync(Guid bauakteId, Guid uploadedBy, string fileName, byte[] pdf, string category, string? folder, string? caption,
        CancellationToken cancellationToken);

    /// <summary>PDF ohne Bauakte ablegen (z. B. Onboarding-Unterlagen für die Mitarbeiterakte); mit dem nächsten SaveChanges.</summary>
    Task<Guid> StoreLooseAsync(Guid uploadedBy, string fileName, byte[] pdf, CancellationToken cancellationToken);
}
