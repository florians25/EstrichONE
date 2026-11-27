namespace EstrichOne.Api.Modules.Contacts.Exports;

/// <summary>Ergebnis der Prüfung vor dem Anlegen eines Kunden.</summary>
/// <param name="Active">Die Prüfung ist zuständig (Modul eingeschaltet, eingerichtet). Sonst wird ohne Prüfung angelegt.</param>
/// <param name="Available">Die Prüfung konnte ausgeführt werden; sonst nennt <paramref name="Message"/> den Grund.</param>
/// <param name="Candidates">Vorhandene Einträge im anderen System, die derselbe Kunde sein könnten (mit Gründen).</param>
/// <param name="CanImport">Der Nutzer darf einen Treffer als Kunden übernehmen.</param>
public sealed record CustomerCreationCheck(bool Active, bool Available, string? Message, IReadOnlyList<object> Candidates, bool CanImport)
{
    public static CustomerCreationCheck Inactive { get; } = new(false, true, null, [], false);
}

/// <summary>
/// Prüfung, die vor dem Anlegen eines Kunden läuft (Modul Lexware Office: Gibt es den Kunden dort schon?). Die
/// Kundenanlage speichert erst nach dieser Prüfung; bei Treffern oder ohne Antwort nur mit ausdrücklicher Bestätigung.
/// </summary>
public interface ICustomerCreationCheck
{
    Task<CustomerCreationCheck> CheckAsync(Contact draft, CancellationToken cancellationToken);
}
