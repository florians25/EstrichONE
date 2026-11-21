using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>Arten der Warnungen bei Krankmeldungen und Abwesenheiten.</summary>
public static class AbsenceWarningKinds
{
    /// <summary>Abwesenheit betrifft geplante Einsätze (Disposition und Personal; Grund immer „abwesend“ bzw. „im Urlaub“).</summary>
    public const string PlanningImpact = "planning_impact";

    /// <summary>AU-Bescheinigung fehlt bzw. eAU nicht abgerufen (nur Personal).</summary>
    public const string Certificate = "certificate_missing";

    /// <summary>Entgeltfortzahlung endet bald (nur Personal).</summary>
    public const string ContinuedPay = "continued_pay";

    /// <summary>Betriebliches Eingliederungsmanagement anbieten (nur Personal).</summary>
    public const string Bem = "bem";

    /// <summary>Krankmeldung überschneidet Urlaub bzw. Urlaubsantrag (nur Personal).</summary>
    public const string VacationOverlap = "vacation_overlap";

    /// <summary>Häufung kurzer Krankmeldungen (nur Personal, Standard aus).</summary>
    public const string FrequentShort = "frequent_short";

    /// <summary>Krankmeldung grenzt an Urlaub oder Wochenende (nur Personal, Standard aus).</summary>
    public const string Adjacent = "adjacent";

    /// <summary>Gesundheitsbezogene Arten: nur mit Recht „hr.manage“ sichtbar.</summary>
    public static bool IsHealthRelated(string kind) => kind != PlanningImpact;
}

/// <summary>Von der Abwesenheit betroffener Einsatz.</summary>
/// <param name="Understaffed">Kolonne unter Mindestbesetzung bzw. niemand mehr eingeplant.</param>
public sealed record AffectedAssignment(Guid Id, DateOnly Day, Guid BauakteId, string BauakteTitle, string Activity, Guid? CrewId, string? CrewName,
    int Present, int? MinimumStaff, bool Understaffed, string Text);

/// <param name="Key">Stabiler Schlüssel (Kenntnisnahme, Benachrichtigung nur einmal).</param>
/// <param name="Severity">error, warning oder info.</param>
/// <param name="Date">Stichtag (z. B. Fälligkeit, Fristende, erster betroffener Tag).</param>
/// <param name="Link">Weiterführende Seite (Planung bzw. Personal).</param>
public sealed record AbsenceWarning(string Key, string Kind, string Severity, Guid UserId, string UserName, Guid? EmployeeId, DateOnly Date, string Title,
    string Text, Guid? AbsenceId, Guid? CaseId, string? Link, IReadOnlyList<AffectedAssignment> Assignments);

/// <param name="Enabled">Hauptschalter in den Einstellungen.</param>
/// <param name="HealthDetails">Angemeldeter Benutzer sieht gesundheitsbezogene Hinweise (Personalverwaltung).</param>
/// <param name="Acknowledged">Zur Kenntnis genommene (ausgeblendete) Warnungen.</param>
public sealed record AbsenceWarningList(bool Enabled, bool HealthDetails, IReadOnlyList<AbsenceWarning> Items, int Acknowledged);

/// <summary>Zur Kenntnis genommene Warnung (blendet sie für alle aus, bis sich der Sachverhalt ändert – neuer Schlüssel).</summary>
public sealed class AbsenceWarningAck
{
    public string Key { get; private set; } = string.Empty;
    public Guid UserId { get; private set; }
    public DateTimeOffset AcknowledgedAt { get; private set; }

    private AbsenceWarningAck()
    {
    }

    public static AbsenceWarningAck Create(string key, Guid userId, DateTimeOffset now) => new() { Key = key, UserId = userId, AcknowledgedAt = now };
}

internal sealed class AbsenceWarningAckConfiguration : IEntityTypeConfiguration<AbsenceWarningAck>
{
    public void Configure(EntityTypeBuilder<AbsenceWarningAck> builder)
    {
        builder.ToTable("absence_warning_acks");
        builder.HasKey(a => a.Key);
        builder.Property(a => a.Key).HasMaxLength(200);
    }
}

/// <summary>Krankmeldung für die Regeln (ohne Notiz, ohne Diagnose).</summary>
/// <param name="ContinuedPayRelevant">Zählt für Entgeltfortzahlung und BEM (nicht „Kind krank“).</param>
public sealed record SickEntry(Guid Id, Guid UserId, DateOnly From, DateOnly To, Guid CaseId, bool HasCertificate, bool ContinuedPayRelevant);

/// <summary>Urlaub (genehmigt) oder offener Urlaubsantrag.</summary>
public sealed record VacationEntry(Guid Id, Guid UserId, DateOnly From, DateOnly To, bool Pending);

/// <summary>Abwesenheit im Vorschauzeitraum der Planung (Bezeichnung bereits ohne Grund, z. B. „abwesend“).</summary>
public sealed record PlannedAbsence(Guid Id, Guid UserId, DateOnly From, DateOnly To, string Label, bool HalfDayStart, bool HalfDayEnd)
{
    public bool FullDay(DateOnly day) => From <= day && day <= To && !((day == From && HalfDayStart) || (day == To && HalfDayEnd));
}

/// <summary>Geplanter Einsatz mit den eingeplanten Personen (Tagesbesetzung bereits aufgelöst).</summary>
public sealed record PlannedAssignment(Guid Id, DateOnly Day, Guid BauakteId, string BauakteTitle, string Activity, Guid? CrewId, string? CrewName,
    int? MinimumStaff, IReadOnlyList<Guid> People);

public sealed record AbsenceWarningInput(
    DateOnly Today, HrSettings Settings, IReadOnlyList<SickEntry> Sick, IReadOnlyList<VacationEntry> Vacations, IReadOnlyList<PlannedAbsence> Absences,
    IReadOnlyList<PlannedAssignment> Assignments, IReadOnlySet<DateOnly> Holidays, IReadOnlyDictionary<Guid, string> Names,
    IReadOnlyDictionary<Guid, Guid> Employees);

/// <summary>
/// Regeln der Warnungen (reine Berechnung, ohne Datenbank). Fristen sind Hinweise, keine Rechtsberatung:
/// AU-Nachweis ab dem eingestellten Kalendertag (§ 5 EFZG, Standard 4. Tag), Entgeltfortzahlung 6 Wochen je Krankheitsfall (§ 3 EFZG),
/// BEM bei mehr als 42 Kalendertagen in 12 Monaten (§ 167 Abs. 2 SGB IX), Überschneidung mit Urlaub (§ 9 BUrlG).
/// </summary>
public static class AbsenceWarningRules
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Entgeltfortzahlung: 6 Wochen = 42 Kalendertage.</summary>
    public const int ContinuedPayDays = 42;

    /// <summary>Hinweis so viele Tage vor dem Ende der Entgeltfortzahlung.</summary>
    public const int ContinuedPayNoticeDays = 7;

    /// <summary>Kurzerkrankung: höchstens so viele Kalendertage.</summary>
    public const int ShortCaseDays = 3;

    public static string D(DateOnly day) => day.ToString("dd.MM.yyyy", German);

    /// <summary>Zeitraum ohne Gedankenstrich: „17.11. bis 23.11.2026“.</summary>
    public static string Range(DateOnly from, DateOnly to) =>
        from == to ? D(from) : $"{from.ToString(from.Year == to.Year ? "dd.MM." : "dd.MM.yyyy", German)} bis {D(to)}";

    private static string Severity(bool error, bool warning = true) => error ? "error" : warning ? "warning" : "info";

    /// <summary>Alle Warnungen (ungefiltert; die Sichtbarkeit nach Rechten prüft der Aufrufer).</summary>
    public static List<AbsenceWarning> Compute(AbsenceWarningInput input)
    {
        var s = input.Settings;
        var list = new List<AbsenceWarning>();
        if (!s.WarningsEnabled)
        {
            return list;
        }

        if (s.WarnPlanningImpact)
        {
            list.AddRange(PlanningImpact(input));
        }

        var cases = input.Sick.GroupBy(e => e.CaseId).Select(g => new SickCase(g.Key, g.First().UserId, g.OrderBy(e => e.From).ToList())).ToList();
        if (s.WarnCertificate)
        {
            list.AddRange(Certificates(input, cases));
        }

        if (s.WarnContinuedPay)
        {
            list.AddRange(ContinuedPay(input, cases));
        }

        if (s.WarnBem)
        {
            list.AddRange(Bem(input, cases));
        }

        if (s.WarnVacationOverlap)
        {
            list.AddRange(Overlaps(input));
        }

        if (s.FrequentShortThreshold > 0)
        {
            list.AddRange(FrequentShort(input, cases));
        }

        if (s.WarnAdjacent)
        {
            list.AddRange(Adjacent(input, cases));
        }

        return list.OrderBy(w => w.Severity switch { "error" => 0, "warning" => 1, _ => 2 }).ThenBy(w => w.Date)
            .ThenBy(w => w.UserName, StringComparer.CurrentCulture).ToList();
    }

    private sealed record SickCase(Guid Id, Guid UserId, List<SickEntry> Entries)
    {
        public DateOnly From => Entries.Min(e => e.From);
        public DateOnly To => Entries.Max(e => e.To);
        public int Length => To.DayNumber - From.DayNumber + 1;
        public bool ContinuedPayRelevant => Entries.Any(e => e.ContinuedPayRelevant);
    }

    private static string Name(AbsenceWarningInput input, Guid userId) => input.Names.GetValueOrDefault(userId) ?? "Unbekannt";

    private static AbsenceWarning Warning(AbsenceWarningInput input, string key, string kind, string severity, Guid userId, DateOnly date, string title, string text,
        Guid? absenceId = null, Guid? caseId = null, string? link = null, IReadOnlyList<AffectedAssignment>? assignments = null) =>
        new(key, kind, severity, userId, Name(input, userId), input.Employees.TryGetValue(userId, out var e) ? e : null, date, title, text, absenceId, caseId,
            link ?? "/personal/urlaub?reiter=krank", assignments ?? []);

    /// <summary>Stabiler Kurzschlüssel über eine Menge von IDs (neuer Schlüssel, sobald sich die betroffenen Einsätze ändern).</summary>
    public static string Hash(IEnumerable<Guid> ids) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", ids.Order())))).ToLowerInvariant()[..12];

    /// <summary>Einsätze, bei denen die Person im Zeitraum eingeplant ist; Kolonne unterbesetzt, wenn weniger als die Mindestbesetzung bleibt.</summary>
    public static List<AffectedAssignment> Affected(PlannedAbsence absence, AbsenceWarningInput input)
    {
        var horizon = input.Today.AddDays(input.Settings.PlanningImpactDays);
        var result = new List<AffectedAssignment>();
        foreach (var a in input.Assignments.Where(a => a.Day >= input.Today && a.Day <= horizon && absence.From <= a.Day && a.Day <= absence.To
                     && a.People.Contains(absence.UserId)).OrderBy(a => a.Day).ThenBy(a => a.CrewName))
        {
            var present = a.People.Count(p => !input.Absences.Any(x => x.UserId == p && x.FullDay(a.Day)));
            var understaffed = present == 0 || (a.MinimumStaff is { } min && present < min);
            var half = !absence.FullDay(a.Day) ? " (halber Tag)" : string.Empty;
            var crew = a.CrewName is null ? string.Empty : $" ({a.CrewName})";
            var text = $"{a.Day.ToString("ddd dd.MM.", German)}{half}: {a.BauakteTitle}, {a.Activity}{crew}." + (present == 0
                ? " Niemand mehr eingeplant."
                : a.MinimumStaff is { } m && present < m ? $" Unterbesetzt: {present} von mindestens {m}." : $" Noch {present} {(present == 1 ? "Person" : "Personen")} eingeplant.");
            result.Add(new AffectedAssignment(a.Id, a.Day, a.BauakteId, a.BauakteTitle, a.Activity, a.CrewId, a.CrewName, present, a.MinimumStaff, understaffed, text));
        }

        return result;
    }

    /// <summary>Warnung „Abwesenheit betrifft Einsätze“ zu einer Abwesenheit (oder null, wenn kein Einsatz betroffen ist).</summary>
    public static AbsenceWarning? ImpactOf(PlannedAbsence absence, AbsenceWarningInput input)
    {
        var affected = Affected(absence, input);
        if (affected.Count == 0)
        {
            return null;
        }

        var understaffed = affected.Where(a => a.Understaffed).ToList();
        var crews = understaffed.Select(a => a.CrewName).OfType<string>().Distinct().ToList();
        var title = $"{Name(input, absence.UserId)} {absence.Label} {Range(absence.From, absence.To)}";
        var text = $"{affected.Count} {(affected.Count == 1 ? "geplanter Einsatz betroffen" : "geplante Einsätze betroffen")}"
            + (understaffed.Count == 0 ? "." : crews.Count > 0 ? $", unterbesetzt: {string.Join(", ", crews)}." : ", Einsatz ohne Personal.");
        var first = affected[0].Day;
        return Warning(input, $"plan:{absence.Id}:{Hash(affected.Select(a => a.Id))}", AbsenceWarningKinds.PlanningImpact, Severity(understaffed.Count > 0),
            absence.UserId, first, title, text, absence.Id, null, $"/planung?ansicht=tag&datum={first:yyyy-MM-dd}&tag={first:yyyy-MM-dd}", affected);
    }

    private static IEnumerable<AbsenceWarning> PlanningImpact(AbsenceWarningInput input)
    {
        var horizon = input.Today.AddDays(input.Settings.PlanningImpactDays);
        foreach (var absence in input.Absences.Where(a => a.To >= input.Today && a.From <= horizon))
        {
            if (ImpactOf(absence, input) is { } warning)
            {
                yield return warning;
            }
        }
    }

    private static IEnumerable<AbsenceWarning> Certificates(AbsenceWarningInput input, List<SickCase> cases)
    {
        var dueDay = input.Settings.CertificateDueDay;
        foreach (var c in cases.Where(c => c.Length >= dueDay))
        {
            var firstDue = c.From.AddDays(dueDay - 1);
            foreach (var (e, index) in c.Entries.Select((e, i) => (e, i)))
            {
                var due = e.From > firstDue ? e.From : firstDue;
                if (e.HasCertificate || e.To < due || input.Today < due || e.To < input.Today.AddDays(-90))
                {
                    continue;
                }

                var day = Math.Min(input.Today.DayNumber, e.To.DayNumber) - c.From.DayNumber + 1;
                var what = index == 0 ? "AU-Bescheinigung" : "Folgebescheinigung";
                yield return Warning(input, $"au:{e.Id}", AbsenceWarningKinds.Certificate, Severity(input.Today > due.AddDays(3)), e.UserId, due,
                    $"{Name(input, e.UserId)}: {what} fehlt",
                    $"Arbeitsunfähig {Range(e.From, e.To)} ({day}. Kalendertag). Nachweis erwartet ab dem {D(due)}. Bitte die eAU bei der Krankenkasse abrufen "
                    + "und als abgerufen markieren oder die Bescheinigung anhängen.", e.Id, c.Id);
            }
        }
    }

    private static int DaysIn(SickCase c, DateOnly from, DateOnly to) =>
        Math.Max(0, Math.Min(c.To.DayNumber, to.DayNumber) - Math.Max(c.From.DayNumber, from.DayNumber) + 1);

    private static IEnumerable<AbsenceWarning> ContinuedPay(AbsenceWarningInput input, List<SickCase> cases)
    {
        var today = input.Today;
        foreach (var c in cases.Where(c => c.ContinuedPayRelevant && c.To >= today))
        {
            var end = c.From.AddDays(ContinuedPayDays - 1); // letzter Tag der 6 Wochen
            // Frühere Krankmeldungen der letzten 6 Monate: bei derselben Ursache (Fortsetzungserkrankung) zählen sie mit.
            var before = cases.Where(o => o.Id != c.Id && o.UserId == c.UserId && o.ContinuedPayRelevant && o.To < c.From)
                .Sum(o => DaysIn(o, c.From.AddMonths(-6), c.From.AddDays(-1)));
            var earliest = before > 0 && before < ContinuedPayDays ? c.From.AddDays(ContinuedPayDays - 1 - before) : end;
            if (today < earliest.AddDays(-ContinuedPayNoticeDays) || c.To < earliest)
            {
                continue; // Hinweis erst eine Woche vorher; endet die Krankmeldung vorher, keiner
            }

            var passed = today > end;
            var text = passed
                ? $"Der Krankheitsfall seit {D(c.From)} dauert länger als 6 Wochen; die Entgeltfortzahlung (§ 3 EFZG) endete voraussichtlich am {D(end)}."
                : $"Die Entgeltfortzahlung (§ 3 EFZG, 6 Wochen) für den Krankheitsfall seit {D(c.From)} endet voraussichtlich am {D(end)}.";
            if (before > 0)
            {
                text += $" Frühere Krankmeldungen in den 6 Monaten davor: {before} Kalendertage. Bei derselben Ursache zählen sie mit"
                    + (earliest < end ? $" (Ende dann bereits am {D(earliest)})." : ".");
            }

            text += " Bitte prüfen; danach zahlt in der Regel die Krankenkasse Krankengeld. Nur ein Hinweis, keine Rechtsauskunft.";
            yield return Warning(input, $"efzg:{c.Id}", AbsenceWarningKinds.ContinuedPay, Severity(false, passed || today >= earliest), c.UserId, earliest,
                $"{Name(input, c.UserId)}: {(passed ? "Entgeltfortzahlung voraussichtlich beendet" : "Entgeltfortzahlung endet bald")}", text, c.Entries[^1].Id, c.Id);
        }
    }

    private static IEnumerable<AbsenceWarning> Bem(AbsenceWarningInput input, List<SickCase> cases)
    {
        var today = input.Today;
        var windowFrom = today.AddDays(-364);
        foreach (var person in cases.Where(c => c.ContinuedPayRelevant && c.From <= today && c.To >= windowFrom).GroupBy(c => c.UserId))
        {
            var days = new HashSet<DateOnly>();
            Guid? crossing = null;
            foreach (var c in person.OrderBy(c => c.From))
            {
                foreach (var e in c.Entries)
                {
                    for (var d = e.From < windowFrom ? windowFrom : e.From; d <= e.To && d <= today; d = d.AddDays(1))
                    {
                        days.Add(d);
                    }
                }

                if (crossing is null && days.Count > ContinuedPayDays)
                {
                    crossing = c.Id;
                }
            }

            if (crossing is { } caseId)
            {
                yield return Warning(input, $"bem:{person.Key}:{caseId}", AbsenceWarningKinds.Bem, "warning", person.Key, today,
                    $"{Name(input, person.Key)}: Betriebliches Eingliederungsmanagement anbieten",
                    $"In den letzten 12 Monaten {days.Count} Kalendertage arbeitsunfähig (mehr als 6 Wochen). Nach § 167 Abs. 2 SGB IX ist ein BEM anzubieten; "
                    + "die Teilnahme ist freiwillig. Nach dem Angebot „Zur Kenntnis genommen“ wählen.", null, caseId);
            }
        }
    }

    private static IEnumerable<AbsenceWarning> Overlaps(AbsenceWarningInput input)
    {
        foreach (var e in input.Sick.Where(e => e.To >= input.Today.AddDays(-30)))
        {
            foreach (var v in input.Vacations.Where(v => v.UserId == e.UserId && v.From <= e.To && v.To >= e.From))
            {
                var from = v.From > e.From ? v.From : e.From;
                var to = v.To < e.To ? v.To : e.To;
                yield return v.Pending
                    ? Warning(input, $"overlap:{e.Id}:{v.Id}", AbsenceWarningKinds.VacationOverlap, "warning", e.UserId, from,
                        $"{Name(input, e.UserId)}: Krankmeldung und offener Urlaubsantrag",
                        $"Die Krankmeldung {Range(e.From, e.To)} überschneidet den offenen Urlaubsantrag {Range(v.From, v.To)} ({Range(from, to)}). "
                        + "Bitte den Antrag vor der Genehmigung mit dem Mitarbeiter klären.", e.Id, e.CaseId, "/personal/urlaub?reiter=antraege")
                    : Warning(input, $"overlap:{e.Id}:{v.Id}", AbsenceWarningKinds.VacationOverlap, "info", e.UserId, from,
                        $"{Name(input, e.UserId)}: Krankmeldung im Urlaub",
                        $"Die Krankmeldung {Range(e.From, e.To)} überschneidet genehmigten Urlaub {Range(v.From, v.To)} ({Range(from, to)}). "
                        + "Die Krankheitstage werden nicht als Urlaub gezählt (§ 9 BUrlG, mit AU-Nachweis); das Urlaubskonto ist bereits angepasst.",
                        e.Id, e.CaseId);
            }
        }
    }

    private static IEnumerable<AbsenceWarning> FrequentShort(AbsenceWarningInput input, List<SickCase> cases)
    {
        var windowFrom = input.Today.AddDays(-364);
        foreach (var person in cases.Where(c => c.ContinuedPayRelevant && c.Length <= ShortCaseDays && c.From >= windowFrom && c.From <= input.Today)
                     .GroupBy(c => c.UserId))
        {
            var count = person.Count();
            if (count >= input.Settings.FrequentShortThreshold)
            {
                var latest = person.OrderBy(c => c.From).Last();
                yield return Warning(input, $"short:{person.Key}:{latest.Id}", AbsenceWarningKinds.FrequentShort, "info", person.Key, latest.From,
                    $"{Name(input, person.Key)}: {count} kurze Krankmeldungen",
                    $"{count} Krankmeldungen von höchstens {ShortCaseDays} Kalendertagen in den letzten 12 Monaten (Schwellwert {input.Settings.FrequentShortThreshold}). "
                    + "Neutraler Hinweis ohne Wertung, nur für die Personalverwaltung.", latest.Entries[0].Id, latest.Id);
            }
        }
    }

    private static bool WorkFree(DateOnly day, IReadOnlySet<DateOnly> holidays) =>
        day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || holidays.Contains(day);

    private static IEnumerable<AbsenceWarning> Adjacent(AbsenceWarningInput input, List<SickCase> cases)
    {
        foreach (var c in cases.Where(c => c.From >= input.Today.AddDays(-30) && c.From <= input.Today))
        {
            var vacations = input.Vacations.Where(v => v.UserId == c.UserId && !v.Pending).ToList();
            string? What(DateOnly start, int step)
            {
                var d = start;
                var free = false;
                while (WorkFree(d, input.Holidays) && Math.Abs(d.DayNumber - start.DayNumber) < 5)
                {
                    free = true;
                    d = d.AddDays(step);
                }

                if (vacations.Any(v => v.From <= d && d <= v.To) || vacations.Any(v => v.From <= start && start <= v.To))
                {
                    return "Urlaub";
                }

                return free ? "Wochenende bzw. Feiertag" : null;
            }

            var before = What(c.From.AddDays(-1), -1);
            var after = What(c.To.AddDays(1), 1);
            if (before is null && after is null)
            {
                continue;
            }

            var parts = new[] { before is null ? null : $"beginnt direkt nach {before}", after is null ? null : $"endet direkt vor {after}" }.OfType<string>();
            yield return Warning(input, $"adjacent:{c.Id}", AbsenceWarningKinds.Adjacent, "info", c.UserId, c.From,
                $"{Name(input, c.UserId)}: Krankmeldung grenzt an freie Tage",
                $"Die Krankmeldung {Range(c.From, c.To)} {string.Join(" und ", parts)}. Neutraler Hinweis ohne Wertung, nur für die Personalverwaltung.",
                c.Entries[0].Id, c.Id);
        }
    }
}

/// <summary>Lädt die Daten für die Warnungen und wendet die Regeln an.</summary>
public sealed class AbsenceWarningService(EstrichOneDbContext db, HrService hr)
{
    private static readonly Dictionary<AbsenceKind, string> Labels = new()
    {
        [AbsenceKind.Vacation] = "im Urlaub", [AbsenceKind.Sick] = "abwesend", [AbsenceKind.Training] = "auf Schulung",
        [AbsenceKind.Other] = "abwesend", [AbsenceKind.SpecialLeave] = "im Sonderurlaub", [AbsenceKind.School] = "in der Berufsschule",
    };

    /// <summary>Bezeichnung für die Planung: Krankheit heißt immer „abwesend“ (wie in Einsatzplan und Cockpit).</summary>
    public static string PlanningLabel(AbsenceKind kind) => Labels[HrService.Mask(kind, false)];

    public async Task<AbsenceWarningInput> InputAsync(CancellationToken cancellationToken)
    {
        var settings = await hr.SettingsAsync(cancellationToken);
        var today = hr.Today;
        var types = await hr.TypesAsync(cancellationToken);
        var from = today.AddDays(-400);
        var horizon = today.AddDays(settings.PlanningImpactDays);
        var sick = await db.Set<Absence>().AsNoTracking().Where(a => a.Kind == AbsenceKind.Sick && a.To >= from && a.From <= today.AddDays(370))
            .ToListAsync(cancellationToken);
        var sickUsers = sick.Select(a => a.UserId).Distinct().ToList();
        var vacationTypes = types.Values.Where(t => t.CountsAsVacation || t.Kind == AbsenceKind.Vacation).Select(t => t.Id).ToHashSet();
        var vacations = (await db.Set<Absence>().AsNoTracking().Where(a => sickUsers.Contains(a.UserId) && a.Kind != AbsenceKind.Sick && a.To >= from)
                .ToListAsync(cancellationToken))
            .Where(a => vacationTypes.Contains(HrService.TypeOf(a))).Select(a => new VacationEntry(a.Id, a.UserId, a.From, a.To, false)).ToList();
        vacations.AddRange((await db.Set<AbsenceRequest>().AsNoTracking()
                .Where(r => sickUsers.Contains(r.UserId) && r.Status == AbsenceRequestStatus.Pending && r.To >= today.AddDays(-30)).ToListAsync(cancellationToken))
            .Where(r => vacationTypes.Contains(HrService.TypeOf(r))).Select(r => new VacationEntry(r.Id, r.UserId, r.From, r.To, true)));

        var assignments = new List<PlannedAssignment>();
        var planned = new List<PlannedAbsence>();
        if (settings.WarningsEnabled && settings.WarnPlanningImpact)
        {
            // Wie in der Planung: Tagesbesetzung (CrewDay) ersetzt an dem Tag die Stammbesetzung, Ausnahmen und Zusatzpersonen am Einsatz.
            var raw = await db.Set<Assignment>().AsNoTracking().Where(a => a.Day >= today && a.Day <= horizon).ToListAsync(cancellationToken);
            var crews = await db.Set<Crew>().AsNoTracking().ToDictionaryAsync(c => c.Id, cancellationToken);
            var rosters = (await db.Set<CrewDay>().AsNoTracking().Where(d => d.Day >= today && d.Day <= horizon).ToListAsync(cancellationToken))
                .ToDictionary(d => (d.CrewId, d.Day));
            var bauakteIds = raw.Select(a => a.BauakteId).Distinct().ToList();
            var titles = await db.Set<Bauakte>().AsNoTracking().Where(b => bauakteIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Title, cancellationToken);
            assignments = raw.Select(a =>
            {
                var crew = a.CrewId is { } c ? crews.GetValueOrDefault(c) : null;
                var members = crew is null ? [] : rosters.TryGetValue((crew.Id, a.Day), out var roster) ? roster.MemberUserIds : crew.MemberUserIds;
                return new PlannedAssignment(a.Id, a.Day, a.BauakteId, titles.GetValueOrDefault(a.BauakteId) ?? "Bauakte gelöscht", a.Activity, a.CrewId, crew?.Name,
                    crew?.MinimumStaff, a.PeopleWith(members));
            }).ToList();
            planned = (await db.Set<Absence>().AsNoTracking().Where(a => a.From <= horizon && a.To >= today).ToListAsync(cancellationToken))
                .Select(a => new PlannedAbsence(a.Id, a.UserId, a.From, a.To, PlanningLabel(a.Kind), a.HalfDayStart, a.HalfDayEnd)).ToList();
        }

        var userIds = sickUsers.Concat(planned.Select(a => a.UserId)).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var employees = await db.Set<Employee>().AsNoTracking().Where(e => e.UserId != null && userIds.Contains(e.UserId.Value))
            .ToDictionaryAsync(e => e.UserId!.Value, e => e.Id, cancellationToken);
        var childSick = types.Values.Where(t => t.Key == "child_sick").Select(t => t.Id).ToHashSet();
        var entries = sick.Select(a => new SickEntry(a.Id, a.UserId, a.From, a.To, a.CaseId ?? a.Id, a.HasCertificate, !childSick.Contains(HrService.TypeOf(a))))
            .ToList();
        var holidays = await hr.HolidaysAsync(from, today.AddDays(400), cancellationToken);
        return new AbsenceWarningInput(today, settings, entries, vacations, planned, assignments, holidays, names, employees);
    }

    /// <summary>Alle Warnungen ohne die zur Kenntnis genommenen.</summary>
    public async Task<(List<AbsenceWarning> Items, int Acknowledged)> ComputeAsync(CancellationToken cancellationToken)
    {
        var all = AbsenceWarningRules.Compute(await InputAsync(cancellationToken));
        var keys = all.Select(w => w.Key).ToList();
        var acked = await db.Set<AbsenceWarningAck>().AsNoTracking().Where(a => keys.Contains(a.Key)).Select(a => a.Key).ToListAsync(cancellationToken);
        return (all.Where(w => !acked.Contains(w.Key)).ToList(), acked.Count);
    }

    /// <summary>Warnung „betrifft Einsätze“ zu einer gerade gespeicherten Abwesenheit (null: nichts betroffen oder abgeschaltet).</summary>
    public async Task<AbsenceWarning?> ImpactAsync(Guid absenceId, CancellationToken cancellationToken)
    {
        var input = await InputAsync(cancellationToken);
        if (!input.Settings.WarningsEnabled || !input.Settings.WarnPlanningImpact)
        {
            return null;
        }

        return input.Absences.FirstOrDefault(a => a.Id == absenceId) is { } absence ? AbsenceWarningRules.ImpactOf(absence, input) : null;
    }
}
