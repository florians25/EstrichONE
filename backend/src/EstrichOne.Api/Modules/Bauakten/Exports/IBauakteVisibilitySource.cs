namespace EstrichOne.Api.Modules.Bauakten.Exports;

/// <summary>
/// Zusätzliche Sichtbarkeit aus anderen Modulen: Wer für eine Bauakte eingeplant ist, sieht sie auch ohne Zuständigkeit
/// oder Teamzugehörigkeit – sonst fehlen auf Handy und Tablet die Daten zum eigenen Einsatz („Daten nicht verfügbar“).
/// </summary>
public interface IBauakteVisibilitySource
{
    /// <summary>IDs der Bauakten, die der Benutzer zusätzlich sehen darf (wird in die Abfrage eingebaut).</summary>
    IQueryable<Guid> AdditionalVisibleIds(Guid userId);

    /// <summary>Ist die Bauakte eingeplant (Grundlage für „Mir zuweisen“ aus „Wer fährt wohin“)?</summary>
    Task<bool> IsPlannedAsync(Guid bauakteId, CancellationToken cancellationToken);
}
