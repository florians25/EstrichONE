using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

/// <param name="CanEdit">Gemeinsame Kalender: mit Planungsrecht; persönliche: nur der Besitzer.</param>
public sealed record CalendarResponse(
    Guid Id, string Name, string? Description, string Color, CalendarKind Kind, Guid? OwnerUserId, int SortOrder, bool IsActive, bool CanEdit, long Version);

public sealed record SaveCalendarRequest(
    [property: Required, MaxLength(100)] string Name, [property: MaxLength(500)] string? Description, [property: Required, MaxLength(7)] string Color,
    CalendarKind Kind = CalendarKind.Shared, int SortOrder = 0, bool IsActive = true);

/// <param name="Weekdays">Nur bei wöchentlich; leer = Wochentag des ersten Termins.</param>
/// <param name="Until">Letzter möglicher Tag (einschließlich) oder leer.</param>
/// <param name="Count">Anzahl der Termine oder leer.</param>
public sealed record RecurrenceRule(RecurrenceFrequency Frequency, int Interval = 1, IReadOnlyList<DayOfWeek>? Weekdays = null, DateOnly? Until = null, int? Count = null);

public enum CalendarEditScope
{
    /// <summary>Ganze Serie (bzw. der einzelne Termin).</summary>
    Series,

    /// <summary>Nur dieses Vorkommen einer Serie (wird als eigener Termin herausgelöst).</summary>
    Occurrence,

    /// <summary>Dieses und alle folgenden Vorkommen (Serie wird geteilt).</summary>
    Following,
}

/// <param name="Start">Ortszeit (ohne Zeitzone), z. B. 2026-11-04T08:00:00; ganztägig: Tag 00:00.</param>
/// <param name="End">Exklusiv; ganztägig: Folgetag 00:00 nach dem letzten Tag.</param>
/// <param name="SeriesId">Beim Bearbeiten eines Vorkommens (<paramref name="Scope"/> Occurrence/Following): die Serie.</param>
/// <param name="OccurrenceDate">Das bearbeitete Vorkommen der Serie.</param>
public sealed record SaveCalendarEventRequest(
    Guid CalendarId, [property: Required, MaxLength(200)] string Title, [property: MaxLength(300)] string? Location,
    [property: MaxLength(4000)] string? Description, CalendarEventKind Kind, [property: MaxLength(7)] string? Color, bool AllDay,
    DateTime Start, DateTime End, IReadOnlyList<Guid>? ParticipantUserIds = null, IReadOnlyList<Guid>? CrewIds = null, Guid? BauakteId = null,
    RecurrenceRule? Recurrence = null, Guid? SeriesId = null, DateOnly? OccurrenceDate = null, CalendarEditScope Scope = CalendarEditScope.Series);

/// <summary>Termin verschieben oder Dauer ändern (Ziehen im Kalender).</summary>
public sealed record MoveCalendarEventRequest(DateTime Start, DateTime End, bool AllDay, DateOnly? OccurrenceDate = null, CalendarEditScope Scope = CalendarEditScope.Series);

public sealed record CalendarEventDetail(
    Guid Id, Guid CalendarId, string Title, string? Location, string? Description, CalendarEventKind Kind, string? Color, bool AllDay,
    DateTime Start, DateTime End, IReadOnlyList<Guid> ParticipantUserIds, IReadOnlyList<Guid> CrewIds, Guid? BauakteId, RecurrenceRule Recurrence,
    IReadOnlyList<DateOnly> ExceptionDates, Guid? SeriesId, bool CanEdit, long Version);

/// <summary>
/// Terminkalender wie Google Kalender: mehrere Kalender (gemeinsam/persönlich, Farben), Tag/Woche/Monat/Agenda,
/// wiederkehrende Termine mit Ausnahmen, Teilnehmer und Kolonnen, Bezug zur Bauakte.
/// </summary>
internal static class CalendarEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/calendar").WithTags("Kalender");
        group.MapGet("/calendars", ListCalendarsAsync).WithName("ListCalendars")
            .WithSummary("Sichtbare Kalender (gemeinsame und der eigene persönliche, der bei Bedarf angelegt wird)")
            .RequirePermission(Permissions.PlanningRead);
        group.MapPut("/calendars/{id:guid}", SaveCalendarAsync).WithName("SaveCalendar")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.PlanningRead);
        group.MapDelete("/calendars/{id:guid}", DeleteCalendarAsync).WithName("DeleteCalendar").WithSummary("Kalender mit allen Terminen löschen")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.PlanningRead);

        group.MapGet("/events", ListEventsAsync).WithName("ListCalendarEvents")
            .WithSummary("Termine von–bis (Ortszeit, höchstens 400 Tage), Serien aufgelöst; calendars = kommagetrennte Kalender-IDs")
            .ProducesProblem(StatusCodes.Status400BadRequest).RequirePermission(Permissions.PlanningRead);
        group.MapGet("/events/{id:guid}", GetEventAsync).WithName("GetCalendarEvent")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.PlanningRead);
        group.MapPut("/events/{id:guid}", SaveEventAsync).WithName("SaveCalendarEvent")
            .WithSummary("Termin anlegen oder ändern; bei Serien auch nur ein Vorkommen oder alle folgenden")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.PlanningRead);
        group.MapPost("/events/{id:guid}/move", MoveEventAsync).WithName("MoveCalendarEvent")
            .WithSummary("Verschieben oder Dauer ändern (Ziehen); Serie, nur dieses Vorkommen oder alle folgenden")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.PlanningRead);
        group.MapDelete("/events/{id:guid}", DeleteEventAsync).WithName("DeleteCalendarEvent")
            .WithSummary("Termin löschen; bei Serien mit occurrence (JJJJ-MM-TT) und scope nur dieses oder alle folgenden")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.PlanningRead);
    }

    private static CalendarResponse ToResponse(Calendar c, bool canEdit) =>
        new(c.Id, c.Name, c.Description, c.Color, c.Kind, c.OwnerUserId, c.SortOrder, c.IsActive, canEdit, c.Version);

    private static CalendarEventDetail ToDetail(CalendarEvent e, bool canEdit) =>
        new(e.Id, e.CalendarId, e.Title, e.Location, e.Description, e.Kind, e.Color, e.AllDay, e.Start, e.End, e.ParticipantUserIds, e.CrewIds,
            e.BauakteId, new RecurrenceRule(e.Frequency, e.Interval, e.WeekdayList, e.Until, e.Count), e.ExceptionDates.Order().ToList(), e.SeriesId,
            canEdit, e.Version);

    private static async Task<Ok<List<CalendarResponse>>> ListCalendarsAsync(CalendarService service, CancellationToken cancellationToken)
    {
        var calendars = await service.VisibleCalendarsAsync(true, cancellationToken);
        var result = new List<CalendarResponse>();
        foreach (var calendar in calendars)
        {
            result.Add(ToResponse(calendar, await service.CanEditAsync(calendar, cancellationToken)));
        }

        return TypedResults.Ok(result);
    }

    private static async Task<Results<Ok<CalendarResponse>, ProblemHttpResult>> SaveCalendarAsync(
        Guid id, SaveCalendarRequest request, CalendarService service, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        try
        {
            var calendar = await db.Set<Calendar>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (calendar is null)
            {
                calendar = Calendar.Create(id, request.Kind, request.Kind == CalendarKind.Personal ? service.UserId : null, request.Name, request.Color);
                if (!await service.CanEditAsync(calendar, cancellationToken))
                {
                    return Forbidden("Gemeinsame Kalender legt die Planung an.");
                }

                db.Add(calendar);
            }
            else if (!await service.CanEditAsync(calendar, cancellationToken))
            {
                return Forbidden("Diesen Kalender dürfen Sie nicht ändern.");
            }

            calendar.Update(request.Name, request.Description, request.Color, request.SortOrder, request.IsActive);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(ToResponse(calendar, true));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteCalendarAsync(
        Guid id, CalendarService service, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var calendar = await db.Set<Calendar>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (calendar is null || (calendar.Kind == CalendarKind.Personal && calendar.OwnerUserId != service.UserId))
        {
            return NotFound("Kalender nicht gefunden");
        }

        if (!await service.CanEditAsync(calendar, cancellationToken))
        {
            return Forbidden("Diesen Kalender dürfen Sie nicht löschen.");
        }

        if (calendar.Kind == CalendarKind.Shared && await db.Set<Calendar>().CountAsync(c => c.Kind == CalendarKind.Shared, cancellationToken) == 1)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Letzter gemeinsamer Kalender",
                detail: "Mindestens ein gemeinsamer Kalender muss bleiben.", extensions: new Dictionary<string, object?> { ["code"] = "LAST_SHARED_CALENDAR" });
        }

        db.Remove(calendar);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<List<CalendarOccurrence>>, ProblemHttpResult>> ListEventsAsync(
        CalendarService service, CancellationToken cancellationToken, [FromQuery] string? from = null, [FromQuery] string? to = null,
        [FromQuery] string? calendars = null)
    {
        if (!PlanningService.TryParseDay(from, out var start) || !PlanningService.TryParseDay(to, out var end) || end < start
            || end.DayNumber - start.DayNumber > 400)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Ungültiger Zeitraum",
                detail: "from und to im Format JJJJ-MM-TT, höchstens 400 Tage.", extensions: new Dictionary<string, object?> { ["code"] = "INVALID_RANGE" });
        }

        List<Guid>? ids = null;
        if (!string.IsNullOrWhiteSpace(calendars))
        {
            ids = [];
            foreach (var part in calendars.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (Guid.TryParse(part, out var calendarId))
                {
                    ids.Add(calendarId);
                }
            }
        }

        var occurrences = await service.OccurrencesAsync(start.ToDateTime(TimeOnly.MinValue), end.AddDays(1).ToDateTime(TimeOnly.MinValue), ids, false,
            cancellationToken);
        return TypedResults.Ok(occurrences);
    }

    private static async Task<Results<Ok<CalendarEventDetail>, ProblemHttpResult>> GetEventAsync(Guid id, CalendarService service, CancellationToken cancellationToken)
    {
        var found = await service.FindReadableAsync(id, false, cancellationToken);
        return found is { } f ? TypedResults.Ok(ToDetail(f.Event, await service.CanEditAsync(f.Calendar, cancellationToken))) : NotFound("Termin nicht gefunden");
    }

    private static async Task<Results<Ok<CalendarEventDetail>, ProblemHttpResult>> SaveEventAsync(
        Guid id, SaveCalendarEventRequest request, CalendarService service, EstrichOneDbContext db, IBauakteAccess bauakten,
        CancellationToken cancellationToken)
    {
        var target = await db.Set<Calendar>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.CalendarId, cancellationToken);
        if (target is null || (target.Kind == CalendarKind.Personal && target.OwnerUserId != service.UserId))
        {
            return Validation("Bitte einen Kalender wählen.");
        }

        if (!await service.CanEditAsync(target, cancellationToken))
        {
            return Forbidden("In diesem Kalender dürfen Sie keine Termine eintragen.");
        }

        if (request.BauakteId is { } bauakteId && !await bauakten.CanReadAsync(bauakteId, cancellationToken))
        {
            return Validation("Die Bauakte gibt es nicht oder sie ist für Sie nicht sichtbar.");
        }

        var users = (request.ParticipantUserIds ?? []).Distinct().ToList();
        var crews = (request.CrewIds ?? []).Distinct().ToList();
        if (await db.Set<User>().CountAsync(u => users.Contains(u.Id), cancellationToken) != users.Count
            || await db.Set<Crew>().CountAsync(c => crews.Contains(c.Id), cancellationToken) != crews.Count)
        {
            return Validation("Unbekannte Teilnehmer oder Kolonnen.");
        }

        try
        {
            var item = await db.Set<CalendarEvent>().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (item is null && request.SeriesId is { } seriesId && request.OccurrenceDate is { } occurrence && request.Scope != CalendarEditScope.Series)
            {
                // Ein Vorkommen bzw. alle folgenden einer Serie bearbeiten: Serie anpassen, neuer Termin mit der gewünschten ID.
                var found = await service.FindReadableAsync(seriesId, true, cancellationToken);
                if (found is not { } series || !series.Event.IsRecurring)
                {
                    return NotFound("Terminserie nicht gefunden");
                }

                if (!await service.CanEditAsync(series.Calendar, cancellationToken))
                {
                    return Forbidden("Diesen Termin dürfen Sie nicht ändern.");
                }

                if (request.Scope == CalendarEditScope.Occurrence)
                {
                    series.Event.Skip(occurrence);
                }
                else
                {
                    series.Event.EndBefore(occurrence);
                }

                item = CalendarEvent.Create(id, request.CalendarId, service.UserId);
                item.DetachFrom(seriesId);
                db.Add(item);
                item.Update(request.CalendarId, request.Title, request.Location, request.Description, request.Kind, request.Color, request.AllDay,
                    request.Start, request.End, users, crews, request.BauakteId);
                var rule = request.Scope == CalendarEditScope.Following ? request.Recurrence ?? RuleOf(series.Event) : new RecurrenceRule(RecurrenceFrequency.None);
                item.Repeat(rule.Frequency, rule.Interval, rule.Weekdays ?? [], rule.Until, rule.Count);
            }
            else
            {
                if (item is null)
                {
                    item = CalendarEvent.Create(id, request.CalendarId, service.UserId);
                    db.Add(item);
                }
                else
                {
                    var current = await db.Set<Calendar>().AsNoTracking().SingleAsync(c => c.Id == item.CalendarId, cancellationToken);
                    if (!await service.CanEditAsync(current, cancellationToken) || (current.Kind == CalendarKind.Personal && current.OwnerUserId != service.UserId))
                    {
                        return Forbidden("Diesen Termin dürfen Sie nicht ändern.");
                    }
                }

                item.Update(request.CalendarId, request.Title, request.Location, request.Description, request.Kind, request.Color, request.AllDay,
                    request.Start, request.End, users, crews, request.BauakteId);
                var rule = request.Recurrence ?? new RecurrenceRule(RecurrenceFrequency.None);
                item.Repeat(rule.Frequency, rule.Interval, rule.Weekdays ?? [], rule.Until, rule.Count);
            }

            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(ToDetail(item, true));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }
    }

    private static RecurrenceRule RuleOf(CalendarEvent e) => new(e.Frequency, e.Interval, e.WeekdayList, e.Until, e.Count);

    private static async Task<Results<Ok<CalendarEventDetail>, ProblemHttpResult>> MoveEventAsync(
        Guid id, MoveCalendarEventRequest request, CalendarService service, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var found = await service.FindReadableAsync(id, true, cancellationToken);
        if (found is not { } f)
        {
            return NotFound("Termin nicht gefunden");
        }

        if (!await service.CanEditAsync(f.Calendar, cancellationToken))
        {
            return Forbidden("Diesen Termin dürfen Sie nicht ändern.");
        }

        var item = f.Event;
        try
        {
            if (!item.IsRecurring || request.Scope == CalendarEditScope.Series || request.OccurrenceDate is null)
            {
                if (item.IsRecurring && request.OccurrenceDate is { } day)
                {
                    // Ganze Serie um die Verschiebung des gezogenen Vorkommens verschieben.
                    var occurrenceStart = day.ToDateTime(TimeOnly.FromTimeSpan(item.Start.TimeOfDay));
                    var shift = request.Start - occurrenceStart;
                    item.Reschedule(request.AllDay, item.Start + shift, item.Start + shift + (request.End - request.Start));
                }
                else
                {
                    item.Reschedule(request.AllDay, request.Start, request.End);
                }

                await db.SaveChangesAsync(cancellationToken);
                return TypedResults.Ok(ToDetail(item, true));
            }

            var occurrence = request.OccurrenceDate.Value;
            var copy = CalendarEvent.Create(Guid.CreateVersion7(), item.CalendarId, service.UserId);
            copy.Update(item.CalendarId, item.Title, item.Location, item.Description, item.Kind, item.Color, request.AllDay, request.Start, request.End,
                item.ParticipantUserIds, item.CrewIds, item.BauakteId);
            copy.DetachFrom(item.Id);
            if (request.Scope == CalendarEditScope.Occurrence)
            {
                item.Skip(occurrence);
            }
            else
            {
                var remaining = item.Count is { } count ? Math.Max(1, count - item.CountBefore(occurrence)) : (int?)null;
                copy.Repeat(item.Frequency, item.Interval, ShiftWeekdays(item, request.Start.Date - occurrence.ToDateTime(TimeOnly.MinValue)), item.Until, remaining);
                item.EndBefore(occurrence);
            }

            db.Add(copy);
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(ToDetail(copy, true));
        }
        catch (ArgumentException exception)
        {
            return Validation(exception.Message);
        }
    }

    /// <summary>Bei „alle folgenden“ auf einen anderen Wochentag gezogen: Wochentage mitverschieben.</summary>
    private static List<DayOfWeek> ShiftWeekdays(CalendarEvent series, TimeSpan shift) =>
        series.WeekdayList.Select(d => (DayOfWeek)((((int)d + (int)Math.Round(shift.TotalDays)) % 7 + 7) % 7)).ToList();

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteEventAsync(
        Guid id, CalendarService service, EstrichOneDbContext db, CancellationToken cancellationToken,
        [FromQuery] string? occurrence = null, [FromQuery] string? scope = null)
    {
        var editScope = Enum.TryParse<CalendarEditScope>(scope, ignoreCase: true, out var parsed) ? parsed : CalendarEditScope.Series;
        var found = await service.FindReadableAsync(id, true, cancellationToken);
        if (found is not { } f)
        {
            return NotFound("Termin nicht gefunden");
        }

        if (!await service.CanEditAsync(f.Calendar, cancellationToken))
        {
            return Forbidden("Diesen Termin dürfen Sie nicht löschen.");
        }

        if (f.Event.IsRecurring && editScope != CalendarEditScope.Series
            && DateOnly.TryParseExact(occurrence, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            if (editScope == CalendarEditScope.Occurrence)
            {
                f.Event.Skip(day);
            }
            else if (day <= DateOnly.FromDateTime(f.Event.Start))
            {
                db.Remove(f.Event);
            }
            else
            {
                f.Event.EndBefore(day);
            }
        }
        else
        {
            db.Remove(f.Event);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult NotFound(string title) =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: title, extensions: new Dictionary<string, object?> { ["code"] = "NOT_FOUND" });

    private static ProblemHttpResult Forbidden(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden,
        title: "Keine Berechtigung", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = "PERMISSION_DENIED" });

    private static ProblemHttpResult Validation(string detail) => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity,
        title: "Ungültige Angaben", detail: detail, extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
}
