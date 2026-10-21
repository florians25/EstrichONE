using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

public sealed record CrewRef(Guid Id, string Name, string? Color);

/// <summary>Ein Vorkommen eines Termins im angefragten Zeitraum (bei Serien je Wiederholung eines).</summary>
/// <param name="OccurrenceDate">Bei Serien: Tag des Vorkommens (zum Verschieben oder Löschen nur dieses Termins).</param>
/// <param name="Color">Eigene Farbe des Termins, sonst leer (dann <paramref name="CalendarColor"/> bzw. Farbe der Terminart).</param>
/// <param name="People">Alle Beteiligten: Teilnehmer und Mitglieder der eingeladenen Kolonnen.</param>
public sealed record CalendarOccurrence(
    Guid EventId, DateOnly? OccurrenceDate, DateTime Start, DateTime End, bool AllDay, string Title, string? Location, string? Description,
    CalendarEventKind Kind, string? Color, Guid CalendarId, string CalendarName, string CalendarColor, bool IsRecurring, bool CanEdit,
    IReadOnlyList<PersonRef> Participants, IReadOnlyList<CrewRef> Crews, IReadOnlyList<PersonRef> People,
    Guid? BauakteId, string? BauakteNumber, string? BauakteTitle, long Version);

/// <summary>Kalender und Termine lesen: Sichtbarkeit, Rechte, Wiederholungen auflösen.</summary>
public sealed class CalendarService(EstrichOneDbContext db, ICurrentUser currentUser, IUserPermissions permissions, IBauakteAccess bauakten)
{
    public const string DefaultPersonalColor = "#188918";

    public Guid UserId => currentUser.UserId;

    public async Task<bool> CanEditAsync(Calendar calendar, CancellationToken cancellationToken) =>
        calendar.Kind == CalendarKind.Personal
            ? calendar.OwnerUserId == currentUser.UserId
            : await permissions.HasAsync(Permissions.PlanningManage, cancellationToken);

    /// <summary>Gemeinsame Kalender und der eigene persönliche Kalender (wird beim ersten Aufruf angelegt).</summary>
    public async Task<List<Calendar>> VisibleCalendarsAsync(bool ensurePersonal, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var calendars = await db.Set<Calendar>().Where(c => c.Kind == CalendarKind.Shared || c.OwnerUserId == userId)
            .OrderBy(c => c.Kind).ThenBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(cancellationToken);
        if (ensurePersonal && userId != Guid.Empty && !calendars.Any(c => c.Kind == CalendarKind.Personal)
            && await db.Set<User>().AnyAsync(u => u.Id == userId, cancellationToken))
        {
            var personal = Calendar.Create(Guid.CreateVersion7(), CalendarKind.Personal, userId, "Mein Kalender", DefaultPersonalColor);
            db.Add(personal);
            await db.SaveChangesAsync(cancellationToken);
            calendars.Add(personal);
        }

        return calendars;
    }

    /// <summary>Termin, den der Benutzer sehen darf (Kalender sichtbar oder selbst Teilnehmer).</summary>
    public async Task<(CalendarEvent Event, Calendar Calendar)?> FindReadableAsync(Guid id, bool track, CancellationToken cancellationToken)
    {
        var query = track ? db.Set<CalendarEvent>() : db.Set<CalendarEvent>().AsNoTracking();
        var item = await query.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var calendar = await db.Set<Calendar>().AsNoTracking().SingleAsync(c => c.Id == item.CalendarId, cancellationToken);
        if (calendar.Kind == CalendarKind.Shared || calendar.OwnerUserId == currentUser.UserId)
        {
            return (item, calendar);
        }

        var crews = await MyCrewIdsAsync(cancellationToken);
        return item.ParticipantUserIds.Contains(currentUser.UserId) || item.CrewIds.Any(crews.Contains) ? (item, calendar) : null;
    }

    private async Task<List<Guid>> MyCrewIdsAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        return await db.Set<Crew>().Where(c => c.MemberUserIds.Contains(userId)).Select(c => c.Id).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Vorkommen im Zeitraum [from, to). <paramref name="calendarIds"/> = null: alle sichtbaren Kalender;
    /// <paramref name="teamOnly"/>: nur Termine mit Teilnehmern oder Kolonnen (für Plantafel und Konflikte).
    /// <paramref name="all"/>: ohne Sichtbarkeitsprüfung (Konfliktprüfung braucht alle Termine der Beteiligten).
    /// </summary>
    public async Task<List<CalendarOccurrence>> OccurrencesAsync(DateTime from, DateTime to, IReadOnlyCollection<Guid>? calendarIds, bool teamOnly,
        CancellationToken cancellationToken, bool all = false)
    {
        var userId = currentUser.UserId;
        var calendars = await db.Set<Calendar>().AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);
        var fromDay = DateOnly.FromDateTime(from);
        var query = db.Set<CalendarEvent>().AsNoTracking()
            .Where(e => e.Start < to && ((e.Frequency == RecurrenceFrequency.None && e.End > from)
                || (e.Frequency != RecurrenceFrequency.None && (e.Until == null || e.Until >= fromDay.AddDays(-366)))));
        if (teamOnly)
        {
            query = query.Where(e => e.ParticipantUserIds.Count > 0 || e.CrewIds.Count > 0);
        }

        if (calendarIds is not null)
        {
            query = query.Where(e => calendarIds.Contains(e.CalendarId));
        }

        var events = await query.ToListAsync(cancellationToken);
        var crews = await db.Set<Crew>().AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);
        var myCrews = crews.Values.Where(c => c.MemberUserIds.Contains(userId)).Select(c => c.Id).ToHashSet();
        var canManage = await permissions.HasAsync(Permissions.PlanningManage, cancellationToken);
        if (!all)
        {
            var mayReadShared = await permissions.HasAsync(Permissions.PlanningRead, cancellationToken);
            events = events.Where(e => calendars.TryGetValue(e.CalendarId, out var c) && c.IsActive
                && ((c.Kind == CalendarKind.Shared && mayReadShared) || c.OwnerUserId == userId
                    || e.ParticipantUserIds.Contains(userId) || e.CrewIds.Any(myCrews.Contains))).ToList();
        }

        IReadOnlyList<Guid> PeopleOf(CalendarEvent e) =>
            e.ParticipantUserIds.Concat(e.CrewIds.SelectMany(id => crews.TryGetValue(id, out var crew) ? crew.MemberUserIds : [])).Distinct().ToList();

        var userIds = events.SelectMany(PeopleOf).Distinct().ToList();
        var users = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var bauakteIds = events.Select(e => e.BauakteId).OfType<Guid>().Distinct().ToList();
        var visible = bauakteIds.Count == 0
            ? new Dictionary<Guid, (string? Number, string Title)>()
            : (await (await bauakten.VisibleAsync(db.Set<Bauakte>().AsNoTracking(), cancellationToken)).Where(b => bauakteIds.Contains(b.Id))
                .Select(b => new { b.Id, b.Number, b.Title }).ToListAsync(cancellationToken)).ToDictionary(b => b.Id, b => (b.Number, b.Title));

        PersonRef Person(Guid id) => new(id, users.GetValueOrDefault(id) ?? "Unbekannt");

        var result = new List<CalendarOccurrence>();
        foreach (var e in events)
        {
            var calendar = calendars[e.CalendarId];
            var canEdit = calendar.Kind == CalendarKind.Personal ? calendar.OwnerUserId == userId : canManage;
            var participants = e.ParticipantUserIds.Select(Person).OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList();
            var crewRefs = e.CrewIds.Where(crews.ContainsKey).Select(id => new CrewRef(id, crews[id].Name, crews[id].Color)).ToList();
            var people = PeopleOf(e).Select(Person).OrderBy(p => p.Name, StringComparer.CurrentCulture).ToList();
            (string? Number, string Title)? bauakte = e.BauakteId is { } bid && visible.TryGetValue(bid, out var b) ? b : null;
            foreach (var start in e.Occurrences(from, to))
            {
                result.Add(new CalendarOccurrence(e.Id, e.IsRecurring ? DateOnly.FromDateTime(start) : null, start, start + e.Duration, e.AllDay,
                    e.Title, e.Location, e.Description, e.Kind, e.Color, calendar.Id, calendar.Name, calendar.Color, e.IsRecurring, canEdit,
                    participants, crewRefs, people, e.BauakteId, bauakte?.Number, bauakte?.Title, e.Version));
            }
        }

        return result.OrderBy(o => o.Start).ThenBy(o => o.Title, StringComparer.CurrentCulture).ToList();
    }
}

/// <summary>Legt den gemeinsamen Firmenkalender an, wenn es noch keinen gemeinsamen Kalender gibt.</summary>
internal sealed class CalendarSeed(EstrichOneDbContext db) : IDataInitializer
{
    public int Order => 60;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (await db.Set<Calendar>().AnyAsync(c => c.Kind == CalendarKind.Shared, cancellationToken))
        {
            return;
        }

        var company = Calendar.Create(Guid.CreateVersion7(), CalendarKind.Shared, null, "Firmenkalender", "#0064D9");
        var customers = Calendar.Create(Guid.CreateVersion7(), CalendarKind.Shared, null, "Kundentermine", "#E76500");
        customers.Update(customers.Name, "Besichtigungen, Aufmaß und Abnahmen mit Kunden", customers.Color, 1, true);
        db.AddRange(company, customers);
        await db.SaveChangesAsync(cancellationToken);
    }
}
