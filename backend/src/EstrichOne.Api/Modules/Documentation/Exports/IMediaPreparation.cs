namespace EstrichOne.Api.Modules.Documentation.Exports;

/// <summary>Geprüfter und verdichteter Inhalt (Foto oder PDF) mit Vorschaubild für Fotos.</summary>
/// <param name="Thumbnail">WebP, lange Kante höchstens 480 px, ohne Metadaten; nur bei Fotos.</param>
public sealed record PreparedMedia(byte[] Content, string ContentType, bool IsImage, byte[]? Thumbnail, int? Width, int? Height);

/// <summary>
/// Inhaltsprüfung und Verdichtung des Dateiservices für Module, die Dateien selbst ablegen (z. B. Chat-Anhänge, verschlüsselt):
/// gleiche Typprüfung (Magic Bytes) und gleiche Verdichtung wie bei Bauakte-Fotos/PDFs (Einstellungen → Speicher).
/// </summary>
public interface IMediaPreparation
{
    /// <summary>Erlaubt: JPEG, PNG, WebP, HEIC (→ JPEG) und PDF. Sonst <c>null</c>.</summary>
    Task<PreparedMedia?> PrepareAsync(byte[] content, CancellationToken cancellationToken);

    /// <summary>Kleines quadratisches Bild (z. B. Gruppenbild), WebP; <c>null</c>, wenn kein Bild.</summary>
    byte[]? Square(byte[] content, int size);
}
