namespace EstrichOne.Api.Modules.Planning.Exports;

/// <summary>Einsatz einer Bauakte für andere Module (Verkauf: Leistungszeitraum und Regiestunden).</summary>
/// <param name="Kind">Art des Einsatzes, z. B. <c>screed</c> (Estrich einbringen), <c>survey</c> (Aufmaß).</param>
/// <param name="People">Personen im Einsatz: direkt eingeplante bzw. die Mitglieder der Kolonne ohne die ausgenommenen.</param>
/// <param name="Hours">Dauer je Person aus Beginn und Ende, falls beide erfasst sind.</param>
public sealed record BauakteAssignmentInfo(Guid Id, DateOnly Day, string Kind, string Activity, int People, decimal? Hours);

/// <summary>Einsätze einer Bauakte (nur lesend).</summary>
public interface IBauakteAssignments
{
    /// <summary>Alle Einsätze der Bauakte, nach Tag sortiert.</summary>
    Task<IReadOnlyList<BauakteAssignmentInfo>> ListAsync(Guid bauakteId, CancellationToken cancellationToken);
}
