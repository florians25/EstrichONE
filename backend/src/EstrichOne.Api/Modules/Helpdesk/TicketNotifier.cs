using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Mail.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Helpdesk;

/// <summary>
/// E-Mail bei Änderungen am Ticket (Vorlage „helpdesk.ticket_updated“): an den Melder, wenn die Bearbeitung antwortet
/// oder den Status ändert; an die zuständige Person, wenn der Melder antwortet. Ohne E-Mail-Konto passiert nichts.
/// </summary>
public sealed class TicketNotifier(EstrichOneDbContext db, IMailService mail, IOptions<EstrichOneOptions> options, ICurrentUser currentUser,
    ILogger<TicketNotifier> logger)
{
    public async Task NotifyAsync(Ticket ticket, string? comment, CancellationToken cancellationToken)
    {
        var recipient = ticket.ReporterUserId == currentUser.UserId ? ticket.AssigneeUserId : ticket.ReporterUserId;
        if (recipient is not { } userId || userId == currentUser.UserId)
        {
            return;
        }

        try
        {
            var user = await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);
            if (string.IsNullOrWhiteSpace(user?.Email))
            {
                return;
            }

            var link = options.Value.PublicUrl is { } url ? new Uri(url, $"/it-center/helpdesk/{ticket.Id}").ToString() : string.Empty;
            await mail.QueueTemplateAsync(MailTemplateKeys.HelpdeskUpdated, user.Email, new Dictionary<string, string?>
            {
                ["name"] = user.DisplayName, ["ticket"] = ticket.Number, ["titel"] = ticket.Title, ["status"] = TicketService.StatusName(ticket.Status),
                ["kommentar"] = comment ?? string.Empty, ["link"] = link,
            }, $"ticket:{ticket.Id}", cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Benachrichtigung ist Zusatz – das Ticket ist gespeichert.
            LogFailed(logger, exception, ticket.Number);
        }
    }

    private static readonly Action<ILogger, string, Exception?> Failed =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(4101, "TicketMailFailed"), "Benachrichtigung zu Ticket {Number} nicht eingereiht");

    private static void LogFailed(ILogger logger, Exception exception, string number) => Failed(logger, number, exception);
}
