namespace EstrichOne.Api.Modules.Bauakten.Exports;

/// <summary>
/// Sichtbarkeit von Bauakten für den angemeldeten Benutzer (docs/reengineering/03, Abschnitt 6).
/// Dateien, Notizen und Aufgaben erben die Sichtbarkeit ihrer Bauakte – andere Module prüfen hierüber.
/// </summary>
public interface IBauakteAccess
{
    Task<bool> CanReadAsync(Guid bauakteId, CancellationToken cancellationToken);

    /// <summary>Filtert eine Abfrage auf die für den Benutzer sichtbaren Bauakten.</summary>
    Task<IQueryable<Bauakte>> VisibleAsync(IQueryable<Bauakte> query, CancellationToken cancellationToken);
}
