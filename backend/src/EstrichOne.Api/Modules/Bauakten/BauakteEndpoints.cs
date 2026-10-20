using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace EstrichOne.Api.Modules.Bauakten;

public sealed record BauakteListItem(
    Guid Id, string? Number, string Title, BauakteStatus Status, string? Street, string? PostalCode, string? City,
    Guid? CustomerId, DateTimeOffset UpdatedAt, long Version);

public sealed record BauaktePage(IReadOnlyList<BauakteListItem> Items, string? NextCursor);

public sealed record BauakteDetail(
    Guid Id, string? Number, string Title, BauakteStatus Status, string? Street, string? PostalCode, string? City,
    double? Latitude, double? Longitude, string? Description, Guid? CustomerId, string? TemplateKey,
    Guid? ResponsibleUserId, Guid CreatedByUserId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version);

/// <summary>Anlage mit vom Client erzeugter ID (UUIDv7, ADR 0006). Pflicht ist nur <see cref="Title"/>.</summary>
public sealed record CreateBauakteRequest(
    [property: Required] Guid Id,
    [property: Required, MaxLength(Bauakte.MaxTitleLength)] string Title,
    [property: MaxLength(200)] string? Street = null,
    [property: MaxLength(10)] string? PostalCode = null,
    [property: MaxLength(100)] string? City = null,
    [property: Range(-90, 90)] double? Latitude = null,
    [property: Range(-180, 180)] double? Longitude = null,
    [property: MaxLength(4000)] string? Description = null,
    Guid? CustomerId = null,
    [property: MaxLength(50)] string? TemplateKey = null);

/// <summary>Ersetzt alle bearbeitbaren Felder. Version per <c>If-Match</c>.</summary>
public sealed record UpdateBauakteRequest(
    [property: Required, MaxLength(Bauakte.MaxTitleLength)] string Title,
    BauakteStatus Status,
    [property: MaxLength(200)] string? Street,
    [property: MaxLength(10)] string? PostalCode,
    [property: MaxLength(100)] string? City,
    [property: Range(-90, 90)] double? Latitude,
    [property: Range(-180, 180)] double? Longitude,
    [property: MaxLength(4000)] string? Description,
    Guid? CustomerId,
    Guid? ResponsibleUserId);

internal static class BauakteEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/bauakten").WithTags("Bauakten");

        group.MapGet("/", ListAsync).WithName("ListBauakten")
            .WithSummary("Sichtbare Bauakten, neueste Änderung zuerst")
            .RequirePermission(Permissions.BauaktenRead);

        group.MapGet("/{id:guid}", GetAsync).WithName("GetBauakte")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.BauaktenRead);

        group.MapPost("/", CreateAsync).WithName("CreateBauakte")
            .WithSummary("Bauakte anlegen (idempotent über die ID)")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.BauaktenCreate);

        group.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateBauakte")
            .WithSummary("Bauakte ändern; erfordert If-Match mit der bekannten Version")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .ProducesProblem(StatusCodes.Status428PreconditionRequired)
            .RequirePermission(Permissions.BauaktenEdit);

        group.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteBauakte")
            .WithSummary("Bauakte in den Papierkorb legen")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.BauaktenDelete);
    }

    private static async Task<Results<Ok<BauaktePage>, ProblemHttpResult>> ListAsync(
        EstrichOneDbContext db, IBauakteAccess access, CancellationToken cancellationToken,
        [FromQuery] string? search = null, [FromQuery] BauakteStatus? status = null,
        [FromQuery] string? cursor = null, [FromQuery] int? limit = null)
    {
        var pageSize = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var query = (await access.VisibleAsync(db.Set<Bauakte>().AsNoTracking(), cancellationToken));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + EscapeLike(search.Trim()) + "%";
            query = query.Where(b => EF.Functions.ILike(b.Title, pattern, "\\")
                || (b.Number != null && EF.Functions.ILike(b.Number, pattern, "\\"))
                || (b.Street != null && EF.Functions.ILike(b.Street, pattern, "\\"))
                || (b.City != null && EF.Functions.ILike(b.City, pattern, "\\")));
        }

        if (status is not null)
        {
            query = query.Where(b => b.Status == status);
        }

        if (cursor is not null)
        {
            if (!TryDecodeCursor(cursor, out var updatedAt, out var lastId))
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Ungültiger Cursor",
                    extensions: new Dictionary<string, object?> { ["code"] = "INVALID_CURSOR" });
            }

            query = query.Where(b => b.UpdatedAt < updatedAt || (b.UpdatedAt == updatedAt && b.Id.CompareTo(lastId) < 0));
        }

        var rows = await query.OrderByDescending(b => b.UpdatedAt).ThenByDescending(b => b.Id)
            .Take(pageSize + 1)
            .Select(b => new BauakteListItem(b.Id, b.Number, b.Title, b.Status, b.Street, b.PostalCode, b.City, b.CustomerId, b.UpdatedAt, b.Version))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > pageSize;
        var items = rows.Take(pageSize).ToList();
        return TypedResults.Ok(new BauaktePage(items, hasMore ? EncodeCursor(items[^1].UpdatedAt, items[^1].Id) : null));
    }

    private static async Task<Results<Ok<BauakteDetail>, ProblemHttpResult>> GetAsync(
        Guid id, EstrichOneDbContext db, IBauakteAccess access, HttpContext http, CancellationToken cancellationToken)
    {
        var bauakte = await (await access.VisibleAsync(db.Set<Bauakte>().AsNoTracking(), cancellationToken))
            .SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bauakte is null)
        {
            return NotFound();
        }

        SetETag(http, bauakte.Version);
        return TypedResults.Ok(ToDetail(bauakte));
    }

    private static async Task<Results<Created<BauakteDetail>, Ok<BauakteDetail>, ProblemHttpResult>> CreateAsync(
        CreateBauakteRequest request, BauakteCommands commands, HttpContext http, CancellationToken cancellationToken)
    {
        if (request.Id == Guid.Empty || string.IsNullOrWhiteSpace(request.Title))
        {
            return Validation("Id und Bezeichnung sind erforderlich.");
        }

        var (bauakte, outcome) = await commands.CreateAsync(new NewBauakte(request.Id, request.Title, request.Street,
            request.PostalCode, request.City, request.Latitude, request.Longitude, request.Description, request.CustomerId,
            request.TemplateKey), cancellationToken);

        switch (outcome)
        {
            case CreateBauakteOutcome.IdConflict:
                return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "ID bereits vergeben",
                    detail: "Unter dieser ID existiert bereits eine andere Bauakte.",
                    extensions: new Dictionary<string, object?> { ["code"] = "ID_CONFLICT" });
            case CreateBauakteOutcome.UnknownCustomer:
                return Validation("Der angegebene Kunde existiert nicht.", "UNKNOWN_CUSTOMER");
            case CreateBauakteOutcome.AlreadyExists:
                SetETag(http, bauakte!.Version);
                return TypedResults.Ok(ToDetail(bauakte));
            default:
                SetETag(http, bauakte!.Version);
                return TypedResults.Created($"/api/v1/bauakten/{bauakte.Id}", ToDetail(bauakte));
        }
    }

    private static async Task<Results<Ok<BauakteDetail>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateBauakteRequest request, EstrichOneDbContext db, IBauakteAccess access, IBauakteHistory history, HttpContext http,
        CancellationToken cancellationToken)
    {
        var bauakte = await (await access.VisibleAsync(db.Set<Bauakte>(), cancellationToken)).SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bauakte is null)
        {
            return NotFound();
        }

        if (!TryReadIfMatch(http, out var expectedVersion))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status428PreconditionRequired, title: "Version fehlt",
                detail: "Änderungen brauchen den Header If-Match mit der zuletzt gelesenen Version.",
                extensions: new Dictionary<string, object?> { ["code"] = "VERSION_REQUIRED" });
        }

        if (bauakte.Version != expectedVersion)
        {
            return VersionConflict(bauakte);
        }

        if (request.CustomerId is { } customerId && !await db.Set<Contact>().AnyAsync(c => c.Id == customerId, cancellationToken))
        {
            return Validation("Der angegebene Kunde existiert nicht.", "UNKNOWN_CUSTOMER");
        }

        var before = ToDetail(bauakte);
        bauakte.Rename(request.Title);
        bauakte.ChangeStatus(request.Status);
        bauakte.ChangeAddress(request.Street, request.PostalCode, request.City, request.Latitude, request.Longitude);
        bauakte.Describe(request.Description);
        bauakte.AssignCustomer(request.CustomerId);
        bauakte.AssignResponsible(request.ResponsibleUserId);
        RecordChanges(history, before, ToDetail(bauakte));

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Zwischen Lesen und Schreiben hat jemand anderes geändert.
            var current = await db.Set<Bauakte>().AsNoTracking().SingleAsync(b => b.Id == id, cancellationToken);
            return VersionConflict(current);
        }

        SetETag(http, bauakte.Version);
        return TypedResults.Ok(ToDetail(bauakte));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, EstrichOneDbContext db, IBauakteAccess access, IBauakteHistory history, TimeProvider clock, CancellationToken cancellationToken)
    {
        var bauakte = await (await access.VisibleAsync(db.Set<Bauakte>(), cancellationToken)).SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bauakte is null)
        {
            return NotFound();
        }

        bauakte.Delete(clock.GetUtcNow());
        history.Record(bauakte.Id, BauakteHistoryActions.Deleted);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    /// <summary>Statuswechsel und geänderte Felder als Verlaufseinträge (ohne Inhalte).</summary>
    internal static void RecordChanges(IBauakteHistory history, BauakteDetail before, BauakteDetail after)
    {
        if (before.Status != after.Status)
        {
            history.Record(after.Id, BauakteHistoryActions.StatusChanged,
                new Dictionary<string, string?> { ["from"] = before.Status.ToString(), ["to"] = after.Status.ToString() });
        }

        var fields = new List<string>();
        if (before.Title != after.Title) fields.Add("title");
        if (before.Street != after.Street) fields.Add("street");
        if (before.PostalCode != after.PostalCode) fields.Add("postalCode");
        if (before.City != after.City) fields.Add("city");
        if (before.Latitude != after.Latitude || before.Longitude != after.Longitude) fields.Add("latitude");
        if (before.Description != after.Description) fields.Add("description");
        if (before.CustomerId != after.CustomerId) fields.Add("customerId");
        if (before.ResponsibleUserId != after.ResponsibleUserId) fields.Add("responsibleUserId");
        if (fields.Count > 0)
        {
            history.Record(after.Id, BauakteHistoryActions.Updated, new Dictionary<string, string?> { ["fields"] = string.Join(',', fields) });
        }
    }

    internal static BauakteDetail ToDetail(Bauakte b) => new(
        b.Id, b.Number, b.Title, b.Status, b.Street, b.PostalCode, b.City, b.Latitude, b.Longitude, b.Description,
        b.CustomerId, b.TemplateKey, b.ResponsibleUserId, b.CreatedByUserId, b.CreatedAt, b.UpdatedAt, b.Version);

    /// <summary>Nicht sichtbare und nicht vorhandene Bauakten sind von außen nicht unterscheidbar.</summary>
    private static ProblemHttpResult NotFound() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Bauakte nicht gefunden",
            extensions: new Dictionary<string, object?> { ["code"] = "BAUAKTE_NOT_FOUND" });

    private static ProblemHttpResult VersionConflict(Bauakte current) =>
        TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Bauakte wurde inzwischen geändert",
            detail: "Bitte den aktuellen Stand laden und die Änderung erneut anwenden.",
            extensions: new Dictionary<string, object?> { ["code"] = "VERSION_CONFLICT", ["currentVersion"] = current.Version });

    private static ProblemHttpResult Validation(string detail, string code = "VALIDATION_FAILED") =>
        TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Ungültige Angaben", detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    private static void SetETag(HttpContext http, long version) =>
        http.Response.Headers.ETag = new EntityTagHeaderValue($"\"{version.ToString(CultureInfo.InvariantCulture)}\"").ToString();

    private static bool TryReadIfMatch(HttpContext http, out long version)
    {
        version = 0;
        var header = http.Request.Headers.IfMatch.ToString().Trim();
        return header.Length > 2 && header[0] == '"' && header[^1] == '"'
            && long.TryParse(header[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out version);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    private static string EncodeCursor(DateTimeOffset updatedAt, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{updatedAt.UtcTicks}:{id:N}")));

    private static bool TryDecodeCursor(string cursor, out DateTimeOffset updatedAt, out Guid id)
    {
        updatedAt = default;
        id = default;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(':');
            if (parts.Length == 2 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && Guid.TryParseExact(parts[1], "N", out id))
            {
                updatedAt = new DateTimeOffset(ticks, TimeSpan.Zero);
                return true;
            }
        }
        catch (FormatException)
        {
        }

        return false;
    }
}
