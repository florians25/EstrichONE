using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Organization;

/// <summary>Einstellung eines Nummernkreises (eine Zeile je Art, Schlüssel siehe <see cref="NumberRangeKeys"/>).</summary>
public sealed class NumberRange : IVersionedEntity
{
    public string RecordType { get; private set; } = string.Empty;
    public string Pattern { get; private set; } = string.Empty;

    /// <summary>Erster Zählerwert (auch nach dem Jahreswechsel bei Mustern mit Jahr).</summary>
    public long RangeStart { get; private set; } = 1;

    /// <summary>Letzter Zählerwert; leer = unbegrenzt (die Nummer wird dann länger als die Mindeststellen).</summary>
    public long? RangeEnd { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private NumberRange()
    {
    }

    public static NumberRange Create(NumberRangeDefinition definition) => new()
    {
        RecordType = definition.Key, Pattern = definition.DefaultPattern, RangeStart = definition.DefaultStart, RangeEnd = definition.DefaultEnd,
    };

    public void Update(string pattern, long rangeStart, long? rangeEnd)
    {
        Pattern = pattern;
        RangeStart = rangeStart;
        RangeEnd = rangeEnd;
    }
}

/// <summary>Zähler eines Nummernkreises je Zeitraum (Jahr bei Mustern mit {JJ}/{JJJJ}, sonst 0).</summary>
public sealed class NumberRangeCounter
{
    public string RecordType { get; private set; } = string.Empty;
    public int Period { get; private set; }
    public long LastNumber { get; private set; }
}

internal sealed class NumberRangeConfiguration : IEntityTypeConfiguration<NumberRange>
{
    public void Configure(EntityTypeBuilder<NumberRange> builder)
    {
        builder.ToTable("number_ranges");
        builder.ConfigureVersioned();
        builder.HasKey(r => r.RecordType);
        builder.Property(r => r.RecordType).HasMaxLength(30);
        builder.Property(r => r.Pattern).HasMaxLength(40);
    }
}

internal sealed class NumberRangeCounterConfiguration : IEntityTypeConfiguration<NumberRangeCounter>
{
    public void Configure(EntityTypeBuilder<NumberRangeCounter> builder)
    {
        builder.ToTable("number_range_counters");
        builder.HasKey(c => new { c.RecordType, c.Period });
        builder.Property(c => c.RecordType).HasMaxLength(30);
    }
}

/// <summary>Nummernkreis ändern. <c>NextNumber</c> leer = Zähler unverändert.</summary>
public sealed record SaveNumberRangeRequest(
    [property: Required, MaxLength(40)] string Pattern,
    [property: Range(1, 999_999_999)] long RangeStart = 1,
    [property: Range(1, 999_999_999)] long? RangeEnd = null,
    [property: Range(1, 999_999_999)] long? NextNumber = null,
    long? Version = null);

internal sealed class NumberRangeService(EstrichOneDbContext db, TimeProvider clock, IUserPermissions permissions, IAuditLog audit, ICurrentUser currentUser)
    : INumberRanges
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public int Year => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Berlin).Year;

    public async Task<string> NextAsync(string key, CancellationToken cancellationToken)
    {
        var definition = NumberRangeDefinitions.Get(key);
        var range = await RangeAsync(definition, cancellationToken);
        var year = Year;
        var period = NumberPattern.IsYearly(range.Pattern) ? year : 0;
        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            // Atomares UPSERT: die Zeilensperre gilt bis zum Ende der Transaktion; parallele Anlagen erhalten verschiedene Werte.
            var value = (await db.Database.SqlQuery<long>($"""
                INSERT INTO number_range_counters (record_type, period, last_number) VALUES ({key}, {period}, {range.RangeStart})
                ON CONFLICT (record_type, period) DO UPDATE SET last_number = greatest(number_range_counters.last_number + 1, {range.RangeStart})
                RETURNING last_number AS "Value"
                """).ToListAsync(cancellationToken)).Single();
            if (range.RangeEnd is { } end && value > end)
            {
                // Zähler nicht weiter erhöhen lassen: auf das Ende zurücksetzen, damit „verbleibend“ 0 bleibt.
                await db.Database.ExecuteSqlAsync(
                    $"UPDATE number_range_counters SET last_number = {end} WHERE record_type = {key} AND period = {period} AND last_number > {end}", cancellationToken);
                throw new NumberRangeExhaustedException(key, definition.Label, range.Pattern);
            }

            var number = NumberPattern.Format(range.Pattern, year, value);
            if (!await IsUsedAsync(definition, number, cancellationToken))
            {
                return number;
            }
        }

        throw new InvalidOperationException($"Keine freie Nummer im Nummernkreis {definition.Label} gefunden. Bitte den Nummernkreis in den Einstellungen prüfen.");
    }

    public async Task<bool> MayOverrideAsync(string key, CancellationToken cancellationToken) =>
        NumberRangeDefinitions.Get(key).EditorsMayOverride || await permissions.HasAsync(Permissions.SettingsManage, cancellationToken);

    /// <summary>Vorhandene Nummer (auch gelöschter oder von Hand nummerierter Datensätze)? Tabelle und Spalte stammen aus der festen Liste.</summary>
    private async Task<bool> IsUsedAsync(NumberRangeDefinition definition, string number, CancellationToken cancellationToken)
    {
#pragma warning disable EF1002 // Bezeichner aus NumberRangeDefinitions (Konstanten), der Wert ist ein Parameter.
        var sql = $"SELECT EXISTS (SELECT 1 FROM {definition.Table} WHERE {definition.Column} = {{0}}) AS \"Value\"";
        return (await db.Database.SqlQueryRaw<bool>(sql, number).ToListAsync(cancellationToken)).Single();
#pragma warning restore EF1002
    }

    public async Task<NumberRange> RangeAsync(NumberRangeDefinition definition, CancellationToken cancellationToken) =>
        await db.Set<NumberRange>().AsNoTracking().SingleOrDefaultAsync(r => r.RecordType == definition.Key, cancellationToken)
        ?? NumberRange.Create(definition);

    public async Task<long> PeekAsync(string key, NumberRange range, CancellationToken cancellationToken)
    {
        var period = NumberPattern.IsYearly(range.Pattern) ? Year : 0;
        var last = (await db.Database.SqlQuery<long?>(
            $"""SELECT max(last_number) AS "Value" FROM number_range_counters WHERE record_type = {key} AND period = {period}""")
            .ToListAsync(cancellationToken)).SingleOrDefault();
        return last is { } l ? Math.Max(l + 1, range.RangeStart) : range.RangeStart;
    }

    public async Task<NumberRangeResponse> StateAsync(NumberRangeDefinition definition, CancellationToken cancellationToken)
    {
        var range = await RangeAsync(definition, cancellationToken);
        var next = await PeekAsync(definition.Key, range, cancellationToken);
        var capacity = NumberPattern.Capacity(range.Pattern);
        var shown = range.RangeEnd is { } end ? Math.Min(next, end) : next;
        return new NumberRangeResponse(definition.Key, definition.Label, definition.Group, range.Pattern, definition.DefaultPattern, range.RangeStart, range.RangeEnd,
            next, NumberPattern.Format(range.Pattern, Year, shown), range.RangeEnd is { } e ? Math.Max(0, e - next + 1) : null, capacity,
            NumberPattern.IsYearly(range.Pattern), definition.MaxLength, definition.EditorsMayOverride, range.Version);
    }

    public Task<NumberRangeResponse> StateAsync(string key, CancellationToken cancellationToken) =>
        StateAsync(NumberRangeDefinitions.Get(key), cancellationToken);

    public async Task SetNextAsync(string key, long nextNumber, CancellationToken cancellationToken)
    {
        var range = await RangeAsync(NumberRangeDefinitions.Get(key), cancellationToken);
        await SetNextAsync(key, range, nextNumber, cancellationToken);
        audit.Record("settings.number_range.saved", currentUser.UserId, "number_range", key,
            new Dictionary<string, string?> { ["nextNumber"] = nextNumber.ToString(CultureInfo.InvariantCulture) });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Nächsten Zählerwert im aktuellen Zeitraum setzen.</summary>
    public async Task SetNextAsync(string key, NumberRange range, long next, CancellationToken cancellationToken)
    {
        var period = NumberPattern.IsYearly(range.Pattern) ? Year : 0;
        var last = next - 1;
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO number_range_counters (record_type, period, last_number) VALUES ({key}, {period}, {last})
            ON CONFLICT (record_type, period) DO UPDATE SET last_number = {last}
            """, cancellationToken);
    }

    /// <summary>Prüft Muster und Bereich; Fehlertext oder null.</summary>
    public static string? Validate(NumberRangeDefinition definition, string pattern, long start, long? end, long? next)
    {
        if (NumberPattern.Validate(pattern, definition.MaxLength) is { } error)
        {
            return error;
        }

        var capacity = NumberPattern.Capacity(pattern);
        var yearly = NumberPattern.IsYearly(pattern);
        if (start < 1 || (!yearly && start > capacity))
        {
            return $"Der Bereich muss zwischen 1 und {capacity.ToString(CultureInfo.InvariantCulture)} beginnen (Stellen des Musters).";
        }

        if (end is { } e && (e < start || e > 999_999_999))
        {
            return "Das Ende des Bereichs muss größer oder gleich dem Anfang sein.";
        }

        if (!yearly && end > capacity)
        {
            return $"Ohne Jahr im Muster muss der Bereich in die Stellen passen (Ende höchstens {capacity.ToString(CultureInfo.InvariantCulture)}), sonst überschneiden sich Nummernkreise.";
        }

        if (next is { } n && (n < start || (end is { } last && n > last)))
        {
            return "Die nächste Nummer muss im Bereich liegen.";
        }

        return null;
    }

    /// <summary>Muster aus den alten Einstellungsfeldern übernehmen (Einstellungen › Bauakten); das Ende folgt den Stellen.</summary>
    public async Task<bool> ApplyLegacyPatternAsync(string key, string pattern, CancellationToken cancellationToken)
    {
        var definition = NumberRangeDefinitions.Get(key);
        var row = await db.Set<NumberRange>().SingleOrDefaultAsync(r => r.RecordType == key, cancellationToken);
        if (row is null)
        {
            row = NumberRange.Create(definition);
            db.Add(row);
        }

        if (row.Pattern == pattern)
        {
            return false;
        }

        row.Update(pattern, row.RangeStart, NumberPattern.IsYearly(pattern) ? null : Math.Max(row.RangeStart, NumberPattern.Capacity(pattern)));
        return true;
    }

    /// <summary>Muster von Bauakten und Kunden in den Einstellungen anzeigen (Quelle sind die Nummernkreise).</summary>
    public static async Task<SystemSettings> OverlayAsync(EstrichOneDbContext db, SystemSettings settings, CancellationToken cancellationToken)
    {
        var rows = await db.Set<NumberRange>().AsNoTracking()
            .Where(r => r.RecordType == NumberRangeKeys.Bauakte || r.RecordType == NumberRangeKeys.Customer)
            .ToDictionaryAsync(r => r.RecordType, r => r.Pattern, cancellationToken);
        return settings with
        {
            Numbering = settings.Numbering with
            {
                BauaktePattern = rows.GetValueOrDefault(NumberRangeKeys.Bauakte) ?? settings.Numbering.BauaktePattern,
                CustomerPattern = rows.GetValueOrDefault(NumberRangeKeys.Customer) ?? settings.Numbering.CustomerPattern,
            },
        };
    }
}

internal static class NumberRangeEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/settings/number-ranges").WithTags("Einstellungen");
        group.MapGet("/", ListAsync).WithName("ListNumberRanges")
            .WithSummary("Alle Nummernkreise: Muster, Bereich, nächste Nummer, Vorschau und verbleibende Nummern")
            .RequirePermission(Permissions.SettingsManage);
        group.MapGet("/{key}", GetAsync).WithName("GetNumberRange")
            .WithSummary("Ein Nummernkreis")
            .RequirePermission(Permissions.SettingsManage);
        group.MapPut("/{key}", SaveAsync).WithName("SaveNumberRange")
            .WithSummary("Muster, Bereich und nächste Nummer eines Nummernkreises ändern (protokolliert)")
            .ProducesProblem(StatusCodes.Status412PreconditionFailed).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.SettingsManage);
    }

    private static async Task<Ok<List<NumberRangeResponse>>> ListAsync(NumberRangeService service, CancellationToken cancellationToken)
    {
        var list = new List<NumberRangeResponse>();
        foreach (var definition in NumberRangeDefinitions.All)
        {
            list.Add(await service.StateAsync(definition, cancellationToken));
        }

        return TypedResults.Ok(list);
    }

    private static async Task<Results<Ok<NumberRangeResponse>, ProblemHttpResult>> GetAsync(string key, NumberRangeService service, CancellationToken cancellationToken) =>
        NumberRangeDefinitions.Find(key) is { } definition ? TypedResults.Ok(await service.StateAsync(definition, cancellationToken)) : NotFound();

    private static async Task<Results<Ok<NumberRangeResponse>, ProblemHttpResult>> SaveAsync(string key, SaveNumberRangeRequest request, NumberRangeService service,
        EstrichOneDbContext db, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (NumberRangeDefinitions.Find(key) is not { } definition)
        {
            return NotFound();
        }

        var pattern = request.Pattern?.Trim() ?? string.Empty;
        if (NumberRangeService.Validate(definition, pattern, request.RangeStart, request.RangeEnd, request.NextNumber) is { } error)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Ungültiger Nummernkreis", detail: error,
                extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
        }

        var row = await db.Set<NumberRange>().SingleOrDefaultAsync(r => r.RecordType == key, cancellationToken);
        if (row is not null && request.Version is { } version && version != row.Version)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Geändert",
                detail: "Der Nummernkreis wurde inzwischen geändert. Bitte neu laden.", extensions: new Dictionary<string, object?> { ["code"] = "VERSION_CONFLICT" });
        }

        if (row is null)
        {
            row = NumberRange.Create(definition);
            db.Add(row);
        }

        var before = $"{row.Pattern} {row.RangeStart}–{row.RangeEnd?.ToString(CultureInfo.InvariantCulture) ?? "∞"}";
        // Ohne Jahr ohne Ende: bis zur letzten Nummer, die in die Stellen passt (kein Überlauf in andere Nummernkreise).
        var end = request.RangeEnd ?? (NumberPattern.IsYearly(pattern) ? null : NumberPattern.Capacity(pattern));
        row.Update(pattern, request.RangeStart, end);
        audit.Record("settings.number_range.saved", currentUser.UserId, "number_range", key, new Dictionary<string, string?>
        {
            ["before"] = before,
            ["after"] = $"{pattern} {request.RangeStart}–{end?.ToString(CultureInfo.InvariantCulture) ?? "∞"}",
            ["nextNumber"] = request.NextNumber?.ToString(CultureInfo.InvariantCulture),
        });
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        if (request.NextNumber is { } next)
        {
            await service.SetNextAsync(key, row, next, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return TypedResults.Ok(await service.StateAsync(definition, cancellationToken));
    }

    private static ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Nicht gefunden",
        extensions: new Dictionary<string, object?> { ["code"] = "NUMBER_RANGE_NOT_FOUND" });
}

/// <summary>Ausgeschöpfter Nummernkreis an beliebiger Stelle (Web, Import, Anfrage-Übernahme …): 409 mit Fehlercode statt 500.</summary>
internal sealed class NumberRangeExceptionHandler(IProblemDetailsService problems) : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not NumberRangeExhaustedException exhausted)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Status = StatusCodes.Status409Conflict, Title = "Nummernkreis ausgeschöpft", Detail = exhausted.Message,
                Extensions = { ["code"] = exhausted.ErrorCode, ["numberRange"] = exhausted.Key },
            },
        });
    }
}
