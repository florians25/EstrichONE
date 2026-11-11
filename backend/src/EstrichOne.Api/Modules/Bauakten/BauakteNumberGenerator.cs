using System.Globalization;
using EstrichOne.Api.Modules.Organization.Exports;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>
/// Vergibt Bauaktennummern aus dem Nummernkreis „Bauakten“ (Standard <c>{JJJJ}-{NNN}</c>, ab 1000 Akten im Jahr
/// vierstellig). Das Hochzählen ist ein atomares UPSERT im gemeinsamen Nummernkreis-Dienst; die Zeilensperre gilt bis
/// zum Ende der Transaktion. Läuft der Aufruf in derselben Transaktion wie das Anlegen der Bauakte, entstehen auch
/// bei einem Abbruch keine Lücken.
/// </summary>
public sealed class BauakteNumberGenerator(INumberRanges ranges)
{
    public Task<string> NextAsync(CancellationToken cancellationToken) => ranges.NextAsync(NumberRangeKeys.Bauakte, cancellationToken);

    /// <summary>Nummer nach Muster: {JJJJ} Jahr, {JJ} zweistellig, {N…} laufende Nummer mit Mindeststellen.</summary>
    public static string Format(string pattern, int year, int number) => NumberPattern.Format(pattern, year, number);

    public static string Format(int year, int number) =>
        string.Create(CultureInfo.InvariantCulture, $"{year}-{number:000}");
}
