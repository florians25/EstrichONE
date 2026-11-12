namespace EstrichOne.Api.Modules.Contacts.Exports;

/// <summary>
/// Verknüpfung eines Kunden mit Daten eines anderen Moduls (z. B. „3 Bauakten“).
/// </summary>
/// <param name="Kind">Stabiler Schlüssel, z. B. <c>bauakten</c>, <c>parties</c>, <c>calls</c>.</param>
/// <param name="Label">Anzeigetext für die Anzahl, z. B. „Bauakten“.</param>
/// <param name="Count">Anzahl der verknüpften Datensätze (auch gelöschte im Papierkorb).</param>
/// <param name="Link">Pfad in der Weboberfläche, unter dem die Datensätze zu finden sind (z. B. <c>/bauakten?kunde={id}</c>).</param>
/// <param name="Items">Bis zu fünf Beispiele (Nummer und Titel) für die Meldung.</param>
public sealed record CustomerReference(string Kind, string Label, int Count, string? Link, IReadOnlyList<CustomerReferenceItem> Items);

/// <param name="Link">Pfad in der Weboberfläche zum einzelnen Datensatz.</param>
public sealed record CustomerReferenceItem(Guid Id, string Text, string? Link);

/// <summary>
/// Jedes Modul, das auf Kunden verweist (Bauakten, Projektbeteiligte, Anrufe, künftig Angebote und Rechnungen), meldet
/// hier seine Bezüge. Ein Kunde mit Bezügen wird nicht gelöscht, sondern archiviert oder anonymisiert.
/// </summary>
public interface ICustomerReferenceSource
{
    Task<IReadOnlyList<CustomerReference>> FindAsync(Guid contactId, CancellationToken cancellationToken);

    /// <summary>
    /// Beim Anonymisieren und beim Löschen: Kopien personenbezogener Daten des Kunden im eigenen Modul ersetzen (die Bezüge
    /// bleiben). Gespeichert wird mit dem nächsten <c>SaveChanges</c> (gleiche Transaktion wie der Kunde).
    /// </summary>
    Task AnonymizeAsync(Guid contactId, string replacementName, CancellationToken cancellationToken);
}
