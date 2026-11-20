using System.Globalization;

namespace EstrichOne.Api.Modules.Fuel;

/// <summary>Hinweis zu einem Tankbeleg (blockiert nichts; das Büro entscheidet).</summary>
/// <param name="Code">odometer_backwards, consumption_unusual, duplicate, exceeds_tank, missing_image.</param>
public sealed record FuelWarning(string Code, string Text);

/// <summary>Beleg in der Form, die die Plausibilitätsprüfung braucht.</summary>
internal sealed record FuelEntry(
    Guid Id, string Number, Guid VehicleId, DateOnly Date, int? OdometerKm, decimal Liters, decimal AmountGross, FuelType FuelType,
    FuelReceiptStatus Status, Guid? ImageFileId, DateTimeOffset CreatedAt)
{
    public static FuelEntry Of(FuelReceipt r) => new(r.Id, r.Number, r.VehicleId, r.Date, r.OdometerKm, r.Liters, r.AmountGross, r.FuelType, r.Status,
        r.ImageFileId, r.CreatedAt);

    /// <summary>Kraftstoff für den Motor (AdBlue zählt nicht zum Verbrauch).</summary>
    public bool IsFuel => FuelType != FuelType.AdBlue;

    public bool Counts => Status != FuelReceiptStatus.Rejected;
}

/// <summary>Gefahrene Strecke seit dem vorigen Tanken, Verbrauch und Hinweise eines Belegs.</summary>
internal sealed record FuelEvaluation(int? DistanceKm, decimal? ConsumptionPer100Km, IReadOnlyList<FuelWarning> Warnings);

/// <summary>Auswertung je Monat (Liter Kraftstoff, AdBlue, Kosten brutto, Strecke, Ø-Verbrauch).</summary>
public sealed record FuelMonthStatistics(string Month, int Receipts, decimal Liters, decimal AdBlueLiters, decimal Cost, int DistanceKm,
    decimal? AverageConsumption);

/// <summary>Auswertung eines Fahrzeugs im Zeitraum, mit Monaten.</summary>
public sealed record FuelVehicleStatistics(Guid VehicleId, string VehicleName, string? Identifier, int Receipts, decimal Liters, decimal AdBlueLiters,
    decimal Cost, int DistanceKm, decimal? AverageConsumption, int? LastOdometerKm, IReadOnlyList<FuelMonthStatistics> Months);

/// <summary>
/// Plausibilität und Verbrauch (Tanken von voll zu voll): Der Verbrauch eines Belegs sind seine Liter auf die Strecke seit dem
/// vorigen Kraftstoffbeleg desselben Fahrzeugs mit Kilometerstand. Abgelehnte Belege zählen nicht mit.
/// </summary>
internal static class FuelAnalysis
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Ohne Vergleichswerte des Fahrzeugs gelten Verbräuche außerhalb dieses Bereichs (l/100 km) als auffällig.</summary>
    public const decimal PlausibleMin = 3m;
    public const decimal PlausibleMax = 45m;

    public static IReadOnlyDictionary<Guid, FuelEvaluation> Evaluate(IReadOnlyCollection<FuelEntry> entries, IReadOnlyDictionary<Guid, decimal?> tankCapacities)
    {
        var result = new Dictionary<Guid, FuelEvaluation>();
        foreach (var vehicle in entries.GroupBy(e => e.VehicleId))
        {
            var ordered = vehicle.OrderBy(e => e.Date).ThenBy(e => e.OdometerKm ?? int.MaxValue).ThenBy(e => e.CreatedAt).ToList();
            var counted = ordered.Where(e => e.Counts).ToList();
            var segments = new Dictionary<Guid, (int Distance, decimal Consumption)>();
            FuelEntry? previousFuel = null;
            foreach (var entry in counted.Where(e => e.IsFuel && e.OdometerKm is not null))
            {
                if (previousFuel is { OdometerKm: { } before } && entry.OdometerKm > before)
                {
                    var distance = entry.OdometerKm.Value - before;
                    segments[entry.Id] = (distance, Math.Round(entry.Liters / distance * 100m, 1));
                }

                previousFuel = entry;
            }

            var typical = Median(segments.Values.Select(s => s.Consumption).ToList());
            tankCapacities.TryGetValue(vehicle.Key, out var tank);
            foreach (var entry in ordered)
            {
                var warnings = new List<FuelWarning>();
                var withOdometer = counted.Where(e => e.OdometerKm is not null && e.Id != entry.Id).ToList();
                if (entry.OdometerKm is { } km)
                {
                    var earlierHigher = withOdometer.Where(e => e.Date < entry.Date && e.OdometerKm > km).OrderByDescending(e => e.Date).FirstOrDefault();
                    var laterLower = withOdometer.Where(e => e.Date > entry.Date && e.OdometerKm < km).OrderBy(e => e.Date).FirstOrDefault();
                    if (earlierHigher is not null)
                    {
                        warnings.Add(new("odometer_backwards",
                            $"Kilometerstand {Km(km)} ist kleiner als beim früheren Beleg {earlierHigher.Number} ({Km(earlierHigher.OdometerKm!.Value)} am {Day(earlierHigher.Date)})."));
                    }
                    else if (laterLower is not null)
                    {
                        warnings.Add(new("odometer_backwards",
                            $"Kilometerstand {Km(km)} ist größer als beim späteren Beleg {laterLower.Number} ({Km(laterLower.OdometerKm!.Value)} am {Day(laterLower.Date)})."));
                    }
                }

                segments.TryGetValue(entry.Id, out var segment);
                var hasSegment = segments.ContainsKey(entry.Id);
                if (hasSegment && IsUnusual(segment.Consumption, typical, segments.Count))
                {
                    warnings.Add(new("consumption_unusual", typical is { } t && segments.Count >= 3
                        ? $"Verbrauch {Number(segment.Consumption)} l/100 km weicht stark vom üblichen Wert dieses Fahrzeugs ab (Ø {Number(t)} l/100 km)."
                        : $"Verbrauch {Number(segment.Consumption)} l/100 km ist auffällig (Strecke {Km(segment.Distance)} seit dem vorigen Tanken)."));
                }

                var duplicate = ordered.FirstOrDefault(e => e.Id != entry.Id && e.Counts && e.Date == entry.Date && e.AmountGross == entry.AmountGross);
                if (duplicate is not null)
                {
                    warnings.Add(new("duplicate", $"Möglicher Doppelbeleg: {duplicate.Number} hat denselben Betrag am selben Tag für dieses Fahrzeug."));
                }

                if (entry.IsFuel && tank is { } capacity && entry.Liters > capacity)
                {
                    warnings.Add(new("exceeds_tank", $"{Number(entry.Liters)} l sind mehr, als in den Tank passt ({Number(capacity)} l)."));
                }

                if (entry.ImageFileId is null)
                {
                    warnings.Add(new("missing_image", "Kein Belegfoto vorhanden."));
                }

                result[entry.Id] = new FuelEvaluation(hasSegment ? segment.Distance : null, hasSegment ? segment.Consumption : null, warnings);
            }
        }

        return result;
    }

    /// <summary>Auffällig: unter der Hälfte bzw. über dem 1,6-Fachen des üblichen Werts (ab drei Vergleichswerten), sonst außerhalb 3–45 l/100 km.</summary>
    internal static bool IsUnusual(decimal consumption, decimal? typical, int comparisons) =>
        typical is { } t && comparisons >= 3
            ? consumption < t * 0.5m || consumption > t * 1.6m
            : consumption is < PlausibleMin or > PlausibleMax;

    public static IReadOnlyList<FuelVehicleStatistics> Statistics(IReadOnlyCollection<FuelEntry> entries, IReadOnlyDictionary<Guid, FuelEvaluation> evaluations,
        IReadOnlyDictionary<Guid, (string Name, string? Identifier)> vehicles, DateOnly from, DateOnly to)
    {
        var inRange = entries.Where(e => e.Counts && e.Date >= from && e.Date <= to).ToList();
        return inRange.GroupBy(e => e.VehicleId).Select(group =>
        {
            var months = group.GroupBy(e => e.Date.ToString("yyyy-MM", CultureInfo.InvariantCulture)).OrderBy(m => m.Key)
                .Select(m => Summarize(m.Key, m.ToList(), evaluations)).ToList();
            var total = Summarize("", group.ToList(), evaluations);
            var name = vehicles.TryGetValue(group.Key, out var v) ? v : ("Unbekanntes Fahrzeug", null);
            var lastKm = entries.Where(e => e.VehicleId == group.Key && e.Counts && e.OdometerKm is not null).Select(e => e.OdometerKm).Max();
            return new FuelVehicleStatistics(group.Key, name.Name, name.Identifier, total.Receipts, total.Liters, total.AdBlueLiters, total.Cost,
                total.DistanceKm, total.AverageConsumption, lastKm, months);
        }).OrderByDescending(s => s.Cost).ThenBy(s => s.VehicleName, StringComparer.CurrentCulture).ToList();
    }

    private static FuelMonthStatistics Summarize(string month, List<FuelEntry> entries, IReadOnlyDictionary<Guid, FuelEvaluation> evaluations)
    {
        var fuel = entries.Where(e => e.IsFuel).ToList();
        var measured = fuel.Where(e => evaluations.TryGetValue(e.Id, out var ev) && ev.DistanceKm is not null).ToList();
        var distance = measured.Sum(e => evaluations[e.Id].DistanceKm!.Value);
        decimal? average = distance > 0 ? Math.Round(measured.Sum(e => e.Liters) / distance * 100m, 1) : null;
        return new FuelMonthStatistics(month, entries.Count, fuel.Sum(e => e.Liters), entries.Where(e => !e.IsFuel).Sum(e => e.Liters),
            entries.Sum(e => e.AmountGross), distance, average);
    }

    private static decimal? Median(List<decimal> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : Math.Round((values[middle - 1] + values[middle]) / 2m, 1);
    }

    internal static string Number(decimal value) => value.ToString("#,##0.##", German);

    internal static string Km(int value) => value.ToString("#,##0", German) + " km";

    internal static string Day(DateOnly day) => day.ToString("dd.MM.yyyy", German);
}
