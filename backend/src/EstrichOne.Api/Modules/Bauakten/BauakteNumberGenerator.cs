using System.Globalization;
using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>
/// Vergibt Bauaktennummern <c>JJJJ-NNN</c> (ab 1000 Akten im Jahr vierstellig). Das Hochzählen ist ein einzelnes
/// atomares UPSERT; die Zeilensperre gilt bis zum Ende der Transaktion. Läuft der Aufruf in derselben Transaktion
/// wie das Anlegen der Bauakte, entstehen auch bei einem Abbruch keine Lücken.
/// </summary>
public sealed class BauakteNumberGenerator(EstrichOneDbContext db, TimeProvider clock, Organization.Exports.ISystemSettings settings)
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public async Task<string> NextAsync(CancellationToken cancellationToken)
    {
        var year = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Berlin).Year;
        var rows = await db.Database
            .SqlQuery<int>($"""
                INSERT INTO bauakte_number_sequences (year, last_number) VALUES ({year}, 1)
                ON CONFLICT (year) DO UPDATE SET last_number = bauakte_number_sequences.last_number + 1
                RETURNING last_number AS "Value"
                """)
            .ToListAsync(cancellationToken);

        var pattern = (await settings.GetAsync(cancellationToken)).Numbering.BauaktePattern;
        return Format(pattern, year, rows.Single());
    }

    /// <summary>Nummer nach Muster (Einstellungen → Bauakten): {JJJJ} Jahr, {JJ} zweistellig, {N…} laufende Nummer mit Mindeststellen.</summary>
    public static string Format(string pattern, int year, int number)
    {
        var text = pattern.Replace("{JJJJ}", year.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{JJ}", (year % 100).ToString("00", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return System.Text.RegularExpressions.Regex.Replace(text, "\\{(N+)\\}",
            match => number.ToString(new string('0', match.Groups[1].Value.Length), CultureInfo.InvariantCulture));
    }

    public static string Format(int year, int number) =>
        string.Create(CultureInfo.InvariantCulture, $"{year}-{number:000}");
}
