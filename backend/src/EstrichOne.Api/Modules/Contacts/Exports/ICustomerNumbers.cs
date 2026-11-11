namespace EstrichOne.Api.Modules.Contacts.Exports;

/// <summary>Kundennummern aus dem eigenen Nummernkreis (Systemeinstellungen, Muster z. B. „K-{NNNNN}“).</summary>
public interface ICustomerNumbers
{
    /// <summary>Nächste freie Nummer; atomar hochgezählt, vorhandene Nummern werden übersprungen.</summary>
    Task<string> NextAsync(CancellationToken cancellationToken);

    /// <summary>Vergibt die nächste Nummer, wenn der Kunde noch keine hat.</summary>
    async Task AssignIfMissingAsync(Contact contact, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contact.CustomerNumber))
        {
            contact.AssignCustomerNumber(await NextAsync(cancellationToken));
        }
    }
}

/// <summary>Der Kunden-Nummernkreis ist ausgeschöpft (z. B. 609999 erreicht); Fehlercode wie bisher CUSTOMER_NUMBERS_EXHAUSTED.</summary>
public sealed class CustomerNumbersExhaustedException(string pattern)
    : Organization.Exports.NumberRangeExhaustedException(Organization.Exports.NumberRangeKeys.Customer, "Kunden", pattern)
{
    public const string Code = "CUSTOMER_NUMBERS_EXHAUSTED";
}
