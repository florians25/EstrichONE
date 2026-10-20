using System.Globalization;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Bauakten;

internal sealed class BauakteHistory(IAuditLog audit, ICurrentUser currentUser) : IBauakteHistory
{
    public const string EntityType = "bauakte";

    public void Record(Guid bauakteId, string action, IReadOnlyDictionary<string, string?>? details = null) =>
        audit.Record($"bauakte.{action}", currentUser.UserId, EntityType, bauakteId.ToString(), details);
}

/// <param name="Text">Fertiger Satz für die Anzeige – auf allen Plattformen gleich (02: serverseitig berechnet).</param>
public sealed record BauakteHistoryEntry(DateTimeOffset At, string Action, Guid? ActorUserId, string? ActorName, string Text);

internal static class BauakteHistoryEndpoints
{
    private static readonly Dictionary<string, string> FieldNames = new()
    {
        ["title"] = "die Bezeichnung", ["street"] = "die Straße", ["postalCode"] = "die PLZ", ["city"] = "den Ort",
        ["latitude"] = "den Standort", ["longitude"] = "den Standort", ["description"] = "die Beschreibung",
        ["customerId"] = "den Kunden", ["responsibleUserId"] = "die Zuständigkeit", ["period"] = "den Zeitraum",
        ["siteContact"] = "den Baustellenkontakt", ["siteNotes"] = "die Hinweise zur Baustelle",
    };

    private static readonly Dictionary<string, string> StatusNames = new()
    {
        ["Inquiry"] = "Anfrage", ["Planned"] = "Geplant", ["InProgress"] = "In Ausführung", ["Drying"] = "Belegreif / Trocknung",
        ["Completed"] = "Abgeschlossen", ["Cancelled"] = "Storniert",
    };

    public static void Map(IEndpointRouteBuilder api)
    {
        api.MapGet("/bauakten/{id:guid}/history", GetAsync).WithName("GetBauakteHistory").WithTags("Bauakten")
            .WithSummary("Verlauf einer Bauakte (neueste zuerst)")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<List<BauakteHistoryEntry>>, ProblemHttpResult>> GetAsync(
        Guid id, IBauakteAccess access, IAuditQueries audit, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(id, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Bauakte nicht gefunden",
                extensions: new Dictionary<string, object?> { ["code"] = "BAUAKTE_NOT_FOUND" });
        }

        var entries = await audit.ForEntityAsync(BauakteHistory.EntityType, id.ToString(), 200, cancellationToken);
        var actorIds = entries.Select(e => e.ActorUserId).OfType<Guid>().Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);

        return TypedResults.Ok(entries.Select(entry =>
        {
            var actor = entry.ActorUserId is { } actorId && names.TryGetValue(actorId, out var name) ? name : null;
            return new BauakteHistoryEntry(entry.OccurredAt, entry.Action, entry.ActorUserId, actor, Describe(actor ?? "Jemand", entry));
        }).ToList());
    }

    internal static string Describe(string actor, AuditEntry entry)
    {
        var details = entry.Details;
        string Detail(string key) => details.TryGetValue(key, out var value) ? value ?? string.Empty : string.Empty;
        var kindIsDefect = Detail("kind") == "Defect";
        return entry.Action switch
        {
            "bauakte.created" => $"{actor} hat die Bauakte angelegt.",
            "bauakte.updated" => $"{actor} hat {DescribeFields(Detail("fields"))} geändert.",
            "bauakte.status_changed" => $"{actor} hat den Status von „{Status(Detail("from"))}“ auf „{Status(Detail("to"))}“ gesetzt.",
            "bauakte.deleted" => $"{actor} hat die Bauakte in den Papierkorb gelegt.",
            "bauakte.note_added" => $"{actor} hat eine Notiz geschrieben.",
            "bauakte.file_added" => Detail("kind") switch
            {
                "Photo" => $"{actor} hat ein Foto hinzugefügt.",
                "Video" => $"{actor} hat ein Video hinzugefügt.",
                _ => $"{actor} hat ein Dokument hinzugefügt.",
            },
            "bauakte.workitem_created" => kindIsDefect ? $"{actor} hat einen Mangel erfasst." : $"{actor} hat eine Aufgabe angelegt.",
            "bauakte.workitem_done" => kindIsDefect ? $"{actor} hat einen Mangel als behoben markiert." : $"{actor} hat eine Aufgabe erledigt.",
            "bauakte.room_added" => $"{actor} hat einen Raum angelegt.",
            "bauakte.members_changed" => $"{actor} hat die zugewiesenen Mitarbeiter geändert.",
            "bauakte.diary_added" => $"{actor} hat das Bautagebuch für den {Day(Detail("day"))} ergänzt.",
            "bauakte.inspection_added" when Detail("kind") == "CmMeasurement" =>
                $"{actor} hat eine CM-Messung erfasst: {Percent(Detail("measured"))} CM-% bei Grenzwert {Percent(Detail("limit"))} CM-% – "
                + (Detail("ready") == "true" ? "belegreif." : "noch nicht belegreif."),
            "bauakte.inspection_added" => Detail("notRequired") == "true"
                ? $"{actor} hat vermerkt, dass keine Erstbegehung notwendig ist."
                : $"{actor} hat die Erstbegehung dokumentiert.",
            _ => $"{actor}: {entry.Action}",
        };
    }

    private static string Day(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : value;

    private static string Percent(string value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number.ToString("0.0#", CultureInfo.GetCultureInfo("de-DE"))
            : value;

    private static string Status(string value) => StatusNames.TryGetValue(value, out var name) ? name : value;

    private static string DescribeFields(string fields)
    {
        var names = fields.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(field => FieldNames.TryGetValue(field, out var name) ? name : field)
            .Distinct().ToList();
        return names.Count switch
        {
            0 => "Angaben",
            1 => names[0],
            _ => string.Join(", ", names[..^1]) + " und " + names[^1],
        };
    }
}
