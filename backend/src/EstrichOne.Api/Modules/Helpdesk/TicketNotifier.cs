using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Mail.Exports;
using EstrichOne.Api.Modules.Notifications.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Helpdesk;

/// <summary>
/// Benachrichtigungen zum Ticket. Melder und Beobachter: Antwort oder Statuswechsel der Bearbeitung (Postfach, Push und
/// E-Mail-Vorlage „helpdesk.ticket_updated“). Bearbeitung: Antwort des Melders, neues Ticket für die Gruppe, Zuweisung,
/// Erwähnung, überschrittenes Ziel. Push und Postfach zeigen nur Nummer, Titel und Status, nie Kommentartexte.
/// Ist das Modul abgeschaltet, verwirft die Zustellung die Helpdesk-Ereignisse.
/// </summary>
public sealed class TicketNotifier(EstrichOneDbContext db, IMailService mail, IOptions<EstrichOneOptions> options, ICurrentUser currentUser,
    INotifier notifier, IRoleAssignments roles, ILogger<TicketNotifier> logger)
{
    private static string Link(Ticket ticket) => $"/it-center/helpdesk/{ticket.Id}";

    /// <summary>Antwort oder Statuswechsel: an die andere Seite (Melder und Beobachter bzw. zuständige Person).</summary>
    public async Task NotifyAsync(Ticket ticket, string? comment, CancellationToken cancellationToken)
    {
        var fromReporterSide = ticket.IsVisibleTo(currentUser.UserId);
        var recipients = (fromReporterSide
                ? new[] { ticket.AssigneeUserId }
                : new[] { ticket.ReporterUserId }.Concat(ticket.WatcherUserIds.Select(w => (Guid?)w)))
            .OfType<Guid>().Where(id => id != currentUser.UserId).Distinct().ToList();
        if (recipients.Count == 0)
        {
            return;
        }

        foreach (var userId in recipients)
        {
            var reporterSide = ticket.IsVisibleTo(userId);
            await notifier.NotifyAsync([userId], new NotificationRequest(
                NotificationCategory.Ticket,
                reporterSide ? $"Antwort zu Ticket {ticket.Number}" : $"Rückmeldung zu Ticket {ticket.Number}",
                $"{ticket.Title} · {TicketService.StatusName(ticket.Status)}",
                Link(ticket), "ticket", ticket.Id, Event: NotificationEvents.TicketUpdated), cancellationToken);
            await MailAsync(ticket, userId, comment, cancellationToken);
        }
    }

    /// <summary>Neues Ticket: an die zugewiesene Person, sonst an die Gruppe, sonst an alle Bearbeiter.</summary>
    public async Task NotifyCreatedAsync(Ticket ticket, HelpdeskSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            if (ticket.AssigneeUserId is { } assignee)
            {
                await NotifyAssignedAsync(ticket, assignee, cancellationToken);
                return;
            }

            if (!settings.NotifyAgentsOnNew)
            {
                return;
            }

            var agents = await roles.GetUserIdsWithPermissionAsync(Permissions.HelpdeskAgent, cancellationToken);
            IEnumerable<Guid> recipients = agents;
            if (ticket.GroupId is { } groupId
                && await db.Set<HelpdeskGroup>().AsNoTracking().SingleOrDefaultAsync(g => g.Id == groupId, cancellationToken) is { } group)
            {
                var members = group.MemberUserIds.Where(agents.Contains).ToList();
                if (members.Count > 0)
                {
                    recipients = members;
                }
            }

            await notifier.NotifyAsync(recipients.Where(id => id != ticket.ReporterUserId && id != ticket.CreatedByUserId).ToList(), new NotificationRequest(
                NotificationCategory.Ticket, $"Neues Ticket {ticket.Number}",
                $"{ticket.Title} · {TicketService.PriorityName(ticket.Priority)}{(ticket.ReporterBlocked ? " · kann nicht arbeiten" : string.Empty)}",
                Link(ticket), "ticket", ticket.Id,
                ticket.ReporterBlocked || ticket.Priority == TicketPriority.Urgent ? NotificationUrgency.High : NotificationUrgency.Normal,
                DedupKey: $"ticket.created:{ticket.Id}", Event: NotificationEvents.TicketCreated), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception, ticket.Number);
        }
    }

    /// <summary>Von der IT erfasst (Anruf, E-Mail, vor Ort): Der Melder erfährt Nummer und Titel und kann den Verlauf verfolgen.</summary>
    public async Task NotifyRecordedAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        if (ticket.ReporterUserId is not { } reporter || reporter == currentUser.UserId)
        {
            return;
        }

        try
        {
            await notifier.NotifyAsync([reporter], new NotificationRequest(
                NotificationCategory.Ticket, $"Ticket {ticket.Number} für Sie angelegt",
                $"{ticket.Title} · aufgenommen per {TicketService.ChannelName(ticket.Channel)}", Link(ticket), "ticket", ticket.Id,
                DedupKey: $"ticket.recorded:{ticket.Id}", Event: NotificationEvents.TicketUpdated), cancellationToken);
            await MailAsync(ticket, reporter, $"Die IT hat Ihre Meldung ({TicketService.ChannelName(ticket.Channel)}) als Ticket aufgenommen.", cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(logger, exception, ticket.Number);
        }
    }

    public Task NotifyAssignedAsync(Ticket ticket, Guid assignee, CancellationToken cancellationToken) =>
        assignee == currentUser.UserId
            ? Task.CompletedTask
            : notifier.NotifyAsync([assignee], new NotificationRequest(
                NotificationCategory.Ticket, $"Ticket {ticket.Number} Ihnen zugewiesen", $"{ticket.Title} · {TicketService.PriorityName(ticket.Priority)}",
                Link(ticket), "ticket", ticket.Id, Event: NotificationEvents.TicketAssigned), cancellationToken);

    public Task NotifyMentionedAsync(Ticket ticket, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
        userIds.Count == 0
            ? Task.CompletedTask
            : notifier.NotifyAsync(userIds.Where(id => id != currentUser.UserId).ToList(), new NotificationRequest(
                NotificationCategory.Ticket, $"Erwähnung in Ticket {ticket.Number}", ticket.Title, Link(ticket), "ticket", ticket.Id,
                Event: NotificationEvents.TicketMentioned), cancellationToken);

    public Task NotifyOverdueAsync(Ticket ticket, IReadOnlyCollection<Guid> userIds, string kind, CancellationToken cancellationToken) =>
        notifier.NotifyAsync(userIds, new NotificationRequest(
            NotificationCategory.Ticket, $"Ticket {ticket.Number}: {(kind == "response" ? "Reaktionsziel" : "Lösungsziel")} überschritten",
            $"{ticket.Title} · {TicketService.PriorityName(ticket.Priority)}", Link(ticket), "ticket", ticket.Id, NotificationUrgency.High,
            DedupKey: $"ticket.overdue:{ticket.Id}:{kind}:{ticket.ResolutionDueAt:O}", Event: NotificationEvents.TicketOverdue), cancellationToken);

    private async Task MailAsync(Ticket ticket, Guid userId, string? comment, CancellationToken cancellationToken)
    {
        try
        {
            var user = await db.Set<User>().AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);
            if (string.IsNullOrWhiteSpace(user?.Email))
            {
                return;
            }

            var link = options.Value.PublicUrl is { } url ? new Uri(url, Link(ticket)).ToString() : string.Empty;
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
