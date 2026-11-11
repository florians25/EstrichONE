namespace EstrichOne.Api.Infrastructure.Calendar;

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
