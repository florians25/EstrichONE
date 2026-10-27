using System.Globalization;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
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
public sealed record BauakteHistoryEntry(DateTimeOffset At, string Action, Guid? ActorUserId, string? ActorName, string Text,
    IReadOnlyList<BauakteFieldChange>? Changes = null);

/// <summary>Geändertes Feld mit altem und neuem Wert (Anzeige; Kunde und Zuständigkeit als Namen).</summary>
public sealed record BauakteFieldChange(string Field, string Label, string? From, string? To);

internal static class BauakteHistoryEndpoints
{
    private static readonly Dictionary<string, string> FieldNames = new()
    {
        ["title"] = "die Bezeichnung", ["street"] = "die Straße", ["postalCode"] = "die PLZ", ["city"] = "den Ort",
        ["latitude"] = "den Standort", ["longitude"] = "den Standort", ["description"] = "die Beschreibung",
        ["customerId"] = "den Kunden", ["responsibleUserId"] = "die Zuständigkeit", ["period"] = "den Zeitraum",
        ["siteContact"] = "den Baustellenkontakt", ["siteNotes"] = "die Hinweise zur Baustelle",
    };

    private static readonly Dictionary<string, string> FieldLabels = new()
    {
        ["title"] = "Bezeichnung", ["street"] = "Straße", ["postalCode"] = "PLZ", ["city"] = "Ort", ["latitude"] = "Standort",
        ["description"] = "Beschreibung", ["customerId"] = "Kunde", ["responsibleUserId"] = "Zuständig", ["period"] = "Zeitraum",
        ["siteContact"] = "Baustellenkontakt", ["siteNotes"] = "Hinweise zur Baustelle",
    };

    private static readonly Dictionary<string, string> StatusNames = new()
    {
        ["Inquiry"] = "Anfrage", ["Planned"] = "Geplant", ["InProgress"] = "In Ausführung", ["Drying"] = "Belegreif / Trocknung",
        ["Completed"] = "Abgeschlossen", ["Cancelled"] = "Storniert",
    };

    /// <summary>Deutscher Name eines Status (für Meldungen).</summary>
    internal static string StatusName(BauakteStatus status) => StatusNames.GetValueOrDefault(status.ToString(), status.ToString());

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
        Guid? Id(string? value) => Guid.TryParse(value, out var guid) ? guid : null;
        var referenced = entries.SelectMany(e => e.Details.Where(d => d.Key.StartsWith("responsibleUserId.", StringComparison.Ordinal)).Select(d => Id(d.Value)))
            .OfType<Guid>();
        var actorIds = entries.Select(e => e.ActorUserId).OfType<Guid>().Concat(referenced).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var customerIds = entries.SelectMany(e => e.Details.Where(d => d.Key.StartsWith("customerId.", StringComparison.Ordinal)).Select(d => Id(d.Value)))
            .OfType<Guid>().Distinct().ToList();
        var customers = customerIds.Count == 0 ? new Dictionary<Guid, string>()
            : await db.Set<Contact>().AsNoTracking().IgnoreQueryFilters().Where(c => customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        string? Value(string field, string? raw) => field switch
        {
            "customerId" => Id(raw) is { } c ? customers.GetValueOrDefault(c, "?") : null,
            "responsibleUserId" => Id(raw) is { } u ? names.GetValueOrDefault(u, "?") : null,
            "period" => raw is null ? null : string.Join(" – ", raw.Split(" · ").Select(Day)),
            _ => raw,
        };

        return TypedResults.Ok(entries.Select(entry =>
        {
            var actor = entry.ActorUserId is { } actorId && names.TryGetValue(actorId, out var name) ? name : null;
            var changes = entry.Action == "bauakte.updated"
                ? entry.Details.Keys.Where(k => k.EndsWith(".from", StringComparison.Ordinal)).Select(k => k[..^5])
                    .Select(f => new BauakteFieldChange(f, FieldLabels.GetValueOrDefault(f, f),
                        Value(f, entry.Details.GetValueOrDefault($"{f}.from")), Value(f, entry.Details.GetValueOrDefault($"{f}.to"))))
                    .ToList()
                : null;
            if (entry.Action == "bauakte.status_changed")
            {
                changes = [new BauakteFieldChange("status", "Status", Status(entry.Details.GetValueOrDefault("from") ?? string.Empty),
                    Status(entry.Details.GetValueOrDefault("to") ?? string.Empty))];
            }

            return new BauakteHistoryEntry(entry.OccurredAt, entry.Action, entry.ActorUserId, actor, Describe(actor ?? "Jemand", entry),
                changes is { Count: > 0 } ? changes : null);
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
            "bauakte.status_changed" when Detail("auto") == "true" =>
                $"Automatisch: Status von „{Status(Detail("from"))}“ auf „{Status(Detail("to"))}“ gesetzt (Auslöser: {Trigger(Detail("trigger"))}).",
            "bauakte.status_changed" => $"{actor} hat den Status von „{Status(Detail("from"))}“ auf „{Status(Detail("to"))}“ gesetzt.",
            "bauakte.deleted" => $"{actor} hat die Bauakte in den Papierkorb gelegt.",
            "bauakte.note_added" => $"{actor} hat eine Notiz geschrieben.",
            "bauakte.report_printed" => $"{actor} hat die Bauakte als PDF erstellt.",
            "bauakte.file_added" => Detail("kind") switch
            {
                "Photo" => $"{actor} hat ein Foto hinzugefügt.",
                "Video" => $"{actor} hat ein Video hinzugefügt.",
                _ => $"{actor} hat ein Dokument hinzugefügt.",
            },
            "bauakte.workitem_created" => kindIsDefect ? $"{actor} hat einen Mangel erfasst." : $"{actor} hat eine Aufgabe angelegt.",
            "bauakte.workitem_done" => kindIsDefect ? $"{actor} hat einen Mangel als behoben markiert." : $"{actor} hat eine Aufgabe erledigt.",
            "bauakte.room_added" => $"{actor} hat einen Raum angelegt.",
            "bauakte.acceptance_added" => $"{actor} hat ein Abnahmeprotokoll erfasst (Ergebnis: {AcceptanceResult(Detail("result"))}).",
            "bauakte.acceptance_link_sent" => Detail("emailed") == "true"
                ? $"{actor} hat dem Kunden das Abnahmeprotokoll per E-Mail zur Freigabe geschickt."
                : $"{actor} hat einen Freigabelink für das Abnahmeprotokoll erstellt.",
            "bauakte.acceptance_customer_approved" => $"Der Kunde ({Detail("name")}) hat das Abnahmeprotokoll online freigegeben.",
            "bauakte.acceptance_customer_rejected" => $"Der Kunde ({Detail("name")}) hat die Abnahme abgelehnt: „{Detail("comment")}“.",
            "bauakte.acceptance_completed" => $"Abnahmeprotokoll abgeschlossen ({AcceptanceResult(Detail("result"))}) und als PDF unter „Abnahmen“ abgelegt.",
            "bauakte.members_changed" => $"{actor} hat die zugewiesenen Mitarbeiter geändert.",
            "bauakte.parties_changed" => Detail("change") switch
            {
                "added" => $"{actor} hat „{Detail("name")}“ als {PartyRole(Detail("role"))} hinzugefügt.",
                "removed" => $"{actor} hat „{Detail("name")}“ ({PartyRole(Detail("role"))}) entfernt.",
                "roleChanged" => $"{actor} hat „{Detail("name")}“ jetzt als {PartyRole(Detail("role"))} eingetragen.",
                "primary" => $"{actor} hat „{Detail("name")}“ ({PartyRole(Detail("role"))}) als Hauptkontakt markiert.",
                _ => $"{actor} hat den Kontakt „{Detail("name")}“ ({PartyRole(Detail("role"))}) geändert.",
            },
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

    private static string AcceptanceResult(string value) => value switch
    {
        "Accepted" => "abgenommen",
        "AcceptedWithReservations" => "abgenommen mit Vorbehalt",
        "Refused" => "Abnahme verweigert",
        _ => value,
    };

    private static string Trigger(string value) => value switch
    {
        "AssignmentDay" => "erster Einsatztag",
        "Photo" => "erstes Foto vor Ort",
        "Diary" => "erster Bautagebucheintrag",
        "Inspection" => "Erstbegehung",
        "Acceptance" => "Abnahme ohne Vorbehalt",
        _ => value,
    };

    private static string PartyRole(string value) => value switch
    {
        "Client" => "Bauherr/Kunde",
        "SiteContact" => "Ansprechpartner vor Ort",
        "SiteManager" => "Bauleiter",
        "Architect" => "Architekt",
        "Planner" => "Planer",
        "GeneralContractor" => "Generalunternehmer",
        "Supplier" => "Lieferant",
        "Subcontractor" => "Nachunternehmer",
        _ => "Beteiligter",
    };

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
