namespace EstrichOne.Api.Modules.Bauakten.Exports;

/// <summary>Kurzinfo für Listen anderer Module (Baustellen-Vorrat): Kunde, Fläche, Estricharten.</summary>
public sealed record BauakteSummary(Guid BauakteId, string? CustomerName, decimal TotalAreaSquareMeters, IReadOnlyList<string> ScreedTypes);

public interface IBauakteSummaries
{
    Task<IReadOnlyDictionary<Guid, BauakteSummary>> GetAsync(IReadOnlyCollection<Guid> bauakteIds, CancellationToken cancellationToken);
}
