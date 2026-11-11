using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Devices.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Helpdesk;

public sealed record CreateTicketRequest(
    [property: Required] Guid Id,
    [property: Required, MaxLength(Ticket.MaxTitleLength)] string Title,
    [property: MaxLength(Ticket.MaxDescriptionLength)] string? Description,
    TicketCategory Category,
    TicketSource Source,
    bool ReporterBlocked,
    Guid? BauakteId,
    JsonObject? Diagnostics,
    IReadOnlyList<Guid>? AttachmentFileIds,
    TicketPriority? Priority = null,
    Guid? SubcategoryId = null)
{
    internal CreateTicketInput ToInput() =>
        new(Id, Title, Description, Category, Source, ReporterBlocked, BauakteId, Diagnostics, AttachmentFileIds, Priority, SubcategoryId);
}

/// <param name="Timestamp">Zeitpunkt der Signatur (ISO 8601, wie gesendet); höchstens 10 Minuten Abweichung.</param>
/// <param name="Signature">Gerätesignatur (Base64, P-256) über <see cref="TicketEndpoints.DeviceProofMessage"/>.</param>
public sealed record DeviceTicketRequest(
    [property: Required] Guid DeviceId,
    [property: Required, MaxLength(40)] string Timestamp,
    [property: Required, MaxLength(200)] string Signature,
    [property: MaxLength(200)] string? ReporterName,
    [property: Required] CreateTicketRequest Ticket);

public sealed record DeviceTicketResponse(string Number);

public sealed record UpdateTicketRequest(
    TicketStatus? Status,
    TicketPriority? Priority,
    TicketCategory? Category,
    Guid? AssigneeUserId,
    bool Unassign,
    [property: MaxLength(Ticket.MaxResolutionLength)] string? Resolution,
    Guid? GroupId = null,
    bool ClearGroup = false,
    Guid? SubcategoryId = null,
    bool ClearSubcategory = false)
{
    internal TicketChange ToChange() => new(Status, Priority, Category, AssigneeUserId, Unassign, Resolution, GroupId, ClearGroup, SubcategoryId, ClearSubcategory);
}

/// <param name="AttachmentFileIds">Eigene, vorher hochgeladene Dateien ohne Bauakte (POST /files), z. B. Bildschirmfotos.</param>
/// <param name="MentionUserIds">Erwähnte Bearbeiter (@Name); nur die Bearbeitung kann erwähnen.</param>
public sealed record AddTicketCommentRequest(
    [property: MaxLength(TicketComment.MaxTextLength)] string? Text, bool IsInternal, IReadOnlyList<Guid>? AttachmentFileIds = null,
    IReadOnlyList<Guid>? MentionUserIds = null);

/// <summary>Gespeicherte Ansichten der Ticketliste.</summary>
public enum TicketFilter
{
    /// <summary>Alle offenen (nicht gelöst, nicht geschlossen).</summary>
    Open,

    /// <summary>Mir zugewiesen, nicht geschlossen.</summary>
    Mine,

    /// <summary>Niemandem zugewiesen, nicht geschlossen.</summary>
    Unassigned,
    All,

    /// <summary>Reaktions- oder Lösungsziel überschritten.</summary>
    Overdue,

    /// <summary>Wartet auf Rückmeldung des Melders.</summary>
    Waiting,

    /// <summary>Meiner Gruppe zugeordnet (offen).</summary>
    MyGroups,

    /// <summary>Gelöst oder geschlossen.</summary>
    Done,
}

internal static class TicketEndpoints
{
    public const string DeviceRateLimitPolicy = "ticket-device";
    public const string DeviceProofPrefix = "EstrichONE ticket v1\n";

    /// <summary>
    /// Signierte Nachricht für Meldungen ohne Anmeldung: Präfix, Geräte-ID, Zeitpunkt, Ticket-ID und SHA-256 über
    /// Titel und Beschreibung (Hex), je durch Zeilenumbruch getrennt.
    /// </summary>
    public static string DeviceProofMessage(Guid deviceId, string timestamp, Guid ticketId, string title, string? description) =>
        DeviceProofPrefix + deviceId.ToString("D") + "\n" + timestamp + "\n" + ticketId.ToString("D") + "\n"
        + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(title + "\n" + (description ?? string.Empty))));

    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/tickets").WithTags("Helpdesk").RequireModule(AppModules.Helpdesk);
        AgentEndpoints.Map(group);

        group.MapPost("/", CreateAsync).WithName("CreateTicket").WithSummary("Problem melden (idempotent über die Ticket-ID)")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AllowPendingDevice();
        group.MapPost("/device", CreateFromDeviceAsync).WithName("CreateTicketFromDevice")
            .WithSummary("Problem melden ohne Anmeldung – nur von eingerichteten, nicht gesperrten Geräten mit Gerätesignatur")
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AllowAnonymous().RequireRateLimiting(DeviceRateLimitPolicy);
        group.MapGet("/unread", UnreadAsync).WithName("GetUnreadTickets")
            .WithSummary("Tickets mit neuen Antworten oder Änderungen seit dem letzten Ansehen (Zähler in Kopfleiste und App)").AllowPendingDevice();
        group.MapPost("/{id:guid}/seen", SeenAsync).WithName("MarkTicketSeen")
            .WithSummary("Ticket als gelesen markieren (geschieht auch beim Abruf des Tickets)")
            .ProducesProblem(StatusCodes.Status404NotFound).AllowPendingDevice();
        group.MapGet("/mine", MineAsync).WithName("ListMyTickets").WithSummary("Eigene Meldungen").AllowPendingDevice();
        group.MapGet("/", ListAsync).WithName("ListTickets").WithSummary("Tickets für die Bearbeitung").RequirePermission(Permissions.HelpdeskAgent);
        group.MapGet("/{id:guid}", GetAsync).WithName("GetTicket").WithSummary("Ticket mit Verlauf (interne Notizen nur für die Bearbeitung)")
            .ProducesProblem(StatusCodes.Status404NotFound).AllowPendingDevice();
        group.MapPatch("/{id:guid}", UpdateAsync).WithName("UpdateTicket").WithSummary("Status, Priorität, Kategorie, Zuweisung, Lösung")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.HelpdeskAgent);
        group.MapPost("/{id:guid}/comments", CommentAsync).WithName("AddTicketComment").WithSummary("Kommentar oder interne Notiz")
            .ProducesProblem(StatusCodes.Status403Forbidden).ProducesProblem(StatusCodes.Status404NotFound).AllowPendingDevice();
        group.MapPost("/{id:guid}/reopen", ReopenAsync).WithName("ReopenTicket").WithSummary("Gelöstes Ticket wieder öffnen (Melder)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict).AllowPendingDevice();
    }

    private static async Task<Results<Created<TicketDetail>, Ok<TicketDetail>, ProblemHttpResult>> CreateAsync(
        CreateTicketRequest request, TicketService tickets, ICurrentUser currentUser, ISessionDevices sessions, CancellationToken cancellationToken)
    {
        var deviceId = await sessions.GetCurrentSessionDeviceAsync(cancellationToken);
        var (ticket, created, failure, message) = await tickets.CreateAsync(request.ToInput(), currentUser.UserId, null, deviceId, cancellationToken);
        if (ticket is null)
        {
            return Failure(failure!.Value, message!);
        }

        var detail = await tickets.DetailAsync(ticket, includeInternal: false, cancellationToken, currentUser.UserId);
        return created ? TypedResults.Created($"/api/v1/tickets/{ticket.Id}", detail) : TypedResults.Ok(detail);
    }

    private static async Task<Results<Ok<DeviceTicketResponse>, ProblemHttpResult>> CreateFromDeviceAsync(
        DeviceTicketRequest request, TicketService tickets, EstrichOneDbContext db, TimeProvider clock, CancellationToken cancellationToken)
    {
        var device = await db.Set<Device>().AsNoTracking().SingleOrDefaultAsync(d => d.Id == request.DeviceId, cancellationToken);
        var signature = TryBase64(request.Signature);
        var timestampValid = DateTimeOffset.TryParse(request.Timestamp, out var signedAt)
            && (clock.GetUtcNow() - signedAt).Duration() <= TimeSpan.FromMinutes(10);
        var message = DeviceProofMessage(request.DeviceId, request.Timestamp, request.Ticket.Id, request.Ticket.Title, request.Ticket.Description);
        if (device is null || device.Status is DeviceStatus.Locked or DeviceStatus.Retired || !timestampValid
            || signature is null || !device.VerifyProof(message, signature))
        {
            // Keine Unterscheidung nach außen: ein unbekanntes Gerät verrät nicht, dass es fremde Geräte gibt.
            return Problem(StatusCodes.Status401Unauthorized, "DEVICE_PROOF_INVALID", "Gerät nicht bestätigt",
                "Das Gerät ist nicht eingerichtet, gesperrt oder die Signatur ist ungültig.");
        }

        var input = request.Ticket.ToInput() with { BauakteId = null, AttachmentFileIds = [], Priority = null };
        var (ticket, _, failure, failureMessage) = await tickets.CreateAsync(input, null, request.ReporterName, device.Id, cancellationToken);
        return ticket is null ? Failure(failure!.Value, failureMessage!) : TypedResults.Ok(new DeviceTicketResponse(ticket.Number));
    }

    private static async Task<Ok<IReadOnlyList<TicketSummary>>> MineAsync(
        TicketService tickets, TicketReadState reads, EstrichOneDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        TypedResults.Ok(await tickets.SummariesAsync(
            db.Set<Ticket>().Where(t => t.ReporterUserId == currentUser.UserId || t.WatcherUserIds.Contains(currentUser.UserId))
                .OrderByDescending(t => t.CreatedAt).Take(100), cancellationToken,
            ids => reads.UnreadAsync(currentUser.UserId, isAgent: false, ids, cancellationToken)));

    private static async Task<Ok<TicketUnreadResponse>> UnreadAsync(
        TicketReadState reads, EstrichOneDbContext db, ICurrentUser currentUser, IUserPermissions permissions, CancellationToken cancellationToken)
    {
        var isAgent = await permissions.HasAsync(Permissions.HelpdeskAgent, cancellationToken);
        var unread = await reads.UnreadAsync(currentUser.UserId, isAgent, null, cancellationToken);
        var ids = unread.Keys.ToList();
        var items = (await db.Set<Ticket>().AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync(cancellationToken))
            .Select(t => new TicketUnreadItem(t.Id, t.Number, t.Title, t.Status, unread[t.Id]))
            .OrderByDescending(t => t.LastActivityAt).ToList();
        return TypedResults.Ok(new TicketUnreadResponse(items.Count, items.Take(50).ToList()));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SeenAsync(
        Guid id, TicketReadState reads, EstrichOneDbContext db, ICurrentUser currentUser, IUserPermissions permissions, CancellationToken cancellationToken)
    {
        var (ticket, _) = await FindVisibleAsync(id, db, currentUser, permissions, cancellationToken);
        if (ticket is null)
        {
            return NotFound();
        }

        await reads.MarkSeenAsync(id, currentUser.UserId, cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Ok<IReadOnlyList<TicketSummary>>> ListAsync(
        TicketService tickets, TicketReadState reads, EstrichOneDbContext db, ICurrentUser currentUser, TimeProvider clock, CancellationToken cancellationToken,
        string? filter = null, string? category = null, string? priority = null, string? status = null, Guid? assignee = null, Guid? group = null,
        string? q = null, string? sort = null, int? limit = null)
    {
        // Werte wie in JSON (camelCase, z. B. „unassigned“, „officeIt“) oder als Enum-Name.
        var selected = Enum.TryParse<TicketFilter>(filter, ignoreCase: true, out var parsedFilter) ? parsedFilter : TicketFilter.Open;
        var query = db.Set<Ticket>().AsQueryable();
        var me = currentUser.UserId;
        var now = clock.GetUtcNow();
        query = selected switch
        {
            TicketFilter.Open => query.Where(t => t.Status != TicketStatus.Closed && t.Status != TicketStatus.Resolved),
            TicketFilter.Mine => query.Where(t => t.AssigneeUserId == me && t.Status != TicketStatus.Closed),
            TicketFilter.Unassigned => query.Where(t => t.AssigneeUserId == null && t.Status != TicketStatus.Closed),
            TicketFilter.Overdue => query.Where(t => (t.Status == TicketStatus.New || t.Status == TicketStatus.InProgress)
                && ((t.FirstResponseAt == null && t.ResponseDueAt < now) || t.ResolutionDueAt < now)),
            TicketFilter.Waiting => query.Where(t => t.Status == TicketStatus.WaitingForReporter),
            TicketFilter.MyGroups => query.Where(t => t.Status != TicketStatus.Closed && t.Status != TicketStatus.Resolved
                && db.Set<HelpdeskGroup>().Any(g => g.Id == t.GroupId && g.MemberUserIds.Contains(me))),
            TicketFilter.Done => query.Where(t => t.Status == TicketStatus.Closed || t.Status == TicketStatus.Resolved),
            _ => query,
        };
        if (Enum.TryParse<TicketCategory>(category, ignoreCase: true, out var c))
        {
            query = query.Where(t => t.Category == c);
        }

        if (Enum.TryParse<TicketPriority>(priority, ignoreCase: true, out var p))
        {
            query = query.Where(t => t.Priority == p);
        }

        if (Enum.TryParse<TicketStatus>(status, ignoreCase: true, out var st))
        {
            query = query.Where(t => t.Status == st);
        }

        if (assignee is { } a)
        {
            query = query.Where(t => t.AssigneeUserId == a);
        }

        if (group is { } g)
        {
            query = query.Where(t => t.GroupId == g);
        }

        if (q?.Trim() is { Length: > 0 } text)
        {
            // Volltext über Nummer (auch ohne „T-“), Titel, Beschreibung, Melder und Antworten.
            foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(6))
            {
                var pattern = $"%{word.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
                query = query.Where(t => EF.Functions.ILike(t.Number, pattern) || EF.Functions.ILike(t.Title, pattern)
                    || (t.Description != null && EF.Functions.ILike(t.Description, pattern))
                    || (t.ReporterName != null && EF.Functions.ILike(t.ReporterName, pattern))
                    || db.Set<User>().Any(u => u.Id == t.ReporterUserId && EF.Functions.ILike(u.DisplayName, pattern))
                    || db.Set<TicketComment>().Any(x => x.TicketId == t.Id && EF.Functions.ILike(x.Text, pattern)));
            }
        }

        query = sort switch
        {
            "recent" => query.OrderByDescending(t => t.LastActivityAt),
            "created" => query.OrderByDescending(t => t.CreatedAt),
            "number" => query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Number),
            "priority" => query.OrderByDescending(t => t.Priority == TicketPriority.Urgent).ThenByDescending(t => t.Priority == TicketPriority.High)
                .ThenBy(t => t.Priority == TicketPriority.Low).ThenBy(t => t.LastActivityAt),
            "due" => query.OrderBy(t => t.ResolutionDueAt == null).ThenBy(t => t.FirstResponseAt == null ? t.ResponseDueAt : t.ResolutionDueAt),
            // Am längsten wartende zuerst; „Ich kann nicht arbeiten“ vorneweg.
            _ => query.OrderByDescending(t => t.ReporterBlocked).ThenBy(t => t.LastActivityAt),
        };

        return TypedResults.Ok(await tickets.SummariesAsync(query.Take(Math.Clamp(limit ?? 500, 1, 1000)), cancellationToken,
            ids => reads.UnreadAsync(currentUser.UserId, isAgent: true, ids, cancellationToken)));
    }

    private static async Task<Results<Ok<TicketDetail>, ProblemHttpResult>> GetAsync(
        Guid id, TicketService tickets, TicketReadState reads, EstrichOneDbContext db, ICurrentUser currentUser, IUserPermissions permissions,
        CancellationToken cancellationToken)
    {
        var (ticket, isAgent) = await FindVisibleAsync(id, db, currentUser, permissions, cancellationToken);
        if (ticket is null)
        {
            return NotFound();
        }

        // Ansehen gilt als gelesen.
        await reads.MarkSeenAsync(id, currentUser.UserId, cancellationToken);
        return TypedResults.Ok(await tickets.DetailAsync(ticket, includeInternal: isAgent, cancellationToken, currentUser.UserId));
    }

    private static async Task<Results<Ok<TicketDetail>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateTicketRequest request, TicketService tickets, TicketNotifier notifier, EstrichOneDbContext db, ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var ticket = await db.Set<Ticket>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null)
        {
            return NotFound();
        }

        var (effect, error) = await tickets.ApplyChangeAsync(ticket, request.ToChange(), currentUser.UserId, cancellationToken);
        if (effect is null)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, error!.Contains("Benutzer", StringComparison.Ordinal) ? "UNKNOWN_USER" : "VALIDATION_FAILED",
                "Ungültige Angaben", error);
        }

        await db.SaveChangesAsync(cancellationToken);
        await NotifyChangeAsync(ticket, effect, notifier, cancellationToken);
        return TypedResults.Ok(await tickets.DetailAsync(ticket, includeInternal: true, cancellationToken, currentUser.UserId));
    }

    internal static async Task NotifyChangeAsync(Ticket ticket, TicketChangeEffect effect, TicketNotifier notifier, CancellationToken cancellationToken)
    {
        if (effect.StatusChanged)
        {
            await notifier.NotifyAsync(ticket, ticket.Status is TicketStatus.Resolved or TicketStatus.Closed ? ticket.Resolution : null, cancellationToken);
        }

        if (effect.NewAssignee is { } assignee)
        {
            await notifier.NotifyAssignedAsync(ticket, assignee, cancellationToken);
        }
    }

    private static async Task<Results<Ok<TicketDetail>, ProblemHttpResult>> CommentAsync(
        Guid id, AddTicketCommentRequest request, TicketService tickets, TicketNotifier notifier, EstrichOneDbContext db, ICurrentUser currentUser,
        IUserPermissions permissions, IRoleAssignments roles, CancellationToken cancellationToken)
    {
        var (ticket, isAgent) = await FindVisibleAsync(id, db, currentUser, permissions, cancellationToken, tracking: true);
        if (ticket is null)
        {
            return NotFound();
        }

        if (request.IsInternal && !isAgent)
        {
            return Problem(StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "Keine Berechtigung", "Interne Notizen schreibt nur die Bearbeitung.");
        }

        var attachments = request.AttachmentFileIds ?? [];
        if (!await tickets.AreOwnLooseFilesAsync(attachments, currentUser.UserId, cancellationToken))
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Ungültige Angaben", "Anhänge müssen eigene Dateien ohne Bauakte sein.");
        }

        IReadOnlyList<Guid> mentions = [];
        if (request.MentionUserIds is { Count: > 0 } wanted)
        {
            if (!isAgent)
            {
                return Problem(StatusCodes.Status403Forbidden, "PERMISSION_DENIED", "Keine Berechtigung", "Kolleginnen und Kollegen erwähnt nur die Bearbeitung.");
            }

            var agents = await roles.GetUserIdsWithPermissionAsync(Permissions.HelpdeskAgent, cancellationToken);
            mentions = wanted.Distinct().Where(agents.Contains).ToList();
        }

        var error = await tickets.AddCommentAsync(ticket, currentUser.UserId, isAgent, request.Text ?? string.Empty, request.IsInternal, attachments, mentions,
            cancellationToken);
        if (error is not null)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Ungültige Angaben", error);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (!request.IsInternal)
        {
            await notifier.NotifyAsync(ticket, request.Text, cancellationToken);
        }

        await notifier.NotifyMentionedAsync(ticket, mentions, cancellationToken);
        return TypedResults.Ok(await tickets.DetailAsync(ticket, includeInternal: isAgent, cancellationToken, currentUser.UserId));
    }

    private static async Task<Results<Ok<TicketDetail>, ProblemHttpResult>> ReopenAsync(
        Guid id, TicketService tickets, EstrichOneDbContext db, ICurrentUser currentUser, IUserPermissions permissions, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var (ticket, isAgent) = await FindVisibleAsync(id, db, currentUser, permissions, cancellationToken, tracking: true);
        if (ticket is null)
        {
            return NotFound();
        }

        if (ticket.Status != TicketStatus.Resolved)
        {
            return Problem(StatusCodes.Status409Conflict, "TICKET_NOT_RESOLVED", "Nicht gelöst", "Nur gelöste Tickets lassen sich wieder öffnen.");
        }

        var now = clock.GetUtcNow();
        var status = ticket.AssigneeUserId is null ? TicketStatus.New : TicketStatus.InProgress;
        db.Add(TicketEvent.Create(ticket.Id, currentUser.UserId, "status", nameof(TicketStatus.Resolved), status.ToString(), now));
        tickets.ChangeStatus(ticket, status, null, currentUser.UserId, await tickets.SettingsAsync(cancellationToken), now);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await tickets.DetailAsync(ticket, includeInternal: isAgent, cancellationToken, currentUser.UserId));
    }

    /// <summary>Sichtbar für den Melder und für die Bearbeitung; sonst 404 (verrät keine fremden Tickets).</summary>
    internal static async Task<(Ticket? Ticket, bool IsAgent)> FindVisibleAsync(
        Guid id, EstrichOneDbContext db, ICurrentUser currentUser, IUserPermissions permissions, CancellationToken cancellationToken,
        bool tracking = false)
    {
        var query = tracking ? db.Set<Ticket>() : db.Set<Ticket>().AsNoTracking();
        var ticket = await query.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        var isAgent = await permissions.HasAsync(Permissions.HelpdeskAgent, cancellationToken);
        return ticket is not null && (isAgent || ticket.IsVisibleTo(currentUser.UserId)) ? (ticket, isAgent) : (null, false);
    }

    private static ProblemHttpResult Failure(TicketFailure failure, string message) => failure switch
    {
        TicketFailure.IdConflict => Problem(StatusCodes.Status409Conflict, "ID_CONFLICT", "ID bereits vergeben", message),
        TicketFailure.BauakteNotFound => Problem(StatusCodes.Status422UnprocessableEntity, "BAUAKTE_NOT_FOUND", "Bauakte nicht gefunden", message),
        _ => Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Ungültige Angaben", message),
    };

    internal static ProblemHttpResult NotFound() =>
        Problem(StatusCodes.Status404NotFound, "TICKET_NOT_FOUND", "Ticket nicht gefunden", "Das Ticket existiert nicht oder ist nicht sichtbar.");

    internal static ProblemHttpResult Problem(int status, string code, string title, string detail) =>
        TypedResults.Problem(statusCode: status, title: title, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });

    private static byte[]? TryBase64(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
