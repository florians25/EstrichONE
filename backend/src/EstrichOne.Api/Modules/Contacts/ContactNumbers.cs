using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EstrichOne.Api.Modules.Contacts;

/// <summary>Stand des Kunden-Nummernkreises (Alias für Nummernkreis „customer“ unter /settings/number-ranges), nächster Zählerwert und Vorschau.</summary>
/// <param name="Capacity">Höchster Zählerwert des Musters (z. B. 9999 bei „60{NNNN}“).</param>
/// <param name="Remaining">Noch freie Zählerwerte; 0 = Nummernkreis ausgeschöpft.</param>
public sealed record CustomerNumberingResponse(string Pattern, long NextNumber, string Preview, long Capacity, long Remaining);

/// <summary>Nächsten Zählerwert setzen (z. B. Start bei 1000 nach Übernahme aus einem Altsystem).</summary>
public sealed record SaveCustomerNumberingRequest([property: Range(1, 999_999_999)] long NextNumber);

/// <summary>
/// Kundennummern aus dem Nummernkreis „Kunden“ (Systemeinstellungen › Nummernkreise, Standard „60{NNNN}“). Die Vergabe
/// (atomar, vorhandene Nummern überspringen, Ende des Bereichs) übernimmt der gemeinsame Nummernkreis-Dienst.
/// </summary>
internal sealed class CustomerNumberGenerator(INumberRanges ranges) : ICustomerNumbers
{
    public async Task<string> NextAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await ranges.NextAsync(NumberRangeKeys.Customer, cancellationToken);
        }
        catch (NumberRangeExhaustedException exhausted) when (exhausted is not CustomerNumbersExhaustedException)
        {
            throw new CustomerNumbersExhaustedException(exhausted.Pattern);
        }
    }

    public static string Format(string pattern, int year, long number) => NumberPattern.Format(pattern, year, number);
}

internal static class ContactNumbers
{
    public static void Map(RouteGroupBuilder contacts)
    {
        contacts.MapGet("/numbering", GetAsync).WithName("GetCustomerNumbering")
            .WithSummary("Kunden-Nummernkreis: Muster, nächster Zählerwert und Vorschau der nächsten Kundennummer")
            .RequirePermission(Permissions.ContactsRead);
        contacts.MapPut("/numbering", SaveAsync).WithName("SaveCustomerNumbering")
            .WithSummary("Nächsten Zählerwert des Kunden-Nummernkreises setzen (das Muster steht in den Systemeinstellungen)")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.SettingsManage);
    }

    private static async Task<Ok<CustomerNumberingResponse>> GetAsync(INumberRanges ranges, CancellationToken cancellationToken)
    {
        var state = await ranges.StateAsync(NumberRangeKeys.Customer, cancellationToken);
        var capacity = state.RangeEnd ?? state.Capacity;
        return TypedResults.Ok(new CustomerNumberingResponse(state.Pattern, state.NextNumber, state.Preview, capacity, Math.Max(0, capacity - state.NextNumber + 1)));
    }

    private static async Task<Results<Ok<CustomerNumberingResponse>, ProblemHttpResult>> SaveAsync(
        SaveCustomerNumberingRequest request, INumberRanges ranges, CancellationToken cancellationToken)
    {
        if (request.NextNumber is < 1 or > 999_999_999)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Ungültiger Wert",
                detail: "Bitte eine Zahl zwischen 1 und 999999999 angeben.", extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
        }

        await ranges.SetNextAsync(NumberRangeKeys.Customer, request.NextNumber, cancellationToken);
        return await GetAsync(ranges, cancellationToken);
    }
}
