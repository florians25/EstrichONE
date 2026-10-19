using System.Globalization;
using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

/// <summary>
/// Vergibt Bauaktennummern <c>JJJJ-NNN</c> (ab 1000 Akten im Jahr vierstellig). Das Hochzählen ist ein einzelnes
/// atomares UPSERT; die Zeilensperre gilt bis zum Ende der Transaktion. Läuft der Aufruf in derselben Transaktion
/// wie das Anlegen der Bauakte, entstehen auch bei einem Abbruch keine Lücken.
/// </summary>
public sealed class BauakteNumberGenerator(EstrichOneDbContext db, TimeProvider clock)
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

        return Format(year, rows.Single());
    }

    public static string Format(int year, int number) =>
        string.Create(CultureInfo.InvariantCulture, $"{year}-{number:000}");
}
