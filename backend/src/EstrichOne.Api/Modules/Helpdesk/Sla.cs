using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using EstrichOne.Api.Infrastructure.Calendar;
using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Helpdesk;

/// <summary>Reaktions- und Lösungsziel je Priorität in Stunden Geschäftszeit.</summary>
/// <param name="AroundTheClock">Uhr läuft rund um die Uhr (z. B. für „dringend“), nicht nur in den Geschäftszeiten.</param>
public sealed record SlaTarget(TicketPriority Priority, double ResponseHours, double ResolutionHours, bool AroundTheClock = false);

/// <summary>Geschäftszeiten der Bearbeitung (Ortszeit Europe/Berlin).</summary>
/// <param name="Days">Arbeitstage, 1 = Montag … 7 = Sonntag.</param>
public sealed record BusinessHours(string Start = "07:00", string End = "17:00", IReadOnlyList<int>? Days = null)
{
    public static readonly IReadOnlyList<int> Weekdays = [1, 2, 3, 4, 5];

    public TimeOnly StartTime => TimeOnly.TryParse(Start, CultureInfo.InvariantCulture, out var t) ? t : new TimeOnly(7, 0);
    public TimeOnly EndTime => TimeOnly.TryParse(End, CultureInfo.InvariantCulture, out var t) ? t : new TimeOnly(17, 0);
    public IReadOnlyList<int> WorkDays => Days is { Count: > 0 } d ? d : Weekdays;

    public bool IsWorkDay(DayOfWeek day) => WorkDays.Contains(day == DayOfWeek.Sunday ? 7 : (int)day);
}

/// <summary>Einstellungen des Helpdesks (Einstellungen › Helpdesk im Web).</summary>
/// <param name="Holidays">Eigene Zusatztage (ISO-Datum, z. B. Betriebsruhe, Heiligabend), an denen die SLA-Uhr steht – zusätzlich zu den
/// gesetzlichen Feiertagen des Bundeslands.</param>
/// <param name="NotifyAgentsOnNew">Neue Tickets an die zuständige Gruppe bzw. alle Bearbeiter melden.</param>
/// <param name="AutoCloseDays">Gelöste Tickets nach so vielen Tagen ohne Widerspruch schließen.</param>
/// <param name="State">Bundesland (BW, BY, … TH): dessen gesetzliche Feiertage – auch die beweglichen um Ostern – berechnet der Server
/// je Jahr selbst. Leer = nur die eigenen Zusatztage.</param>
public sealed record HelpdeskSettings(
    IReadOnlyList<SlaTarget>? Targets = null,
    BusinessHours? Hours = null,
    IReadOnlyList<string>? Holidays = null,
    bool SlaEnabled = true,
    bool RatingEnabled = true,
    bool TimeTrackingEnabled = true,
    bool NotifyAgentsOnNew = true,
    bool SuggestArticles = true,
    int AutoCloseDays = 7,
    string? State = null)
{
    public static IReadOnlyList<SlaTarget> DefaultTargets { get; } =
    [
        new(TicketPriority.Urgent, 1, 4),
        new(TicketPriority.High, 4, 16),
        new(TicketPriority.Normal, 8, 40),
        new(TicketPriority.Low, 24, 80),
    ];

    public SlaTarget Target(TicketPriority priority) =>
        Targets?.FirstOrDefault(t => t.Priority == priority) ?? DefaultTargets.First(t => t.Priority == priority);

    private IReadOnlySet<DateOnly> ExtraDates =>
        (Holidays ?? []).Select(h => DateOnly.TryParse(h, CultureInfo.InvariantCulture, out var d) ? d : (DateOnly?)null).OfType<DateOnly>().ToHashSet();

    /// <summary>Gesetzliche Feiertage des Bundeslands und eigene Zusatztage.</summary>
    [JsonIgnore]
    public HolidayCalendar Calendar => new(State, ExtraDates);

    /// <summary>Werte begrenzen und fehlende Prioritäten mit Standards ergänzen.</summary>
    public HelpdeskSettings Normalize()
    {
        var targets = Enum.GetValues<TicketPriority>().Select(p =>
        {
            var t = Targets?.FirstOrDefault(x => x.Priority == p) ?? DefaultTargets.First(x => x.Priority == p);
            var response = Math.Clamp(t.ResponseHours, 0.25, 24 * 30);
            return t with { ResponseHours = response, ResolutionHours = Math.Clamp(t.ResolutionHours, response, 24 * 90) };
        }).ToList();
        var hours = Hours ?? new BusinessHours();
        if (hours.EndTime <= hours.StartTime)
        {
            hours = new BusinessHours();
        }

        hours = hours with
        {
            Start = hours.StartTime.ToString("HH:mm", CultureInfo.InvariantCulture), End = hours.EndTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            Days = hours.WorkDays.Where(d => d is >= 1 and <= 7).Distinct().Order().ToList(),
        };
        return this with
        {
            Targets = targets, Hours = hours,
            Holidays = ExtraDates.Order().Select(d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Take(200).ToList(),
            AutoCloseDays = Math.Clamp(AutoCloseDays, 1, 60),
            State = State?.Trim().ToUpperInvariant() is { } code && GermanHolidays.States.ContainsKey(code) ? code : null,
        };
    }
}

/// <summary>
/// Tage ohne SLA-Uhr: gesetzliche Feiertage des Bundeslands (je Jahr berechnet, inkl. Karfreitag, Ostermontag, Christi Himmelfahrt,
/// Pfingstmontag, Fronleichnam) und eigene Zusatztage.
/// </summary>
public sealed class HolidayCalendar(string? state, IReadOnlySet<DateOnly> extra)
{
    public static readonly HolidayCalendar None = new(null, new HashSet<DateOnly>());

    private static readonly ConcurrentDictionary<(string State, int Year), Dictionary<DateOnly, string>> Statutory = new();

    public string? State { get; } = state;

    public static HolidayCalendar Of(IReadOnlySet<DateOnly> dates) => new(null, dates);

    public bool Contains(DateOnly date) => extra.Contains(date) || (State is not null && StatutoryFor(State, date.Year).ContainsKey(date));

    /// <summary>Name des Feiertags bzw. „Zusatztag“, sonst null.</summary>
    public string? NameOf(DateOnly date) =>
        State is not null && StatutoryFor(State, date.Year).TryGetValue(date, out var name) ? name : extra.Contains(date) ? "Zusatztag" : null;

    /// <summary>Alle Tage eines Jahres (gesetzlich und eigene), sortiert.</summary>
    public IReadOnlyList<HelpdeskHoliday> For(int year)
    {
        var statutory = State is null ? [] : StatutoryFor(State, year).Select(h => new HelpdeskHoliday(h.Key, h.Value, true));
        var own = extra.Where(d => d.Year == year && (State is null || !StatutoryFor(State, year).ContainsKey(d)))
            .Select(d => new HelpdeskHoliday(d, "Zusatztag", false));
        return statutory.Concat(own).OrderBy(h => h.Date).ToList();
    }

    private static Dictionary<DateOnly, string> StatutoryFor(string state, int year) =>
        year is < 1900 or > 2200
            ? []
            : Statutory.GetOrAdd((state, year), key => GermanHolidays.For(key.Year, key.State).ToDictionary(h => h.Date, h => h.Name));
}

/// <param name="IsStatutory">Gesetzlicher Feiertag des Bundeslands (sonst eigener Zusatztag).</param>
public sealed record HelpdeskHoliday(DateOnly Date, string Name, bool IsStatutory);

/// <summary>Berechnung von Fälligkeiten in Geschäftszeit (rein, ohne Datenbank – siehe Tests).</summary>
public static class SlaCalculator
{
    public static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    /// <summary>Zeitpunkt nach <paramref name="duration"/> Geschäftszeit ab <paramref name="start"/>.</summary>
    public static DateTimeOffset AddBusinessTime(DateTimeOffset start, TimeSpan duration, BusinessHours hours, IReadOnlySet<DateOnly> holidays,
        TimeZoneInfo? zone = null) => AddBusinessTime(start, duration, hours, HolidayCalendar.Of(holidays), zone);

    /// <summary>Zeitpunkt nach <paramref name="duration"/> Geschäftszeit ab <paramref name="start"/>.</summary>
    public static DateTimeOffset AddBusinessTime(DateTimeOffset start, TimeSpan duration, BusinessHours hours, HolidayCalendar holidays,
        TimeZoneInfo? zone = null)
    {
        zone ??= Berlin;
        if (duration <= TimeSpan.Zero)
        {
            return start;
        }

        var remaining = duration;
        var local = TimeZoneInfo.ConvertTime(start, zone).DateTime;
        for (var i = 0; i < 3700; i++)
        {
            var date = DateOnly.FromDateTime(local);
            if (hours.IsWorkDay(local.DayOfWeek) && !holidays.Contains(date))
            {
                var open = date.ToDateTime(hours.StartTime);
                var close = date.ToDateTime(hours.EndTime);
                var from = local < open ? open : local;
                if (from < close)
                {
                    var available = close - from;
                    if (remaining <= available)
                    {
                        return ToZone(from + remaining, zone);
                    }

                    remaining -= available;
                }
            }

            local = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        }

        return start + duration;
    }

    private static DateTimeOffset ToZone(DateTime local, TimeZoneInfo zone)
    {
        // Lokale Zeit in der Lücke der Sommerzeitumstellung: eine Stunde später.
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    /// <summary>Geschäftszeit zwischen zwei Zeitpunkten (z. B. Dauer einer Rückfrage).</summary>
    public static TimeSpan BusinessTimeBetween(DateTimeOffset from, DateTimeOffset to, BusinessHours hours, IReadOnlySet<DateOnly> holidays,
        TimeZoneInfo? zone = null) => BusinessTimeBetween(from, to, hours, HolidayCalendar.Of(holidays), zone);

    /// <summary>Geschäftszeit zwischen zwei Zeitpunkten (z. B. Dauer einer Rückfrage).</summary>
    public static TimeSpan BusinessTimeBetween(DateTimeOffset from, DateTimeOffset to, BusinessHours hours, HolidayCalendar holidays,
        TimeZoneInfo? zone = null)
    {
        zone ??= Berlin;
        if (to <= from)
        {
            return TimeSpan.Zero;
        }

        var total = TimeSpan.Zero;
        var local = TimeZoneInfo.ConvertTime(from, zone).DateTime;
        var end = TimeZoneInfo.ConvertTime(to, zone).DateTime;
        for (var i = 0; i < 3700 && local < end; i++)
        {
            var date = DateOnly.FromDateTime(local);
            if (hours.IsWorkDay(local.DayOfWeek) && !holidays.Contains(date))
            {
                var open = date.ToDateTime(hours.StartTime);
                var close = date.ToDateTime(hours.EndTime);
                var a = local < open ? open : local;
                var b = end < close ? end : close;
                if (b > a)
                {
                    total += b - a;
                }
            }

            local = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        }

        return total;
    }

    /// <summary>Reaktions- und Lösungsziel für ein Ticket (angehaltene Zeit eingerechnet).</summary>
    public static (DateTimeOffset Response, DateTimeOffset Resolution) DueDates(DateTimeOffset created, TicketPriority priority, int pausedMinutes,
        HelpdeskSettings settings)
    {
        var target = settings.Target(priority);
        var hours = settings.Hours ?? new BusinessHours();
        var holidays = settings.Calendar;
        var paused = TimeSpan.FromMinutes(pausedMinutes);
        if (target.AroundTheClock)
        {
            return (created + TimeSpan.FromHours(target.ResponseHours), created + TimeSpan.FromHours(target.ResolutionHours) + paused);
        }

        return (AddBusinessTime(created, TimeSpan.FromHours(target.ResponseHours), hours, holidays),
            AddBusinessTime(created, TimeSpan.FromHours(target.ResolutionHours) + paused, hours, holidays));
    }

    /// <summary>Angehaltene Zeit einer Rückfrage in Minuten (Geschäftszeit bzw. rund um die Uhr).</summary>
    public static int PausedMinutes(DateTimeOffset from, DateTimeOffset to, TicketPriority priority, HelpdeskSettings settings)
    {
        var span = settings.Target(priority).AroundTheClock
            ? to - from
            : BusinessTimeBetween(from, to, settings.Hours ?? new BusinessHours(), settings.Calendar);
        return (int)Math.Max(0, Math.Round(span.TotalMinutes));
    }

    /// <summary>
    /// Verstrichene Zeit zwischen zwei Zeitpunkten so, wie die SLA-Uhr sie zählt: Geschäftszeit (ohne Wochenende, Feiertage,
    /// Zusatztage) bzw. rund um die Uhr bei Prioritäten mit „rund um die Uhr“. Für Kennzahlen zu Reaktions- und Lösungszeit.
    /// </summary>
    public static TimeSpan Elapsed(DateTimeOffset from, DateTimeOffset to, TicketPriority priority, HelpdeskSettings settings) =>
        settings.Target(priority).AroundTheClock
            ? (to > from ? to - from : TimeSpan.Zero)
            : BusinessTimeBetween(from, to, settings.Hours ?? new BusinessHours(), settings.Calendar);

    /// <summary>
    /// Stand der Ziele für Listen und Ampel: <c>met</c> erledigt im Ziel, <c>breached</c> erledigt nach dem Ziel,
    /// <c>paused</c> Rückfrage, <c>overdue</c> überschritten, <c>dueSoon</c> weniger als eine Stunde oder 20 % übrig, <c>ok</c>.
    /// </summary>
    public static SlaState State(Ticket t, DateTimeOffset now)
    {
        if (t.ResponseDueAt is null && t.ResolutionDueAt is null)
        {
            return new SlaState(null, null, null);
        }

        if (t.Status is TicketStatus.Resolved or TicketStatus.Closed)
        {
            var late = t.ResolvedAt is { } resolved && t.ResolutionDueAt is { } due && resolved > due;
            return new SlaState(late ? "breached" : "met", null, null);
        }

        var (dueAt, kind) = t.FirstResponseAt is null && t.ResponseDueAt is { } response && (t.ResolutionDueAt is null || response <= t.ResolutionDueAt)
            ? (t.ResponseDueAt, "response")
            : (t.ResolutionDueAt, "resolution");
        if (t.Status == TicketStatus.WaitingForReporter)
        {
            return new SlaState("paused", dueAt, kind);
        }

        if (dueAt is not { } at)
        {
            return new SlaState(null, null, null);
        }

        if (now > at)
        {
            return new SlaState("overdue", at, kind);
        }

        var total = at - t.CreatedAt;
        var left = at - now;
        return new SlaState(left < TimeSpan.FromHours(1) || left < total * 0.2 ? "dueSoon" : "ok", at, kind);
    }
}

/// <param name="State">met, breached, paused, overdue, dueSoon, ok – oder null ohne SLA.</param>
/// <param name="DueKind">response (erste Antwort) oder resolution (Lösung).</param>
public sealed record SlaState(string? State, DateTimeOffset? DueAt, string? DueKind);

/// <summary>Lesen und Speichern der Helpdesk-Einstellungen.</summary>
public sealed class HelpdeskSettingsStore(EstrichOneDbContext db)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private HelpdeskSettings? _cached;

    public async Task<HelpdeskSettings> GetAsync(CancellationToken cancellationToken)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var row = await db.Set<HelpdeskSettingsRow>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == HelpdeskSettingsRow.SingletonId, cancellationToken);
        HelpdeskSettings? parsed = null;
        try
        {
            parsed = row is null ? null : JsonSerializer.Deserialize<HelpdeskSettings>(row.Data, Json);
        }
        catch (JsonException)
        {
            // Unlesbare Daten: Standards verwenden.
        }

        return _cached = (parsed ?? new HelpdeskSettings()).Normalize();
    }

    public async Task<HelpdeskSettings> SaveAsync(HelpdeskSettings settings, CancellationToken cancellationToken)
    {
        var normalized = settings.Normalize();
        var row = await db.Set<HelpdeskSettingsRow>().SingleOrDefaultAsync(r => r.Id == HelpdeskSettingsRow.SingletonId, cancellationToken);
        if (row is null)
        {
            row = new HelpdeskSettingsRow();
            db.Add(row);
        }

        row.Set(JsonSerializer.Serialize(normalized, Json));
        await db.SaveChangesAsync(cancellationToken);
        return _cached = normalized;
    }
}
