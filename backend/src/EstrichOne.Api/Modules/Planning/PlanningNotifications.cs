using System.Globalization;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Notifications.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>Kolonnenmitglieder als Empfänger für Nachrichten aus dem Büro.</summary>
internal sealed class CrewAudience(EstrichOneDbContext db) : INotificationAudience
{
    public async Task<IReadOnlyList<Guid>> CrewMembersAsync(IReadOnlyCollection<Guid> crewIds, CancellationToken cancellationToken)
    {
        var crews = await db.Set<Crew>().AsNoTracking().Where(c => crewIds.Contains(c.Id)).ToListAsync(cancellationToken);
        return crews.SelectMany(c => c.MemberUserIds).Distinct().ToList();
    }

    public async Task<IReadOnlyList<AudienceOption>> CrewsAsync(CancellationToken cancellationToken)
    {
        var crews = await db.Set<Crew>().AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);
        return crews.Select(c => new AudienceOption(c.Id, c.Name, c.MemberUserIds.Count)).ToList();
    }
}

/// <summary>
/// Benachrichtigt die eingeplanten Mitarbeiter, wenn ihr Plan freigegeben wird (gebündelt: eine Nachricht je Person und Freigabe).
/// Bei einer erneuten Freigabe nur die Personen, deren Einsätze sich seit der letzten Freigabe geändert haben oder neu sind.
/// </summary>
internal static class PlanningNotifications
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <param name="previous">Einsätze (ID → Version) der vorigen Freigabe; <c>null</c> = erste Freigabe.</param>
    public static async Task ReleasedAsync(PlanningService planning, INotifier notifier, DateOnly from, DateOnly to,
        IReadOnlyDictionary<Guid, long>? previous, CancellationToken cancellationToken)
    {
        var update = previous is not null;
        var range = await planning.RangeAsync(from, to, null, cancellationToken);
        var byPerson = range.Assignments
            .SelectMany(a => a.People.Select(p => (p.Id, Assignment: a)))
            .GroupBy(x => x.Id, x => x.Assignment);
        var period = from == to ? from.ToString("ddd, dd.MM.", German) : $"{from.ToString("dd.MM.", German)}–{to.ToString("dd.MM.", German)}";
        foreach (var person in byPerson)
        {
            var items = person.OrderBy(a => a.Day).ThenBy(a => a.Sequence)
                .Where(a => previous is null || !previous.TryGetValue(a.Id, out var version) || version != a.Version).ToList();
            if (items.Count == 0)
            {
                continue; // Für diese Person hat sich nichts geändert.
            }

            var lines = items.Take(3).Select(a => (from == to ? string.Empty : a.Day.ToString("ddd dd.MM.", German) + ": ") + a.BauakteTitle).ToList();
            if (items.Count > 3)
            {
                lines.Add($"und {items.Count - 3} weitere");
            }

            await notifier.NotifyAsync([person.Key], new NotificationRequest(
                NotificationCategory.Planning,
                update ? $"Plan geändert: {period}" : $"Plan freigegeben: {period}",
                string.Join("\n", lines),
                $"/planung/wer-faehrt-wohin?tag={items[0].Day:yyyy-MM-dd}",
                "plan", null, update ? NotificationUrgency.High : NotificationUrgency.Normal,
                Event: update ? NotificationEvents.PlanChanged : NotificationEvents.PlanReleased), cancellationToken);
        }
    }
}
