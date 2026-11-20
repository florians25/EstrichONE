using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace EstrichOne.Api.Modules.Organization.Exports;

/// <summary>
/// Nummernkreise aller Datensätze mit Nummer (Kunden, Bauakten, Personal, Maschinen …). Jede Art hat ein Muster,
/// einen Bereich (Start, optional Ende) und einen Zähler (bei Mustern mit Jahr je Jahr). Die Vergabe ist atomar
/// (Zeilensperre bis Transaktionsende), bereits vorhandene Nummern werden übersprungen.
/// </summary>
public interface INumberRanges
{
    /// <summary>Nächste freie Nummer der Art; wirft <see cref="NumberRangeExhaustedException"/>, wenn der Bereich ausgeschöpft ist.</summary>
    Task<string> NextAsync(string key, CancellationToken cancellationToken);

    /// <summary>Stand eines Nummernkreises (Muster, Bereich, nächste Nummer, Vorschau).</summary>
    Task<NumberRangeResponse> StateAsync(string key, CancellationToken cancellationToken);

    /// <summary>Nächsten Zählerwert im aktuellen Zeitraum setzen (protokolliert).</summary>
    Task SetNextAsync(string key, long nextNumber, CancellationToken cancellationToken);
}

/// <summary>Stand eines Nummernkreises für Einstellungen und Vorschau.</summary>
/// <param name="NextNumber">Nächster Zählerwert im aktuellen Zeitraum (Jahr bzw. fortlaufend).</param>
/// <param name="Preview">So sieht die nächste Nummer aus.</param>
/// <param name="Remaining">Noch freie Zählerwerte bis zum Ende des Bereichs; leer = unbegrenzt.</param>
/// <param name="Capacity">Höchster Zählerwert, der in die Stellen des Musters passt.</param>
/// <param name="EditorsMayOverride">Veraltet, immer false: Nummern werden ausschließlich aus dem Nummernkreis vergeben.</param>
public sealed record NumberRangeResponse(string Key, string Label, string Group, string Pattern, string DefaultPattern, long RangeStart, long? RangeEnd,
    long NextNumber, string Preview, long? Remaining, long Capacity, bool Yearly, int MaxLength, bool EditorsMayOverride, long Version);

/// <summary>Schlüssel der Nummernkreise (stabil, stehen in der Datenbank und in der API).</summary>
public static class NumberRangeKeys
{
    public const string Bauakte = "bauakte";
    public const string Customer = "customer";
    public const string Supplier = "supplier";
    public const string Employee = "employee";
    public const string Resource = "resource";
    public const string Crew = "crew";
    public const string Material = "material";
    public const string Order = "order";
    public const string Inquiry = "inquiry";
    public const string Task = "task";
    public const string Defect = "defect";
    public const string Ticket = "ticket";
    public const string Feedback = "feedback";
    public const string FuelReceipt = "fuelReceipt";
}

/// <summary>
/// Beschreibung einer Nummernkreis-Art. <paramref name="Table"/>/<paramref name="Column"/> nennen die Spalte, deren
/// vorhandene Werte (auch gelöschter Datensätze) beim Vergeben übersprungen werden.
/// </summary>
public sealed record NumberRangeDefinition(string Key, string Label, string Group, string DefaultPattern, long DefaultStart, string Table, string Column,
    int MaxLength)
{
    /// <summary>Standard-Ende: bei Mustern ohne Jahr die Stellenzahl (z. B. 9999 bei „60{NNNN}“), mit Jahr unbegrenzt.</summary>
    public long? DefaultEnd => NumberPattern.IsYearly(DefaultPattern) ? null : NumberPattern.Capacity(DefaultPattern);
}

/// <summary>
/// Alle Nummernkreise mit Standardmustern. Schema: Stammdaten zählen ohne Jahr mit eigener Vorziffer
/// (Personal 10…, Maschinen 20…, Material 50…, Kunden 60…, Lieferanten 70…), Vorgänge zählen je Jahr mit Kürzel
/// (B- Bestellung, A- Anfrage, AU- Aufgabe, M- Mangel, T- Ticket, F- Rückmeldung, TB- Tankbeleg); Bauakten wie bisher {JJJJ}-{NNN}.
/// </summary>
public static class NumberRangeDefinitions
{
    public static readonly IReadOnlyList<NumberRangeDefinition> All =
    [
        new(NumberRangeKeys.Bauakte, "Bauakten", "Vorgänge", "{JJJJ}-{NNN}", 1, "bauakten", "number", 18),
        new(NumberRangeKeys.Customer, "Kunden", "Stammdaten", "60{NNNN}", 1, "contacts", "customer_number", 30),
        new(NumberRangeKeys.Supplier, "Lieferanten", "Stammdaten", "70{NNNN}", 1, "suppliers", "number", 30),
        new(NumberRangeKeys.Employee, "Personal", "Stammdaten", "10{NNN}", 1, "employees", "personnel_number", 18),
        new(NumberRangeKeys.Resource, "Maschinen und Fahrzeuge", "Stammdaten", "20{NNN}", 1, "resources", "inventory_number", 30),
        new(NumberRangeKeys.Crew, "Kolonnen", "Stammdaten", "K{NN}", 1, "crews", "number", 30),
        new(NumberRangeKeys.Material, "Material", "Stammdaten", "50{NNNN}", 1, "materials", "number", 30),
        new(NumberRangeKeys.Order, "Bestellungen", "Vorgänge", "B-{JJJJ}-{NNNN}", 1, "material_orders", "number", 18),
        new(NumberRangeKeys.Inquiry, "Anfragen", "Vorgänge", "A-{JJJJ}-{NNNN}", 1, "calls", "number", 30),
        new(NumberRangeKeys.Task, "Aufgaben", "Vorgänge", "AU-{JJJJ}-{NNNN}", 1, "work_items", "number", 30),
        new(NumberRangeKeys.Defect, "Mängel", "Vorgänge", "M-{JJJJ}-{NNNN}", 1, "work_items", "number", 30),
        new(NumberRangeKeys.Ticket, "Helpdesk-Tickets", "Verwaltung", "T-{JJJJ}-{NNNN}", 1, "tickets", "number", 18),
        new(NumberRangeKeys.Feedback, "Fehler und Wünsche", "Verwaltung", "F-{JJJJ}-{NNNN}", 1, "feedback_items", "number", 18),
        new(NumberRangeKeys.FuelReceipt, "Tankbelege", "Vorgänge", "TB-{JJJJ}-{NNNN}", 1, "fuel_receipts", "number", 18),
    ];

    public static NumberRangeDefinition Get(string key) =>
        All.FirstOrDefault(d => d.Key == key) ?? throw new ArgumentOutOfRangeException(nameof(key), key, "Unbekannter Nummernkreis.");

    public static NumberRangeDefinition? Find(string key) => All.FirstOrDefault(d => d.Key == key);
}

/// <summary>Muster: {JJJJ} Jahr, {JJ} Jahr zweistellig, {N…} laufende Nummer mit Mindeststellen; sonst Buchstaben, Ziffern und - / . _</summary>
public static partial class NumberPattern
{
    public static bool IsYearly(string pattern) => pattern.Contains("{JJ", StringComparison.Ordinal);

    /// <summary>Höchster Zählerwert, der in die Stellen des Musters passt (z. B. 9999 bei „60{NNNN}“).</summary>
    public static long Capacity(string pattern)
    {
        var digits = Counter().Matches(pattern).Select(m => m.Groups[1].Value.Length).DefaultIfEmpty(9).Min();
        return (long)Math.Pow(10, Math.Min(digits, 15)) - 1;
    }

    public static string Format(string pattern, int year, long number)
    {
        var text = pattern.Replace("{JJJJ}", year.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{JJ}", (year % 100).ToString("00", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return Counter().Replace(text, match => number.ToString(new string('0', match.Groups[1].Value.Length), CultureInfo.InvariantCulture));
    }

    /// <summary>Fehlertext oder null, wenn das Muster gültig ist.</summary>
    public static string? Validate(string? pattern, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return "Bitte ein Muster angeben, z. B. 60{NNNN}.";
        }

        if (pattern.Length > maxLength)
        {
            return $"Das Muster darf höchstens {maxLength} Zeichen lang sein.";
        }

        if (Counter().Count(pattern) != 1)
        {
            return "Das Muster braucht genau eine laufende Nummer {N…}, z. B. {NNNN}.";
        }

        if (Counter().Match(pattern).Groups[1].Value.Length > 9)
        {
            return "Die laufende Nummer darf höchstens 9 Stellen haben.";
        }

        var rest = Counter().Replace(pattern.Replace("{JJJJ}", string.Empty, StringComparison.Ordinal).Replace("{JJ}", string.Empty, StringComparison.Ordinal), string.Empty);
        return Literal().IsMatch(rest) ? null : "Erlaubt sind {JJJJ}, {JJ}, {N…} sowie Buchstaben, Ziffern und - / . _";
    }

    [GeneratedRegex("\\{(N+)\\}")]
    private static partial Regex Counter();

    [GeneratedRegex("^[A-Za-z0-9\\-/._]*$")]
    private static partial Regex Literal();
}

/// <summary>Der Nummernkreis ist ausgeschöpft (Ende des Bereichs erreicht).</summary>
public class NumberRangeExhaustedException(string key, string label, string pattern)
    : InvalidOperationException($"Der Nummernkreis {label} „{pattern}“ ist ausgeschöpft. Bitte in den Systemeinstellungen (Nummernkreise) ein neues Muster oder einen neuen Bereich festlegen.")
{
    public const string DefaultCode = "NUMBER_RANGE_EXHAUSTED";

    public string Key { get; } = key;

    public string Pattern { get; } = pattern;

    /// <summary>Kunden behalten ihren bisherigen Fehlercode (Apps, Web).</summary>
    public virtual string ErrorCode => Key == NumberRangeKeys.Customer ? "CUSTOMER_NUMBERS_EXHAUSTED" : DefaultCode;

    /// <summary>Antwort für REST-Endpunkte (409).</summary>
    public ProblemHttpResult ToProblem() => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Nummernkreis ausgeschöpft",
        detail: Message, extensions: new Dictionary<string, object?> { ["code"] = ErrorCode, ["numberRange"] = Key });
}

/// <summary>
/// Nummern werden ausschließlich aus dem Nummernkreis vergeben: Eine vom Client gesendete Nummer ist nur erlaubt,
/// wenn sie leer ist oder der bereits vergebenen entspricht. Ausnahme ist der Kunden-Import (vorhandene Nummern aus der Datei).
/// </summary>
public static class NumberProblems
{
    public const string ManualForbiddenCode = "NUMBER_MANUAL_FORBIDDEN";

    /// <summary>400: Nummer von Hand eingegeben oder geändert.</summary>
    public static ProblemHttpResult ManualForbidden() => TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
        title: "Nummer wird automatisch vergeben", detail: ManualForbiddenDetail,
        extensions: new Dictionary<string, object?> { ["code"] = ManualForbiddenCode });

    public const string ManualForbiddenDetail =
        "Nummern werden automatisch aus dem Nummernkreis vergeben und können nicht von Hand eingegeben oder geändert werden.";

    /// <summary>Leere Eingabe = automatisch (null), sonst ohne Leerzeichen am Rand.</summary>
    public static string? Clean(string? number) => string.IsNullOrWhiteSpace(number) ? null : number.Trim();

    /// <summary>
    /// Prüft eine vom Client gesendete Nummer: leer oder gleich der vergebenen ist in Ordnung (null), jede andere
    /// wird abgelehnt (<see cref="ManualForbidden"/>) – bei der Anlage ebenso wie beim Ändern.
    /// </summary>
    public static ProblemHttpResult? RejectManual(string? requested, string? current) =>
        Clean(requested) is { } number && number != current ? ManualForbidden() : null;

    /// <summary>Unique-Verletzung (23505) auf einer Nummernspalte?</summary>
    public static bool IsUniqueViolation(Exception exception, string columnFragment) =>
        exception.InnerException is Npgsql.PostgresException { SqlState: "23505" } unique
        && unique.ConstraintName?.Contains(columnFragment, StringComparison.Ordinal) == true;
}
