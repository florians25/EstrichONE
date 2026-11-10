using Microsoft.AspNetCore.Http.HttpResults;

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

/// <summary>Der Kunden-Nummernkreis ist ausgeschöpft (z. B. 609999 erreicht).</summary>
public sealed class CustomerNumbersExhaustedException(string pattern)
    : InvalidOperationException($"Der Kunden-Nummernkreis „{pattern}“ ist ausgeschöpft. Bitte in den Einstellungen (Bauakten › Nummernkreis Kunden) ein neues Muster festlegen.")
{
    public const string Code = "CUSTOMER_NUMBERS_EXHAUSTED";

    /// <summary>Antwort für REST-Endpunkte (409).</summary>
    public ProblemHttpResult ToProblem() => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Nummernkreis ausgeschöpft",
        detail: Message, extensions: new Dictionary<string, object?> { ["code"] = Code });
}
