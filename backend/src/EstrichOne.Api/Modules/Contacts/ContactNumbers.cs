using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Contacts;

/// <summary>Stand des Kunden-Nummernkreises: Muster aus den Systemeinstellungen, nächster Zählerwert und Vorschau.</summary>
/// <param name="Capacity">Höchster Zählerwert des Musters (z. B. 9999 bei „60{NNNN}“).</param>
/// <param name="Remaining">Noch freie Zählerwerte; 0 = Nummernkreis ausgeschöpft.</param>
public sealed record CustomerNumberingResponse(string Pattern, long NextNumber, string Preview, long Capacity, long Remaining);

/// <summary>Nächsten Zählerwert setzen (z. B. Start bei 1000 nach Übernahme aus einem Altsystem).</summary>
public sealed record SaveCustomerNumberingRequest([property: Range(1, 999_999_999)] long NextNumber);

/// <summary>Einzeiliger Zähler des Kunden-Nummernkreises (Schlüssel immer 1).</summary>
public sealed class CustomerNumberSequence
{
    public int Id { get; private set; }
    public long LastNumber { get; private set; }
}

internal sealed class CustomerNumberSequenceConfiguration : IEntityTypeConfiguration<CustomerNumberSequence>
{
    public void Configure(EntityTypeBuilder<CustomerNumberSequence> builder)
    {
        builder.ToTable("customer_number_sequences");
        builder.HasKey(sequence => sequence.Id);
        builder.Property(sequence => sequence.Id).ValueGeneratedNever();
    }
}

/// <summary>
/// Vergibt Kundennummern nach dem Muster der Systemeinstellungen ({N…} laufende Nummer, {JJJJ}/{JJ} Jahr). Das Hochzählen
/// ist ein atomares UPSERT auf einer Zeile (Zeilensperre bis Transaktionsende); parallele Anlagen erhalten verschiedene
/// Werte. Bereits vorhandene Nummern (auch gelöschter Kunden oder von Hand vergebene) werden übersprungen.
/// </summary>
internal sealed class CustomerNumberGenerator(EstrichOneDbContext db, ISystemSettings settings, TimeProvider clock) : ICustomerNumbers
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public async Task<string> NextAsync(CancellationToken cancellationToken)
    {
        var pattern = (await settings.GetAsync(cancellationToken)).Numbering.CustomerPattern;
        var year = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Berlin).Year;
        var capacity = NumberingSettings.Capacity(pattern);
        for (var attempt = 0; attempt < 1000; attempt++)
        {
            var value = (await db.Database.SqlQuery<long>($"""
                INSERT INTO customer_number_sequences (id, last_number) VALUES (1, 1)
                ON CONFLICT (id) DO UPDATE SET last_number = customer_number_sequences.last_number + 1
                RETURNING last_number AS "Value"
                """).ToListAsync(cancellationToken)).Single();
            if (value > capacity)
            {
                // Zähler nicht weiter erhöhen lassen: auf die Grenze zurücksetzen, damit „Remaining“ 0 bleibt.
                await db.Database.ExecuteSqlAsync($"UPDATE customer_number_sequences SET last_number = {capacity} WHERE id = 1 AND last_number > {capacity}", cancellationToken);
                throw new CustomerNumbersExhaustedException(pattern);
            }

            var number = Format(pattern, year, value);
            if (!await db.Set<Contact>().IgnoreQueryFilters([QueryFilters.NotDeleted]).AnyAsync(c => c.CustomerNumber == number, cancellationToken))
            {
                return number;
            }
        }

        throw new InvalidOperationException("Keine freie Kundennummer gefunden. Bitte den Nummernkreis in den Einstellungen prüfen.");
    }

    public static string Format(string pattern, int year, long number) =>
        NumberingSettings.Format(pattern, year, number);

    public async Task<long> PeekAsync(CancellationToken cancellationToken) =>
        (await db.Database.SqlQuery<long>($"""SELECT coalesce(max(last_number), 0) + 1 AS "Value" FROM customer_number_sequences""")
            .ToListAsync(cancellationToken)).Single();
}

internal static class ContactNumbers
{
    public static void Map(RouteGroupBuilder contacts)
    {
        contacts.MapGet("/numbering", GetAsync).WithName("GetCustomerNumbering")
            .WithSummary("Kunden-Nummernkreis: Muster, nächster Zählerwert und Vorschau der nächsten Kundennummer")
            .RequirePermission(Permissions.ContactsRead);
        contacts.MapPut("/numbering", SaveAsync).WithName("SaveCustomerNumbering")
            .WithSummary("Nächsten Zählerwert des Kunden-Nummernkreises setzen (das Muster steht in den Systemeinstellungen)")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.SettingsManage);
    }

    private static async Task<Ok<CustomerNumberingResponse>> GetAsync(EstrichOneDbContext db, ISystemSettings settings, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var generator = new CustomerNumberGenerator(db, settings, clock);
        var pattern = (await settings.GetAsync(cancellationToken)).Numbering.CustomerPattern;
        var next = await generator.PeekAsync(cancellationToken);
        var capacity = NumberingSettings.Capacity(pattern);
        return TypedResults.Ok(new CustomerNumberingResponse(pattern, next, CustomerNumberGenerator.Format(pattern, clock.GetUtcNow().Year, Math.Min(next, capacity)),
            capacity, Math.Max(0, capacity - next + 1)));
    }

    private static async Task<Results<Ok<CustomerNumberingResponse>, ProblemHttpResult>> SaveAsync(
        SaveCustomerNumberingRequest request, EstrichOneDbContext db, ISystemSettings settings, TimeProvider clock, CancellationToken cancellationToken)
    {
        if (request.NextNumber is < 1 or > 999_999_999)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Ungültiger Wert",
                detail: "Bitte eine Zahl zwischen 1 und 999999999 angeben.", extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
        }

        var last = request.NextNumber - 1;
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO customer_number_sequences (id, last_number) VALUES (1, {last})
            ON CONFLICT (id) DO UPDATE SET last_number = {last}
            """, cancellationToken);
        return await GetAsync(db, settings, clock, cancellationToken);
    }
}
