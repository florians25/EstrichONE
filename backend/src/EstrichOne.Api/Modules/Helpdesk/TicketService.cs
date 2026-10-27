using System.Globalization;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Helpdesk.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Helpdesk;

public sealed record CreateTicketInput(
    Guid Id, string Title, string? Description, TicketCategory Category, TicketSource Source, bool ReporterBlocked, Guid? BauakteId,
    JsonNode? Diagnostics, IReadOnlyList<Guid>? AttachmentFileIds, TicketPriority? Priority = null);

public enum TicketFailure
{
    IdConflict,
    BauakteNotFound,
    Validation,
}

public sealed record TicketCommentDto(
    Guid Id, Guid AuthorUserId, string? AuthorName, string Text, bool IsInternal, DateTimeOffset CreatedAt, IReadOnlyList<Guid>? AttachmentFileIds = null);

/// <summary>
/// Eintrag im Verlauf eines Tickets (Web und App zeigen ihn als Zeitleiste): Meldung, Antworten, interne Notizen,
/// Statuswechsel, Zuweisung, Priorität, Kategorie. <see cref="Summary"/> ist der fertige deutsche Text des Ereignisses.
/// </summary>
/// <param name="Kind">created, comment, note, status, assignee, priority, category</param>
/// <param name="IsReporter">Vom Melder selbst (Web/App zeigen das wie eine Chat-Blase auf der anderen Seite).</param>
public sealed record TicketTimelineItem(
    Guid Id, DateTimeOffset At, string Kind, Guid? ActorUserId, string? ActorName, string Summary, string? Text, string? FromValue, string? ToValue,
    bool IsInternal, bool IsReporter, IReadOnlyList<Guid> AttachmentFileIds);

public sealed record TicketEventDto(Guid? ActorUserId, string? ActorName, string Kind, string? FromValue, string? ToValue, DateTimeOffset At);

public sealed record TicketSummary(
    Guid Id, string Number, string Title, TicketCategory Category, TicketPriority Priority, TicketStatus Status, bool ReporterBlocked,
    Guid? ReporterUserId, string? ReporterDisplayName, Guid? AssigneeUserId, string? AssigneeName, TicketSource Source,
    DateTimeOffset CreatedAt, DateTimeOffset LastActivityAt, long Version, bool HasUnread = false);

/// <param name="Diagnostics">Die vom Melder freigegebenen Diagnosedaten (unverändert).</param>
public sealed record TicketDetail(
    TicketSummary Summary, string? Description, Guid? DeviceId, string? DeviceName, Guid? BauakteId, string? Resolution,
    DateTimeOffset? ResolvedAt, DateTimeOffset? ClosedAt, JsonNode? Diagnostics, IReadOnlyList<Guid> AttachmentFileIds,
    IReadOnlyList<TicketCommentDto> Comments, IReadOnlyList<TicketEventDto> Events, IReadOnlyList<TicketTimelineItem>? Timeline = null);

/// <summary>Anlegen und Lesen von Tickets – gemeinsam für REST, Sync und Meldung ohne Anmeldung.</summary>
public sealed class TicketService(EstrichOneDbContext db, IBauakteAccess bauakten, TimeProvider clock) : ITicketAttachments
{
    private static readonly Dictionary<string, string> StatusText = new()
    {
        [nameof(TicketStatus.New)] = "Neu", [nameof(TicketStatus.InProgress)] = "In Bearbeitung", [nameof(TicketStatus.WaitingForReporter)] = "Rückfrage",
        [nameof(TicketStatus.Resolved)] = "Gelöst", [nameof(TicketStatus.Closed)] = "Geschlossen",
    };

    public static string StatusName(TicketStatus status) => StatusText.GetValueOrDefault(status.ToString()) ?? status.ToString();

    private static readonly Dictionary<string, string> PriorityText = new()
    {
        [nameof(TicketPriority.Low)] = "Niedrig", [nameof(TicketPriority.Normal)] = "Normal", [nameof(TicketPriority.High)] = "Hoch",
        [nameof(TicketPriority.Urgent)] = "Dringend",
    };

    private static readonly Dictionary<string, string> CategoryText = new()
    {
        [nameof(TicketCategory.SignIn)] = "Anmeldung", [nameof(TicketCategory.Sync)] = "Synchronisation", [nameof(TicketCategory.AppError)] = "App-Fehler",
        [nameof(TicketCategory.DeviceDefect)] = "Gerät defekt", [nameof(TicketCategory.OfficeIt)] = "Drucker/Büro-IT", [nameof(TicketCategory.Access)] = "Zugang/Rechte",
        [nameof(TicketCategory.Other)] = "Sonstiges",
    };

    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public async Task<(Ticket? Ticket, bool Created, TicketFailure? Failure, string? Message)> CreateAsync(
        CreateTicketInput input, Guid? reporterUserId, string? reporterName, Guid? deviceId, CancellationToken cancellationToken)
    {
        var existing = await db.Set<Ticket>().SingleOrDefaultAsync(t => t.Id == input.Id, cancellationToken);
        if (existing is not null)
        {
            // Gleiche ID vom selben Melder bzw. Gerät: Wiederholung nach Verbindungsabbruch, kein zweites Ticket.
            var same = existing.ReporterUserId == reporterUserId && existing.DeviceId == deviceId && existing.Title == input.Title.Trim();
            return same ? (existing, false, null, null) : (null, false, TicketFailure.IdConflict, "Unter dieser ID existiert bereits ein anderes Ticket.");
        }

        if (input.BauakteId is { } bauakteId && (reporterUserId is null || !await bauakten.CanReadAsync(bauakteId, cancellationToken)))
        {
            return (null, false, TicketFailure.BauakteNotFound, "Die Bauakte existiert nicht (mehr) oder ist nicht sichtbar.");
        }

        var (diagnostics, error) = TicketDiagnostics.Validate(input.Diagnostics);
        if (error is not null)
        {
            return (null, false, TicketFailure.Validation, error);
        }

        var attachments = input.AttachmentFileIds ?? [];
        if (attachments.Count > 0)
        {
            // Nur eigene Dateien ohne Bauakte (Bildschirmfotos, Fotos zum Problem) – nie fremde Bauaktenfotos.
            var own = await db.Database.SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM files WHERE id = ANY({attachments.ToArray()}) AND bauakte_id IS NULL AND uploaded_by_user_id = {reporterUserId}
                """).ToListAsync(cancellationToken);
            if (reporterUserId is null || own.Count != attachments.Distinct().Count())
            {
                return (null, false, TicketFailure.Validation, "Anhänge müssen eigene Dateien ohne Bauakte sein.");
            }
        }

        var now = clock.GetUtcNow();
        Ticket ticket;
        try
        {
            ticket = Ticket.Create(input.Id, await NextNumberAsync(now, cancellationToken), input.Title, input.Description, input.Category,
                input.Source, input.ReporterBlocked, reporterUserId, reporterName, deviceId, input.BauakteId, diagnostics, attachments, now);
            if (input.Priority is { } priority)
            {
                ticket.Prioritize(priority);
            }
        }
        catch (ArgumentException exception)
        {
            return (null, false, TicketFailure.Validation, exception.Message);
        }

        db.Add(ticket);
        db.Add(TicketEvent.Create(ticket.Id, reporterUserId, "created", null, TicketStatus.New.ToString(), now));
        await db.SaveChangesAsync(cancellationToken);
        return (ticket, true, null, null);
    }

    private async Task<string> NextNumberAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var year = TimeZoneInfo.ConvertTime(now, Berlin).Year;
        var rows = await db.Database.SqlQuery<int>($"""
            INSERT INTO ticket_number_sequences (year, last_number) VALUES ({year}, 1)
            ON CONFLICT (year) DO UPDATE SET last_number = ticket_number_sequences.last_number + 1
            RETURNING last_number AS "Value"
            """).ToListAsync(cancellationToken);
        return string.Create(CultureInfo.InvariantCulture, $"T-{year}-{rows.Single():0000}");
    }

    public async Task<IReadOnlyList<TicketSummary>> SummariesAsync(IQueryable<Ticket> query, CancellationToken cancellationToken,
        Func<IReadOnlyCollection<Guid>, Task<Dictionary<Guid, DateTimeOffset>>>? unread = null)
    {
        var tickets = await query.AsNoTracking().ToListAsync(cancellationToken);
        var names = await NamesAsync(tickets.SelectMany(t => new[] { t.ReporterUserId, t.AssigneeUserId }), cancellationToken);
        var news = unread is null || tickets.Count == 0 ? [] : await unread(tickets.Select(t => t.Id).ToList());
        return tickets.Select(t => Summarize(t, names) with { HasUnread = news.ContainsKey(t.Id) }).ToList();
    }

    public async Task<TicketDetail> DetailAsync(Ticket ticket, bool includeInternal, CancellationToken cancellationToken)
    {
        var comments = await db.Set<TicketComment>().AsNoTracking()
            .Where(c => c.TicketId == ticket.Id && (includeInternal || !c.IsInternal))
            .OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);
        var events = await db.Set<TicketEvent>().AsNoTracking().Where(e => e.TicketId == ticket.Id).OrderBy(e => e.At).ToListAsync(cancellationToken);
        var assigneeIds = events.Where(e => e.Kind == "assignee").SelectMany(e => new[] { e.FromValue, e.ToValue })
            .Select(v => Guid.TryParse(v, out var g) ? g : (Guid?)null);
        var names = await NamesAsync(new[] { ticket.ReporterUserId, ticket.AssigneeUserId }
            .Concat(comments.Select(c => (Guid?)c.AuthorUserId)).Concat(events.Select(e => e.ActorUserId)).Concat(assigneeIds), cancellationToken);
        var deviceName = ticket.DeviceId is { } deviceId
            ? await db.Database.SqlQuery<string>($"""SELECT name AS "Value" FROM devices WHERE id = {deviceId}""").SingleOrDefaultAsync(cancellationToken)
            : null;

        return new TicketDetail(Summarize(ticket, names), ticket.Description, ticket.DeviceId, deviceName, ticket.BauakteId, ticket.Resolution,
            ticket.ResolvedAt, ticket.ClosedAt, TicketDiagnostics.Parse(ticket.Diagnostics), ticket.AttachmentFileIds,
            comments.Select(c => new TicketCommentDto(c.Id, c.AuthorUserId, names.GetValueOrDefault(c.AuthorUserId), c.Text, c.IsInternal, c.CreatedAt,
                c.AttachmentFileIds)).ToList(),
            events.Select(e => new TicketEventDto(e.ActorUserId, e.ActorUserId is { } a ? names.GetValueOrDefault(a) : null, e.Kind, e.FromValue, e.ToValue, e.At)).ToList(),
            Timeline(ticket, comments, events, names));
    }

    private static List<TicketTimelineItem> Timeline(Ticket ticket, List<TicketComment> comments, List<TicketEvent> events, Dictionary<Guid, string> names)
    {
        string? Name(Guid? id) => id is { } value ? names.GetValueOrDefault(value) : null;
        string Person(string? value) => Guid.TryParse(value, out var id) ? names.GetValueOrDefault(id) ?? "Unbekannt" : "niemand";
        static string Text(Dictionary<string, string> map, string? value) => value is null ? "–" : map.GetValueOrDefault(value) ?? value;

        var items = new List<TicketTimelineItem>();
        foreach (var e in events)
        {
            var actor = Name(e.ActorUserId) ?? (e.Kind == "created" ? ticket.ReporterName ?? "Gerät" : "System");
            var isReporter = e.ActorUserId is { } a && a == ticket.ReporterUserId;
            var item = e.Kind switch
            {
                "created" => new TicketTimelineItem(e.Id, e.At, "created", e.ActorUserId, actor, $"{actor} hat das Problem gemeldet", ticket.Description, null,
                    e.ToValue, false, true, ticket.AttachmentFileIds),
                "status" => new TicketTimelineItem(e.Id, e.At, "status", e.ActorUserId, actor,
                    $"Status: {Text(StatusText, e.FromValue)} → {Text(StatusText, e.ToValue)}",
                    e.ToValue == nameof(TicketStatus.Resolved) ? ticket.Resolution : null, e.FromValue, e.ToValue, false, isReporter, []),
                "assignee" => new TicketTimelineItem(e.Id, e.At, "assignee", e.ActorUserId, actor,
                    e.ToValue is null ? "Zuweisung aufgehoben" : $"Zugewiesen an {Person(e.ToValue)}", null, e.FromValue, e.ToValue, false, isReporter, []),
                "priority" => new TicketTimelineItem(e.Id, e.At, "priority", e.ActorUserId, actor,
                    $"Priorität: {Text(PriorityText, e.FromValue)} → {Text(PriorityText, e.ToValue)}", null, e.FromValue, e.ToValue, false, isReporter, []),
                "category" => new TicketTimelineItem(e.Id, e.At, "category", e.ActorUserId, actor,
                    $"Kategorie: {Text(CategoryText, e.FromValue)} → {Text(CategoryText, e.ToValue)}", null, e.FromValue, e.ToValue, false, isReporter, []),
                _ => new TicketTimelineItem(e.Id, e.At, e.Kind, e.ActorUserId, actor, e.Kind, null, e.FromValue, e.ToValue, false, isReporter, []),
            };
            items.Add(item);
        }

        foreach (var c in comments)
        {
            var author = Name(c.AuthorUserId) ?? "Unbekannt";
            items.Add(new TicketTimelineItem(c.Id, c.CreatedAt, c.IsInternal ? "note" : "comment", c.AuthorUserId, author,
                c.IsInternal ? $"{author} hat eine interne Notiz geschrieben" : $"{author} hat geantwortet", c.Text, null, null, c.IsInternal,
                c.AuthorUserId == ticket.ReporterUserId, c.AttachmentFileIds));
        }

        return items.OrderBy(i => i.At).ThenBy(i => i.Kind == "created" ? 0 : 1).ToList();
    }

    public async Task<bool> IsAttachedToTicketAsync(Guid fileId, CancellationToken cancellationToken) =>
        await db.Set<Ticket>().AnyAsync(t => t.AttachmentFileIds.Contains(fileId), cancellationToken)
        || await db.Set<TicketComment>().AnyAsync(c => c.AttachmentFileIds.Contains(fileId), cancellationToken);

    /// <summary>Nur eigene, hochgeladene Dateien ohne Bauakte dürfen an eine Antwort.</summary>
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

    /// <summary>Schließt gelöste Tickets nach <see cref="Ticket.AutoCloseAfter"/> ohne Widerspruch.</summary>
    public async Task<int> CloseResolvedAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var due = now - Ticket.AutoCloseAfter;
        var tickets = await db.Set<Ticket>().Where(t => t.Status == TicketStatus.Resolved && t.ResolvedAt <= due).ToListAsync(cancellationToken);
        foreach (var ticket in tickets)
        {
            ticket.ChangeStatus(TicketStatus.Closed, null, now);
            db.Add(TicketEvent.Create(ticket.Id, null, "status", nameof(TicketStatus.Resolved), nameof(TicketStatus.Closed), now));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Ein anderer Lauf (zweite Instanz) hat dieselben Tickets gerade geschlossen.
            return 0;
        }

        return tickets.Count;
    }

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var wanted = ids.OfType<Guid>().Distinct().ToList();
        return wanted.Count == 0
            ? []
            : await db.Set<User>().AsNoTracking().Where(u => wanted.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
    }

    private static TicketSummary Summarize(Ticket t, Dictionary<Guid, string> names) => new(
        t.Id, t.Number, t.Title, t.Category, t.Priority, t.Status, t.ReporterBlocked, t.ReporterUserId,
        t.ReporterUserId is { } reporter ? names.GetValueOrDefault(reporter) : t.ReporterName is null ? null : $"{t.ReporterName} (laut Eingabe)",
        t.AssigneeUserId, t.AssigneeUserId is { } assignee ? names.GetValueOrDefault(assignee) : null, t.Source, t.CreatedAt, t.LastActivityAt, t.Version);
}

/// <summary>Stündlich: gelöste Tickets nach 7 Tagen schließen.</summary>
internal sealed partial class TicketMaintenance(IServiceScopeFactory scopes, TimeProvider clock, ILogger<TicketMaintenance> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var closed = await scope.ServiceProvider.GetRequiredService<TicketService>().CloseResolvedAsync(stoppingToken);
                if (closed > 0)
                {
                    LogClosed(logger, closed);
                }
            }
            catch (Npgsql.PostgresException exception) when (exception.SqlState == Npgsql.PostgresErrorCodes.UndefinedTable)
            {
                // Datenbank noch nicht migriert.
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "{Count} gelöste Tickets automatisch geschlossen")]
    private static partial void LogClosed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Automatisches Schließen der Tickets fehlgeschlagen")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
