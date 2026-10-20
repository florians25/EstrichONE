using System.Globalization;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Planning;

/// <summary>„Einsatz heute“ bzw. „Nächster Einsatz“ in der Bauakte.</summary>
internal sealed class PlanningHints(EstrichOneDbContext db) : IBauakteHintSource
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public async Task<IReadOnlyList<BauakteHint>> GetHintsAsync(Bauakte bauakte, DateOnly today, CancellationToken cancellationToken)
    {
        var upcoming = await db.Set<Assignment>().AsNoTracking()
            .Where(a => a.BauakteId == bauakte.Id && a.Day >= today && a.Day <= today.AddDays(30))
            .OrderBy(a => a.Day).ThenBy(a => a.StartTime)
            .ToListAsync(cancellationToken);
        if (upcoming.Count == 0)
        {
            return [];
        }

        var crewIds = upcoming.Select(a => a.CrewId).OfType<Guid>().Distinct().ToList();
        var crews = await db.Set<Crew>().AsNoTracking().Where(c => crewIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);
        string Who(Assignment a) => a.CrewId is { } id && crews.TryGetValue(id, out var name) ? $" ({name})" : string.Empty;
        string When(Assignment a) => a.StartTime is { } t ? $" ab {t.ToString("HH:mm", German)} Uhr" : string.Empty;

        var todays = upcoming.Where(a => a.Day == today).ToList();
        if (todays.Count > 0)
        {
            return todays.Select(a => new BauakteHint("assignment_today", BauakteHintSeverity.Success,
                $"Einsatz heute{When(a)}: {a.Activity}{Who(a)}")).ToList();
        }

        var next = upcoming[0];
        return [new BauakteHint("assignment_next", BauakteHintSeverity.Info,
            $"Nächster Einsatz {next.Day.ToString("dddd, dd.MM.", German)}{When(next)}: {next.Activity}{Who(next)}")];
    }
}
