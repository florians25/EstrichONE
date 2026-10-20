using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Contacts;

public sealed record ContactResponse(
    Guid Id, ContactKind Kind, string Name, string? CustomerNumber, string? Street, string? PostalCode, string? City,
    string? Phone, string? Email, string? Notes, long Version);

public sealed record SaveContactRequest(
    [property: Required] Guid Id,
    ContactKind Kind,
    [property: Required, MaxLength(200)] string Name,
    [property: MaxLength(50)] string? CustomerNumber = null,
    [property: MaxLength(200)] string? Street = null,
    [property: MaxLength(10)] string? PostalCode = null,
    [property: MaxLength(100)] string? City = null,
    [property: MaxLength(50)] string? Phone = null,
    [property: MaxLength(320), EmailAddress] string? Email = null,
    [property: MaxLength(4000)] string? Notes = null);

public sealed record UpdateContactRequest(
    long Version,
    ContactKind Kind,
    [property: Required, MaxLength(200)] string Name,
    [property: MaxLength(50)] string? CustomerNumber = null,
    [property: MaxLength(200)] string? Street = null,
    [property: MaxLength(10)] string? PostalCode = null,
    [property: MaxLength(100)] string? City = null,
    [property: MaxLength(50)] string? Phone = null,
    [property: MaxLength(320), EmailAddress] string? Email = null,
    [property: MaxLength(4000)] string? Notes = null);

internal static class ContactEndpoints
{
    private const long MaxImportBytes = 5 * 1024 * 1024;

    public static void Map(IEndpointRouteBuilder api)
    {
        var contacts = api.MapGroup("/contacts").WithTags("Kontakte");

        contacts.MapGet("/", SearchAsync).WithName("SearchContacts")
            .WithSummary("Kunden suchen (Name, Kundennummer, Ort) – z. B. für die Kundenauswahl der Schnellanlage")
            .RequirePermission(Permissions.ContactsRead);

        contacts.MapGet("/{id:guid}", async (Guid id, EstrichOneDbContext db, CancellationToken ct) =>
                await db.Set<Contact>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct) is { } contact
                    ? Results.Ok(ToResponse(contact))
                    : Results.Problem(statusCode: 404, title: "Kontakt nicht gefunden", extensions: new Dictionary<string, object?> { ["code"] = "CONTACT_NOT_FOUND" }))
            .WithName("GetContact").Produces<ContactResponse>().ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.ContactsRead);

        contacts.MapPost("/", CreateAsync).WithName("CreateContact")
            .WithSummary("Kontakt anlegen (idempotent über die ID)")
            .ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.ContactsEdit);

        contacts.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateContact")
            .WithSummary("Kontakt ändern; erfordert die zuletzt gelesene Version (version im Inhalt)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status412PreconditionFailed)
            .RequirePermission(Permissions.ContactsEdit);

        ContactDuplicates.Map(contacts);

        contacts.MapPost("/import", ImportAsync).WithName("ImportContacts")
            .WithSummary("CSV-Import (text/csv im Anfragekörper); mit dryRun=true nur prüfen")
            .Accepts<string>("text/csv")
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .RequirePermission(Permissions.ContactsEdit);
    }

    private static async Task<Ok<List<ContactResponse>>> SearchAsync(EstrichOneDbContext db, CancellationToken cancellationToken,
        [FromQuery] string? search = null, [FromQuery] int? limit = null)
    {
        var query = db.Set<Contact>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + search.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%";
            query = query.Where(c => EF.Functions.ILike(c.Name, pattern, "\\")
                || (c.CustomerNumber != null && EF.Functions.ILike(c.CustomerNumber, pattern, "\\"))
                || (c.City != null && EF.Functions.ILike(c.City, pattern, "\\")));
        }

        var contacts = await query.OrderBy(c => c.Name).Take(Math.Clamp(limit ?? 25, 1, 100)).ToListAsync(cancellationToken);
        return TypedResults.Ok(contacts.Select(ToResponse).ToList());
    }

    private static async Task<Results<Created<ContactResponse>, Ok<ContactResponse>, ProblemHttpResult>> CreateAsync(
        SaveContactRequest request, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var existing = await db.Set<Contact>().IgnoreQueryFilters([QueryFilters.NotDeleted]).AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == request.Id, cancellationToken);
        if (existing is not null)
        {
            return existing.Name == request.Name.Trim() && existing.DeletedAt is null
                ? TypedResults.Ok(ToResponse(existing))
                : TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "ID bereits vergeben",
                    extensions: new Dictionary<string, object?> { ["code"] = "ID_CONFLICT" });
        }

        if (request.CustomerNumber is not null
            && await db.Set<Contact>().AnyAsync(c => c.CustomerNumber == request.CustomerNumber, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Kundennummer bereits vergeben",
                extensions: new Dictionary<string, object?> { ["code"] = "CUSTOMER_NUMBER_TAKEN" });
        }

        var contact = Contact.Create(request.Id, request.Kind, request.Name);
        contact.Update(request.Kind, request.Name, request.CustomerNumber, request.Street, request.PostalCode, request.City,
            request.Phone, request.Email, request.Notes);
        db.Add(contact);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Created($"/api/v1/contacts/{contact.Id}", ToResponse(contact));
    }

    private static async Task<Results<Ok<ContactResponse>, ProblemHttpResult>> UpdateAsync(
        Guid id, UpdateContactRequest request, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var contact = await db.Set<Contact>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Kontakt nicht gefunden",
                extensions: new Dictionary<string, object?> { ["code"] = "CONTACT_NOT_FOUND" });
        }

        if (contact.Version != request.Version)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status412PreconditionFailed, title: "Zwischenzeitlich geändert",
                detail: "Der Kontakt wurde inzwischen geändert. Bitte neu laden.", extensions: new Dictionary<string, object?> { ["code"] = "VERSION_CONFLICT" });
        }

        var number = string.IsNullOrWhiteSpace(request.CustomerNumber) ? null : request.CustomerNumber.Trim();
        if (number is not null && await db.Set<Contact>().AnyAsync(c => c.CustomerNumber == number && c.Id != id, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Kundennummer bereits vergeben",
                extensions: new Dictionary<string, object?> { ["code"] = "CUSTOMER_NUMBER_TAKEN" });
        }

        static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        contact.Update(request.Kind, request.Name, number, Clean(request.Street), Clean(request.PostalCode), Clean(request.City),
            Clean(request.Phone), Clean(request.Email), Clean(request.Notes));
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToResponse(contact));
    }

    private static async Task<Results<Ok<ContactImportResult>, ProblemHttpResult>> ImportAsync(
        HttpRequest request, ContactImport import, CancellationToken cancellationToken, [FromQuery] bool dryRun = true)
    {
        if (request.ContentLength > MaxImportBytes)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Datei zu groß",
                detail: "Höchstens 5 MB je Import.", extensions: new Dictionary<string, object?> { ["code"] = "FILE_TOO_LARGE" });
        }

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > MaxImportBytes)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: "Datei zu groß",
                extensions: new Dictionary<string, object?> { ["code"] = "FILE_TOO_LARGE" });
        }

        return TypedResults.Ok(await import.ImportAsync(buffer.ToArray(), dryRun, cancellationToken));
    }

    internal static ContactResponse ToResponse(Contact c) => new(c.Id, c.Kind, c.Name, c.CustomerNumber, c.Street, c.PostalCode,
        c.City, c.Phone, c.Email, c.Notes, c.Version);
}
