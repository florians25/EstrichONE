namespace EstrichOne.Api.Modules.Bauakten.Exports;

/// <summary>Prüft Bezüge anderer Module auf Räume einer Bauakte (z. B. Foto aus einem Raum).</summary>
public interface IBauakteRooms
{
    Task<bool> BelongsToAsync(Guid bauakteId, Guid roomId, CancellationToken cancellationToken);
}
