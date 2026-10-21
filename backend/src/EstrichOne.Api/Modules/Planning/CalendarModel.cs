using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Planning;

public enum CalendarKind
{
    /// <summary>Gemeinsamer Kalender der Firma (z. B. Firmenkalender, Kundentermine); pflegt die Planung.</summary>
    Shared,

    /// <summary>Persönlicher Kalender eines Benutzers; nur er selbst bearbeitet ihn.</summary>
    Personal,
}

/// <summary>Kalender mit Farbe – einzeln ein- und ausblendbar wie in Google Kalender.</summary>
public sealed class Calendar : IVersionedEntity
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Color { get; private set; } = "#0064D9";
    public CalendarKind Kind { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private Calendar()
    {
    }

    public static Calendar Create(Guid id, CalendarKind kind, Guid? ownerUserId, string name, string color)
    {
        if (kind == CalendarKind.Personal && ownerUserId is null)
        {
            throw new ArgumentException("Ein persönlicher Kalender braucht einen Besitzer.", nameof(ownerUserId));
        }

        var calendar = new Calendar { Id = id, Kind = kind, OwnerUserId = kind == CalendarKind.Personal ? ownerUserId : null };
        calendar.Update(name, null, color, 0, true);
        return calendar;
    }

    public void Update(string name, string? description, string color, int sortOrder, bool isActive)
    {
        Name = PlanningText.Required(name, 100, "Bitte einen Namen für den Kalender angeben.");
        Description = PlanningText.Optional(description, 500);
        Color = PlanningText.Color(color) ?? throw new ArgumentException("Bitte eine Farbe wählen.", nameof(color));
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}

/// <summary>Terminart: bestimmt die Standardfarbe, wenn der Termin keine eigene Farbe hat.</summary>
public enum CalendarEventKind
{
    Appointment,
    Meeting,
    CustomerAppointment,
    SiteVisit,
    Delivery,
    Vacation,
    Training,
    Private,
    Other,
}

public enum RecurrenceFrequency
{
    None,
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

/// <summary>
/// Termin (auch ganztägig, auch wiederkehrend). Zeiten sind Ortszeit des Betriebs (Europe/Berlin, „Wanduhrzeit“):
/// ein wöchentlicher Termin um 7:30 bleibt auch nach der Zeitumstellung um 7:30. <see cref="End"/> ist exklusiv,
/// bei ganztägigen Terminen 0:00 Uhr des Folgetags.
/// </summary>
public sealed class CalendarEvent : IVersionedEntity
{
    public const int MaxParticipants = 100;

    public Guid Id { get; private set; }
    public Guid CalendarId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Location { get; private set; }
    public string? Description { get; private set; }
    public CalendarEventKind Kind { get; private set; }

    /// <summary>Eigene Farbe (#RRGGBB) statt der Kalenderfarbe.</summary>
    public string? Color { get; private set; }
    public bool AllDay { get; private set; }
    public DateTime Start { get; private set; }
    public DateTime End { get; private set; }

    public RecurrenceFrequency Frequency { get; private set; }
    public int Interval { get; private set; } = 1;

    /// <summary>Wochentage bei wöchentlicher Wiederholung als Bitmaske (Bit 0 = Sonntag … Bit 6 = Samstag).</summary>
    public int Weekdays { get; private set; }
    public DateOnly? Until { get; private set; }
    public int? Count { get; private set; }

    /// <summary>Ausgelassene Vorkommen (gelöscht oder einzeln verschoben).</summary>
    public List<DateOnly> ExceptionDates { get; private set; } = [];

    /// <summary>Bei einzeln verschobenem Vorkommen: die ursprüngliche Serie.</summary>
    public Guid? SeriesId { get; private set; }
    public List<Guid> ParticipantUserIds { get; private set; } = [];
    public List<Guid> CrewIds { get; private set; } = [];
    public Guid? BauakteId { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private CalendarEvent()
    {
    }

    public static CalendarEvent Create(Guid id, Guid calendarId, Guid createdBy) => new() { Id = id, CalendarId = calendarId, CreatedByUserId = createdBy };

    public bool IsRecurring => Frequency != RecurrenceFrequency.None;

    public TimeSpan Duration => End - Start;

    public void Update(Guid calendarId, string title, string? location, string? description, CalendarEventKind kind, string? color,
        bool allDay, DateTime start, DateTime end, IReadOnlyCollection<Guid> participantUserIds, IReadOnlyCollection<Guid> crewIds, Guid? bauakteId)
    {
        CalendarId = calendarId;
        Title = PlanningText.Required(title, 200, "Bitte einen Titel für den Termin angeben.");
        Location = PlanningText.Optional(location, 300);
        Description = PlanningText.Optional(description, 4000);
        Kind = kind;
        Color = PlanningText.Color(color);
        Reschedule(allDay, start, end);
        var participants = participantUserIds.Distinct().ToList();
        if (participants.Count > MaxParticipants || crewIds.Count > 50)
        {
            throw new ArgumentException("Zu viele Teilnehmer.", nameof(participantUserIds));
        }

        ParticipantUserIds = participants;
        CrewIds = crewIds.Distinct().ToList();
        BauakteId = bauakteId;
    }

    public void Reschedule(bool allDay, DateTime start, DateTime end)
    {
        start = DateTime.SpecifyKind(start, DateTimeKind.Unspecified);
        end = DateTime.SpecifyKind(end, DateTimeKind.Unspecified);
        if (allDay)
        {
            start = start.Date;
            end = end.TimeOfDay == TimeSpan.Zero ? end.Date : end.Date.AddDays(1);
            if (end <= start)
            {
                end = start.AddDays(1);
            }
        }

        if (end <= start)
        {
            throw new ArgumentException("Das Ende muss nach dem Beginn liegen.", nameof(end));
        }

        if (end - start > TimeSpan.FromDays(366))
        {
            throw new ArgumentException("Ein Termin dauert höchstens ein Jahr.", nameof(end));
        }

        AllDay = allDay;
        Start = start;
        End = end;
        if (IsRecurring && Until is { } until && until < DateOnly.FromDateTime(start))
        {
            throw new ArgumentException("Das Ende der Wiederholung liegt vor dem ersten Termin.", nameof(start));
        }
    }

    public void Repeat(RecurrenceFrequency frequency, int interval, IReadOnlyCollection<DayOfWeek> weekdays, DateOnly? until, int? count)
    {
        if (frequency == RecurrenceFrequency.None)
        {
            Frequency = frequency;
            Interval = 1;
            Weekdays = 0;
            Until = null;
            Count = null;
            ExceptionDates = [];
            return;
        }

        if (interval is < 1 or > 99)
        {
            throw new ArgumentException("Die Wiederholung muss zwischen 1 und 99 liegen.", nameof(interval));
        }

        if (count is < 1 or > 999)
        {
            throw new ArgumentException("Höchstens 999 Wiederholungen.", nameof(count));
        }

        if (until is { } u && u < DateOnly.FromDateTime(Start))
        {
            throw new ArgumentException("Das Ende der Wiederholung liegt vor dem ersten Termin.", nameof(until));
        }

        if (End - Start > TimeSpan.FromDays(frequency == RecurrenceFrequency.Daily ? interval : 7 * interval) && frequency is RecurrenceFrequency.Daily or RecurrenceFrequency.Weekly)
        {
            throw new ArgumentException("Ein wiederkehrender Termin darf nicht länger als der Abstand der Wiederholung dauern.", nameof(interval));
        }

        Frequency = frequency;
        Interval = interval;
        Weekdays = frequency == RecurrenceFrequency.Weekly
            ? (weekdays.Count == 0 ? 1 << (int)Start.DayOfWeek : weekdays.Aggregate(0, (mask, day) => mask | (1 << (int)day)))
            : 0;
        Until = until;
        Count = count;
    }

    public void Skip(DateOnly occurrence)
    {
        if (!ExceptionDates.Contains(occurrence))
        {
            ExceptionDates = [.. ExceptionDates, occurrence];
        }
    }

    public void DetachFrom(Guid seriesId) => SeriesId = seriesId;

    /// <summary>Ende der Serie vor einem Vorkommen (für „dieses und alle folgenden“).</summary>
    public void EndBefore(DateOnly occurrence)
    {
        Until = occurrence.AddDays(-1);
        Count = null;
    }

    /// <summary>Anzahl der Serientermine vor einem Tag (für die Restanzahl beim Teilen einer Serie).</summary>
    public int CountBefore(DateOnly day) => Series().TakeWhile(start => DateOnly.FromDateTime(start) < day).Take(1000).Count();

    public IReadOnlyList<DayOfWeek> WeekdayList =>
        Enumerable.Range(0, 7).Where(bit => (Weekdays & (1 << bit)) != 0).Select(bit => (DayOfWeek)bit).ToList();

    /// <summary>Beginn aller Vorkommen, die den Zeitraum [from, to) berühren (höchstens 2000).</summary>
    public IEnumerable<DateTime> Occurrences(DateTime from, DateTime to)
    {
        var duration = Duration;
        if (!IsRecurring)
        {
            if (Start < to && Start + duration > from)
            {
                yield return Start;
            }

            yield break;
        }

        var produced = 0;
        var emitted = 0;
        foreach (var start in Series())
        {
            if (start >= to || (Until is { } until && DateOnly.FromDateTime(start) > until) || (Count is { } count && produced >= count))
            {
                yield break;
            }

            produced++;
            if (start + duration > from && !ExceptionDates.Contains(DateOnly.FromDateTime(start)))
            {
                yield return start;
                if (++emitted >= 2000)
                {
                    yield break;
                }
            }
        }
    }

    private IEnumerable<DateTime> Series()
    {
        var time = Start.TimeOfDay;
        switch (Frequency)
        {
            case RecurrenceFrequency.Daily:
                for (var i = 0; i < 40_000; i++)
                {
                    yield return Start.AddDays((double)i * Interval);
                }

                break;
            case RecurrenceFrequency.Weekly:
                var weekStart = Start.Date.AddDays(-(((int)Start.DayOfWeek + 6) % 7));
                for (var week = 0; week < 6000; week++)
                {
                    var monday = weekStart.AddDays(7.0 * week * Interval);
                    for (var offset = 0; offset < 7; offset++)
                    {
                        var day = monday.AddDays(offset);
                        if ((Weekdays & (1 << (int)day.DayOfWeek)) != 0 && day + time >= Start)
                        {
                            yield return day + time;
                        }
                    }
                }

                break;
            case RecurrenceFrequency.Monthly:
                for (var month = 0; month < 1200; month += Interval)
                {
                    var first = new DateTime(Start.Year, Start.Month, 1).AddMonths(month);
                    if (Start.Day <= DateTime.DaysInMonth(first.Year, first.Month))
                    {
                        yield return first.AddDays(Start.Day - 1) + time;
                    }
                }

                break;
            case RecurrenceFrequency.Yearly:
                for (var year = 0; year < 200; year += Interval)
                {
                    if (Start.Month == 2 && Start.Day == 29 && !DateTime.IsLeapYear(Start.Year + year))
                    {
                        continue;
                    }

                    yield return Start.AddYears(year);
                }

                break;
        }
    }
}

internal sealed class CalendarConfiguration : IEntityTypeConfiguration<Calendar>
{
    public void Configure(EntityTypeBuilder<Calendar> builder)
    {
        builder.ToTable("calendars");
        builder.ConfigureVersioned();
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(100);
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.Property(c => c.Color).HasMaxLength(7);
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => c.OwnerUserId);
    }
}

internal sealed class CalendarEventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> builder)
    {
        builder.ToTable("calendar_events", table => table.HasCheckConstraint("ck_calendar_events_range", "\"end\" > start"));
        builder.ConfigureVersioned();
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Title).HasMaxLength(200);
        builder.Property(e => e.Location).HasMaxLength(300);
        builder.Property(e => e.Description).HasMaxLength(4000);
        builder.Property(e => e.Color).HasMaxLength(7);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Frequency).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Start).HasColumnType("timestamp without time zone");
        builder.Property(e => e.End).HasColumnType("timestamp without time zone");
        builder.Property(e => e.ExceptionDates).HasColumnType("date[]");
        builder.Property(e => e.ParticipantUserIds).HasColumnType("uuid[]");
        builder.Property(e => e.CrewIds).HasColumnType("uuid[]");
        builder.Ignore(e => e.WeekdayList);
        builder.HasOne<Calendar>().WithMany().HasForeignKey(e => e.CalendarId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Bauakte>().WithMany().HasForeignKey(e => e.BauakteId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => new { e.CalendarId, e.Start });
        builder.HasIndex(e => e.BauakteId);
        builder.HasIndex(e => e.ParticipantUserIds).HasMethod("gin");
        builder.HasIndex(e => e.CrewIds).HasMethod("gin");
    }
}
