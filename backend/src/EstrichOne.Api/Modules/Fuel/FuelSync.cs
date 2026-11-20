using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using EstrichOne.Api.Modules.Sync.Exports;

namespace EstrichOne.Api.Modules.Fuel;

/// <summary>
/// Tankbeleg aus der App (offline über die Outbox): <c>fuelReceipt.create</c>. Das Belegfoto ist vorher als eigene Datei ohne
/// Bauakte angekündigt (<c>file.announce</c>) und läuft über die Upload-Warteschlange. Die Belegnummer vergibt der Server.
/// </summary>
internal sealed class FuelSync(FuelService service, IUserPermissions permissions, ISystemSettings settings) : ISyncOperationHandler
{
    public IReadOnlyCollection<string> OperationTypes { get; } = ["fuelReceipt.create"];

    public async Task<SyncOperationResult> ApplyAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        if (!(await settings.GetAsync(cancellationToken)).Modules.IsEnabled(AppModules.Fuel))
        {
            return SyncOperationResult.Rejected(op.OpId, AppModules.DisabledCode, "Das Modul „Tankbelege“ ist ausgeschaltet.");
        }

        if (!await permissions.HasAsync(Permissions.FuelRecord, cancellationToken))
        {
            return SyncOperationResult.Rejected(op.OpId, "PERMISSION_DENIED", "Für Tankbelege fehlt die Berechtigung.");
        }

        var p = op.Payload;
        var source = SyncPayload.Read<string>(p, "source");
        var input = new FuelReceiptInput(
            SyncPayload.Read<Guid?>(p, "vehicleId") ?? throw new SyncValidationException("Bitte das Fahrzeug wählen."),
            SyncPayload.Read<DateOnly?>(p, "date") ?? throw new SyncValidationException("Bitte das Datum des Belegs angeben."),
            SyncPayload.Read<int?>(p, "odometerKm"),
            SyncPayload.Read<decimal?>(p, "liters") ?? throw new SyncValidationException("Bitte die getankte Menge in Litern angeben."),
            SyncPayload.Read<decimal?>(p, "amountGross") ?? throw new SyncValidationException("Bitte den Betrag brutto angeben."),
            SyncPayload.Read<FuelType?>(p, "fuelType") ?? FuelType.Diesel,
            SyncPayload.Read<FuelPaymentMethod?>(p, "paymentMethod") ?? FuelPaymentMethod.FuelCard,
            SyncPayload.Read<string>(p, "station"), SyncPayload.Read<string>(p, "notes"), SyncPayload.Read<Guid?>(p, "imageFileId"), null,
            source is "ios" or "android" ? source : "android");
        var result = await service.SaveAsync(op.EntityId, input, null, cancellationToken);
        if (result.Receipt is not { } receipt)
        {
            return result.Failure switch
            {
                FuelFailure.IdConflict or FuelFailure.NotFound => SyncOperationResult.Rejected(op.OpId, "ID_CONFLICT", result.Message ?? "ID bereits vergeben."),
                FuelFailure.Final => SyncOperationResult.Rejected(op.OpId, "ID_CONFLICT", result.Message ?? "Der Beleg ist bereits bearbeitet."),
                _ => throw new SyncValidationException(result.Message ?? "Ungültige Angaben."),
            };
        }

        var response = (await service.ResponsesAsync([receipt], cancellationToken))[0];
        return new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: receipt.Version, Entity: SyncPayload.ToNode(response));
    }
}
