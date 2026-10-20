namespace EstrichOne.Api.Modules.Work.Exports;

/// <summary>Prüft Bezüge anderer Module auf Aufgaben und Mängel (z. B. Foto eines Mangels).</summary>
public interface IWorkItemReferences
{
    Task<bool> BelongsToAsync(Guid bauakteId, Guid workItemId, CancellationToken cancellationToken);
}
