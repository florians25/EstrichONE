using System.Globalization;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Sync.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Documentation;

public sealed record DiaryEntryDto(
    Guid Id, Guid BauakteId, Guid AuthorUserId, DateOnly Day, string Text, DiaryWeather? Weather, int? TemperatureCelsius,
    int? WorkersPresent, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt, long Version, TimeOnly? StartTime = null,
    TimeOnly? EndTime = null, string? Crew = null, IReadOnlyList<Guid>? PhotoFileIds = null)
{
    public static DiaryEntryDto From(DiaryEntry entry) => new(entry.Id, entry.BauakteId, entry.AuthorUserId, entry.Day, entry.Text,
        entry.Weather, entry.TemperatureCelsius, entry.WorkersPresent, entry.CreatedAt, entry.EditedAt, entry.Version, entry.StartTime,
        entry.EndTime, entry.Crew, entry.PhotoFileIds);
}

/// <param name="LimitCmPercent">Grenzwert für die Belegreife, vom Server aus Bindemittel und Beheizung bestimmt.</param>
/// <param name="IsReadyForCovering">Nur bei CM-Messungen: Messwert ≤ Grenzwert.</param>
public sealed record InspectionDto(
    Guid Id, Guid BauakteId, InspectionKind Kind, Guid PerformedByUserId, DateTimeOffset PerformedAt, Guid? CorrectsInspectionId,
    string? Remarks, bool NotRequired, IReadOnlyDictionary<string, CheckResult> Checks, IReadOnlyList<Guid> PhotoFileIds,
    string? Location, ScreedBinderKind? Binder, bool? Heated, int? ThicknessMm, decimal? MeasuredCmPercent, decimal? LimitCmPercent,
    bool? IsReadyForCovering, long Version, string? Notes = null)
{
    public static InspectionDto From(Inspection i) => new(i.Id, i.BauakteId, i.Kind, i.PerformedByUserId, i.PerformedAt,
        i.CorrectsInspectionId, i.Remarks, i.NotRequired, i.Checks, i.PhotoFileIds, i.Location, i.Binder, i.Heated, i.ThicknessMm,
        i.MeasuredCmPercent, i.LimitCmPercent, i.IsReadyForCovering, i.Version, i.Notes);
}

/// <summary>Bautagebuch (anfügen, Verfasser ändert) und Prüfungen (nur anlegen) über den Sync.</summary>
internal sealed class SiteRecordSync(EstrichOneDbContext db, IBauakteAccess access, IUserPermissions permissions, ICurrentUser currentUser,
    IBauakteHistory history, TimeProvider clock, IBauakteAutoStatus autoStatus) : ISyncOperationHandler
{
    public IReadOnlyCollection<string> OperationTypes { get; } = ["diary.create", "diary.update", "diary.delete", "inspection.create"];

    public async Task<SyncOperationResult> ApplyAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        var permission = op.Type.StartsWith("diary.", StringComparison.Ordinal) ? Permissions.DiaryWrite : Permissions.InspectionsWrite;
        if (!await permissions.HasAsync(permission, cancellationToken))
        {
            return SyncOperationResult.Rejected(op.OpId, "PERMISSION_DENIED", "Für diese Aktion fehlt die Berechtigung.");
        }

        try
        {
            return op.Type switch
            {
                "diary.create" => await CreateDiaryAsync(op, cancellationToken),
                "diary.update" => await UpdateDiaryAsync(op, cancellationToken),
                "diary.delete" => await DeleteDiaryAsync(op, cancellationToken),
                _ => await CreateInspectionAsync(op, cancellationToken),
            };
        }
        catch (ArgumentException exception)
        {
            throw new SyncValidationException(exception.Message);
        }
    }

    private async Task<SyncOperationResult> CreateDiaryAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        var bauakteId = SyncPayload.Read<Guid>(op.Payload, "bauakteId");
        if (!await access.CanReadAsync(bauakteId, cancellationToken))
        {
            return BauakteNotFound(op);
        }

        var existing = await db.Set<DiaryEntry>().IgnoreQueryFilters([QueryFilters.NotDeleted]).SingleOrDefaultAsync(e => e.Id == op.EntityId, cancellationToken);
        if (existing is not null)
        {
            return existing.AuthorUserId == currentUser.UserId
                ? Applied(op, existing.Version, DiaryEntryDto.From(existing))
                : SyncOperationResult.Rejected(op.OpId, "ID_CONFLICT", "Unter dieser ID existiert bereits ein anderer Eintrag.");
        }

        var day = SyncPayload.Read<DateOnly?>(op.Payload, "day") ?? DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var entry = DiaryEntry.Create(op.EntityId, bauakteId, currentUser.UserId, day, SyncPayload.Read<string>(op.Payload, "text") ?? string.Empty,
            SyncPayload.Read<DiaryWeather?>(op.Payload, "weather"), SyncPayload.Read<int?>(op.Payload, "temperatureCelsius"),
            SyncPayload.Read<int?>(op.Payload, "workersPresent"));
        entry.SetWorkDetails(SyncPayload.Read<TimeOnly?>(op.Payload, "startTime"), SyncPayload.Read<TimeOnly?>(op.Payload, "endTime"),
            SyncPayload.Read<string>(op.Payload, "crew"), SyncPayload.Read<List<Guid>>(op.Payload, "photoFileIds") ?? []);
        db.Add(entry);
        history.Record(bauakteId, BauakteHistoryActions.DiaryAdded, new Dictionary<string, string?> { ["day"] = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
        await autoStatus.TryStartAsync(bauakteId, BauakteAutoTrigger.Diary, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Applied(op, entry.Version, DiaryEntryDto.From(entry));
    }

    /// <summary>Nur der Verfasser ändert; letzte Änderung gewinnt (seine Geräte untereinander).</summary>
    private async Task<SyncOperationResult> UpdateDiaryAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        var entry = await db.Set<DiaryEntry>().SingleOrDefaultAsync(e => e.Id == op.EntityId, cancellationToken);
        if (entry is null || !await access.CanReadAsync(entry.BauakteId, cancellationToken))
        {
            return SyncOperationResult.Rejected(op.OpId, "NOT_FOUND", "Der Eintrag existiert nicht (mehr) oder ist nicht sichtbar.");
        }

        if (entry.AuthorUserId != currentUser.UserId)
        {
            return SyncOperationResult.Rejected(op.OpId, "NOT_AUTHOR", "Nur der Verfasser kann den Eintrag ändern.");
        }

        var changes = op.Payload["changes"] as JsonObject ?? new JsonObject();
        var allowed = new HashSet<string> { "text", "weather", "temperatureCelsius", "workersPresent", "startTime", "endTime", "crew", "photoFileIds" };
        if (changes.Select(pair => pair.Key).FirstOrDefault(field => !allowed.Contains(field)) is { } invalid)
        {
            throw new SyncValidationException($"Das Feld „{invalid}“ kann nicht geändert werden.");
        }

        entry.Edit(
            changes.ContainsKey("text") ? SyncPayload.Read<string>(changes, "text") ?? string.Empty : entry.Text,
            changes.ContainsKey("weather") ? SyncPayload.Read<DiaryWeather?>(changes, "weather") : entry.Weather,
            changes.ContainsKey("temperatureCelsius") ? SyncPayload.Read<int?>(changes, "temperatureCelsius") : entry.TemperatureCelsius,
            changes.ContainsKey("workersPresent") ? SyncPayload.Read<int?>(changes, "workersPresent") : entry.WorkersPresent,
            clock.GetUtcNow());
        entry.SetWorkDetails(
            changes.ContainsKey("startTime") ? SyncPayload.Read<TimeOnly?>(changes, "startTime") : entry.StartTime,
            changes.ContainsKey("endTime") ? SyncPayload.Read<TimeOnly?>(changes, "endTime") : entry.EndTime,
            changes.ContainsKey("crew") ? SyncPayload.Read<string>(changes, "crew") : entry.Crew,
            changes.ContainsKey("photoFileIds") ? SyncPayload.Read<List<Guid>>(changes, "photoFileIds") ?? [] : entry.PhotoFileIds);
        await db.SaveChangesAsync(cancellationToken);
        return Applied(op, entry.Version, DiaryEntryDto.From(entry));
    }

    private async Task<SyncOperationResult> DeleteDiaryAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        var entry = await db.Set<DiaryEntry>().IgnoreQueryFilters([QueryFilters.NotDeleted]).SingleOrDefaultAsync(e => e.Id == op.EntityId, cancellationToken);
        if (entry is null || !await access.CanReadAsync(entry.BauakteId, cancellationToken))
        {
            return SyncOperationResult.Rejected(op.OpId, "NOT_FOUND", "Der Eintrag existiert nicht (mehr) oder ist nicht sichtbar.");
        }

        if (entry.AuthorUserId != currentUser.UserId)
        {
            return SyncOperationResult.Rejected(op.OpId, "NOT_AUTHOR", "Nur der Verfasser kann den Eintrag löschen.");
        }

        entry.Delete(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: entry.Version);
    }

    private async Task<SyncOperationResult> CreateInspectionAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        var p = op.Payload;
        var bauakteId = SyncPayload.Read<Guid>(p, "bauakteId");
        if (!await access.CanReadAsync(bauakteId, cancellationToken))
        {
            return BauakteNotFound(op);
        }

        var existing = await db.Set<Inspection>().SingleOrDefaultAsync(i => i.Id == op.EntityId, cancellationToken);
        if (existing is not null)
        {
            return existing.PerformedByUserId == currentUser.UserId
                ? Applied(op, existing.Version, InspectionDto.From(existing))
                : SyncOperationResult.Rejected(op.OpId, "ID_CONFLICT", "Unter dieser ID existiert bereits eine andere Prüfung.");
        }

        var corrects = SyncPayload.Read<Guid?>(p, "correctsInspectionId");
        if (corrects is { } correctedId && !await db.Set<Inspection>().AnyAsync(i => i.Id == correctedId && i.BauakteId == bauakteId, cancellationToken))
        {
            throw new SyncValidationException("Die korrigierte Prüfung gehört nicht zu dieser Bauakte.");
        }

        var userId = currentUser.UserId;
        var performedAt = SyncPayload.Read<DateTimeOffset?>(p, "performedAt") ?? clock.GetUtcNow();
        if (performedAt > clock.GetUtcNow().AddMinutes(10))
        {
            throw new SyncValidationException("Der Prüfzeitpunkt liegt in der Zukunft.");
        }

        var kind = SyncPayload.Read<InspectionKind>(p, "kind");
        var remarks = SyncPayload.Read<string>(p, "remarks");
        var notes = SyncPayload.Read<string>(p, "notes");
        var inspection = kind switch
        {
            InspectionKind.InitialInspection => Inspection.CreateInitial(op.EntityId, bauakteId, userId, performedAt,
                SyncPayload.Read<bool>(p, "notRequired"), SyncPayload.Read<Dictionary<string, CheckResult>>(p, "checks") ?? [],
                SyncPayload.Read<List<Guid>>(p, "photoFileIds") ?? [], remarks, corrects, notes),
            _ => Inspection.CreateCmMeasurement(op.EntityId, bauakteId, userId, performedAt, SyncPayload.Read<string>(p, "location") ?? string.Empty,
                SyncPayload.Read<ScreedBinderKind?>(p, "binder") ?? throw new SyncValidationException("Bitte das Bindemittel angeben."),
                SyncPayload.Read<bool>(p, "heated"), SyncPayload.Read<int?>(p, "thicknessMm"),
                SyncPayload.Read<decimal?>(p, "measuredCmPercent") ?? throw new SyncValidationException("Bitte den Messwert angeben."),
                remarks, corrects, notes),
        };

        db.Add(inspection);
        var details = new Dictionary<string, string?> { ["kind"] = inspection.Kind.ToString() };
        if (inspection.Kind == InspectionKind.CmMeasurement)
        {
            details["measured"] = inspection.MeasuredCmPercent?.ToString(CultureInfo.InvariantCulture);
            details["limit"] = inspection.LimitCmPercent?.ToString(CultureInfo.InvariantCulture);
            details["ready"] = inspection.IsReadyForCovering == true ? "true" : "false";
        }
        else
        {
            details["notRequired"] = inspection.NotRequired ? "true" : "false";
        }

        history.Record(bauakteId, BauakteHistoryActions.InspectionAdded, details);
        if (inspection.Kind == InspectionKind.InitialInspection && !inspection.NotRequired)
        {
            await autoStatus.TryStartAsync(bauakteId, BauakteAutoTrigger.Inspection, cancellationToken);
        }
        await db.SaveChangesAsync(cancellationToken);
        return Applied(op, inspection.Version, InspectionDto.From(inspection));
    }

    private static SyncOperationResult Applied<T>(SyncOperation op, long version, T entity) =>
        new(op.OpId, SyncOperationStatus.Applied, Version: version, Entity: SyncPayload.ToNode(entity));

    private static SyncOperationResult BauakteNotFound(SyncOperation op) =>
        SyncOperationResult.Rejected(op.OpId, "BAUAKTE_NOT_FOUND", "Die Bauakte existiert nicht (mehr) oder ist nicht sichtbar.");
}

internal sealed class DiaryEntrySource(EstrichOneDbContext db) : ISyncEntitySource
{
    public string EntityType => "diaryEntry";

    public string? RequiredPermission => null;

    public async Task<IReadOnlyDictionary<Guid, JsonNode>> LoadAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Set<DiaryEntry>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToListAsync(cancellationToken))
            .ToDictionary(e => e.Id, e => SyncPayload.ToNode(DiaryEntryDto.From(e))!);
}

internal sealed class InspectionSource(EstrichOneDbContext db) : ISyncEntitySource
{
    public string EntityType => "inspection";

    public string? RequiredPermission => null;

    public async Task<IReadOnlyDictionary<Guid, JsonNode>> LoadAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        (await db.Set<Inspection>().AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken))
            .ToDictionary(i => i.Id, i => SyncPayload.ToNode(InspectionDto.From(i))!);
}

/// <summary>Lesen für Web und Desktop. Erfassen läuft über den Sync (eine Logik für alle Plattformen).</summary>
internal static class SiteRecordEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/bauakten/{id:guid}").WithTags("Bautagebuch und Prüfungen");
        group.MapGet("/diary", ListDiaryAsync).WithName("ListDiaryEntries").WithSummary("Bautagebuch (neuester Tag zuerst)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenRead);
        group.MapGet("/inspections", ListInspectionsAsync).WithName("ListInspections").WithSummary("Prüfungen (neueste zuerst)")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.BauaktenRead);
    }

    private static async Task<Results<Ok<List<DiaryEntryDto>>, ProblemHttpResult>> ListDiaryAsync(
        Guid id, IBauakteAccess access, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var entries = await db.Set<DiaryEntry>().AsNoTracking().Where(e => e.BauakteId == id)
            .OrderByDescending(e => e.Day).ThenByDescending(e => e.CreatedAt).ToListAsync(cancellationToken);
        return TypedResults.Ok(entries.Select(DiaryEntryDto.From).ToList());
    }

    private static async Task<Results<Ok<List<InspectionDto>>, ProblemHttpResult>> ListInspectionsAsync(
        Guid id, IBauakteAccess access, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        var inspections = await db.Set<Inspection>().AsNoTracking().Where(i => i.BauakteId == id)
            .OrderByDescending(i => i.PerformedAt).ToListAsync(cancellationToken);
        return TypedResults.Ok(inspections.Select(InspectionDto.From).ToList());
    }

    private static ProblemHttpResult NotFound() => TypedResults.Problem(statusCode: StatusCodes.Status404NotFound,
        title: "Bauakte nicht gefunden", extensions: new Dictionary<string, object?> { ["code"] = "BAUAKTE_NOT_FOUND" });
}
