using EstrichOne.Client;

namespace EstrichOne.Desktop;

/// <summary>Eine Zeile der Bedarfsrechnung (wie im Web „Materialbedarf aus Räumen &amp; Flächen“).</summary>
public sealed record MaterialNeed(string Key, string Description, string Detail, decimal Amount, string Unit);

/// <summary>Bedarf aus „Räume &amp; Flächen“: Estrichvolumen je Estrichart (auf 0,5 m³) und Dämmfläche je Dicke (auf volle m²).</summary>
public static class MaterialRequirements
{
    public static IReadOnlyList<MaterialNeed> Calculate(RoomOverview rooms, decimal reservePercent)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        var factor = 1 + (reservePercent / 100m);
        var screed = new Dictionary<string, (decimal Area, decimal Volume, int Missing)>();
        var screedOrder = new List<string>();
        var insulation = new SortedDictionary<int, decimal>();
        foreach (var room in rooms.Rooms)
        {
            var area = room.AreaSquareMeters ?? 0;
            if (area <= 0)
            {
                continue;
            }

            var name = room.ScreedTypeName ?? "Estrich (Art offen)";
            if (!screed.TryGetValue(name, out var entry))
            {
                screedOrder.Add(name);
            }

            entry.Area += area;
            if (room.ThicknessMillimeters is { } mm and > 0)
            {
                entry.Volume += area * mm / 1000m;
            }
            else
            {
                entry.Missing++;
            }

            screed[name] = entry;
            if (room.InsulationMillimeters is { } ins and > 0)
            {
                insulation[ins] = insulation.GetValueOrDefault(ins) + area;
            }
        }

        var needs = new List<MaterialNeed>();
        foreach (var name in screedOrder)
        {
            var (area, volume, missing) = screed[name];
            if (volume > 0)
            {
                var detail = $"{Labels.Quantity(area)} m² · {Labels.Quantity(Math.Round(volume, 2))} m³ netto"
                    + (missing > 0 ? $" · {missing} Räume ohne Dicke nicht berechnet" : string.Empty);
                needs.Add(new MaterialNeed($"screed-{name}", name, detail, RoundUp(volume * factor, 0.5m), "m³"));
            }
        }

        foreach (var (mm, area) in insulation)
        {
            needs.Add(new MaterialNeed($"ins-{mm}", $"Dämmung {mm} mm", $"{Labels.Quantity(area)} m² netto", RoundUp(area * factor, 1m), "m²"));
        }

        return needs;
    }

    public static decimal RoundUp(decimal value, decimal step) => Math.Ceiling((value / step) - 0.000000001m) * step;
}
