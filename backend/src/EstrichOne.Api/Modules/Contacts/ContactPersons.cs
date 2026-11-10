using System.ComponentModel.DataAnnotations;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Contacts;

/// <summary>Ansprechpartner anlegen oder ändern (Anlegen über die vom Client erzeugte ID).</summary>
public sealed record SaveContactPersonRequest(
    [property: Required, MaxLength(200)] string Name,
    [property: MaxLength(100)] string? Function = null,
    [property: MaxLength(50)] string? Phone = null,
    [property: MaxLength(320), EmailAddress] string? Email = null);

/// <summary>Ansprechpartner eines Kunden (z. B. Bauleiter eines Bauträgers) pflegen.</summary>
internal static class ContactPersons
{
    public static void Map(RouteGroupBuilder contacts)
    {
        contacts.MapPut("/{id:guid}/persons/{personId:guid}", SaveAsync).WithName("SaveContactPerson")
            .WithSummary("Ansprechpartner anlegen oder ändern (idempotent über die ID)")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.ContactsEdit);

        contacts.MapDelete("/{id:guid}/persons/{personId:guid}", DeleteAsync).WithName("DeleteContactPerson")
            .WithSummary("Ansprechpartner entfernen")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.ContactsEdit);
    }

    private static async Task<Results<Ok<ContactPersonResponse>, ProblemHttpResult>> SaveAsync(
        Guid id, Guid personId, SaveContactPersonRequest request, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Set<Contact>().AnyAsync(c => c.Id == id, cancellationToken))
        {
            return ContactNotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Ungültige Angaben",
                detail: "Bitte den Namen des Ansprechpartners angeben.", extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_FAILED" });
        }

        var person = await db.Set<ContactPerson>().SingleOrDefaultAsync(p => p.Id == personId, cancellationToken);
        if (person is not null && person.ContactId != id)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Ansprechpartner nicht gefunden",
                extensions: new Dictionary<string, object?> { ["code"] = "CONTACT_PERSON_NOT_FOUND" });
        }

        if (person is null)
        {
            person = new ContactPerson(personId, id, request.Name, request.Function, request.Phone, request.Email);
            db.Add(person);
        }
        else
        {
            person.Update(request.Name, request.Function, request.Phone, request.Email);
        }

        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(new ContactPersonResponse(person.Id, person.Name, person.Function, person.Phone, person.Email));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, Guid personId, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var person = await db.Set<ContactPerson>().SingleOrDefaultAsync(p => p.Id == personId && p.ContactId == id, cancellationToken);
        if (person is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Ansprechpartner nicht gefunden",
                extensions: new Dictionary<string, object?> { ["code"] = "CONTACT_PERSON_NOT_FOUND" });
        }

        db.Remove(person);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static ProblemHttpResult ContactNotFound() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Kontakt nicht gefunden",
            extensions: new Dictionary<string, object?> { ["code"] = "CONTACT_NOT_FOUND" });
}
