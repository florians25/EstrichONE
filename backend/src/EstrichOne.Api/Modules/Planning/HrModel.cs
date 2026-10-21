using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Planning;

public enum AbsenceRequestStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled,
}

/// <summary>Urlaubsantrag (auch Sonderurlaub, Berufsschule, Sonstiges): beantragt → genehmigt (wird Abwesenheit) oder abgelehnt.</summary>
public sealed class AbsenceRequest : IVersionedEntity
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public DateOnly From { get; private set; }
    public DateOnly To { get; private set; }
    public AbsenceKind Kind { get; private set; }
    public string? Note { get; private set; }
    public AbsenceRequestStatus Status { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionComment { get; private set; }
    public Guid? AbsenceId { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private AbsenceRequest()
    {
    }

    public static AbsenceRequest Create(Guid id, Guid userId, DateOnly from, DateOnly to, AbsenceKind kind, string? note, Guid requestedBy, DateTimeOffset now)
    {
        if (kind == AbsenceKind.Sick)
        {
            throw new ArgumentException("Krankmeldungen werden nicht beantragt, sondern von der Personalverwaltung erfasst.", nameof(kind));
        }

        if (to < from)
        {
            throw new ArgumentException("Das Ende liegt vor dem Beginn.", nameof(to));
        }

        if (to.DayNumber - from.DayNumber > 366)
        {
            throw new ArgumentException("Ein Antrag umfasst höchstens ein Jahr.", nameof(to));
        }

        return new AbsenceRequest
        {
            Id = id, UserId = userId, From = from, To = to, Kind = kind, Note = PlanningText.Optional(note, 500), Status = AbsenceRequestStatus.Pending,
            RequestedByUserId = requestedBy, RequestedAt = now,
        };
    }

    public void Decide(bool approve, string? comment, Guid decidedBy, DateTimeOffset now, Guid? absenceId)
    {
        if (Status != AbsenceRequestStatus.Pending)
        {
            throw new InvalidOperationException("Über diesen Antrag wurde bereits entschieden.");
        }

        Status = approve ? AbsenceRequestStatus.Approved : AbsenceRequestStatus.Rejected;
        DecisionComment = PlanningText.Optional(comment, 1000);
        DecidedByUserId = decidedBy;
        DecidedAt = now;
        AbsenceId = approve ? absenceId : null;
    }

    public void Cancel(Guid by, DateTimeOffset now)
    {
        if (Status is AbsenceRequestStatus.Rejected or AbsenceRequestStatus.Cancelled)
        {
            throw new InvalidOperationException("Der Antrag ist bereits abgelehnt oder zurückgezogen.");
        }

        Status = AbsenceRequestStatus.Cancelled;
        DecidedByUserId ??= by;
        DecidedAt ??= now;
        AbsenceId = null;
    }
}

/// <summary>Einstellungen der Personalverwaltung (eine Zeile): Bundesland für Feiertage, Grenze gleichzeitiger Abwesenheit je Kolonne.</summary>
public sealed class HrSettings
{
    public static readonly Guid SingletonId = Guid.Parse("01a00000-0000-7000-8000-0000000000a1");

    public Guid Id { get; private set; } = SingletonId;
    public string State { get; private set; } = "NW";

    /// <summary>Hinweis, wenn mehr als dieser Anteil (in Prozent) einer Kolonne gleichzeitig abwesend wäre.</summary>
    public int MaxCrewAbsencePercent { get; private set; } = 50;

    public bool RemindBirthdays { get; private set; } = true;
    public bool RemindAnniversaries { get; private set; } = true;

    public void Update(string state, int maxCrewAbsencePercent, bool remindBirthdays, bool remindAnniversaries)
    {
        if (!GermanHolidays.States.ContainsKey(state))
        {
            throw new ArgumentException("Unbekanntes Bundesland.", nameof(state));
        }

        if (maxCrewAbsencePercent is < 0 or > 100)
        {
            throw new ArgumentException("Bitte einen Anteil zwischen 0 und 100 % angeben.", nameof(maxCrewAbsencePercent));
        }

        State = state;
        MaxCrewAbsencePercent = maxCrewAbsencePercent;
        RemindBirthdays = remindBirthdays;
        RemindAnniversaries = remindAnniversaries;
    }
}

public sealed record Holiday(DateOnly Date, string Name);

/// <summary>Gesetzliche Feiertage in Deutschland je Bundesland (Ostern nach Gauß/Meeus).</summary>
public static class GermanHolidays
{
    public static IReadOnlyDictionary<string, string> States { get; } = new Dictionary<string, string>
    {
        ["BW"] = "Baden-Württemberg", ["BY"] = "Bayern", ["BE"] = "Berlin", ["BB"] = "Brandenburg", ["HB"] = "Bremen", ["HH"] = "Hamburg",
        ["HE"] = "Hessen", ["MV"] = "Mecklenburg-Vorpommern", ["NI"] = "Niedersachsen", ["NW"] = "Nordrhein-Westfalen", ["RP"] = "Rheinland-Pfalz",
        ["SL"] = "Saarland", ["SN"] = "Sachsen", ["ST"] = "Sachsen-Anhalt", ["SH"] = "Schleswig-Holstein", ["TH"] = "Thüringen",
    };

    public static DateOnly EasterSunday(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = (19 * a + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + 2 * e + 2 * i - h - k) % 7;
        var m = (a + 11 * h + 22 * l) / 451;
        var month = (h + l - 7 * m + 114) / 31;
        var day = ((h + l - 7 * m + 114) % 31) + 1;
        return new DateOnly(year, month, day);
    }

    public static List<Holiday> For(int year, string state)
    {
        var easter = EasterSunday(year);
        var list = new List<Holiday>
        {
            new(new DateOnly(year, 1, 1), "Neujahr"),
            new(easter.AddDays(-2), "Karfreitag"),
            new(easter.AddDays(1), "Ostermontag"),
            new(new DateOnly(year, 5, 1), "Tag der Arbeit"),
            new(easter.AddDays(39), "Christi Himmelfahrt"),
            new(easter.AddDays(50), "Pfingstmontag"),
            new(new DateOnly(year, 10, 3), "Tag der Deutschen Einheit"),
            new(new DateOnly(year, 12, 25), "1. Weihnachtstag"),
            new(new DateOnly(year, 12, 26), "2. Weihnachtstag"),
        };
        void Add(bool when, DateOnly date, string name)
        {
            if (when)
            {
                list.Add(new Holiday(date, name));
            }
        }

        Add(state is "BW" or "BY" or "ST", new DateOnly(year, 1, 6), "Heilige Drei Könige");
        Add(state is "BE" || (state == "MV" && year >= 2023), new DateOnly(year, 3, 8), "Internationaler Frauentag");
        Add(state == "BB", easter, "Ostersonntag");
        Add(state == "BB", easter.AddDays(49), "Pfingstsonntag");
        Add(state is "BW" or "BY" or "HE" or "NW" or "RP" or "SL", easter.AddDays(60), "Fronleichnam");
        Add(state is "BY" or "SL", new DateOnly(year, 8, 15), "Mariä Himmelfahrt");
        Add(state == "TH" && year >= 2019, new DateOnly(year, 9, 20), "Weltkindertag");
        Add(state is "BB" or "HB" or "HH" or "MV" or "NI" or "SN" or "ST" or "SH" or "TH" || year == 2017, new DateOnly(year, 10, 31), "Reformationstag");
        Add(state is "BW" or "BY" or "NW" or "RP" or "SL", new DateOnly(year, 11, 1), "Allerheiligen");
        if (state == "SN")
        {
            // Buß- und Bettag: Mittwoch vor dem 23. November.
            var day = new DateOnly(year, 11, 22);
            while (day.DayOfWeek != DayOfWeek.Wednesday)
            {
                day = day.AddDays(-1);
            }

            list.Add(new Holiday(day, "Buß- und Bettag"));
        }

        return list.DistinctBy(h => h.Date).OrderBy(h => h.Date).ToList();
    }

    public static HashSet<DateOnly> Dates(DateOnly from, DateOnly to, string state) =>
        Enumerable.Range(from.Year, to.Year - from.Year + 1).SelectMany(y => For(y, state)).Select(h => h.Date)
            .Where(d => d >= from && d <= to).ToHashSet();
}

internal sealed class AbsenceRequestConfiguration : IEntityTypeConfiguration<AbsenceRequest>
{
    public void Configure(EntityTypeBuilder<AbsenceRequest> builder)
    {
        builder.ToTable("absence_requests", table => table.HasCheckConstraint("ck_absence_requests_range", "\"to\" >= \"from\""));
        builder.ConfigureVersioned();
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Note).HasMaxLength(500);
        builder.Property(r => r.DecisionComment).HasMaxLength(1000);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.Status, r.From });
        builder.HasIndex(r => r.UserId);
    }
}

internal sealed class HrSettingsConfiguration : IEntityTypeConfiguration<HrSettings>
{
    public void Configure(EntityTypeBuilder<HrSettings> builder)
    {
        builder.ToTable("hr_settings");
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.State).HasMaxLength(2);
    }
}
