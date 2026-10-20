namespace EstrichOne.Api.Modules.Bauakten.Exports;

/// <summary>Bauakte aus einem anderen Modul anlegen (z. B. Anfrage → Bauakte), mit Nummer und Verlauf.</summary>
public interface IBauakteFactory
{
    /// <returns>ID und Nummer der neuen Bauakte; <c>null</c>, wenn der Kunde nicht existiert.</returns>
    Task<(Guid Id, string? Number)?> CreateAsync(Guid id, string title, string? street, string? postalCode, string? city, string? description,
        Guid? customerId, BauakteStatus status, CancellationToken cancellationToken);
}
