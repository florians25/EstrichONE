namespace EstrichOne.Api.Modules.Helpdesk.Exports;

/// <summary>Für den Dateiservice: Darf die Bearbeitung eine Datei sehen, weil sie an einem Ticket hängt?</summary>
public interface ITicketAttachments
{
    Task<bool> IsAttachedToTicketAsync(Guid fileId, CancellationToken cancellationToken);
}
