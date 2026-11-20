using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Documentation.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using EstrichOne.Api.Modules.Planning.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Fuel;

/// <param name="StatusText">Neu, Geprüft, Abgelehnt, Erstattet (privat ausgelegt) bzw. Gebucht (Karte).</param>
/// <param name="ImageStored">Belegfoto vollständig hochgeladen (sonst noch unterwegs vom Gerät).</param>
/// <param name="DistanceKm">Strecke seit dem vorigen Tanken dieses Fahrzeugs (mit Kilometerstand).</param>
/// <param name="ConsumptionPer100Km">Verbrauch dieses Tankens (voll zu voll).</param>
public sealed record FuelReceiptResponse(
    Guid Id, string Number, Guid VehicleId, string VehicleName, string? VehicleIdentifier, Guid UserId, string UserName, DateOnly Date, int? OdometerKm,
    decimal Liters, decimal AmountGross, decimal PricePerLiter, FuelType FuelType, FuelPaymentMethod PaymentMethod, string? Station, string? Notes,
    Guid? ImageFileId, bool ImageStored, FuelReceiptStatus Status, string StatusText, string? RejectionReason, string? CheckedByName,
    DateTimeOffset? CheckedAt, string? SettledByName, DateTimeOffset? SettledAt, int? DistanceKm, decimal? ConsumptionPer100Km,
    IReadOnlyList<FuelWarning> Warnings, string Source, bool CanEdit, DateTimeOffset CreatedAt, long Version);

/// <summary>Eingaben eines Tankbelegs (App über Sync, Web über PUT).</summary>
internal sealed record FuelReceiptInput(
    Guid VehicleId, DateOnly Date, int? OdometerKm, decimal Liters, decimal AmountGross, FuelType FuelType, FuelPaymentMethod PaymentMethod,
    string? Station, string? Notes, Guid? ImageFileId, Guid? UserId, string Source);

internal enum FuelFailure
{
    Validation,
    NotFound,
    Forbidden,
    Final,
    IdConflict,
}

internal sealed record FuelResult(FuelReceipt? Receipt, FuelFailure? Failure = null, string? Message = null);

/// <summary>Anlegen, Ändern und Prüfen von Tankbelegen; Antworten mit Plausibilitätshinweisen.</summary>
internal sealed class FuelService(
    EstrichOneDbContext db, ICurrentUser currentUser, IUserPermissions permissions, IVehicleDirectory vehicles, IStoredFiles files,
    INumberRanges numberRanges, IAuditLog audit, TimeProvider clock)
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Berlin).DateTime);

    public Task<bool> MayManageAsync(CancellationToken cancellationToken) => permissions.HasAsync(Permissions.FuelManage, cancellationToken);

    /// <summary>Anlegen (neue ID) oder ändern. Eigene Belege bis zur Freigabe; mit <c>fuel.manage</c> alle bis zur Erstattung/Buchung.</summary>
    public async Task<FuelResult> SaveAsync(Guid id, FuelReceiptInput input, long? expectedVersion, CancellationToken cancellationToken)
    {
        var manage = await MayManageAsync(cancellationToken);
        var receipt = await db.Set<FuelReceipt>().IgnoreQueryFilters([QueryFilters.NotDeleted]).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (receipt is { DeletedAt: not null })
        {
            return new(null, FuelFailure.IdConflict, "Unter dieser ID gab es bereits einen (inzwischen gelöschten) Tankbeleg.");
        }

        if (receipt is not null)
        {
            if (receipt.UserId != currentUser.UserId && receipt.CreatedByUserId != currentUser.UserId && !manage)
            {
                return new(null, FuelFailure.NotFound, "Der Tankbeleg existiert nicht.");
            }

            if (receipt.IsFinal)
            {
                return new(null, FuelFailure.Final, "Erstattete bzw. gebuchte Tankbelege können nicht mehr geändert werden.");
            }

            if (!manage && receipt.Status == FuelReceiptStatus.Checked)
            {
                return new(null, FuelFailure.Final, "Der Beleg ist bereits geprüft. Änderungen bitte über das Büro.");
            }

            if (expectedVersion is { } version && version != receipt.Version)
            {
                return new(null, FuelFailure.Final, "Der Tankbeleg wurde inzwischen geändert. Bitte neu laden.");
            }
        }

        var userId = input.UserId is { } chosen && manage ? chosen : receipt?.UserId ?? currentUser.UserId;
        if (userId != currentUser.UserId && !await db.Set<User>().AnyAsync(u => u.Id == userId, cancellationToken))
        {
            return new(null, FuelFailure.Validation, "Unbekannter Mitarbeiter.");
        }

        var known = await vehicles.ListAsync(includeInactive: true, cancellationToken);
        if (known.All(v => v.Id != input.VehicleId))
        {
            return new(null, FuelFailure.Validation, "Unbekanntes Fahrzeug. Bitte ein Fahrzeug aus „Geräte & Fahrzeuge“ wählen.");
        }

        if (input.ImageFileId is { } imageId && imageId != receipt?.ImageFileId)
        {
            // Nur eigene Dateien ohne Bauakte; die Archivregel „Tankbelege“ gilt dann für das Foto.
            if (!await files.ClassifyOwnAsync(imageId, currentUser.UserId, StoredFileCategories.FuelReceipt, cancellationToken))
            {
                return new(null, FuelFailure.Validation, "Das Belegfoto wurde nicht gefunden.");
            }
        }

        var created = receipt is null;
        try
        {
            receipt ??= FuelReceipt.Create(id, await numberRanges.NextAsync(NumberRangeKeys.FuelReceipt, cancellationToken), userId, currentUser.UserId,
                input.Source);
            receipt.Update(input.VehicleId, input.Date, input.OdometerKm, input.Liters, input.AmountGross, input.FuelType, input.PaymentMethod,
                input.Station, input.Notes, input.ImageFileId, Today);
            receipt.ChangeUser(userId);
        }
        catch (ArgumentException exception)
        {
            return new(null, FuelFailure.Validation, exception.Message);
        }

        if (created)
        {
            db.Add(receipt);
        }

        audit.Record(created ? "fuel.receipt_created" : "fuel.receipt_changed", currentUser.UserId, "fuel_receipt", receipt.Id.ToString(),
            new Dictionary<string, string?> { ["number"] = receipt.Number, ["amount"] = receipt.AmountGross.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        await db.SaveChangesAsync(cancellationToken);
        return new(receipt);
    }

    /// <summary>Prüfen, freigeben, ablehnen, erstatten/buchen oder zurücknehmen – für mehrere Belege auf einmal.</summary>
    public async Task<(IReadOnlyList<FuelReceipt> Changed, string? Problem)> DecideAsync(IReadOnlyCollection<Guid> ids, FuelDecision action, string? reason,
        CancellationToken cancellationToken)
    {
        var receipts = await db.Set<FuelReceipt>().Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken);
        if (receipts.Count != ids.Distinct().Count())
        {
            return ([], "Mindestens ein Tankbeleg existiert nicht (mehr).");
        }

        var now = clock.GetUtcNow();
        foreach (var receipt in receipts)
        {
            try
            {
                switch (action)
                {
                    case FuelDecision.Check: receipt.Check(currentUser.UserId, now); break;
                    case FuelDecision.Reject: receipt.Reject(currentUser.UserId, now, reason); break;
                    case FuelDecision.Settle: receipt.Settle(currentUser.UserId, now); break;
                    default: receipt.Reopen(); break;
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                return ([], $"{receipt.Number}: {exception.Message}");
            }

            audit.Record($"fuel.receipt_{action.ToString().ToLowerInvariant()}", currentUser.UserId, "fuel_receipt", receipt.Id.ToString(),
                new Dictionary<string, string?> { ["number"] = receipt.Number, ["reason"] = action == FuelDecision.Reject ? reason : null });
        }

        await db.SaveChangesAsync(cancellationToken);
        return (receipts, null);
    }

    public async Task<FuelResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var receipt = await db.Set<FuelReceipt>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        var manage = await MayManageAsync(cancellationToken);
        if (receipt is null || (!manage && receipt.UserId != currentUser.UserId && receipt.CreatedByUserId != currentUser.UserId))
        {
            return new(null, FuelFailure.NotFound, "Der Tankbeleg existiert nicht.");
        }

        if (receipt.IsFinal || (!manage && receipt.Status == FuelReceiptStatus.Checked))
        {
            return new(null, FuelFailure.Final, "Geprüfte, erstattete oder gebuchte Tankbelege können nicht gelöscht werden.");
        }

        receipt.Delete(clock.GetUtcNow());
        audit.Record("fuel.receipt_deleted", currentUser.UserId, "fuel_receipt", receipt.Id.ToString(),
            new Dictionary<string, string?> { ["number"] = receipt.Number });
        await db.SaveChangesAsync(cancellationToken);
        return new(receipt);
    }

    /// <summary>Antworten mit Fahrzeug, Person, Fotostand und Hinweisen (Vergleich mit allen Belegen der Fahrzeuge).</summary>
    public async Task<IReadOnlyList<FuelReceiptResponse>> ResponsesAsync(IReadOnlyList<FuelReceipt> receipts, CancellationToken cancellationToken)
    {
        if (receipts.Count == 0)
        {
            return [];
        }

        var manage = await MayManageAsync(cancellationToken);
        var vehicleIds = receipts.Select(r => r.VehicleId).Distinct().ToList();
        var history = await HistoryAsync(vehicleIds, cancellationToken);
        var vehicleList = (await vehicles.ListAsync(includeInactive: true, cancellationToken)).ToDictionary(v => v.Id);
        var evaluations = FuelAnalysis.Evaluate(history, vehicleList.ToDictionary(v => v.Key, v => v.Value.TankCapacityLiters));
        var userIds = receipts.SelectMany(r => new[] { r.UserId, r.CheckedByUserId ?? Guid.Empty, r.SettledByUserId ?? Guid.Empty }).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var images = await files.FindAsync(receipts.Where(r => r.ImageFileId != null).Select(r => r.ImageFileId!.Value).ToList(), cancellationToken);
        return receipts.Select(r =>
        {
            var vehicle = vehicleList.GetValueOrDefault(r.VehicleId);
            var evaluation = evaluations.GetValueOrDefault(r.Id) ?? new FuelEvaluation(null, null, []);
            var own = r.UserId == currentUser.UserId || r.CreatedByUserId == currentUser.UserId;
            return new FuelReceiptResponse(r.Id, r.Number, r.VehicleId, vehicle?.Name ?? "Unbekanntes Fahrzeug", vehicle?.Identifier, r.UserId,
                names.GetValueOrDefault(r.UserId) ?? "Unbekannt", r.Date, r.OdometerKm, r.Liters, r.AmountGross, r.PricePerLiter, r.FuelType, r.PaymentMethod,
                r.Station, r.Notes, r.ImageFileId, r.ImageFileId is { } image && images.TryGetValue(image, out var info) && info.IsStored, r.Status,
                StatusText(r.Status, r.PaymentMethod), r.RejectionReason, r.CheckedByUserId is { } c ? names.GetValueOrDefault(c) : null, r.CheckedAt,
                r.SettledByUserId is { } s ? names.GetValueOrDefault(s) : null, r.SettledAt, evaluation.DistanceKm, evaluation.ConsumptionPer100Km,
                evaluation.Warnings, r.Source, !r.IsFinal && (manage || (own && r.Status != FuelReceiptStatus.Checked)), r.CreatedAt, r.Version);
        }).ToList();
    }

    /// <summary>Alle (nicht gelöschten) Belege der Fahrzeuge der letzten drei Jahre – Grundlage für Verbrauch und Hinweise.</summary>
    public async Task<IReadOnlyList<FuelEntry>> HistoryAsync(IReadOnlyCollection<Guid> vehicleIds, CancellationToken cancellationToken)
    {
        var since = Today.AddYears(-3);
        return (await db.Set<FuelReceipt>().AsNoTracking().Where(r => vehicleIds.Contains(r.VehicleId) && r.Date >= since).ToListAsync(cancellationToken))
            .Select(FuelEntry.Of).ToList();
    }

    public static string StatusText(FuelReceiptStatus status, FuelPaymentMethod payment) => status switch
    {
        FuelReceiptStatus.New => "Neu",
        FuelReceiptStatus.Checked => "Geprüft",
        FuelReceiptStatus.Rejected => "Abgelehnt",
        _ => payment == FuelPaymentMethod.Private ? "Erstattet" : "Gebucht",
    };
}

public enum FuelDecision
{
    /// <summary>Prüfen und freigeben.</summary>
    Check,
    Reject,

    /// <summary>Erstattet (privat ausgelegt) bzw. gebucht.</summary>
    Settle,

    /// <summary>Freigabe oder Ablehnung zurücknehmen.</summary>
    Reopen,
}
