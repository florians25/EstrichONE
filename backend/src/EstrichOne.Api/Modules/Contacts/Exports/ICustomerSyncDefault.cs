namespace EstrichOne.Api.Modules.Contacts.Exports;

/// <summary>
/// Vorgabe für „Mit Lexware Office abgleichen“ bei neuen Kunden (<see cref="Contact.LexwareSync"/>). Liefert das Modul
/// Lexware: eingeschaltetes Modul und die Einstellung „Neue Kunden standardmäßig mit Lexware Office abgleichen“.
/// </summary>
public interface ICustomerSyncDefault
{
    /// <summary>Wert für einen neuen Kunden: Modul eingeschaltet und Einstellung gesetzt.</summary>
    Task<bool> NewCustomersAsync(CancellationToken cancellationToken);

    /// <summary>Nur die Einstellung, unabhängig davon, ob das Modul eingeschaltet ist.</summary>
    Task<bool> PreferenceAsync(CancellationToken cancellationToken);
}
