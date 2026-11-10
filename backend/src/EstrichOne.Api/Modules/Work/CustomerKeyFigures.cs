using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Work;

/// <summary>
/// Kennzahlen eines Kunden für den Kopf der Kundenakte, berechnet über die für den Benutzer sichtbaren Bauakten.
/// </summary>
/// <param name="TotalArea">Summe der Gesamtflächen (Kennwerte), sonst der Raumflächen je Bauakte; in m².</param>
/// <param name="LastActivity">Letzte Änderung an einer Bauakte oder Aufgabe des Kunden.</param>
/// <param name="OpenTasks">Offene Aufgaben; <c>null</c> ohne Leserecht für Aufgaben.</param>
/// <param name="OpenDefects">Offene Mängel; <c>null</c> ohne Leserecht für Aufgaben.</param>
public sealed record CustomerKeyFigures(
    int BauaktenTotal, int BauaktenActive, int Inquiries, int Completed, decimal TotalArea, DateTimeOffset? LastActivity,
    int? OpenTasks, int? OpenDefects, DateOnly? NextPlannedStart);

internal static class CustomerKeyFigureEndpoints
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/contacts/{id:guid}/key-figures", GetAsync).WithName("GetCustomerKeyFigures").WithTags("Kontakte")
            .WithSummary("Kennzahlen des Kunden (Bauakten aktiv/gesamt, Fläche, letzte Aktivität, offene Aufgaben und Mängel) über die sichtbaren Bauakten")
            .RequirePermission(Permissions.ContactsRead).RequirePermission(Permissions.BauaktenRead);

    private static async Task<Ok<CustomerKeyFigures>> GetAsync(
        Guid id, EstrichOneDbContext db, IBauakteAccess access, IBauakteSummaries summaries, IUserPermissions permissions, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var akten = await (await access.VisibleAsync(db.Set<Bauakte>().AsNoTracking(), cancellationToken))
            .Where(b => b.CustomerId == id)
            .ToListAsync(cancellationToken);
        var ids = akten.Select(b => b.Id).ToList();
        var rooms = ids.Count == 0 ? new Dictionary<Guid, BauakteSummary>() : await summaries.GetAsync(ids, cancellationToken);
        var area = akten.Where(b => b.Status != BauakteStatus.Cancelled)
            .Sum(b => b.Project.TotalArea is { } total && total > 0 ? total : rooms.GetValueOrDefault(b.Id)?.TotalAreaSquareMeters ?? 0);

        int? openTasks = null, openDefects = null;
        DateTimeOffset? lastWork = null;
        if (await permissions.HasAsync(Permissions.WorkRead, cancellationToken))
        {
            var work = ids.Count == 0 ? [] : await db.Set<WorkItem>().AsNoTracking().Where(w => w.BauakteId != null && ids.Contains(w.BauakteId.Value))
                .Select(w => new WorkState(w.Kind, w.Status, w.UpdatedAt)).ToListAsync(cancellationToken);
            openTasks = work.Count(w => w.Kind == WorkItemKind.Task && w.Status != WorkItemStatus.Done);
            openDefects = work.Count(w => w.Kind == WorkItemKind.Defect && w.Status != WorkItemStatus.Done);
            lastWork = work.Count > 0 ? work.Max(w => w.UpdatedAt) : null;
        }

        var lastAkte = akten.Count > 0 ? akten.Max(b => b.UpdatedAt) : (DateTimeOffset?)null;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var next = akten.Where(b => b.Status is not (BauakteStatus.Completed or BauakteStatus.Cancelled) && b.PlannedStart is { } s && s >= today)
            .Select(b => b.PlannedStart).Min();

        return TypedResults.Ok(new CustomerKeyFigures(
            akten.Count,
            akten.Count(b => b.Status is not (BauakteStatus.Completed or BauakteStatus.Cancelled or BauakteStatus.Inquiry)),
            akten.Count(b => b.Status == BauakteStatus.Inquiry),
            akten.Count(b => b.Status == BauakteStatus.Completed),
            Math.Round(area, 2),
            lastWork is { } w && (lastAkte is null || w > lastAkte) ? w : lastAkte,
            openTasks, openDefects, next));
    }

    private sealed record WorkState(WorkItemKind Kind, WorkItemStatus Status, DateTimeOffset UpdatedAt);
}
