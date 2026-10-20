using EstrichOne.Api.Modules.Bauakten.Exports;

namespace EstrichOne.Api.Modules.Bauakten;

internal sealed class BauakteFactory(BauakteCommands commands) : IBauakteFactory
{
    public async Task<(Guid Id, string? Number)?> CreateAsync(Guid id, string title, string? street, string? postalCode, string? city,
        string? description, Guid? customerId, BauakteStatus status, CancellationToken cancellationToken)
    {
        var (bauakte, outcome) = await commands.CreateAsync(
            new NewBauakte(id, title, street, postalCode, city, Description: description, CustomerId: customerId, InitialStatus: status), cancellationToken);
        return outcome is CreateBauakteOutcome.Created or CreateBauakteOutcome.AlreadyExists ? (bauakte!.Id, bauakte.Number) : null;
    }
}
