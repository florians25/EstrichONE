namespace EstrichOne.Api.Modules.Bauakten.Exports;

/// <summary>Fläche eines Raums mit Estrichaufbau (für Vorschläge anderer Module, z. B. Angebotspositionen).</summary>
/// <param name="ScreedType">Name der Estrichart aus den Stammdaten, falls gesetzt.</param>
public sealed record BauakteRoomArea(Guid Id, string Name, string? Floor, decimal? AreaSquareMeters, string? ScreedType, int? ThicknessMillimeters);

/// <summary>Prüft Bezüge anderer Module auf Räume einer Bauakte (z. B. Foto aus einem Raum).</summary>
public interface IBauakteRooms
{
    Task<bool> BelongsToAsync(Guid bauakteId, Guid roomId, CancellationToken cancellationToken);

    /// <summary>Räume der Bauakte mit Fläche und Aufbau („Räume und Flächen“), sortiert wie in der Bauakte.</summary>
    Task<IReadOnlyList<BauakteRoomArea>> AreasAsync(Guid bauakteId, CancellationToken cancellationToken);
}
