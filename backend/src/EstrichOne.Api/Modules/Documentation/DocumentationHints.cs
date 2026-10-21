using System.Globalization;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Documentation;

/// <summary>„Erstbegehung offen“ und der Stand der Belegreife aus der letzten CM-Messung.</summary>
internal sealed class DocumentationHints(EstrichOneDbContext db, Organization.Exports.ISystemSettings settings) : IBauakteHintSource
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public async Task<IReadOnlyList<BauakteHint>> GetHintsAsync(Bauakte bauakte, DateOnly today, CancellationToken cancellationToken)
    {
        var hints = new List<BauakteHint>();
        var inspections = await db.Set<Inspection>().AsNoTracking().Where(i => i.BauakteId == bauakte.Id)
            .OrderByDescending(i => i.PerformedAt).ToListAsync(cancellationToken);

        if (bauakte.Status is BauakteStatus.Planned or BauakteStatus.InProgress
            && inspections.All(i => i.Kind != InspectionKind.InitialInspection)
            && (await settings.GetAsync(cancellationToken)).Bauakte.WarnMissingInspection)
        {
            hints.Add(new("initial_inspection_missing", BauakteHintSeverity.Warning, "Erstbegehung noch nicht ausgeführt."));
        }

        var lastCm = inspections.FirstOrDefault(i => i.Kind == InspectionKind.CmMeasurement);
        if (lastCm is not null && bauakte.Status is BauakteStatus.InProgress or BauakteStatus.Drying)
        {
            var measured = lastCm.MeasuredCmPercent?.ToString("0.0#", German) ?? "–";
            var limit = lastCm.LimitCmPercent?.ToString("0.0#", German) ?? "–";
            var day = lastCm.PerformedAt.ToString("dd.MM.", German);
            hints.Add(lastCm.IsReadyForCovering == true
                ? new("cm_ready", BauakteHintSeverity.Success, $"Belegreif laut CM-Messung vom {day} ({lastCm.Location}: {measured} CM-%, Grenzwert {limit}).")
                : new("cm_not_ready", BauakteHintSeverity.Warning, $"Noch nicht belegreif: CM-Messung vom {day} ({lastCm.Location}: {measured} CM-%, Grenzwert {limit})."));
        }

        return hints;
    }
}
