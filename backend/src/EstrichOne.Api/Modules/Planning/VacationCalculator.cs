namespace EstrichOne.Api.Modules.Planning;

/// <summary>Abwesenheit für die Berechnung (genehmigt oder beantragt), Art bereits aufgelöst.</summary>
public sealed record AbsenceSpan(DateOnly From, DateOnly To, Guid TypeId, bool HalfDayStart = false, bool HalfDayEnd = false)
{
    public bool Covers(DateOnly day) => From <= day && day <= To;

    /// <summary>Anteil des Tages: erster/letzter Tag halb, sonst ganz.</summary>
    public decimal Fraction(DateOnly day) => (day == From && HalfDayStart) || (day == To && HalfDayEnd) ? 0.5m : 1m;
}

/// <summary>Tage je Abwesenheitsart im Jahr (Jahresübersicht).</summary>
public sealed record AbsenceTypeDays(Guid TypeId, string Name, string Color, decimal Days);

/// <summary>Regeln einer Art für die Berechnung.</summary>
public sealed record AbsenceTypeRule(Guid Id, string Name, string Color, AbsenceKind Kind, bool CountsAsVacation)
{
    public bool IsSickness => Kind == AbsenceKind.Sick;

    public static AbsenceTypeRule From(AbsenceType t) => new(t.Id, t.Name, t.Color, t.Kind, t.CountsAsVacation);
}

/// <summary>Eingaben für ein Urlaubskonto eines Jahres.</summary>
/// <param name="AutoCarryOver">Rest des Vorjahres (automatischer Übertrag), wenn kein Übertrag erfasst ist.</param>
public sealed record VacationInput(
    int Year, DateOnly Today, decimal AnnualEntitlement, int WorkDaysMask, DateOnly? EntryDate, DateOnly? ExitDate, VacationAccount? Account,
    decimal? AutoCarryOver, IReadOnlyList<AbsenceSpan> Absences, IReadOnlyList<AbsenceSpan> Pending, IReadOnlySet<DateOnly> Holidays,
    DateOnly? DefaultCarryOverExpiry, bool Prorate, IReadOnlyDictionary<Guid, AbsenceTypeRule> Types, bool IncludeSick);

/// <summary>
/// Urlaubskonto: Anspruch (anteilig nach vollen Beschäftigungsmonaten), Übertrag mit Verfall, genommen/geplant/beantragt, Rest.
/// Gezählt werden Arbeitstage nach Arbeitszeitmodell ohne gesetzliche Feiertage, halbe Tage als 0,5.
/// Krankheitstage während eines Urlaubs werden nicht vom Urlaub abgezogen (§ 9 BUrlG).
/// Der Übertrag wird zuerst verbraucht; was bis zum Verfallsdatum nicht genommen oder geplant ist, verfällt.
/// </summary>
public static class VacationCalculator
{
    public const int DefaultWorkDays = 0b0111110;

    public static bool IsWorkDay(int mask, DateOnly day, IReadOnlySet<DateOnly> holidays) =>
        (mask & (1 << (int)day.DayOfWeek)) != 0 && !holidays.Contains(day);

    /// <summary>Arbeitstage eines Zeitraums (halbe Tage am Rand), ohne Wochenenden laut Arbeitszeitmodell und ohne Feiertage.</summary>
    public static decimal WorkingDays(int mask, DateOnly from, DateOnly to, bool halfDayStart, bool halfDayEnd, IReadOnlySet<DateOnly> holidays)
    {
        var span = new AbsenceSpan(from, to, Guid.Empty, halfDayStart, halfDayEnd);
        decimal days = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (IsWorkDay(mask, d, holidays))
            {
                days += span.Fraction(d);
            }
        }

        return days;
    }

    /// <summary>
    /// Anteiliger Anspruch bei Ein- oder Austritt im Jahr: ein Zwölftel je vollem Beschäftigungsmonat.
    /// Bruchteile ab einem halben Tag werden aufgerundet, kleinere auf einen halben Tag (§ 5 BUrlG).
    /// </summary>
    public static decimal Prorated(decimal annual, DateOnly? entry, DateOnly? exit, int year)
    {
        var start = new DateOnly(year, 1, 1);
        var end = new DateOnly(year, 12, 31);
        var from = entry is { } e && e > start ? e : start;
        var to = exit is { } x && x < end ? x : end;
        if (to < from)
        {
            return 0;
        }

        if (from == start && to == end)
        {
            return annual;
        }

        var months = 0;
        for (var m = 1; m <= 12; m++)
        {
            var first = new DateOnly(year, m, 1);
            var last = first.AddMonths(1).AddDays(-1);
            if (from <= first && to >= last)
            {
                months++;
            }
        }

        var value = annual * months / 12m;
        var whole = Math.Floor(value);
        var fraction = value - whole;
        return fraction == 0 ? whole : fraction >= 0.5m ? whole + 1 : whole + 0.5m;
    }

    public static VacationSummary Calculate(VacationInput input)
    {
        var year = input.Year;
        var start = new DateOnly(year, 1, 1);
        var end = new DateOnly(year, 12, 31);
        var account = input.Account;
        var entitlement = account?.Entitlement ?? (input.Prorate ? Prorated(input.AnnualEntitlement, input.EntryDate, input.ExitDate, year) : input.AnnualEntitlement);
        var carry = account?.CarryOver ?? input.AutoCarryOver ?? 0;
        var expiry = account?.CarryOverExpiresOn ?? input.DefaultCarryOverExpiry;
        AbsenceTypeRule Rule(Guid id) => input.Types.TryGetValue(id, out var r) ? r : new AbsenceTypeRule(id, "Abwesenheit", "#9E9E9E", AbsenceKind.Other, false);

        decimal taken = 0, planned = 0, usedByExpiry = 0;
        var sick = 0;
        var byType = new Dictionary<Guid, decimal>();
        var relevant = input.Absences.Where(a => a.To >= start && a.From <= end).ToList();
        if (relevant.Count > 0)
        {
            var from = relevant.Min(a => a.From) < start ? start : relevant.Min(a => a.From);
            var to = relevant.Max(a => a.To) > end ? end : relevant.Max(a => a.To);
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                if (!IsWorkDay(input.WorkDaysMask, d, input.Holidays) || (input.EntryDate is { } entry && d < entry) || (input.ExitDate is { } exit && d > exit))
                {
                    continue;
                }

                var covering = relevant.Where(a => a.Covers(d)).ToList();
                if (covering.Count == 0)
                {
                    continue;
                }

                var sickness = covering.Where(a => Rule(a.TypeId).IsSickness).ToList();
                if (sickness.Count > 0)
                {
                    if (d <= input.Today)
                    {
                        sick++;
                    }

                    if (input.IncludeSick)
                    {
                        foreach (var g in sickness.GroupBy(a => a.TypeId))
                        {
                            byType[g.Key] = byType.GetValueOrDefault(g.Key) + g.Max(a => a.Fraction(d));
                        }
                    }

                    // Krank während des Urlaubs: der Tag zählt nicht als Urlaub.
                    continue;
                }

                foreach (var g in covering.GroupBy(a => a.TypeId))
                {
                    byType[g.Key] = byType.GetValueOrDefault(g.Key) + g.Max(a => a.Fraction(d));
                }

                var vacation = covering.Where(a => Rule(a.TypeId).CountsAsVacation).Select(a => a.Fraction(d)).DefaultIfEmpty(0).Max();
                if (vacation == 0)
                {
                    continue;
                }

                if (d <= input.Today)
                {
                    taken += vacation;
                }
                else
                {
                    planned += vacation;
                }

                if (expiry is { } x && d <= x)
                {
                    usedByExpiry += vacation;
                }
            }
        }

        decimal requested = 0;
        foreach (var p in input.Pending.Where(p => Rule(p.TypeId).CountsAsVacation && p.To >= start && p.From <= end))
        {
            var from = p.From < start ? start : p.From;
            var to = p.To > end ? end : p.To;
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                if (IsWorkDay(input.WorkDaysMask, d, input.Holidays))
                {
                    requested += p.Fraction(d);
                }
            }
        }

        decimal expired = 0, expiring = 0;
        if (carry > 0 && expiry is { } expiresOn)
        {
            var lapsing = carry - Math.Min(carry, usedByExpiry);
            if (input.Today > expiresOn)
            {
                expired = lapsing;
            }
            else
            {
                expiring = lapsing;
            }
        }

        var adjustment = account?.Adjustment ?? 0;
        var remaining = entitlement + carry + adjustment - expired - taken - planned;
        var types = byType.Where(x => x.Value > 0).Select(x => (Rule: Rule(x.Key), Days: x.Value))
            .OrderBy(x => x.Rule.Name, StringComparer.CurrentCulture)
            .Select(x => new AbsenceTypeDays(x.Rule.Id, x.Rule.Name, x.Rule.Color, x.Days)).ToList();
        return new VacationSummary(year, entitlement, carry, taken, planned, remaining, input.IncludeSick ? sick : 0, requested,
            input.AnnualEntitlement, adjustment, carry > 0 ? expiry : null, expired, expiring, remaining - requested, types, account?.Note);
    }
}
