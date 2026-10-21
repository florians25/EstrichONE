using System.Globalization;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Materials;

/// <summary>„Lieferung heute“, „Lieferung überfällig“ und nicht abgeschickte Bestellungen in der Bauakte.</summary>
internal sealed class MaterialHints(EstrichOneDbContext db, ISystemSettings settings) : IBauakteHintSource
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public async Task<IReadOnlyList<BauakteHint>> GetHintsAsync(Bauakte bauakte, DateOnly today, CancellationToken cancellationToken)
    {
        if (!(await settings.GetAsync(cancellationToken)).Modules.Materials)
        {
            return [];
        }

        var orders = await db.Set<MaterialOrder>().AsNoTracking()
            .Where(o => o.BauakteId == bauakte.Id && o.Status != OrderStatus.Delivered && o.Status != OrderStatus.Cancelled)
            .OrderBy(o => o.DeliveryDate).ThenBy(o => o.Number)
            .Select(o => new { o.Number, o.Status, o.DeliveryDate, o.DeliveryTime, o.SupplierId })
            .ToListAsync(cancellationToken);
        if (orders.Count == 0)
        {
            return [];
        }

        var supplierIds = orders.Select(o => o.SupplierId).Distinct().ToList();
        var names = await db.Set<Supplier>().AsNoTracking().IgnoreQueryFilters([QueryFilters.NotDeleted]).Where(s => supplierIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        var hints = new List<BauakteHint>();
        foreach (var o in orders)
        {
            var supplier = names.GetValueOrDefault(o.SupplierId) ?? "Lieferant";
            var time = o.DeliveryTime is { } t ? $" ({t})" : string.Empty;
            if (o.Status == OrderStatus.Draft)
            {
                hints.Add(new BauakteHint("order_draft", BauakteHintSeverity.Info, $"Bestellung {o.Number} bei {supplier} ist noch nicht abgeschickt."));
            }
            else if (o.DeliveryDate == today)
            {
                hints.Add(new BauakteHint("delivery_today", BauakteHintSeverity.Success, $"Lieferung heute{time}: {supplier}, Bestellung {o.Number}"));
            }
            else if (o.DeliveryDate < today)
            {
                hints.Add(new BauakteHint("delivery_overdue", BauakteHintSeverity.Warning,
                    $"Lieferung vom {o.DeliveryDate.Value.ToString("dd.MM.", German)} nicht als geliefert bestätigt: {supplier}, Bestellung {o.Number}"));
            }
            else if (o.DeliveryDate is { } day && day <= today.AddDays(7))
            {
                hints.Add(new BauakteHint("delivery_next", BauakteHintSeverity.Info,
                    $"Lieferung {day.ToString("dddd, dd.MM.", German)}{time}: {supplier}, Bestellung {o.Number}"));
            }
        }

        return hints;
    }
}
