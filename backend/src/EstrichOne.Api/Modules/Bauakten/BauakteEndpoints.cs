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
    Guid? CustomerId, DateTimeOffset UpdatedAt, long Version, Guid? ResponsibleUserId, double? Latitude, double? Longitude);

/// <summary>Baustelle mit Standort für die Kartenansicht.</summary>
public sealed record BauakteMapPoint(
    Guid Id, string? Number, string Title, BauakteStatus Status, string? Street, string? City, double Latitude, double Longitude,
    Guid? ResponsibleUserId);

public sealed record BauaktePage(IReadOnlyList<BauakteListItem> Items, string? NextCursor);

public sealed record BauakteDetail(
    Guid Id, string? Number, string Title, BauakteStatus Status, string? Street, string? PostalCode, string? City,
    double? Latitude, double? Longitude, string? Description, Guid? CustomerId, string? TemplateKey,
    Guid? ResponsibleUserId, Guid CreatedByUserId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version,
    DateOnly? PlannedStart = null, DateOnly? PlannedEnd = null, string? SiteContactName = null, string? SiteContactPhone = null,
    string? SiteContactEmail = null, string? SiteNotes = null, string? CancellationReason = null, BauakteProject? Project = null);

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
    [property: MaxLength(50)] string? TemplateKey = null,
    BauakteStatus? Status = null,
    BauakteProject? Project = null,
    DateOnly? PlannedStart = null,
    DateOnly? PlannedEnd = null);

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
    Guid? ResponsibleUserId,
    DateOnly? PlannedStart = null,
    DateOnly? PlannedEnd = null,
    [property: MaxLength(100)] string? SiteContactName = null,
    [property: MaxLength(50)] string? SiteContactPhone = null,
    [property: MaxLength(320), EmailAddress] string? SiteContactEmail = null,
    [property: MaxLength(2000)] string? SiteNotes = null,
    [property: MaxLength(500)] string? CancellationReason = null,
    BauakteProject? Project = null);

/// <summary>Löschen bestätigt der Benutzer mit seinem Passwort (02: „Löschen mit Passwortbestätigung“).</summary>
public sealed record DeleteBauakteRequest([property: Required, MaxLength(1024)] string Password);

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

        group.MapGet("/map", MapPointsAsync).WithName("ListBauakteMapPoints")
            .WithSummary("Sichtbare Baustellen mit Standort; ohne abgeschlossene und stornierte, außer mit all=true")
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

        group.MapPost("/{id:guid}/delete", DeleteAsync).WithName("DeleteBauakte")
            .WithSummary("Bauakte in den Papierkorb legen; bestätigt mit dem eigenen Passwort")
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.BauaktenDelete);
    }

    private static async Task<Results<Ok<BauaktePage>, ProblemHttpResult>> ListAsync(
        EstrichOneDbContext db, IBauakteAccess access, CancellationToken cancellationToken,
        [FromQuery] string? search = null, [FromQuery] string? status = null,
        [FromQuery] string? cursor = null, [FromQuery] int? limit = null,
        [FromQuery] Guid? responsibleUserId = null, [FromQuery] Guid? customerId = null)
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

        // Wie in JSON (camelCase, „inquiry“) oder als Enum-Name; Unbekanntes ist ein Fehler, kein stiller Filterverzicht.
        if (status is not null)
        {
            if (!Enum.TryParse<BauakteStatus>(status, ignoreCase: true, out var wanted) || int.TryParse(status, out _))
            {
                return Validation($"Unbekannter Status „{status}“.");
            }

            query = query.Where(b => b.Status == wanted);
        }

        if (responsibleUserId is { } responsible)
        {
            query = query.Where(b => b.ResponsibleUserId == responsible);
        }

        if (customerId is { } customer)
        {
            query = query.Where(b => b.CustomerId == customer);
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
            .Select(b => new BauakteListItem(b.Id, b.Number, b.Title, b.Status, b.Street, b.PostalCode, b.City, b.CustomerId, b.UpdatedAt, b.Version,
                b.ResponsibleUserId, b.Latitude, b.Longitude))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > pageSize;
        var items = rows.Take(pageSize).ToList();
        return TypedResults.Ok(new BauaktePage(items, hasMore ? EncodeCursor(items[^1].UpdatedAt, items[^1].Id) : null));
    }

    private const int MaxMapPoints = 2000;

    private static async Task<Ok<List<BauakteMapPoint>>> MapPointsAsync(
        EstrichOneDbContext db, IBauakteAccess access, CancellationToken cancellationToken, [FromQuery] bool all = false)
    {
        var query = (await access.VisibleAsync(db.Set<Bauakte>().AsNoTracking(), cancellationToken))
            .Where(b => b.Latitude != null && b.Longitude != null);
        if (!all)
        {
            query = query.Where(b => b.Status != BauakteStatus.Completed && b.Status != BauakteStatus.Cancelled);
        }

        var points = await query.OrderByDescending(b => b.UpdatedAt).Take(MaxMapPoints)
            .Select(b => new BauakteMapPoint(b.Id, b.Number, b.Title, b.Status, b.Street, b.City, b.Latitude!.Value, b.Longitude!.Value,
                b.ResponsibleUserId))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(points);
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

        if (request.Project?.Normalize().Validate() is { } projectError)
        {
            return Validation(projectError, "INVALID_PROJECT");
        }

        var (bauakte, outcome) = await commands.CreateAsync(new NewBauakte(request.Id, request.Title, request.Street,
            request.PostalCode, request.City, request.Latitude, request.Longitude, request.Description, request.CustomerId,
            request.TemplateKey, request.Status == BauakteStatus.Inquiry ? BauakteStatus.Inquiry : BauakteStatus.Planned,
            request.Project, request.PlannedStart, request.PlannedEnd), cancellationToken);

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

        if (request.PlannedStart is { } start && request.PlannedEnd is { } end && end < start)
        {
            return Validation("Das Ende darf nicht vor dem Beginn liegen.", "INVALID_PERIOD");
        }

        if (request.Status == BauakteStatus.Cancelled && bauakte.Status != BauakteStatus.Cancelled && string.IsNullOrWhiteSpace(request.CancellationReason))
        {
            return Validation("Bitte einen Stornogrund angeben.", "CANCELLATION_REASON_REQUIRED");
        }

        var before = ToDetail(bauakte);
        bauakte.Rename(request.Title);
        if (request.Status == BauakteStatus.Cancelled && !string.IsNullOrWhiteSpace(request.CancellationReason))
        {
            bauakte.Cancel(request.CancellationReason);
        }
        else
        {
            bauakte.ChangeStatus(request.Status);
        }

        bauakte.Schedule(request.PlannedStart, request.PlannedEnd);
        bauakte.ChangeSiteContact(request.SiteContactName, request.SiteContactPhone, request.SiteContactEmail);
        bauakte.ChangeSiteNotes(request.SiteNotes);
        if (request.Project is { } project)
        {
            if (project.Normalize().Validate() is { } projectError)
            {
                return Validation(projectError, "INVALID_PROJECT");
            }

            bauakte.ChangeProject(project);
        }

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
        Guid id, DeleteBauakteRequest request, EstrichOneDbContext db, IBauakteAccess access, IBauakteHistory history, IUserAccounts accounts,
        ICurrentUser currentUser, TimeProvider clock, CancellationToken cancellationToken)
    {
        var bauakte = await (await access.VisibleAsync(db.Set<Bauakte>(), cancellationToken)).SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (bauakte is null)
        {
            return NotFound();
        }

        if (!await accounts.VerifyPasswordAsync(currentUser.UserId, request.Password, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Passwort falsch",
                detail: "Das Passwort ist nicht korrekt. Die Bauakte wurde nicht gelöscht.",
                extensions: new Dictionary<string, object?> { ["code"] = "PASSWORD_CONFIRMATION_FAILED" });
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

        var details = new Dictionary<string, string?>();
        void Change(string field, string? from, string? to)
        {
            if (from == to)
            {
                return;
            }

            details[$"{field}.from"] = Clip(from);
            details[$"{field}.to"] = Clip(to);
        }

        static string? Clip(string? value) => value is { Length: > 300 } ? value[..297] + "…" : value;
        static string? Day(DateOnly? day) => day?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        static string? Join(params string?[] parts) =>
            parts.Any(p => !string.IsNullOrWhiteSpace(p)) ? string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p))) : null;

        Change("title", before.Title, after.Title);
        Change("street", before.Street, after.Street);
        Change("postalCode", before.PostalCode, after.PostalCode);
        Change("city", before.City, after.City);
        Change("latitude", before.Latitude is null ? null : $"{before.Latitude:0.#####}, {before.Longitude:0.#####}",
            after.Latitude is null ? null : $"{after.Latitude:0.#####}, {after.Longitude:0.#####}");
        Change("description", before.Description, after.Description);
        Change("customerId", before.CustomerId?.ToString(), after.CustomerId?.ToString());
        Change("responsibleUserId", before.ResponsibleUserId?.ToString(), after.ResponsibleUserId?.ToString());
        Change("period", Join(Day(before.PlannedStart), Day(before.PlannedEnd)), Join(Day(after.PlannedStart), Day(after.PlannedEnd)));
        Change("siteContact", Join(before.SiteContactName, before.SiteContactPhone, before.SiteContactEmail),
            Join(after.SiteContactName, after.SiteContactPhone, after.SiteContactEmail));
        Change("siteNotes", before.SiteNotes, after.SiteNotes);
        Change("project", ProjectText(before.Project), ProjectText(after.Project));
        var fields = details.Keys.Where(k => k.EndsWith(".from", StringComparison.Ordinal)).Select(k => k[..^5]).ToList();
        if (fields.Count > 0)
        {
            details["fields"] = string.Join(',', fields);
            history.Record(after.Id, BauakteHistoryActions.Updated, details);
        }
    }

    private static string? ProjectText(BauakteProject? p) => p is null || p == BauakteProject.Empty ? null : string.Join(" · ", new[]
    {
        p.ObjectType, p.TotalArea is { } a ? $"{a:0.##} m²" : null, p.ScreedType, p.Construction,
        p.NominalThicknessMm is { } t ? $"{t} mm" : null, p.StrengthClass, p.UnderfloorHeating == true ? "Fußbodenheizung" : null,
    }.Where(x => !string.IsNullOrWhiteSpace(x)));

    internal static BauakteDetail ToDetail(Bauakte b) => new(
        b.Id, b.Number, b.Title, b.Status, b.Street, b.PostalCode, b.City, b.Latitude, b.Longitude, b.Description,
        b.CustomerId, b.TemplateKey, b.ResponsibleUserId, b.CreatedByUserId, b.CreatedAt, b.UpdatedAt, b.Version,
        b.PlannedStart, b.PlannedEnd, b.SiteContactName, b.SiteContactPhone, b.SiteContactEmail, b.SiteNotes, b.CancellationReason, b.Project);

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
