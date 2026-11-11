using System.Globalization;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Helpdesk.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Helpdesk;

public sealed record CreateTicketInput(
    Guid Id, string Title, string? Description, TicketCategory Category, TicketSource Source, bool ReporterBlocked, Guid? BauakteId,
    JsonNode? Diagnostics, IReadOnlyList<Guid>? AttachmentFileIds, TicketPriority? Priority = null, Guid? SubcategoryId = null);

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
/// Statuswechsel, Zuweisung, Priorität, Kategorie, Gruppe, Zusammenführen, Verknüpfungen, Bewertung.
/// <see cref="Summary"/> ist der fertige deutsche Text des Ereignisses.
/// </summary>
/// <param name="Kind">created, comment, note, status, assignee, priority, category, subcategory, group, rule, merged, mergedFrom, link, unlink, article, rating, time, watcher</param>
/// <param name="IsReporter">Vom Melder selbst (Web/App zeigen das wie eine Chat-Blase auf der anderen Seite).</param>
public sealed record TicketTimelineItem(
    Guid Id, DateTimeOffset At, string Kind, Guid? ActorUserId, string? ActorName, string Summary, string? Text, string? FromValue, string? ToValue,
    bool IsInternal, bool IsReporter, IReadOnlyList<Guid> AttachmentFileIds, IReadOnlyList<Guid>? MentionUserIds = null);

public sealed record TicketEventDto(Guid? ActorUserId, string? ActorName, string Kind, string? FromValue, string? ToValue, DateTimeOffset At);

/// <param name="SlaState">met, breached, paused, overdue, dueSoon, ok – oder null (ohne SLA).</param>
/// <param name="DueAt">Nächstes Ziel: erste Antwort (<paramref name="DueKind"/> = response) oder Lösung (resolution).</param>
public sealed record TicketSummary(
    Guid Id, string Number, string Title, TicketCategory Category, TicketPriority Priority, TicketStatus Status, bool ReporterBlocked,
    Guid? ReporterUserId, string? ReporterDisplayName, Guid? AssigneeUserId, string? AssigneeName, TicketSource Source,
    DateTimeOffset CreatedAt, DateTimeOffset LastActivityAt, long Version, bool HasUnread = false,
    Guid? GroupId = null, string? GroupName = null, Guid? SubcategoryId = null, string? SubcategoryName = null,
    DateTimeOffset? FirstResponseAt = null, DateTimeOffset? ResponseDueAt = null, DateTimeOffset? ResolutionDueAt = null,
    string? SlaState = null, DateTimeOffset? DueAt = null, string? DueKind = null, Guid? MergedIntoTicketId = null, int? Rating = null,
    DateTimeOffset? ResolvedAt = null);

public sealed record TicketLinkDto(Guid TicketId, string Number, string Title, TicketStatus Status, string Kind);

public sealed record TicketArticleRef(Guid Id, string Title, bool IsPublished);

/// <param name="Diagnostics">Die vom Melder freigegebenen Diagnosedaten (unverändert).</param>
/// <param name="CanRate">Der Abrufende ist Melder, das Ticket ist gelöst und Bewertungen sind eingeschaltet.</param>
/// <param name="TotalMinutes">Erfasste Arbeitszeit (nur Bearbeitung).</param>
public sealed record TicketDetail(
    TicketSummary Summary, string? Description, Guid? DeviceId, string? DeviceName, Guid? BauakteId, string? Resolution,
    DateTimeOffset? ResolvedAt, DateTimeOffset? ClosedAt, JsonNode? Diagnostics, IReadOnlyList<Guid> AttachmentFileIds,
    IReadOnlyList<TicketCommentDto> Comments, IReadOnlyList<TicketEventDto> Events, IReadOnlyList<TicketTimelineItem>? Timeline = null,
    IReadOnlyList<TicketLinkDto>? Links = null, IReadOnlyList<TicketArticleRef>? Articles = null, string? MergedIntoNumber = null,
    int? Rating = null, string? RatingComment = null, DateTimeOffset? RatedAt = null, bool CanRate = false, int? TotalMinutes = null,
    IReadOnlyList<string>? WatcherNames = null);

/// <summary>Änderung durch die Bearbeitung (PATCH und Sammelbearbeitung); null = unverändert.</summary>
public sealed record TicketChange(
    TicketStatus? Status = null, TicketPriority? Priority = null, TicketCategory? Category = null, Guid? AssigneeUserId = null, bool Unassign = false,
    string? Resolution = null, Guid? GroupId = null, bool ClearGroup = false, Guid? SubcategoryId = null, bool ClearSubcategory = false);

/// <summary>Was eine Änderung ausgelöst hat (für Benachrichtigungen nach dem Speichern).</summary>
public sealed record TicketChangeEffect(bool StatusChanged, Guid? NewAssignee);

/// <summary>Anlegen, Ändern und Lesen von Tickets – gemeinsam für REST, Sync und Meldung ohne Anmeldung.</summary>
public sealed class TicketService(
    EstrichOneDbContext db, IBauakteAccess bauakten, TimeProvider clock, INumberRanges numberRanges, HelpdeskSettingsStore settingsStore,
    IRoleAssignments roles, TicketNotifier notifier) : ITicketAttachments
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

    public static string PriorityName(TicketPriority priority) => PriorityText.GetValueOrDefault(priority.ToString()) ?? priority.ToString();

    private static readonly Dictionary<string, string> CategoryText = new()
    {
        [nameof(TicketCategory.SignIn)] = "Anmeldung", [nameof(TicketCategory.Sync)] = "Synchronisation", [nameof(TicketCategory.AppError)] = "App-Fehler",
        [nameof(TicketCategory.DeviceDefect)] = "Gerät defekt", [nameof(TicketCategory.OfficeIt)] = "Drucker/Büro-IT", [nameof(TicketCategory.Access)] = "Zugang/Rechte",
        [nameof(TicketCategory.Other)] = "Sonstiges",
    };

    public static string CategoryName(TicketCategory category) => CategoryText.GetValueOrDefault(category.ToString()) ?? category.ToString();

    /// <summary>Verlaufseinträge nur für die Bearbeitung.</summary>
    private static readonly HashSet<string> InternalKinds = ["group", "rule", "link", "unlink", "time", "watcher"];

    public Task<HelpdeskSettings> SettingsAsync(CancellationToken cancellationToken) => settingsStore.GetAsync(cancellationToken);

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

        if (input.SubcategoryId is { } sub
            && !await db.Set<TicketSubcategory>().AnyAsync(s => s.Id == sub && s.Category == input.Category && s.IsActive, cancellationToken))
        {
            return (null, false, TicketFailure.Validation, "Die Unterkategorie passt nicht zur Kategorie oder ist nicht mehr aktiv.");
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
        var settings = await settingsStore.GetAsync(cancellationToken);
        Ticket ticket;
        try
        {
            ticket = Ticket.Create(input.Id, await numberRanges.NextAsync(NumberRangeKeys.Ticket, cancellationToken), input.Title, input.Description, input.Category,
                input.Source, input.ReporterBlocked, reporterUserId, reporterName, deviceId, input.BauakteId, diagnostics, attachments, now);
            if (input.Priority is { } priority)
            {
                ticket.Prioritize(priority);
            }

            ticket.SetSubcategory(input.SubcategoryId);
        }
        catch (ArgumentException exception)
        {
            return (null, false, TicketFailure.Validation, exception.Message);
        }

        db.Add(ticket);
        db.Add(TicketEvent.Create(ticket.Id, reporterUserId, "created", null, TicketStatus.New.ToString(), now));
        var decision = await DecideAssignmentAsync(ticket, cancellationToken);
        if (decision is not null)
        {
            db.Add(TicketEvent.Create(ticket.Id, null, "rule", null, Clip(decision.RuleName), now));
            if (decision.GroupId is { } groupId)
            {
                ticket.SetGroup(groupId);
                db.Add(TicketEvent.Create(ticket.Id, null, "group", null, groupId.ToString(), now));
            }

            if (decision.AssigneeUserId is { } assignee)
            {
                ticket.Assign(assignee);
                db.Add(TicketEvent.Create(ticket.Id, null, "assignee", null, assignee.ToString(), now));
            }

            if (decision.Priority is { } rulePriority && input.Priority is null && rulePriority != ticket.Priority)
            {
                db.Add(TicketEvent.Create(ticket.Id, null, "priority", ticket.Priority.ToString(), rulePriority.ToString(), now));
                ticket.Prioritize(rulePriority);
            }
        }

        if (settings.SlaEnabled)
        {
            var (response, resolution) = SlaCalculator.DueDates(now, ticket.Priority, 0, settings);
            ticket.SetDueDates(response, resolution);
        }

        await db.SaveChangesAsync(cancellationToken);
        await notifier.NotifyCreatedAsync(ticket, settings, cancellationToken);
        return (ticket, true, null, null);
    }

    private async Task<AssignmentDecision?> DecideAssignmentAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        var rules = await db.Set<HelpdeskRule>().AsNoTracking().Where(r => r.IsActive).ToListAsync(cancellationToken);
        if (rules.Count == 0)
        {
            return null;
        }

        var groups = await db.Set<HelpdeskGroup>().AsNoTracking().ToDictionaryAsync(g => g.Id, cancellationToken);
        var agents = (await roles.GetUserIdsWithPermissionAsync(Permissions.HelpdeskAgent, cancellationToken)).ToHashSet();
        var load = await db.Set<Ticket>().AsNoTracking()
            .Where(t => t.AssigneeUserId != null && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .GroupBy(t => t.AssigneeUserId!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return AutoAssignment.Decide(rules, ticket.Category, ticket.SubcategoryId, ticket.Title, ticket.Description, groups, agents, load);
    }

    /// <summary>
    /// Änderung der Bearbeitung anwenden (ohne zu speichern): Verlauf, SLA (Prioritätswechsel, Uhr bei Rückfrage anhalten),
    /// erste Antwort. Liefert eine Fehlermeldung oder die Wirkung für Benachrichtigungen.
    /// </summary>
    public async Task<(TicketChangeEffect? Effect, string? Error)> ApplyChangeAsync(Ticket ticket, TicketChange change, Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var settings = await settingsStore.GetAsync(cancellationToken);
        void Event(string kind, object? from, object? to)
        {
            if (!Equals(from, to))
            {
                db.Add(TicketEvent.Create(ticket.Id, actorUserId, kind, from?.ToString(), to?.ToString(), now));
            }
        }

        Guid? newAssignee = null;
        var statusChanged = false;
        try
        {
            if (change.AssigneeUserId is not null || change.Unassign)
            {
                var target = change.Unassign ? null : change.AssigneeUserId;
                if (target is { } userId && !await db.Set<User>().AnyAsync(u => u.Id == userId && u.IsActive, cancellationToken))
                {
                    return (null, "Der Benutzer existiert nicht oder ist gesperrt.");
                }

                if (target != ticket.AssigneeUserId)
                {
                    newAssignee = target;
                }

                Event("assignee", ticket.AssigneeUserId, target);
                ticket.Assign(target);
            }

            if (change.GroupId is not null || change.ClearGroup)
            {
                var group = change.ClearGroup ? null : change.GroupId;
                if (group is { } groupId && !await db.Set<HelpdeskGroup>().AnyAsync(g => g.Id == groupId, cancellationToken))
                {
                    return (null, "Die Gruppe existiert nicht.");
                }

                Event("group", ticket.GroupId, group);
                ticket.SetGroup(group);
            }

            if (change.Category is { } category && category != ticket.Category)
            {
                Event("category", ticket.Category, category);
                ticket.Categorize(category);
                if (ticket.SubcategoryId is not null && change.SubcategoryId is null)
                {
                    // Unterkategorie gehört zur alten Kategorie.
                    Event("subcategory", ticket.SubcategoryId, null);
                    ticket.SetSubcategory(null);
                }
            }

            if (change.SubcategoryId is not null || change.ClearSubcategory)
            {
                var sub = change.ClearSubcategory ? null : change.SubcategoryId;
                if (sub is { } subId && !await db.Set<TicketSubcategory>().AnyAsync(s => s.Id == subId && s.Category == ticket.Category, cancellationToken))
                {
                    return (null, "Die Unterkategorie passt nicht zur Kategorie.");
                }

                Event("subcategory", ticket.SubcategoryId, sub);
                ticket.SetSubcategory(sub);
            }

            if (change.Priority is { } priority && priority != ticket.Priority)
            {
                Event("priority", ticket.Priority, priority);
                ticket.Prioritize(priority);
                if (settings.SlaEnabled)
                {
                    var (response, resolution) = SlaCalculator.DueDates(ticket.CreatedAt, priority, ticket.SlaPausedMinutes, settings);
                    ticket.SetDueDates(response, resolution);
                }
            }

            if (change.Status is { } status)
            {
                statusChanged = status != ticket.Status;
                Event("status", ticket.Status, status);
                ChangeStatus(ticket, status, change.Resolution, actorUserId, settings, now);
            }
        }
        catch (ArgumentException exception)
        {
            return (null, exception.Message);
        }

        ticket.Touch(now);
        return (new TicketChangeEffect(statusChanged, newAssignee), null);
    }

    /// <summary>Statuswechsel mit SLA: „Rückfrage“ hält die Uhr an; Antwort der Bearbeitung zählt als Reaktion.</summary>
    public void ChangeStatus(Ticket ticket, TicketStatus status, string? resolution, Guid? actorUserId, HelpdeskSettings settings, DateTimeOffset now)
    {
        var previous = ticket.Status;
        ticket.ChangeStatus(status, resolution, now);
        if (previous == TicketStatus.WaitingForReporter && status != TicketStatus.WaitingForReporter)
        {
            ResumeSla(ticket, settings, now);
        }

        if (status == TicketStatus.WaitingForReporter)
        {
            ticket.PauseSla(now);
        }

        if (status is TicketStatus.WaitingForReporter or TicketStatus.Resolved or TicketStatus.Closed
            && actorUserId is { } actor && !ticket.IsVisibleTo(actor))
        {
            ticket.MarkFirstResponse(now);
        }
    }

    private static void ResumeSla(Ticket ticket, HelpdeskSettings settings, DateTimeOffset now)
    {
        if (ticket.SlaPausedAt is not { } since)
        {
            return;
        }

        var paused = SlaCalculator.PausedMinutes(since, now, ticket.Priority, settings);
        DateTimeOffset? resolution = null;
        if (ticket.ResolutionDueAt is not null)
        {
            resolution = SlaCalculator.DueDates(ticket.CreatedAt, ticket.Priority, ticket.SlaPausedMinutes + paused, settings).Resolution;
        }

        ticket.ResumeSla(paused, resolution);
    }

    /// <summary>Antwort bzw. Notiz anlegen (ohne zu speichern); Melder-Antwort auf Rückfrage holt das Ticket zurück.</summary>
    public async Task<string?> AddCommentAsync(Ticket ticket, Guid authorUserId, bool isAgent, string text, bool isInternal,
        IReadOnlyCollection<Guid> attachments, IReadOnlyCollection<Guid> mentions, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        try
        {
            db.Add(TicketComment.Create(ticket.Id, authorUserId, text, isInternal, now, attachments, mentions));
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }

        var settings = await settingsStore.GetAsync(cancellationToken);
        var fromReporterSide = ticket.IsVisibleTo(authorUserId);
        if (fromReporterSide && ticket.Status == TicketStatus.WaitingForReporter)
        {
            db.Add(TicketEvent.Create(ticket.Id, authorUserId, "status", nameof(TicketStatus.WaitingForReporter), nameof(TicketStatus.InProgress), now));
            ChangeStatus(ticket, TicketStatus.InProgress, null, authorUserId, settings, now);
        }
        else if (isAgent && !isInternal && !fromReporterSide)
        {
            ticket.MarkFirstResponse(now);
        }

        ticket.Touch(now);
        return null;
    }

    /// <summary>
    /// Duplikat zusammenführen: Die Quelle wird mit Hinweis geschlossen, ihr Melder beobachtet das Ziel und erhält dort die
    /// Antworten. Beschreibung und Anhänge der Quelle landen als interne Notiz im Ziel. Speichert.
    /// </summary>
    public async Task<string?> MergeAsync(Ticket source, Ticket target, Guid actorUserId, CancellationToken cancellationToken)
    {
        if (source.Id == target.Id)
        {
            return "Ein Ticket kann nicht mit sich selbst zusammengeführt werden.";
        }

        if (source.MergedIntoTicketId is not null)
        {
            return $"Ticket {source.Number} wurde bereits zusammengeführt.";
        }

        if (target.MergedIntoTicketId is not null || target.Status == TicketStatus.Closed)
        {
            return $"Ticket {target.Number} ist geschlossen. Bitte ein offenes Ticket als Ziel wählen.";
        }

        var now = clock.GetUtcNow();
        var settings = await settingsStore.GetAsync(cancellationToken);
        db.Add(TicketEvent.Create(source.Id, actorUserId, "status", source.Status.ToString(), nameof(TicketStatus.Closed), now));
        ChangeStatus(source, TicketStatus.Closed, $"Doppelt gemeldet. Die Bearbeitung läuft in Ticket {target.Number} weiter.", actorUserId, settings, now);
        source.MergeInto(target.Id);
        db.Add(TicketEvent.Create(source.Id, actorUserId, "merged", null, target.Number, now));
        db.Add(TicketEvent.Create(target.Id, actorUserId, "mergedFrom", source.Number, null, now));
        foreach (var watcher in new[] { source.ReporterUserId }.OfType<Guid>().Concat(source.WatcherUserIds))
        {
            if (target.AddWatcher(watcher))
            {
                db.Add(TicketEvent.Create(target.Id, actorUserId, "watcher", null, watcher.ToString(), now));
            }
        }

        var reporter = source.ReporterUserId is { } reporterId
            ? await db.Set<User>().Where(u => u.Id == reporterId).Select(u => u.DisplayName).SingleOrDefaultAsync(cancellationToken)
            : source.ReporterName;
        var note = $"Aus Ticket {source.Number} übernommen (gemeldet von {reporter ?? "unbekannt"}): {source.Title}"
            + (source.Description is { Length: > 0 } d ? $"\n\n{d}" : string.Empty);
        db.Add(TicketComment.Create(target.Id, actorUserId, note.Length > TicketComment.MaxTextLength ? note[..TicketComment.MaxTextLength] : note,
            isInternal: true, now, source.AttachmentFileIds.Take(20).ToList()));
        if (!await LinkedAsync(source.Id, target.Id, cancellationToken))
        {
            db.Add(TicketLink.Create(source.Id, target.Id, TicketLink.Duplicate, actorUserId, now));
        }

        target.Touch(now);
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    public Task<bool> LinkedAsync(Guid a, Guid b, CancellationToken cancellationToken) =>
        db.Set<TicketLink>().AnyAsync(l => (l.TicketId == a && l.OtherTicketId == b) || (l.TicketId == b && l.OtherTicketId == a), cancellationToken);

    public async Task<IReadOnlyList<TicketSummary>> SummariesAsync(IQueryable<Ticket> query, CancellationToken cancellationToken,
        Func<IReadOnlyCollection<Guid>, Task<Dictionary<Guid, DateTimeOffset>>>? unread = null)
    {
        var tickets = await query.AsNoTracking().ToListAsync(cancellationToken);
        var lookups = await LookupsAsync(tickets.SelectMany(t => new[] { t.ReporterUserId, t.AssigneeUserId }), cancellationToken);
        var news = unread is null || tickets.Count == 0 ? [] : await unread(tickets.Select(t => t.Id).ToList());
        var now = clock.GetUtcNow();
        return tickets.Select(t => Summarize(t, lookups, now) with { HasUnread = news.ContainsKey(t.Id) }).ToList();
    }

    public async Task<TicketDetail> DetailAsync(Ticket ticket, bool includeInternal, CancellationToken cancellationToken, Guid? viewerUserId = null)
    {
        var comments = await db.Set<TicketComment>().AsNoTracking()
            .Where(c => c.TicketId == ticket.Id && (includeInternal || !c.IsInternal))
            .OrderBy(c => c.CreatedAt).ToListAsync(cancellationToken);
        var events = await db.Set<TicketEvent>().AsNoTracking().Where(e => e.TicketId == ticket.Id && (includeInternal || !InternalKinds.Contains(e.Kind)))
            .OrderBy(e => e.At).ToListAsync(cancellationToken);
        var personIds = events.Where(e => e.Kind is "assignee" or "watcher").SelectMany(e => new[] { e.FromValue, e.ToValue })
            .Select(v => Guid.TryParse(v, out var g) ? g : (Guid?)null);
        var lookups = await LookupsAsync(new[] { ticket.ReporterUserId, ticket.AssigneeUserId }
            .Concat(comments.Select(c => (Guid?)c.AuthorUserId)).Concat(events.Select(e => e.ActorUserId)).Concat(personIds)
            .Concat(ticket.WatcherUserIds.Select(w => (Guid?)w)), cancellationToken);
        var names = lookups.Names;
        var deviceName = ticket.DeviceId is { } deviceId
            ? await db.Database.SqlQuery<string>($"""SELECT name AS "Value" FROM devices WHERE id = {deviceId}""").SingleOrDefaultAsync(cancellationToken)
            : null;

        var linkRows = await db.Set<TicketLink>().AsNoTracking().Where(l => l.TicketId == ticket.Id || l.OtherTicketId == ticket.Id).ToListAsync(cancellationToken);
        var otherIds = linkRows.Select(l => l.TicketId == ticket.Id ? l.OtherTicketId : l.TicketId)
            .Concat(ticket.MergedIntoTicketId is { } merged ? [merged] : []).Distinct().ToList();
        var others = await db.Set<Ticket>().AsNoTracking().Where(t => otherIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Number, t.Title, t.Status }).ToDictionaryAsync(t => t.Id, cancellationToken);
        var links = includeInternal
            ? linkRows.Select(l => (Link: l, Other: l.TicketId == ticket.Id ? l.OtherTicketId : l.TicketId))
                .Where(x => others.ContainsKey(x.Other))
                .Select(x => new TicketLinkDto(x.Other, others[x.Other].Number, others[x.Other].Title, others[x.Other].Status, x.Link.Kind)).ToList()
            : [];
        var articles = ticket.ArticleIds.Count == 0
            ? []
            : await db.Set<KnowledgeArticle>().AsNoTracking().Where(a => ticket.ArticleIds.Contains(a.Id) && (includeInternal || a.IsPublished))
                .Select(a => new TicketArticleRef(a.Id, a.Title, a.IsPublished)).ToListAsync(cancellationToken);
        int? minutes = includeInternal
            ? await db.Set<TicketTimeEntry>().Where(t => t.TicketId == ticket.Id).SumAsync(t => (int?)t.Minutes, cancellationToken) ?? 0
            : null;
        var settings = await settingsStore.GetAsync(cancellationToken);
        var canRate = settings.RatingEnabled && viewerUserId is { } viewer && viewer == ticket.ReporterUserId && ticket.MergedIntoTicketId is null
            && ticket.Status is TicketStatus.Resolved or TicketStatus.Closed;

        return new TicketDetail(Summarize(ticket, lookups, clock.GetUtcNow()), ticket.Description, ticket.DeviceId, deviceName, ticket.BauakteId, ticket.Resolution,
            ticket.ResolvedAt, ticket.ClosedAt, TicketDiagnostics.Parse(ticket.Diagnostics), ticket.AttachmentFileIds,
            comments.Select(c => new TicketCommentDto(c.Id, c.AuthorUserId, names.GetValueOrDefault(c.AuthorUserId), c.Text, c.IsInternal, c.CreatedAt,
                c.AttachmentFileIds)).ToList(),
            events.Select(e => new TicketEventDto(e.ActorUserId, e.ActorUserId is { } a ? names.GetValueOrDefault(a) : null, e.Kind, e.FromValue, e.ToValue, e.At)).ToList(),
            Timeline(ticket, comments, events, lookups), links, articles,
            ticket.MergedIntoTicketId is { } into && others.TryGetValue(into, out var target) ? target.Number : null,
            ticket.Rating, ticket.RatingComment, ticket.RatedAt, canRate, minutes,
            includeInternal ? ticket.WatcherUserIds.Select(w => names.GetValueOrDefault(w) ?? "Unbekannt").ToList() : null);
    }

    private static List<TicketTimelineItem> Timeline(Ticket ticket, List<TicketComment> comments, List<TicketEvent> events, Lookups lookups)
    {
        var names = lookups.Names;
        string? Name(Guid? id) => id is { } value ? names.GetValueOrDefault(value) : null;
        string Person(string? value) => Guid.TryParse(value, out var id) ? names.GetValueOrDefault(id) ?? "Unbekannt" : "niemand";
        string Group(string? value) => Guid.TryParse(value, out var id) ? lookups.Groups.GetValueOrDefault(id) ?? "gelöschte Gruppe" : "keine";
        string Sub(string? value) => Guid.TryParse(value, out var id) ? lookups.Subcategories.GetValueOrDefault(id) ?? "gelöscht" : "keine";
        static string Text(Dictionary<string, string> map, string? value) => value is null ? "–" : map.GetValueOrDefault(value) ?? value;

        var items = new List<TicketTimelineItem>();
        foreach (var e in events)
        {
            var actor = Name(e.ActorUserId) ?? (e.Kind == "created" ? ticket.ReporterName ?? "Gerät" : "System");
            var isReporter = e.ActorUserId is { } a && a == ticket.ReporterUserId;
            var internalKind = InternalKinds.Contains(e.Kind);
            TicketTimelineItem Item(string summary, string? text = null) =>
                new(e.Id, e.At, e.Kind, e.ActorUserId, actor, summary, text, e.FromValue, e.ToValue, internalKind, isReporter, []);
            var item = e.Kind switch
            {
                "created" => new TicketTimelineItem(e.Id, e.At, "created", e.ActorUserId, actor, $"{actor} hat das Problem gemeldet", ticket.Description, null,
                    e.ToValue, false, true, ticket.AttachmentFileIds),
                "status" => Item($"Status: {Text(StatusText, e.FromValue)} → {Text(StatusText, e.ToValue)}",
                    e.ToValue is nameof(TicketStatus.Resolved) or nameof(TicketStatus.Closed) ? ticket.Resolution : null),
                "assignee" => Item(e.ToValue is null ? "Zuweisung aufgehoben" : $"Zugewiesen an {Person(e.ToValue)}"),
                "priority" => Item($"Priorität: {Text(PriorityText, e.FromValue)} → {Text(PriorityText, e.ToValue)}"),
                "category" => Item($"Kategorie: {Text(CategoryText, e.FromValue)} → {Text(CategoryText, e.ToValue)}"),
                "subcategory" => Item($"Unterkategorie: {Sub(e.FromValue)} → {Sub(e.ToValue)}"),
                "group" => Item($"Gruppe: {Group(e.FromValue)} → {Group(e.ToValue)}"),
                "rule" => Item($"Automatisch zugeordnet (Regel „{e.ToValue}“)"),
                "merged" => Item($"Mit Ticket {e.ToValue} zusammengeführt"),
                "mergedFrom" => Item($"Ticket {e.FromValue} wurde hierher zusammengeführt"),
                "link" => Item($"Verknüpft mit Ticket {e.ToValue}"),
                "unlink" => Item($"Verknüpfung zu Ticket {e.ToValue} entfernt"),
                "article" => Item($"Anleitung verknüpft: {e.ToValue}"),
                "rating" => Item($"Bewertung: {e.ToValue} von 5 Sternen", ticket.RatingComment),
                "time" => Item($"Arbeitszeit erfasst: {Duration(e.ToValue)}"),
                "watcher" => Item($"{Person(e.ToValue)} verfolgt das Ticket"),
                _ => Item(e.Kind),
            };
            items.Add(item);
        }

        foreach (var c in comments)
        {
            var author = Name(c.AuthorUserId) ?? "Unbekannt";
            items.Add(new TicketTimelineItem(c.Id, c.CreatedAt, c.IsInternal ? "note" : "comment", c.AuthorUserId, author,
                c.IsInternal ? $"{author} hat eine interne Notiz geschrieben" : $"{author} hat geantwortet", c.Text, null, null, c.IsInternal,
                c.AuthorUserId == ticket.ReporterUserId, c.AttachmentFileIds, c.MentionUserIds));
        }

        return items.OrderBy(i => i.At).ThenBy(i => i.Kind == "created" ? 0 : 1).ToList();
    }

    private static string Duration(string? minutes) =>
        int.TryParse(minutes, CultureInfo.InvariantCulture, out var m) ? m >= 60 ? $"{m / 60} h {m % 60:00} min" : $"{m} min" : "–";

    public static string? Clip(string? value, int max = 100) => value is null ? null : value.Length > max ? value[..max] : value;

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

    /// <summary>Schließt gelöste Tickets nach der eingestellten Frist (Standard 7 Tage) ohne Widerspruch.</summary>
    public async Task<int> CloseResolvedAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var settings = await settingsStore.GetAsync(cancellationToken);
        var due = now - TimeSpan.FromDays(settings.AutoCloseDays);
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

    /// <summary>
    /// SLA nachziehen: Tickets ohne Ziele (vor der Einführung angelegt) erhalten Ziele; überschrittene Ziele werden der
    /// zuständigen Person, sonst der Gruppe bzw. allen Bearbeitern einmal gemeldet.
    /// </summary>
    public async Task<int> CheckSlaAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        if (!settings.SlaEnabled)
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var missing = await db.Set<Ticket>()
            .Where(t => t.ResponseDueAt == null && t.ResolutionDueAt == null && t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .Take(500).ToListAsync(cancellationToken);
        foreach (var ticket in missing)
        {
            var (response, resolution) = SlaCalculator.DueDates(ticket.CreatedAt, ticket.Priority, ticket.SlaPausedMinutes, settings);
            ticket.SetDueDates(response, resolution);
            if (resolution < now || response < now)
            {
                // Altbestand: nicht nachträglich als „überschritten“ melden.
                ticket.MarkOverdueNotified(now);
            }
        }

        var overdue = await db.Set<Ticket>()
            .Where(t => t.OverdueNotifiedAt == null && (t.Status == TicketStatus.New || t.Status == TicketStatus.InProgress)
                && ((t.FirstResponseAt == null && t.ResponseDueAt < now) || t.ResolutionDueAt < now))
            .Take(200).ToListAsync(cancellationToken);
        var agents = overdue.Count == 0 ? [] : await roles.GetUserIdsWithPermissionAsync(Permissions.HelpdeskAgent, cancellationToken);
        var groups = overdue.Count == 0 ? [] : await db.Set<HelpdeskGroup>().AsNoTracking().ToDictionaryAsync(g => g.Id, cancellationToken);
        var notices = new List<(Ticket Ticket, IReadOnlyCollection<Guid> Users, string Kind)>();
        foreach (var ticket in overdue)
        {
            ticket.MarkOverdueNotified(now);
            IReadOnlyCollection<Guid> users = ticket.AssigneeUserId is { } a
                ? [a]
                : ticket.GroupId is { } g && groups.TryGetValue(g, out var group) && group.MemberUserIds.Any(agents.Contains)
                    ? group.MemberUserIds.Where(agents.Contains).ToList()
                    : agents;
            notices.Add((ticket, users, ticket.FirstResponseAt is null && ticket.ResponseDueAt < now ? "response" : "resolution"));
        }

        if (missing.Count == 0 && overdue.Count == 0)
        {
            return 0;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return 0;
        }

        foreach (var (ticket, users, kind) in notices)
        {
            await notifier.NotifyOverdueAsync(ticket, users, kind, cancellationToken);
        }

        return overdue.Count;
    }

    private sealed record Lookups(Dictionary<Guid, string> Names, Dictionary<Guid, string> Groups, Dictionary<Guid, string> Subcategories);

    private async Task<Lookups> LookupsAsync(IEnumerable<Guid?> userIds, CancellationToken cancellationToken)
    {
        var wanted = userIds.OfType<Guid>().Distinct().ToList();
        var names = wanted.Count == 0
            ? []
            : await db.Set<User>().AsNoTracking().Where(u => wanted.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var groups = await db.Set<HelpdeskGroup>().AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Name, cancellationToken);
        var subcategories = await db.Set<TicketSubcategory>().AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);
        return new Lookups(names, groups, subcategories);
    }

    private static TicketSummary Summarize(Ticket t, Lookups lookups, DateTimeOffset now)
    {
        var names = lookups.Names;
        var sla = SlaCalculator.State(t, now);
        return new TicketSummary(
            t.Id, t.Number, t.Title, t.Category, t.Priority, t.Status, t.ReporterBlocked, t.ReporterUserId,
            t.ReporterUserId is { } reporter ? names.GetValueOrDefault(reporter) : t.ReporterName is null ? null : $"{t.ReporterName} (laut Eingabe)",
            t.AssigneeUserId, t.AssigneeUserId is { } assignee ? names.GetValueOrDefault(assignee) : null, t.Source, t.CreatedAt, t.LastActivityAt, t.Version,
            GroupId: t.GroupId, GroupName: t.GroupId is { } g ? lookups.Groups.GetValueOrDefault(g) : null,
            SubcategoryId: t.SubcategoryId, SubcategoryName: t.SubcategoryId is { } s ? lookups.Subcategories.GetValueOrDefault(s) : null,
            FirstResponseAt: t.FirstResponseAt, ResponseDueAt: t.ResponseDueAt, ResolutionDueAt: t.ResolutionDueAt, SlaState: sla.State, DueAt: sla.DueAt,
            DueKind: sla.DueKind, MergedIntoTicketId: t.MergedIntoTicketId, Rating: t.Rating, ResolvedAt: t.ResolvedAt);
    }
}

/// <summary>Stündlich: gelöste Tickets schließen, alle 5 Minuten: SLA prüfen (überschrittene Ziele melden).</summary>
internal sealed partial class TicketMaintenance(IServiceScopeFactory scopes, TimeProvider clock, ILogger<TicketMaintenance> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5), clock);
        var lastClose = DateTimeOffset.MinValue;
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var tickets = scope.ServiceProvider.GetRequiredService<TicketService>();
                if (clock.GetUtcNow() - lastClose >= TimeSpan.FromHours(1))
                {
                    lastClose = clock.GetUtcNow();
                    var closed = await tickets.CloseResolvedAsync(stoppingToken);
                    if (closed > 0)
                    {
                        LogClosed(logger, closed);
                    }
                }

                await tickets.CheckSlaAsync(stoppingToken);
            }
            catch (Npgsql.PostgresException exception) when (exception.SqlState == Npgsql.PostgresErrorCodes.UndefinedTable
                || exception.SqlState == Npgsql.PostgresErrorCodes.UndefinedColumn)
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Wartung der Tickets fehlgeschlagen")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
