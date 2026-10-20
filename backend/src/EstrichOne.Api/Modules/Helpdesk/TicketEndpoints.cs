using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Devices.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
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
    IReadOnlyList<Guid>? AttachmentFileIds)
{
    internal CreateTicketInput ToInput() => new(Id, Title, Description, Category, Source, ReporterBlocked, BauakteId, Diagnostics, AttachmentFileIds);
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
    [property: MaxLength(Ticket.MaxResolutionLength)] string? Resolution);

public sealed record AddTicketCommentRequest([property: Required, MaxLength(TicketComment.MaxTextLength)] string Text, bool IsInternal);

public enum TicketFilter
{
    Open,
    Mine,
    Unassigned,
    All,
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
        var group = api.MapGroup("/tickets").WithTags("Helpdesk");

        group.MapPost("/", CreateAsync).WithName("CreateTicket").WithSummary("Problem melden (idempotent über die Ticket-ID)")
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AllowPendingDevice();
        group.MapPost("/device", CreateFromDeviceAsync).WithName("CreateTicketFromDevice")
            .WithSummary("Problem melden ohne Anmeldung – nur von eingerichteten, nicht gesperrten Geräten mit Gerätesignatur")
            .ProducesProblem(StatusCodes.Status401Unauthorized).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .AllowAnonymous().RequireRateLimiting(DeviceRateLimitPolicy);
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

        var detail = await tickets.DetailAsync(ticket, includeInternal: false, cancellationToken);
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

        var input = request.Ticket.ToInput() with { BauakteId = null, AttachmentFileIds = [] };
        var (ticket, _, failure, failureMessage) = await tickets.CreateAsync(input, null, request.ReporterName, device.Id, cancellationToken);
        return ticket is null ? Failure(failure!.Value, failureMessage!) : TypedResults.Ok(new DeviceTicketResponse(ticket.Number));
    }

    private static async Task<Ok<IReadOnlyList<TicketSummary>>> MineAsync(
        TicketService tickets, EstrichOneDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken) =>
        TypedResults.Ok(await tickets.SummariesAsync(
            db.Set<Ticket>().Where(t => t.ReporterUserId == currentUser.UserId).OrderByDescending(t => t.CreatedAt).Take(100), cancellationToken));

    private static async Task<Ok<IReadOnlyList<TicketSummary>>> ListAsync(
        TicketService tickets, EstrichOneDbContext db, ICurrentUser currentUser, CancellationToken cancellationToken,
        string? filter = null, string? category = null)
    {
        // Werte wie in JSON (camelCase, z. B. „unassigned“, „officeIt“) oder als Enum-Name.
        var selected = Enum.TryParse<TicketFilter>(filter, ignoreCase: true, out var parsedFilter) ? parsedFilter : TicketFilter.Open;
        TicketCategory? categoryFilter = Enum.TryParse<TicketCategory>(category, ignoreCase: true, out var parsedCategory) ? parsedCategory : null;
        var query = db.Set<Ticket>().AsQueryable();
        query = selected switch
        {
            TicketFilter.Open => query.Where(t => t.Status != TicketStatus.Closed && t.Status != TicketStatus.Resolved),
            TicketFilter.Mine => query.Where(t => t.AssigneeUserId == currentUser.UserId && t.Status != TicketStatus.Closed),
            TicketFilter.Unassigned => query.Where(t => t.AssigneeUserId == null && t.Status != TicketStatus.Closed),
            _ => query,
        };
        if (categoryFilter is { } c)
        {
            query = query.Where(t => t.Category == c);
        }

        // Am längsten wartende zuerst; „Ich kann nicht arbeiten“ vorneweg.
        return TypedResults.Ok(await tickets.SummariesAsync(
            query.OrderByDescending(t => t.ReporterBlocked).ThenBy(t => t.LastActivityAt).Take(500), cancellationToken));
    }

    private static async Task<Results<Ok<TicketDetail>, ProblemHttpResult>> GetAsync(
        Guid id, TicketService tickets, EstrichOneDbContext db, ICurrentUser currentUser, IUserPermissions permissions, CancellationToken cancellationToken)
    {
        var (ticket, isAgent) = await FindVisibleAsync(id, db, currentUser, permissions, cancellationToken);
        return ticket is null ? NotFound() : TypedResults.Ok(await tickets.DetailAsync(ticket, includeInternal: isAgent, cancellationToken));
    }

    private static async Task<Results<Ok<TicketDetail>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateTicketRequest request, TicketService tickets, EstrichOneDbContext db, ICurrentUser currentUser, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var ticket = await db.Set<Ticket>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (ticket is null)
        {
            return NotFound();
        }

        var now = clock.GetUtcNow();
        void Event(string kind, object? from, object? to)
        {
            if (!Equals(from, to))
            {
                db.Add(TicketEvent.Create(ticket.Id, currentUser.UserId, kind, from?.ToString(), to?.ToString(), now));
            }
        }

        try
        {
            if (request.AssigneeUserId is { } assignee || request.Unassign)
            {
                var target = request.Unassign ? null : request.AssigneeUserId;
                if (target is { } userId && !await db.Set<User>().AnyAsync(u => u.Id == userId && u.IsActive, cancellationToken))
                {
                    return Problem(StatusCodes.Status422UnprocessableEntity, "UNKNOWN_USER", "Unbekannter Benutzer", "Der Benutzer existiert nicht oder ist gesperrt.");
                }

                Event("assignee", ticket.AssigneeUserId, target);
                ticket.Assign(target);
            }

            if (request.Priority is { } priority)
            {
                Event("priority", ticket.Priority, priority);
                ticket.Prioritize(priority);
            }

            if (request.Category is { } category)
            {
                Event("category", ticket.Category, category);
                ticket.Categorize(category);
            }

            if (request.Status is { } status)
            {
                Event("status", ticket.Status, status);
                ticket.ChangeStatus(status, request.Resolution, now);
            }
        }
        catch (ArgumentException exception)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Ungültige Angaben", exception.Message);
        }

        ticket.Touch(now);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await tickets.DetailAsync(ticket, includeInternal: true, cancellationToken));
    }

    private static async Task<Results<Ok<TicketDetail>, ProblemHttpResult>> CommentAsync(
        Guid id, AddTicketCommentRequest request, TicketService tickets, EstrichOneDbContext db, ICurrentUser currentUser,
        IUserPermissions permissions, TimeProvider clock, CancellationToken cancellationToken)
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

        var now = clock.GetUtcNow();
        try
        {
            db.Add(TicketComment.Create(ticket.Id, currentUser.UserId, request.Text, request.IsInternal, now));
        }
        catch (ArgumentException exception)
        {
            return Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Ungültige Angaben", exception.Message);
        }

        // Antwort des Melders auf eine Rückfrage: zurück in die Bearbeitung.
        if (ticket.ReporterUserId == currentUser.UserId && ticket.Status == TicketStatus.WaitingForReporter)
        {
            db.Add(TicketEvent.Create(ticket.Id, currentUser.UserId, "status", nameof(TicketStatus.WaitingForReporter), nameof(TicketStatus.InProgress), now));
            ticket.ChangeStatus(TicketStatus.InProgress, null, now);
        }

        ticket.Touch(now);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await tickets.DetailAsync(ticket, includeInternal: isAgent, cancellationToken));
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
        ticket.ChangeStatus(status, null, now);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(await tickets.DetailAsync(ticket, includeInternal: isAgent, cancellationToken));
    }

    /// <summary>Sichtbar für den Melder und für die Bearbeitung; sonst 404 (verrät keine fremden Tickets).</summary>
    private static async Task<(Ticket? Ticket, bool IsAgent)> FindVisibleAsync(
        Guid id, EstrichOneDbContext db, ICurrentUser currentUser, IUserPermissions permissions, CancellationToken cancellationToken,
        bool tracking = false)
    {
        var query = tracking ? db.Set<Ticket>() : db.Set<Ticket>().AsNoTracking();
        var ticket = await query.SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        var isAgent = await permissions.HasAsync(Permissions.HelpdeskAgent, cancellationToken);
        return ticket is not null && (isAgent || ticket.ReporterUserId == currentUser.UserId) ? (ticket, isAgent) : (null, false);
    }

    private static ProblemHttpResult Failure(TicketFailure failure, string message) => failure switch
    {
        TicketFailure.IdConflict => Problem(StatusCodes.Status409Conflict, "ID_CONFLICT", "ID bereits vergeben", message),
        TicketFailure.BauakteNotFound => Problem(StatusCodes.Status422UnprocessableEntity, "BAUAKTE_NOT_FOUND", "Bauakte nicht gefunden", message),
        _ => Problem(StatusCodes.Status422UnprocessableEntity, "VALIDATION_FAILED", "Ungültige Angaben", message),
    };

    private static ProblemHttpResult NotFound() =>
        Problem(StatusCodes.Status404NotFound, "TICKET_NOT_FOUND", "Ticket nicht gefunden", "Das Ticket existiert nicht oder ist nicht sichtbar.");

    private static ProblemHttpResult Problem(int status, string code, string title, string detail) =>
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
