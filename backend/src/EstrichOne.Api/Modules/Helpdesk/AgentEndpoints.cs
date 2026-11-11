using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.AppUpdates.Exports;
using EstrichOne.Api.Modules.Devices.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Helpdesk;

/// <summary>Sammelbearbeitung: gleiche Änderung für mehrere Tickets.</summary>
public sealed record BulkTicketRequest(
    [property: Required, MaxLength(200)] IReadOnlyList<Guid> Ids,
    TicketStatus? Status = null,
    TicketPriority? Priority = null,
    Guid? AssigneeUserId = null,
    bool Unassign = false,
    Guid? GroupId = null,
    bool ClearGroup = false,
    [property: MaxLength(Ticket.MaxResolutionLength)] string? Resolution = null);

public sealed record BulkTicketFailure(Guid Id, string? Number, string Message);

public sealed record BulkTicketResponse(int Updated, IReadOnlyList<BulkTicketFailure> Failed);

public sealed record MergeTicketRequest([property: Required] Guid TargetTicketId);

public sealed record LinkTicketRequest([property: Required] Guid OtherTicketId, [property: MaxLength(20)] string? Kind = null);

public sealed record AddTimeEntryRequest([property: Range(1, 1440)] int Minutes, DateOnly? WorkDate = null, [property: MaxLength(500)] string? Note = null);

public sealed record TicketTimeEntryDto(Guid Id, Guid UserId, string? UserName, int Minutes, DateOnly WorkDate, string? Note, DateTimeOffset CreatedAt);

public sealed record LinkArticleRequest([property: Required] Guid ArticleId);

public sealed record RateTicketRequest([property: Range(1, 5)] int Rating, [property: MaxLength(2000)] string? Comment = null);

/// <summary>Ticket im Namen eines Melders anlegen (IT nach Anruf, E-Mail oder Besuch).</summary>
/// <param name="ReporterUserId">Melder (Benutzer); alternativ <paramref name="ReporterName"/> für Personen ohne Zugang.</param>
/// <param name="Channel">Telefon, E-Mail oder vor Ort.</param>
/// <param name="AssigneeUserId">Gleich zuweisen (an sich selbst: Status „In Bearbeitung“); sonst gelten die Zuweisungsregeln.</param>
/// <param name="AttachmentFileIds">Eigene, vorher hochgeladene Dateien der erfassenden Person (z. B. Bildschirmfoto aus der E-Mail).</param>
public sealed record CreateTicketOnBehalfRequest(
    [property: Required] Guid Id,
    [property: Required, MaxLength(Ticket.MaxTitleLength)] string Title,
    [property: MaxLength(Ticket.MaxDescriptionLength)] string? Description,
    TicketCategory Category,
    TicketChannel Channel,
    TicketSource Source,
    Guid? ReporterUserId = null,
    [property: MaxLength(200)] string? ReporterName = null,
    bool ReporterBlocked = false,
    TicketPriority? Priority = null,
    Guid? SubcategoryId = null,
    Guid? AssigneeUserId = null,
    Guid? GroupId = null,
    IReadOnlyList<Guid>? AttachmentFileIds = null);

public sealed record CategoryCount(TicketCategory Category, int Open, int Total30Days);

public sealed record AgentLoad(Guid UserId, string Name, int Open, int Overdue);

public sealed record DayCount(DateOnly Day, int Created, int Resolved);

/// <summary>Kennzahlen für die Übersicht der Bearbeitung.</summary>
/// <param name="AvgFirstResponseMinutes">Mittlere Zeit bis zur ersten Antwort in Geschäftszeit, wie die SLA-Uhr sie zählt (ohne Nächte,
/// Wochenenden, Feiertage; „rund um die Uhr“-Prioritäten durchgehend), Tickets der letzten 30 Tage.</param>
/// <param name="AvgResolutionHours">Mittlere Lösungszeit in Geschäftszeit ohne angehaltene Zeit („Rückfrage“), letzte 30 Tage.</param>
/// <param name="SlaMetPercent">Anteil der in den letzten 30 Tagen gelösten Tickets innerhalb des Lösungsziels.</param>
/// <param name="TimeBasis">„business“: Reaktions- und Lösungszeit in Geschäftszeit laut Helpdesk-Einstellungen.</param>
public sealed record HelpdeskStats(
    int Open, int Unassigned, int Overdue, int Waiting, int Mine, int Blocked, int CreatedToday, int CreatedThisWeek, int ResolvedThisWeek,
    double? AvgFirstResponseMinutes, double? AvgResolutionHours, double? SlaMetPercent, double? SatisfactionAverage, int Ratings,
    IReadOnlyList<CategoryCount> ByCategory, IReadOnlyList<AgentLoad> ByAgent, IReadOnlyList<DayCount> Trend, string TimeBasis = "business");

/// <param name="UpdateState">current, recommended, required, newerThanServer (siehe App-Updates) oder null.</param>
public sealed record ReporterDevice(
    Guid Id, string Name, string Platform, string Kind, string Status, string? Model, string? OsVersion, string? AppVersion, string? UpdateState,
    string? LatestVersion, DateTimeOffset? LastSeenAt, bool IsTicketDevice);

public sealed record ReporterInfo(Guid Id, string DisplayName, string UserName, string? Email, string? Phone, bool IsActive, DateTimeOffset? LastLoginAt);

/// <summary>Umfeld eines Tickets für die Bearbeitung: Melder, seine Geräte (App-Version, letzter Kontakt), weitere Tickets.</summary>
public sealed record TicketContext(
    ReporterInfo? Reporter, IReadOnlyList<ReporterDevice> Devices, IReadOnlyList<TicketSummary> OtherTickets, IReadOnlyList<KnowledgeArticleSummary> SuggestedArticles);

/// <summary>Arbeitsplatz der Bearbeitung unter /tickets: Kennzahlen, Sammelbearbeitung, Zusammenführen, Verknüpfen, Zeiten, Umfeld, Bewertung.</summary>
internal static class AgentEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/stats", StatsAsync).WithName("GetHelpdeskStats").WithSummary("Kennzahlen der Bearbeitung (offen, überfällig, Reaktionszeit, Zufriedenheit)")
            .RequirePermission(Permissions.HelpdeskAgent);
        group.MapPost("/bulk", BulkAsync).WithName("BulkUpdateTickets").WithSummary("Mehrere Tickets zuweisen, Status oder Priorität setzen")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity).RequirePermission(Permissions.HelpdeskAgent);
        group.MapPost("/{id:guid}/merge", MergeAsync).WithName("MergeTicket").WithSummary("Duplikat in ein anderes Ticket zusammenführen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).RequirePermission(Permissions.HelpdeskAgent);
        group.MapPost("/{id:guid}/links", LinkAsync).WithName("LinkTicket").WithSummary("Ticket mit einem anderen verknüpfen")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HelpdeskAgent);
        group.MapDelete("/{id:guid}/links/{otherId:guid}", UnlinkAsync).WithName("UnlinkTicket").WithSummary("Verknüpfung entfernen")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HelpdeskAgent);
        group.MapGet("/{id:guid}/time", TimeAsync).WithName("ListTicketTime").WithSummary("Erfasste Arbeitszeit")
            .RequirePermission(Permissions.HelpdeskAgent);
        group.MapPost("/{id:guid}/time", AddTimeAsync).WithName("AddTicketTime").WithSummary("Arbeitszeit erfassen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).RequirePermission(Permissions.HelpdeskAgent);
        group.MapDelete("/{id:guid}/time/{entryId:guid}", DeleteTimeAsync).WithName("DeleteTicketTime").WithSummary("Eigene Zeitbuchung löschen")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HelpdeskAgent);
        group.MapGet("/{id:guid}/context", ContextAsync).WithName("GetTicketContext")
            .WithSummary("Melder, seine Geräte (App-Version, Update-Stand, letzter Kontakt), weitere Tickets, passende Artikel")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HelpdeskAgent);
        group.MapPost("/{id:guid}/articles", LinkArticleAsync).WithName("LinkTicketArticle").WithSummary("Artikel der Wissensdatenbank verknüpfen")
            .ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HelpdeskAgent);
        group.MapDelete("/{id:guid}/articles/{articleId:guid}", UnlinkArticleAsync).WithName("UnlinkTicketArticle")
            .WithSummary("Artikel-Verknüpfung entfernen").ProducesProblem(StatusCodes.Status404NotFound).RequirePermission(Permissions.HelpdeskAgent);
        group.MapPost("/{id:guid}/rating", RateAsync).WithName("RateTicket").WithSummary("Zufriedenheit nach der Lösung (Melder, 1–5 Sterne)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).AllowPendingDevice();
        group.MapPost("/on-behalf", CreateOnBehalfAsync).WithName("CreateTicketOnBehalf")
            .WithSummary("Ticket im Namen eines Melders anlegen (Telefon, E-Mail, vor Ort)")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.HelpdeskAgent);
        group.MapGet("/page", TicketEndpoints.PageAsync).WithName("ListTicketPage")
            .WithSummary("Arbeitsliste seitenweise (offset, limit bis 500) mit Gesamtzahl").RequirePermission(Permissions.HelpdeskAgent);
    }

    private static async Task<Results<Created<TicketDetail>, Ok<TicketDetail>, ProblemHttpResult>> CreateOnBehalfAsync(
        CreateTicketOnBehalfRequest request, TicketService tickets, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        if (request.Channel == TicketChannel.SelfService)
        {
            return TicketEndpoints.Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Ungültige Angaben",
                "Bitte den Eingangskanal wählen (Telefon, E-Mail oder vor Ort).");
        }

        if (request.ReporterUserId is null && string.IsNullOrWhiteSpace(request.ReporterName))
        {
            return TicketEndpoints.Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Ungültige Angaben",
                "Bitte den Melder auswählen oder seinen Namen eintragen.");
        }

        var input = new CreateTicketInput(request.Id, request.Title, request.Description, request.Category, request.Source, request.ReporterBlocked, null,
            null, request.AttachmentFileIds, request.Priority, request.SubcategoryId);
        var intake = new TicketIntake(request.Channel, currentUser.UserId, request.AssigneeUserId, request.GroupId);
        var (ticket, created, failure, message) = await tickets.CreateAsync(input, request.ReporterUserId,
            request.ReporterUserId is null ? request.ReporterName : null, null, cancellationToken, intake);
        if (ticket is null)
        {
            return failure == TicketFailure.IdConflict
                ? TicketEndpoints.Problem(StatusCodes.Status409Conflict, "ID_CONFLICT", "ID bereits vergeben", message!)
                : TicketEndpoints.Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Ungültige Angaben", message!);
        }

        var detail = await tickets.DetailAsync(ticket, includeInternal: true, cancellationToken, currentUser.UserId);
        return created ? TypedResults.Created($"/api/v1/tickets/{ticket.Id}", detail) : TypedResults.Ok(detail);
    }

    private static DateTimeOffset StartOfWeek(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, SlaCalculator.Berlin);
        var monday = DateOnly.FromDateTime(local.Date).AddDays(-(((int)local.DayOfWeek + 6) % 7));
        var start = monday.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(start, SlaCalculator.Berlin.GetUtcOffset(start)).ToUniversalTime();
    }

    private static async Task<Ok<HelpdeskStats>> StatsAsync(EstrichOneDbContext db, ICurrentUser currentUser, TimeProvider clock,
        HelpdeskSettingsStore settingsStore, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.GetAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var since = now.AddDays(-30);
        var week = StartOfWeek(now);
        var todayLocal = TimeZoneInfo.ConvertTime(now, SlaCalculator.Berlin).Date;
        var today = new DateTimeOffset(todayLocal, SlaCalculator.Berlin.GetUtcOffset(todayLocal)).ToUniversalTime();
        var open = await db.Set<Ticket>().AsNoTracking().Where(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed)
            .Select(t => new
            {
                t.Category, t.AssigneeUserId, t.Status, t.ReporterBlocked,
                Overdue = (t.Status == TicketStatus.New || t.Status == TicketStatus.InProgress)
                    && ((t.FirstResponseAt == null && t.ResponseDueAt < now) || t.ResolutionDueAt < now),
            }).ToListAsync(cancellationToken);
        var recent = await db.Set<Ticket>().AsNoTracking().Where(t => t.CreatedAt >= since || t.ResolvedAt >= since)
            .Select(t => new { t.Category, t.Priority, t.CreatedAt, t.FirstResponseAt, t.ResolvedAt, t.ResolutionDueAt, t.Rating, t.MergedIntoTicketId, t.SlaPausedMinutes })
            .ToListAsync(cancellationToken);

        var created = recent.Where(t => t.CreatedAt >= since).ToList();
        // Reaktions- und Lösungszeit so, wie die SLA-Uhr zählt: Geschäftszeit, Rückfrage herausgerechnet.
        var responded = created.Where(t => t.FirstResponseAt is not null)
            .Select(t => SlaCalculator.Elapsed(t.CreatedAt, t.FirstResponseAt!.Value, t.Priority, settings).TotalMinutes).ToList();
        var resolved = recent.Where(t => t.ResolvedAt >= since && t.MergedIntoTicketId is null).ToList();
        var withTarget = resolved.Where(t => t.ResolutionDueAt is not null).ToList();
        var ratings = recent.Where(t => t.Rating is not null).Select(t => (double)t.Rating!.Value).ToList();

        var agentIds = open.Where(t => t.AssigneeUserId is not null).Select(t => t.AssigneeUserId!.Value).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => agentIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var trend = Enumerable.Range(0, 14).Select(i => DateOnly.FromDateTime(todayLocal).AddDays(i - 13)).Select(day => new DayCount(day,
            recent.Count(t => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(t.CreatedAt, SlaCalculator.Berlin).Date) == day),
            recent.Count(t => t.ResolvedAt is { } r && t.MergedIntoTicketId is null
                && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r, SlaCalculator.Berlin).Date) == day))).ToList();

        return TypedResults.Ok(new HelpdeskStats(
            open.Count, open.Count(t => t.AssigneeUserId is null), open.Count(t => t.Overdue), open.Count(t => t.Status == TicketStatus.WaitingForReporter),
            open.Count(t => t.AssigneeUserId == currentUser.UserId), open.Count(t => t.ReporterBlocked),
            created.Count(t => t.CreatedAt >= today), created.Count(t => t.CreatedAt >= week), resolved.Count(t => t.ResolvedAt >= week),
            responded.Count > 0 ? Math.Round(responded.Average(), 1) : null,
            resolved.Count > 0
                ? Math.Round(resolved.Average(t => Math.Max(0, (SlaCalculator.Elapsed(t.CreatedAt, t.ResolvedAt!.Value, t.Priority, settings)
                    - TimeSpan.FromMinutes(t.SlaPausedMinutes)).TotalHours)), 1)
                : null,
            withTarget.Count > 0 ? Math.Round(100.0 * withTarget.Count(t => t.ResolvedAt <= t.ResolutionDueAt) / withTarget.Count, 1) : null,
            ratings.Count > 0 ? Math.Round(ratings.Average(), 2) : null, ratings.Count,
            Enum.GetValues<TicketCategory>().Select(c => new CategoryCount(c, open.Count(t => t.Category == c), created.Count(t => t.Category == c)))
                .Where(c => c.Open > 0 || c.Total30Days > 0).OrderByDescending(c => c.Open).ThenByDescending(c => c.Total30Days).ToList(),
            agentIds.Select(id => new AgentLoad(id, names.GetValueOrDefault(id) ?? "Unbekannt", open.Count(t => t.AssigneeUserId == id),
                open.Count(t => t.AssigneeUserId == id && t.Overdue))).OrderByDescending(a => a.Open).ToList(),
            trend));
    }

    private static async Task<Results<Ok<BulkTicketResponse>, ProblemHttpResult>> BulkAsync(
        BulkTicketRequest request, TicketService tickets, TicketNotifier notifier, EstrichOneDbContext db, ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var ids = request.Ids.Distinct().ToList();
        if (ids.Count is 0 or > 200)
        {
            return TicketEndpoints.Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Ungültige Angaben",
                "Bitte zwischen 1 und 200 Tickets auswählen.");
        }

        var change = new TicketChange(request.Status, request.Priority, null, request.AssigneeUserId, request.Unassign, request.Resolution,
            request.GroupId, request.ClearGroup);
        var failed = new List<BulkTicketFailure>();
        var updated = 0;
        foreach (var id in ids)
        {
            // Je Ticket einzeln speichern: ein Fehler (z. B. fehlende Lösung) hält die anderen nicht auf.
            var ticket = await db.Set<Ticket>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
            if (ticket is null)
            {
                failed.Add(new BulkTicketFailure(id, null, "Ticket nicht gefunden."));
                continue;
            }

            var (effect, error) = await tickets.ApplyChangeAsync(ticket, change, currentUser.UserId, cancellationToken);
            if (effect is null)
            {
                db.ChangeTracker.Clear();
                failed.Add(new BulkTicketFailure(id, ticket.Number, error!));
                continue;
            }

            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
                failed.Add(new BulkTicketFailure(id, ticket.Number, "Das Ticket wurde gerade von jemand anderem geändert."));
                continue;
            }

            updated++;
            await TicketEndpoints.NotifyChangeAsync(ticket, effect, notifier, cancellationToken);
            db.ChangeTracker.Clear();
        }

        return TypedResults.Ok(new BulkTicketResponse(updated, failed));
    }

    private static async Task<Results<Ok<TicketDetail>, ProblemHttpResult>> MergeAsync(
        Guid id, MergeTicketRequest request, TicketService tickets, TicketNotifier notifier, EstrichOneDbContext db, ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var source = await db.Set<Ticket>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        var target = await db.Set<Ticket>().SingleOrDefaultAsync(t => t.Id == request.TargetTicketId, cancellationToken);
        if (source is null || target is null)
        {
            return TicketEndpoints.NotFound();
        }

        var error = await tickets.MergeAsync(source, target, currentUser.UserId, cancellationToken);
        if (error is not null)
        {
            return TicketEndpoints.Problem(StatusCodes.Status409Conflict, "TICKET_MERGE_CONFLICT", "Zusammenführen nicht möglich", error);
        }

        await notifier.NotifyAsync(source, source.Resolution, cancellationToken);
        return TypedResults.Ok(await tickets.DetailAsync(target, includeInternal: true, cancellationToken, currentUser.UserId));
    }

    private static async Task<Results<Ok<TicketDetail>, ProblemHttpResult>> LinkAsync(
        Guid id, LinkTicketRequest request, TicketService tickets, EstrichOneDbContext db, ICurrentUser currentUser, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var ticket = await db.Set<Ticket>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        var other = await db.Set<Ticket>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == request.OtherTicketId, cancellationToken);
        if (ticket is null || other is null)
        {
            return TicketEndpoints.NotFound();
        }

        if (ticket.Id == other.Id)
        {
            return TicketEndpoints.Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Ungültige Angaben",
                "Ein Ticket kann nicht mit sich selbst verknüpft werden.");
        }

        if (!await tickets.LinkedAsync(ticket.Id, other.Id, cancellationToken))
        {
            var now = clock.GetUtcNow();
            db.Add(TicketLink.Create(ticket.Id, other.Id, request.Kind ?? TicketLink.Related, currentUser.UserId, now));
            db.Add(TicketEvent.Create(ticket.Id, currentUser.UserId, "link", null, other.Number, now));
            db.Add(TicketEvent.Create(other.Id, currentUser.UserId, "link", null, ticket.Number, now));
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(await tickets.DetailAsync(ticket, includeInternal: true, cancellationToken, currentUser.UserId));
    }

    private static async Task<Results<Ok<TicketDetail>, ProblemHttpResult>> UnlinkAsync(
        Guid id, Guid otherId, TicketService tickets, EstrichOneDbContext db, ICurrentUser currentUser, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var ticket = await db.Set<Ticket>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        var link = await db.Set<TicketLink>().SingleOrDefaultAsync(l => (l.TicketId == id && l.OtherTicketId == otherId)
            || (l.TicketId == otherId && l.OtherTicketId == id), cancellationToken);
        if (ticket is null || link is null)
        {
            return TicketEndpoints.NotFound();
        }

        var otherNumber = await db.Set<Ticket>().Where(t => t.Id == otherId).Select(t => t.Number).SingleAsync(cancellationToken);
        var now = clock.GetUtcNow();
        db.Remove(link);
        db.Add(TicketEvent.Create(ticket.Id, currentUser.UserId, "unlink", null, otherNumber, now));
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await tickets.DetailAsync(ticket, includeInternal: true, cancellationToken, currentUser.UserId));
    }

    private static async Task<Ok<IReadOnlyList<TicketTimeEntryDto>>> TimeAsync(Guid id, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var entries = await db.Set<TicketTimeEntry>().AsNoTracking().Where(t => t.TicketId == id).OrderBy(t => t.WorkDate).ThenBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
        var userIds = entries.Select(e => e.UserId).Distinct().ToList();
        var names = await db.Set<User>().AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        return TypedResults.Ok<IReadOnlyList<TicketTimeEntryDto>>(entries
            .Select(e => new TicketTimeEntryDto(e.Id, e.UserId, names.GetValueOrDefault(e.UserId), e.Minutes, e.WorkDate, e.Note, e.CreatedAt)).ToList());
    }

    private static async Task<Results<Ok<TicketTimeEntryDto>, ProblemHttpResult>> AddTimeAsync(
        Guid id, AddTimeEntryRequest request, TicketService tickets, EstrichOneDbContext db, ICurrentUser currentUser, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        if (!(await tickets.SettingsAsync(cancellationToken)).TimeTrackingEnabled)
        {
            return TicketEndpoints.Problem(StatusCodes.Status409Conflict, "TIME_TRACKING_DISABLED", "Zeiterfassung ausgeschaltet",
                "Die Zeiterfassung ist in den Helpdesk-Einstellungen ausgeschaltet.");
        }

        var ticket = await db.Set<Ticket>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null)
        {
            return TicketEndpoints.NotFound();
        }

        var now = clock.GetUtcNow();
        TicketTimeEntry entry;
        try
        {
            entry = TicketTimeEntry.Create(id, currentUser.UserId, request.Minutes,
                request.WorkDate ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, SlaCalculator.Berlin).Date), request.Note, now);
        }
        catch (ArgumentException exception)
        {
            return TicketEndpoints.Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Ungültige Angaben", exception.Message);
        }

        db.Add(entry);
        db.Add(TicketEvent.Create(id, currentUser.UserId, "time", null, entry.Minutes.ToString(System.Globalization.CultureInfo.InvariantCulture), now));
        ticket.Touch(now);
        await db.SaveChangesAsync(cancellationToken);
        var name = await db.Set<User>().Where(u => u.Id == currentUser.UserId).Select(u => u.DisplayName).SingleOrDefaultAsync(cancellationToken);
        return TypedResults.Ok(new TicketTimeEntryDto(entry.Id, entry.UserId, name, entry.Minutes, entry.WorkDate, entry.Note, entry.CreatedAt));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteTimeAsync(
        Guid id, Guid entryId, EstrichOneDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var entry = await db.Set<TicketTimeEntry>().SingleOrDefaultAsync(t => t.Id == entryId && t.TicketId == id && t.UserId == currentUser.UserId,
            cancellationToken);
        if (entry is null)
        {
            return TicketEndpoints.Problem(StatusCodes.Status404NotFound, "TIME_ENTRY_NOT_FOUND", "Nicht gefunden",
                "Die Zeitbuchung existiert nicht oder gehört jemand anderem.");
        }

        db.Remove(entry);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<TicketContext>, ProblemHttpResult>> ContextAsync(
        Guid id, TicketService tickets, EstrichOneDbContext db, IAppUpdatePolicy appUpdates, CancellationToken cancellationToken)
    {
        var ticket = await db.Set<Ticket>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null)
        {
            return TicketEndpoints.NotFound();
        }

        var reporter = ticket.ReporterUserId is { } reporterId
            ? await db.Set<User>().AsNoTracking().Where(u => u.Id == reporterId)
                .Select(u => new ReporterInfo(u.Id, u.DisplayName, u.UserName, u.Email, u.Phone, u.IsActive, u.LastLoginAt)).SingleOrDefaultAsync(cancellationToken)
            : null;
        var devices = await db.Set<Device>().AsNoTracking()
            .Where(d => d.Id == ticket.DeviceId || (ticket.ReporterUserId != null && d.AssignedUserId == ticket.ReporterUserId && d.Status != DeviceStatus.Retired))
            .OrderByDescending(d => d.LastSeenAt).Take(10).ToListAsync(cancellationToken);
        var rows = new List<ReporterDevice>();
        foreach (var d in devices)
        {
            var platform = d.Platform switch { DevicePlatform.Ios => "ios", DevicePlatform.Android => "android", DevicePlatform.Windows => "windows", _ => "other" };
            var state = platform is "ios" or "android" ? await appUpdates.EvaluateAsync(platform, d.AppVersion, cancellationToken) : null;
            rows.Add(new ReporterDevice(d.Id, d.Name, platform, d.Kind.ToString().ToLowerInvariant(), d.Status.ToString().ToLowerInvariant(), d.Model,
                d.OsVersion, d.AppVersion, state?.State, state?.LatestVersion, d.LastSeenAt, d.Id == ticket.DeviceId));
        }

        var others = ticket.ReporterUserId is { } reporterUser
            ? await tickets.SummariesAsync(db.Set<Ticket>().Where(t => t.ReporterUserId == reporterUser && t.Id != ticket.Id)
                .OrderByDescending(t => t.CreatedAt).Take(8), cancellationToken)
            : [];
        var articles = await db.Set<KnowledgeArticle>().AsNoTracking().ToListAsync(cancellationToken);
        var suggested = KnowledgeSearch.Rank(articles, $"{ticket.Title} {ticket.Description}", ticket.Category, publishedOnly: false)
            .Where(a => !ticket.ArticleIds.Contains(a.Id)).Take(5).ToList();
        return TypedResults.Ok(new TicketContext(reporter, rows, others, suggested));
    }

    private static async Task<Results<Ok<TicketDetail>, ProblemHttpResult>> LinkArticleAsync(
        Guid id, LinkArticleRequest request, TicketService tickets, EstrichOneDbContext db, ICurrentUser currentUser, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var ticket = await db.Set<Ticket>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        var article = await db.Set<KnowledgeArticle>().AsNoTracking().SingleOrDefaultAsync(a => a.Id == request.ArticleId, cancellationToken);
        if (ticket is null || article is null)
        {
            return TicketEndpoints.NotFound();
        }

        if (ticket.LinkArticle(article.Id))
        {
            var now = clock.GetUtcNow();
            db.Add(TicketEvent.Create(ticket.Id, currentUser.UserId, "article", null, TicketService.Clip(article.Title), now));
            ticket.Touch(now);
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(await tickets.DetailAsync(ticket, includeInternal: true, cancellationToken, currentUser.UserId));
    }

    private static async Task<Results<Ok<TicketDetail>, ProblemHttpResult>> UnlinkArticleAsync(
        Guid id, Guid articleId, TicketService tickets, EstrichOneDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var ticket = await db.Set<Ticket>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null || !ticket.UnlinkArticle(articleId))
        {
            return TicketEndpoints.NotFound();
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await tickets.DetailAsync(ticket, includeInternal: true, cancellationToken, currentUser.UserId));
    }

    private static async Task<Results<Ok<TicketDetail>, ProblemHttpResult>> RateAsync(
        Guid id, RateTicketRequest request, TicketService tickets, EstrichOneDbContext db, ICurrentUser currentUser, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var ticket = await db.Set<Ticket>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null || ticket.ReporterUserId != currentUser.UserId)
        {
            // Bewerten darf nur der Melder; fremde Tickets bleiben unsichtbar.
            return TicketEndpoints.NotFound();
        }

        if (!(await tickets.SettingsAsync(cancellationToken)).RatingEnabled || ticket.Status is not (TicketStatus.Resolved or TicketStatus.Closed)
            || ticket.MergedIntoTicketId is not null)
        {
            return TicketEndpoints.Problem(StatusCodes.Status409Conflict, "TICKET_NOT_RATABLE", "Bewertung nicht möglich",
                "Bewerten lässt sich ein Ticket, sobald die IT es als gelöst markiert hat.");
        }

        var now = clock.GetUtcNow();
        try
        {
            ticket.Rate(request.Rating, request.Comment, now);
        }
        catch (ArgumentException exception)
        {
            return TicketEndpoints.Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Ungültige Angaben", exception.Message);
        }

        db.Add(TicketEvent.Create(ticket.Id, currentUser.UserId, "rating", null, request.Rating.ToString(System.Globalization.CultureInfo.InvariantCulture), now));
        ticket.Touch(now);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await tickets.DetailAsync(ticket, includeInternal: false, cancellationToken, currentUser.UserId));
    }
}
