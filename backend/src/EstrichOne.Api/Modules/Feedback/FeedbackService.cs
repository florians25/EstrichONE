using System.Globalization;
using System.Text.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Documentation.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Feedback;

/// <param name="AttachmentCount">Anhänge der Meldung selbst (ohne Antworten).</param>
public sealed record FeedbackSummary(
    Guid Id, string Number, FeedbackKind Kind, string Title, string Area, FeedbackStatus Status, FeedbackPriority Priority, FeedbackImpact Impact,
    Guid ReporterUserId, string? ReporterName, Guid? AssigneeUserId, string? AssigneeName, Guid? DuplicateOfId, string? DuplicateOfNumber,
    int AttachmentCount, DateTimeOffset CreatedAt, DateTimeOffset LastActivityAt, DateTimeOffset? ClosedAt, long Version);

public sealed record FeedbackAttachment(Guid Id, string Name, string ContentType, long SizeBytes);

/// <summary>Eintrag im Verlauf (Web zeigt ihn als Zeitleiste). <see cref="Summary"/> ist der fertige deutsche Text.</summary>
/// <param name="Kind">created, comment, note, status, priority, assignee, classify</param>
/// <param name="IsReporter">Vom Einreicher selbst.</param>
public sealed record FeedbackTimelineItem(
    Guid Id, DateTimeOffset At, string Kind, Guid? ActorUserId, string? ActorName, string Summary, string? Text, bool IsInternal, bool IsReporter,
    IReadOnlyList<FeedbackAttachment> Attachments);

/// <param name="Context">Automatisch mitgeschickte technische Angaben (Seite, Browser, Bildschirm, App-Version).</param>
/// <param name="Timeline">Verlauf; interne Notizen nur für die Verwaltung.</param>
public sealed record FeedbackDetail(
    FeedbackSummary Summary, string? Description, string? StepsToReproduce, string? ExpectedResult, string? ActualResult, FeedbackFrequency? Frequency,
    string? Problem, string? DesiredSolution, FeedbackContext? Context, IReadOnlyList<FeedbackAttachment> Attachments,
    IReadOnlyList<FeedbackTimelineItem> Timeline);

/// <summary>Zähler über alle Meldungen (unabhängig vom Filter) für Kacheln und Segmente.</summary>
public sealed record FeedbackCounts(
    int Total, int Open, int New, int InReview, int Planned, int InProgress, int Done, int Rejected, int Duplicate, int OpenBugs, int OpenFeatures);

/// <param name="Areas">Alle vorkommenden Bereiche (für den Filter).</param>
public sealed record FeedbackListResponse(IReadOnlyList<FeedbackSummary> Items, FeedbackCounts Counts, IReadOnlyList<string> Areas);

/// <summary>Lesen, Nummern, Texte und Anhänge der Meldungen.</summary>
public sealed class FeedbackService(EstrichOneDbContext db, IStoredFiles files)
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    internal static readonly JsonSerializerOptions ContextJson = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, string> StatusText = new()
    {
        [nameof(FeedbackStatus.New)] = "Neu", [nameof(FeedbackStatus.InReview)] = "In Prüfung", [nameof(FeedbackStatus.Planned)] = "Geplant",
        [nameof(FeedbackStatus.InProgress)] = "In Umsetzung", [nameof(FeedbackStatus.Done)] = "Erledigt", [nameof(FeedbackStatus.Rejected)] = "Abgelehnt",
        [nameof(FeedbackStatus.Duplicate)] = "Duplikat",
    };

    private static readonly Dictionary<string, string> PriorityText = new()
    {
        [nameof(FeedbackPriority.Low)] = "Niedrig", [nameof(FeedbackPriority.Normal)] = "Normal", [nameof(FeedbackPriority.High)] = "Hoch",
        [nameof(FeedbackPriority.Urgent)] = "Dringend",
    };

    public static string StatusName(FeedbackStatus status) => StatusText.GetValueOrDefault(status.ToString()) ?? status.ToString();

    public static string KindName(FeedbackKind kind) => kind == FeedbackKind.Bug ? "Fehler" : "Funktionswunsch";

    public async Task<string> NextNumberAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var year = TimeZoneInfo.ConvertTime(now, Berlin).Year;
        var rows = await db.Database.SqlQuery<int>($"""
            INSERT INTO feedback_number_sequences (year, last_number) VALUES ({year}, 1)
            ON CONFLICT (year) DO UPDATE SET last_number = feedback_number_sequences.last_number + 1
            RETURNING last_number AS "Value"
            """).ToListAsync(cancellationToken);
        return string.Create(CultureInfo.InvariantCulture, $"F-{year}-{rows.Single():0000}");
    }

    /// <summary>Nur eigene, hochgeladene Dateien ohne Bauakte (Bildschirmfotos) dürfen angehängt werden.</summary>
    public async Task<bool> AreOwnLooseFilesAsync(IReadOnlyCollection<Guid> fileIds, Guid userId, CancellationToken cancellationToken)
    {
        if (fileIds.Count == 0)
        {
            return true;
        }

        var ids = fileIds.Distinct().ToArray();
        var own = await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM files WHERE id = ANY({ids}) AND bauakte_id IS NULL AND uploaded_by_user_id = {userId}
            """).ToListAsync(cancellationToken);
        return own.Count == ids.Length;
    }

    public async Task<IReadOnlyList<FeedbackSummary>> SummariesAsync(IQueryable<FeedbackItem> query, CancellationToken cancellationToken)
    {
        var items = await query.AsNoTracking().ToListAsync(cancellationToken);
        var names = await NamesAsync(items.SelectMany(f => new[] { (Guid?)f.ReporterUserId, f.AssigneeUserId }), cancellationToken);
        var numbers = await NumbersAsync(items.Select(f => f.DuplicateOfId), cancellationToken);
        return items.Select(f => Summarize(f, names, numbers)).ToList();
    }

    public async Task<FeedbackCounts> CountsAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Set<FeedbackItem>().AsNoTracking().GroupBy(f => new { f.Status, f.Kind })
            .Select(g => new { g.Key.Status, g.Key.Kind, Count = g.Count() }).ToListAsync(cancellationToken);
        int Of(FeedbackStatus status) => rows.Where(r => r.Status == status).Sum(r => r.Count);
        var open = rows.Where(r => !FeedbackItem.IsClosed(r.Status)).ToList();
        return new FeedbackCounts(rows.Sum(r => r.Count), open.Sum(r => r.Count), Of(FeedbackStatus.New), Of(FeedbackStatus.InReview),
            Of(FeedbackStatus.Planned), Of(FeedbackStatus.InProgress), Of(FeedbackStatus.Done), Of(FeedbackStatus.Rejected), Of(FeedbackStatus.Duplicate),
            open.Where(r => r.Kind == FeedbackKind.Bug).Sum(r => r.Count), open.Where(r => r.Kind == FeedbackKind.Feature).Sum(r => r.Count));
    }

    public async Task<FeedbackDetail> DetailAsync(FeedbackItem item, bool includeInternal, CancellationToken cancellationToken)
    {
        var comments = await db.Set<FeedbackComment>().AsNoTracking()
            .Where(c => c.FeedbackId == item.Id && (includeInternal || !c.IsInternal)).OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);
        var events = await db.Set<FeedbackEvent>().AsNoTracking().Where(e => e.FeedbackId == item.Id).OrderBy(e => e.At).ToListAsync(cancellationToken);
        var assigneeIds = events.Where(e => e.Kind == "assignee").SelectMany(e => new[] { e.FromValue, e.ToValue })
            .Select(v => Guid.TryParse(v, out var g) ? g : (Guid?)null);
        var names = await NamesAsync(new[] { (Guid?)item.ReporterUserId, item.AssigneeUserId }
            .Concat(comments.Select(c => (Guid?)c.AuthorUserId)).Concat(events.Select(e => e.ActorUserId)).Concat(assigneeIds), cancellationToken);
        var numbers = await NumbersAsync([item.DuplicateOfId], cancellationToken);
        var fileIds = item.AttachmentFileIds.Concat(comments.SelectMany(c => c.AttachmentFileIds)).Distinct().ToList();
        var stored = fileIds.Count == 0 ? new Dictionary<Guid, StoredFileInfo>() : await files.FindAsync(fileIds, cancellationToken);
        IReadOnlyList<FeedbackAttachment> Attachments(IEnumerable<Guid> ids) =>
            ids.Select(id => stored.TryGetValue(id, out var f) ? new FeedbackAttachment(id, f.OriginalName, f.ContentType, f.SizeBytes)
                : new FeedbackAttachment(id, "Anhang", "application/octet-stream", 0)).ToList();

        FeedbackContext? context = null;
        if (item.Context is { Length: > 0 } json)
        {
            try
            {
                context = JsonSerializer.Deserialize<FeedbackContext>(json, ContextJson);
            }
            catch (JsonException)
            {
                context = null;
            }
        }

        return new FeedbackDetail(Summarize(item, names, numbers), item.Description, item.StepsToReproduce, item.ExpectedResult, item.ActualResult,
            item.Frequency, item.Problem, item.DesiredSolution, context, Attachments(item.AttachmentFileIds),
            Timeline(item, comments, events, names, Attachments));
    }

    private static List<FeedbackTimelineItem> Timeline(FeedbackItem item, List<FeedbackComment> comments, List<FeedbackEvent> events,
        Dictionary<Guid, string> names, Func<IEnumerable<Guid>, IReadOnlyList<FeedbackAttachment>> attachments)
    {
        string Name(Guid? id) => id is { } value ? names.GetValueOrDefault(value) ?? "Unbekannt" : "System";
        string Person(string? value) => Guid.TryParse(value, out var id) ? names.GetValueOrDefault(id) ?? "Unbekannt" : "niemand";
        static string Map(Dictionary<string, string> map, string? value) => value is null ? "–" : map.GetValueOrDefault(value) ?? value;

        var items = new List<FeedbackTimelineItem>();
        foreach (var e in events)
        {
            var actor = Name(e.ActorUserId);
            var isReporter = e.ActorUserId == item.ReporterUserId;
            var summary = e.Kind switch
            {
                "created" => $"{actor} hat {(item.Kind == FeedbackKind.Bug ? "einen Fehler gemeldet" : "eine Funktion gewünscht")}",
                "status" => $"Status: {Map(StatusText, e.FromValue)} → {Map(StatusText, e.ToValue)}",
                "priority" => $"Priorität: {Map(PriorityText, e.FromValue)} → {Map(PriorityText, e.ToValue)}",
                "assignee" => e.ToValue is null ? "Zuständigkeit aufgehoben" : $"Zuständig: {Person(e.ToValue)}",
                "classify" => $"Eingeordnet: {e.FromValue} → {e.ToValue}",
                _ => e.Kind,
            };
            var text = e.Kind == "status" && e.ToValue == nameof(FeedbackStatus.Duplicate) && item.DuplicateOfId is not null ? "Siehe ursprüngliche Meldung." : null;
            items.Add(new FeedbackTimelineItem(e.Id, e.At, e.Kind, e.ActorUserId, actor, summary, text, false, isReporter,
                e.Kind == "created" ? attachments(item.AttachmentFileIds) : []));
        }

        foreach (var c in comments)
        {
            var author = Name(c.AuthorUserId);
            items.Add(new FeedbackTimelineItem(c.Id, c.CreatedAt, c.IsInternal ? "note" : "comment", c.AuthorUserId, author,
                c.IsInternal ? $"{author} · interne Notiz" : $"{author} hat geantwortet", c.Text, c.IsInternal, c.AuthorUserId == item.ReporterUserId,
                attachments(c.AttachmentFileIds)));
        }

        return items.OrderBy(i => i.At).ThenBy(i => i.Kind == "created" ? 0 : 1).ToList();
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.OfType<Guid>().Distinct().ToList();
        return wanted.Count == 0
            ? []
            : await db.Set<User>().AsNoTracking().Where(u => wanted.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
    }

    private async Task<Dictionary<Guid, string>> NumbersAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.OfType<Guid>().Distinct().ToList();
        return wanted.Count == 0
            ? []
            : await db.Set<FeedbackItem>().AsNoTracking().Where(f => wanted.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Number, cancellationToken);
    }

    private static FeedbackSummary Summarize(FeedbackItem f, Dictionary<Guid, string> names, Dictionary<Guid, string> numbers) => new(
        f.Id, f.Number, f.Kind, f.Title, f.Area, f.Status, f.Priority, f.Impact, f.ReporterUserId, names.GetValueOrDefault(f.ReporterUserId),
        f.AssigneeUserId, f.AssigneeUserId is { } a ? names.GetValueOrDefault(a) : null, f.DuplicateOfId,
        f.DuplicateOfId is { } d ? numbers.GetValueOrDefault(d) : null, f.AttachmentFileIds.Count, f.CreatedAt, f.LastActivityAt, f.ClosedAt, f.Version);
}
