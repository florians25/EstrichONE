using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Sync.Exports;

namespace EstrichOne.Api.Modules.Helpdesk;

/// <summary>
/// „Problem melden“ offline: Die App legt das Ticket als Outbox-Operation an; Anhänge laufen über die normale
/// Upload-Warteschlange (Dateien ohne Bauakte). Die Operation ist über ihre opId genau einmal wirksam (14, AK-1).
/// Optional <c>priority</c> (low, normal, high, urgent) wie beim Anlegen im Web.
/// </summary>
internal sealed class TicketSync(TicketService tickets, ICurrentUser currentUser, ISessionDevices sessions) : ISyncOperationHandler
{
    public IReadOnlyCollection<string> OperationTypes { get; } = ["ticket.create"];

    public async Task<SyncOperationResult> ApplyAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        var p = op.Payload;
        var input = new CreateTicketInput(op.EntityId, SyncPayload.Read<string>(p, "title") ?? string.Empty, SyncPayload.Read<string>(p, "description"),
            SyncPayload.Read<TicketCategory?>(p, "category") ?? TicketCategory.Other, SyncPayload.Read<TicketSource?>(p, "source") ?? TicketSource.Ios,
            SyncPayload.Read<bool>(p, "reporterBlocked"), SyncPayload.Read<Guid?>(p, "bauakteId"), p["diagnostics"]?.DeepClone(),
            SyncPayload.Read<List<Guid>>(p, "attachmentFileIds"), SyncPayload.Read<TicketPriority?>(p, "priority"));
        var deviceId = await sessions.GetCurrentSessionDeviceAsync(cancellationToken);
        var (ticket, _, failure, message) = await tickets.CreateAsync(input, currentUser.UserId, null, deviceId, cancellationToken);
        if (ticket is null)
        {
            return failure switch
            {
                TicketFailure.IdConflict => SyncOperationResult.Rejected(op.OpId, "ID_CONFLICT", message!),
                TicketFailure.BauakteNotFound => SyncOperationResult.Rejected(op.OpId, "BAUAKTE_NOT_FOUND", message!),
                _ => throw new SyncValidationException(message!),
            };
        }

        var detail = await tickets.DetailAsync(ticket, includeInternal: false, cancellationToken);
        return new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: ticket.Version, Entity: SyncPayload.ToNode(detail.Summary));
    }
}
