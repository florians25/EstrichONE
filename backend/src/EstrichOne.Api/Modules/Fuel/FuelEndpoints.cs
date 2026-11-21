using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using EstrichOne.Api.Modules.Planning.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Fuel;

/// <param name="UserId">Nur mit <c>fuel.manage</c>: Beleg einer anderen Person nachtragen (sonst die eigene).</param>
/// <param name="Number">Nur zur Kontrolle: Nummern vergibt der Nummernkreis „Tankbelege“ (leer oder unverändert).</param>
public sealed record SaveFuelReceiptRequest(
    Guid VehicleId, DateOnly Date, int? OdometerKm, decimal Liters, decimal AmountGross, FuelType FuelType, FuelPaymentMethod PaymentMethod,
    [property: MaxLength(FuelReceipt.MaxStationLength)] string? Station = null, [property: MaxLength(FuelReceipt.MaxNotesLength)] string? Notes = null,
    Guid? ImageFileId = null, Guid? UserId = null, [property: MaxLength(30)] string? Number = null, long? Version = null);

/// <param name="Reason">Pflicht beim Ablehnen.</param>
public sealed record FuelDecisionRequest([property: Required, MinLength(1), MaxLength(500)] IReadOnlyList<Guid> Ids, FuelDecision Action,
    [property: MaxLength(500)] string? Reason = null);

/// <param name="LastOdometerKm">Höchster bekannter Kilometerstand (Hinweis in der App, wenn kleiner eingegeben).</param>
public sealed record FuelVehicleOption(Guid Id, string Kind, string Name, string? Identifier, decimal? TankCapacityLiters, int? LastOdometerKm,
    FuelType? LastFuelType);

/// <param name="SuggestedVehicleId">Heutiges Dispo-Fahrzeug der Person (Vorbelegung in der App).</param>
public sealed record FuelOptionsResponse(IReadOnlyList<FuelVehicleOption> Vehicles, Guid? SuggestedVehicleId, DateOnly Today);

/// <summary>Tankbelege: Erfassen (App, Web), Prüfen und Freigeben, Auswertung je Fahrzeug, CSV für die Buchhaltung.</summary>
internal static class FuelEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var fuel = api.MapGroup("/fuel-receipts").WithTags("Tankbelege").RequireModule(AppModules.Fuel);
        fuel.MapGet("/options", OptionsAsync).WithName("GetFuelReceiptOptions")
            .WithSummary("Fahrzeuge zur Auswahl mit letztem Kilometerstand und das heutige Dispo-Fahrzeug (Vorbelegung)")
            .RequirePermission(Permissions.FuelRecord);
        fuel.MapGet("/mine", MineAsync).WithName("ListMyFuelReceipts")
            .WithSummary("Eigene Tankbelege (neueste zuerst)").RequirePermission(Permissions.FuelRecord);
        fuel.MapGet("/", ListAsync).WithName("ListFuelReceipts")
            .WithSummary("Alle Tankbelege mit Filter (Zeitraum, Fahrzeug, Mitarbeiter, Status, Zahlart) und Plausibilitätshinweisen")
            .RequirePermission(Permissions.FuelManage);
        fuel.MapGet("/statistics", StatisticsAsync).WithName("GetFuelStatistics")
            .WithSummary("Auswertung je Fahrzeug: Liter, Kosten, Strecke und Ø-Verbrauch je Monat (ohne abgelehnte Belege)")
            .ProducesProblem(StatusCodes.Status400BadRequest).RequirePermission(Permissions.FuelManage);
        fuel.MapGet("/export.csv", ExportAsync).WithName("ExportFuelReceipts")
            .WithSummary("CSV für die Buchhaltung (DATEV-freundlich: Semikolon, Komma als Dezimalzeichen, Belegdatum, Belegnummer, Buchungstext, Kostenstelle)")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv").ProducesProblem(StatusCodes.Status400BadRequest)
            .RequirePermission(Permissions.FuelExport);
        fuel.MapGet("/{id:guid}", GetAsync).WithName("GetFuelReceipt").ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.FuelRecord);
        fuel.MapPut("/{id:guid}", SaveAsync).WithName("SaveFuelReceipt")
            .WithSummary("Tankbeleg anlegen oder ändern (Nummer aus dem Nummernkreis; eigene bis zur Prüfung, mit fuel.manage alle bis zur Erstattung)")
            .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.FuelRecord);
        fuel.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteFuelReceipt")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).RequirePermission(Permissions.FuelRecord);
        fuel.MapPost("/decisions", DecideAsync).WithName("DecideFuelReceipts")
            .WithSummary("Prüfen/Freigeben, Ablehnen (mit Grund), als erstattet bzw. gebucht markieren oder zurücknehmen, auch für mehrere Belege")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.FuelManage);
    }

    private static async Task<Ok<FuelOptionsResponse>> OptionsAsync(
        FuelService service, IVehicleDirectory vehicles, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var list = await vehicles.ListAsync(includeInactive: false, cancellationToken);
        var history = await service.HistoryAsync(list.Select(v => v.Id).ToList(), cancellationToken);
        var options = list.Select(v =>
        {
            var own = history.Where(h => h.VehicleId == v.Id && h.Counts).ToList();
            return new FuelVehicleOption(v.Id, v.Kind, v.Name, v.Identifier, v.TankCapacityLiters, own.Max(h => h.OdometerKm),
                own.Where(h => h.IsFuel).OrderByDescending(h => h.Date).ThenByDescending(h => h.CreatedAt).Select(h => (FuelType?)h.FuelType).FirstOrDefault());
        }).OrderBy(v => v.Kind == "vehicle" ? 0 : 1).ThenBy(v => v.Name, StringComparer.CurrentCulture).ToList();
        var suggested = await vehicles.VehicleOfDayAsync(currentUser.UserId, service.Today, cancellationToken);
        return TypedResults.Ok(new FuelOptionsResponse(options, suggested, service.Today));
    }

    private static async Task<Ok<IReadOnlyList<FuelReceiptResponse>>> MineAsync(
        EstrichOneDbContext db, FuelService service, ICurrentUser currentUser, CancellationToken cancellationToken, [FromQuery] int? limit = null)
    {
        var rows = await db.Set<FuelReceipt>().AsNoTracking().Where(r => r.UserId == currentUser.UserId || r.CreatedByUserId == currentUser.UserId)
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt).Take(Math.Clamp(limit ?? 100, 1, 500)).ToListAsync(cancellationToken);
        return TypedResults.Ok(await service.ResponsesAsync(rows, cancellationToken));
    }

    private static IQueryable<FuelReceipt> Filter(IQueryable<FuelReceipt> query, DateOnly? from, DateOnly? to, Guid? vehicleId, Guid? userId,
        string? status, string? paymentMethod)
    {
        if (from is { } f)
        {
            query = query.Where(r => r.Date >= f);
        }

        if (to is { } t)
        {
            query = query.Where(r => r.Date <= t);
        }

        if (vehicleId is { } vehicle)
        {
            query = query.Where(r => r.VehicleId == vehicle);
        }

        if (userId is { } user)
        {
            query = query.Where(r => r.UserId == user);
        }

        // Mehrere Werte kommagetrennt (z. B. status=new,checked); „open“ = neu oder geprüft (noch nicht erstattet/gebucht).
        var statuses = (status ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(s => s == "open" ? [nameof(FuelReceiptStatus.New), nameof(FuelReceiptStatus.Checked)] : new[] { s })
            .Select(s => Enum.TryParse<FuelReceiptStatus>(s, ignoreCase: true, out var value) ? (FuelReceiptStatus?)value : null).OfType<FuelReceiptStatus>()
            .Distinct().ToList();
        if (statuses.Count > 0)
        {
            query = query.Where(r => statuses.Contains(r.Status));
        }

        if (Enum.TryParse<FuelPaymentMethod>(paymentMethod, ignoreCase: true, out var payment))
        {
            query = query.Where(r => r.PaymentMethod == payment);
        }

        return query;
    }

    private static async Task<Ok<IReadOnlyList<FuelReceiptResponse>>> ListAsync(
        EstrichOneDbContext db, FuelService service, CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] Guid? vehicleId = null, [FromQuery] Guid? userId = null,
        [FromQuery] string? status = null, [FromQuery] string? paymentMethod = null, [FromQuery] bool warningsOnly = false, [FromQuery] int? limit = null)
    {
        var rows = await Filter(db.Set<FuelReceipt>().AsNoTracking(), from, to, vehicleId, userId, status, paymentMethod)
            .OrderByDescending(r => r.Date).ThenByDescending(r => r.Number).Take(Math.Clamp(limit ?? 500, 1, 2000)).ToListAsync(cancellationToken);
        var responses = await service.ResponsesAsync(rows, cancellationToken);
        return TypedResults.Ok(warningsOnly ? responses.Where(r => r.Warnings.Any(w => w.Code != "missing_image")).ToList() : responses);
    }

    private static async Task<Results<Ok<IReadOnlyList<FuelVehicleStatistics>>, ProblemHttpResult>> StatisticsAsync(
        FuelService service, IVehicleDirectory vehicles, CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] Guid? vehicleId = null)
    {
        var end = to ?? service.Today;
        var start = from ?? new DateOnly(end.Year, end.Month, 1).AddMonths(-11);
        if (end < start || end.DayNumber - start.DayNumber > 3 * 366)
        {
            return Problem(StatusCodes.Status400BadRequest, "BAD_REQUEST", "Bitte einen Zeitraum von höchstens drei Jahren wählen.");
        }

        var list = await vehicles.ListAsync(includeInactive: true, cancellationToken);
        var ids = vehicleId is { } one ? [one] : list.Select(v => v.Id).ToList();
        var history = await service.HistoryAsync(ids, cancellationToken);
        var evaluations = FuelAnalysis.Evaluate(history, list.ToDictionary(v => v.Id, v => v.TankCapacityLiters));
        return TypedResults.Ok(FuelAnalysis.Statistics(history, evaluations, list.ToDictionary(v => v.Id, v => (v.Name, v.Identifier)), start, end));
    }

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> ExportAsync(
        EstrichOneDbContext db, FuelService service, IVehicleDirectory vehicles, CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] Guid? vehicleId = null, [FromQuery] Guid? userId = null,
        [FromQuery] string? status = null, [FromQuery] string? paymentMethod = null)
    {
        if (from is { } f && to is { } t && t < f)
        {
            return Problem(StatusCodes.Status400BadRequest, "BAD_REQUEST", "Das Ende des Zeitraums liegt vor dem Anfang.");
        }

        // Ohne Statusfilter: alles außer abgelehnten Belegen.
        var query = Filter(db.Set<FuelReceipt>().AsNoTracking(), from, to, vehicleId, userId, status, paymentMethod);
        if (string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(r => r.Status != FuelReceiptStatus.Rejected);
        }

        var rows = await query.OrderBy(r => r.Date).ThenBy(r => r.Number).Take(20_000).ToListAsync(cancellationToken);
        var list = (await vehicles.ListAsync(includeInactive: true, cancellationToken)).ToDictionary(v => v.Id);
        var userIds = rows.Select(r => r.UserId).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var csv = FuelCsv.Build(rows, list, names);
        var name = $"Tankbelege {(from ?? rows.FirstOrDefault()?.Date ?? service.Today):yyyy-MM-dd} bis {(to ?? service.Today):yyyy-MM-dd}.csv";
        return TypedResults.File(csv, "text/csv; charset=utf-8", name);
    }

    private static async Task<Results<Ok<FuelReceiptResponse>, ProblemHttpResult>> GetAsync(
        Guid id, EstrichOneDbContext db, FuelService service, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var receipt = await db.Set<FuelReceipt>().AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receipt is null || (receipt.UserId != currentUser.UserId && receipt.CreatedByUserId != currentUser.UserId
            && !await service.MayManageAsync(cancellationToken)))
        {
            return Problem(StatusCodes.Status404NotFound, "FUEL_RECEIPT_NOT_FOUND", "Der Tankbeleg existiert nicht.");
        }

        return TypedResults.Ok((await service.ResponsesAsync([receipt], cancellationToken))[0]);
    }

    private static async Task<Results<Ok<FuelReceiptResponse>, ProblemHttpResult>> SaveAsync(
        Guid id, SaveFuelReceiptRequest request, EstrichOneDbContext db, FuelService service, CancellationToken cancellationToken)
    {
        var current = await db.Set<FuelReceipt>().AsNoTracking().Where(r => r.Id == id).Select(r => r.Number).SingleOrDefaultAsync(cancellationToken);
        if (NumberProblems.RejectManual(request.Number, current) is { } manual)
        {
            return manual;
        }

        var input = new FuelReceiptInput(request.VehicleId, request.Date, request.OdometerKm, request.Liters, request.AmountGross, request.FuelType,
            request.PaymentMethod, request.Station, request.Notes, request.ImageFileId, request.UserId, "web");
        FuelResult result;
        try
        {
            result = await service.SaveAsync(id, input, request.Version, cancellationToken);
        }
        catch (NumberRangeExhaustedException exception)
        {
            return exception.ToProblem();
        }

        return result.Receipt is { } receipt
            ? TypedResults.Ok((await service.ResponsesAsync([receipt], cancellationToken))[0])
            : Failure(result);
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid id, FuelService service, CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken);
        return result.Receipt is null ? Failure(result) : TypedResults.NoContent();
    }

    private static async Task<Results<Ok<IReadOnlyList<FuelReceiptResponse>>, ProblemHttpResult>> DecideAsync(
        FuelDecisionRequest request, FuelService service, CancellationToken cancellationToken)
    {
        if (request.Action == FuelDecision.Reject && string.IsNullOrWhiteSpace(request.Reason))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Bitte kurz begründen, warum der Beleg abgelehnt wird.");
        }

        var (changed, problem) = await service.DecideAsync(request.Ids, request.Action, request.Reason, cancellationToken);
        return problem is null
            ? TypedResults.Ok(await service.ResponsesAsync(changed, cancellationToken))
            : Problem(StatusCodes.Status409Conflict, "FUEL_STATUS_NOT_ALLOWED", problem);
    }

    private static ProblemHttpResult Failure(FuelResult result) => result.Failure switch
    {
        FuelFailure.NotFound => Problem(StatusCodes.Status404NotFound, "FUEL_RECEIPT_NOT_FOUND", result.Message),
        FuelFailure.Final => Problem(StatusCodes.Status409Conflict, "FUEL_RECEIPT_LOCKED", result.Message),
        FuelFailure.IdConflict => Problem(StatusCodes.Status409Conflict, "ID_CONFLICT", result.Message),
        _ => Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", result.Message),
    };

    private static ProblemHttpResult Problem(int status, string code, string? detail) => TypedResults.Problem(statusCode: status,
        title: status == StatusCodes.Status404NotFound ? "Nicht gefunden" : "Tankbeleg", detail: detail,
        extensions: new Dictionary<string, object?> { ["code"] = code });
}

/// <summary>
/// CSV für die Buchhaltung: Semikolon, Komma als Dezimalzeichen, UTF-8 mit BOM (öffnet in Excel und im DATEV-Import „ASCII-Daten“).
/// Spalten in der Reihenfolge eines Buchungsstapels: Umsatz, Soll/Haben, WKZ, Belegdatum, Belegfeld 1 (Belegnummer), Buchungstext
/// (höchstens 60 Zeichen), Kostenstelle (Inventarnummer des Fahrzeugs), dazu die Angaben des Belegs.
/// </summary>
internal static class FuelCsv
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static readonly string[] Header =
    [
        "Umsatz (brutto)", "Soll/Haben-Kennzeichen", "WKZ Umsatz", "Belegdatum", "Belegfeld 1", "Buchungstext", "KOST1 - Kostenstelle", "Steuersatz",
        "Fahrzeug", "Kennzeichen", "Kraftstoff", "Liter", "Preis je Liter", "Kilometerstand", "Zahlart", "Erstattung an", "Mitarbeiter", "Tankstelle",
        "Status",
    ];

    public static byte[] Build(IReadOnlyList<FuelReceipt> rows, IReadOnlyDictionary<Guid, VehicleInfo> vehicles, IReadOnlyDictionary<Guid, string> names)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Join(';', Header.Select(Field)));
        foreach (var r in rows)
        {
            var vehicle = vehicles.GetValueOrDefault(r.VehicleId);
            var person = names.GetValueOrDefault(r.UserId) ?? string.Empty;
            var booking = $"{FuelTypeText(r.FuelType)} {r.Liters.ToString("0.##", German)} l {vehicle?.Identifier ?? vehicle?.Name}".Trim();
            text.AppendLine(string.Join(';', new[]
            {
                r.AmountGross.ToString("0.00", German), "S", "EUR", r.Date.ToString("dd.MM.yyyy", German), r.Number,
                booking.Length > 60 ? booking[..60] : booking, vehicle?.InventoryNumber ?? string.Empty,
                r.FuelType == FuelType.Other ? string.Empty : "19", vehicle?.Name ?? string.Empty, vehicle?.Identifier ?? string.Empty,
                FuelTypeText(r.FuelType), r.Liters.ToString("0.00", German), r.PricePerLiter.ToString("0.000", German),
                r.OdometerKm?.ToString(CultureInfo.InvariantCulture) ?? string.Empty, PaymentText(r.PaymentMethod),
                r.PaymentMethod == FuelPaymentMethod.Private ? person : string.Empty, person, r.Station ?? string.Empty,
                FuelService.StatusText(r.Status, r.PaymentMethod),
            }.Select(Field)));
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text.ToString())];
    }

    public static string FuelTypeText(FuelType type) => type switch
    {
        FuelType.Diesel => "Diesel",
        FuelType.Petrol => "Benzin",
        FuelType.AdBlue => "AdBlue",
        _ => "Sonstiges",
    };

    public static string PaymentText(FuelPaymentMethod method) => method switch
    {
        FuelPaymentMethod.FuelCard => "Tankkarte",
        FuelPaymentMethod.CompanyCard => "Firmenkarte",
        _ => "Privat ausgelegt",
    };

    /// <summary>Feld in Anführungszeichen, wenn nötig; führende =, +, -, @ werden entschärft (keine Formeln in Excel).</summary>
    internal static string Field(string value)
    {
        if (value.Length > 0 && "=+@".Contains(value[0], StringComparison.Ordinal) || (value.StartsWith('-') && !decimal.TryParse(value, NumberStyles.Number, German, out _)))
        {
            value = "'" + value;
        }

        return value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
    }
}
