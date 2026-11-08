using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Notifications.Exports;

namespace EstrichOne.Api.Modules.Feedback;

/// <summary>
/// Benachrichtigungen zu Meldungen: neue Meldungen und Rückmeldungen der Einreicher an die IT (Recht feedback.manage,
/// Ereignis „feedback.submitted“), Statuswechsel und Antworten an den Einreicher („feedback.updated“). Titel und Text
/// nennen nur Nummer, Titel und Status – nie den Inhalt einer Antwort.
/// </summary>
public sealed class FeedbackNotifier(INotifier notifier, IRoleAssignments roles)
{
    public static string ReporterLink(Guid id) => $"/meine-meldungen/{id}";

    public static string ManageLink(Guid id) => $"/it-center/meldungen/{id}";

    public async Task SubmittedAsync(FeedbackItem item, CancellationToken cancellationToken)
    {
        var managers = await roles.GetUserIdsWithPermissionAsync(Permissions.FeedbackManage, cancellationToken);
        await notifier.NotifyAsync(managers, new NotificationRequest(NotificationCategory.Ticket,
            $"{(item.Kind == FeedbackKind.Bug ? "Neue Fehlermeldung" : "Neuer Funktionswunsch")} {item.Number}", $"{item.Title} · {item.Area}",
            ManageLink(item.Id), "feedback", item.Id, Event: NotificationEvents.FeedbackSubmitted), cancellationToken);
    }

    /// <summary>Der Einreicher hat geantwortet: an die zuständige Person, sonst an alle mit feedback.manage.</summary>
    public async Task ReporterRepliedAsync(FeedbackItem item, CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> recipients = item.AssigneeUserId is { } assignee
            ? [assignee]
            : await roles.GetUserIdsWithPermissionAsync(Permissions.FeedbackManage, cancellationToken);
        await notifier.NotifyAsync(recipients, new NotificationRequest(NotificationCategory.Ticket,
            $"Rückmeldung zu Meldung {item.Number}", item.Title, ManageLink(item.Id), "feedback", item.Id,
            Event: NotificationEvents.FeedbackSubmitted), cancellationToken);
    }

    public Task ReporterUpdatedAsync(FeedbackItem item, bool reply, CancellationToken cancellationToken) =>
        notifier.NotifyAsync([item.ReporterUserId], new NotificationRequest(NotificationCategory.Ticket,
            reply ? $"Antwort zu Meldung {item.Number}" : $"Status geändert: Meldung {item.Number}",
            reply ? item.Title : $"{item.Title} · {FeedbackService.StatusName(item.Status)}",
            ReporterLink(item.Id), "feedback", item.Id, Event: NotificationEvents.FeedbackUpdated), cancellationToken);
}
