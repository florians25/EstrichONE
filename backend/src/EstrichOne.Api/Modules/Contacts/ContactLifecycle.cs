using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Contacts;

/// <summary>Bezüge eines Kunden: Ohne Bezüge darf er gelöscht werden, sonst nur archiviert oder anonymisiert.</summary>
/// <param name="ConfirmText">Text, den der Benutzer zur Bestätigung eintippen muss (Kundennummer, ohne Nummer der Name).</param>
public sealed record CustomerReferencesResponse(
    Guid Id, string Name, string? CustomerNumber, bool Deletable, string ConfirmText, bool Archived, bool Anonymized,
    IReadOnlyList<CustomerReference> References);

/// <summary>Anonymisieren (DSGVO) mit doppelter Bestätigung: Kundennummer bzw. Name eintippen und Unwiderruflichkeit bestätigen.</summary>
public sealed record AnonymizeContactRequest([property: Required, MaxLength(200)] string Confirm, bool Irreversible);

/// <summary>
/// Kunden löschen, archivieren, reaktivieren und anonymisieren. Löschen nur ohne Bezüge (Bauakten auch im Papierkorb,
/// Projektbeteiligungen, Anrufe/Anfragen; künftige Module melden sich über <see cref="ICustomerReferenceSource"/>).
/// Gelöscht wird endgültig: Name, Anschriften, Telefon, E-Mail, Notiz und Ansprechpartner sind danach weg; es bleibt nur
/// ein Tombstone mit ID und Kundennummer, damit die Apps den Kunden beim Abgleich entfernen und die Nummer nicht neu
/// vergeben wird.
/// </summary>
internal static class ContactLifecycle
{
    public const string HasReferencesCode = "CUSTOMER_HAS_REFERENCES";
    public const string ConfirmationCode = "CONFIRMATION_MISMATCH";

    public static void Map(RouteGroupBuilder contacts)
    {
        contacts.MapGet("/{id:guid}/references", ReferencesAsync).WithName("GetContactReferences")
            .WithSummary("Bezüge des Kunden (Bauakten, Projektbeteiligungen, Anrufe …) und ob er gelöscht werden darf")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.ContactsRead);

        contacts.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteContact")
            .WithSummary("Kunden ohne Bezüge endgültig löschen; confirm = Kundennummer (ohne Nummer: Name). Mit Bezügen 409 CUSTOMER_HAS_REFERENCES")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.ContactsDelete);

        contacts.MapPost("/{id:guid}/archive", ArchiveAsync).WithName("ArchiveContact")
            .WithSummary("Kunden archivieren (inaktiv): bleibt mit allen Bezügen erhalten, erscheint nicht mehr in Auswahllisten und Suche")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequirePermission(Permissions.ContactsEdit);

        contacts.MapPost("/{id:guid}/reactivate", ReactivateAsync).WithName("ReactivateContact")
            .WithSummary("Archivierten Kunden wieder aktiv setzen")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict)
            .RequirePermission(Permissions.ContactsEdit);

        contacts.MapPost("/{id:guid}/anonymize", AnonymizeAsync).WithName("AnonymizeContact")
            .WithSummary("Kunden anonymisieren (DSGVO): Name und Kontaktdaten werden unwiderruflich ersetzt, Bezüge bleiben; doppelte Bestätigung")
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .RequirePermission(Permissions.ContactsAnonymize);
    }

    /// <summary>Kundennummer, ohne Nummer der Name.</summary>
    public static string ConfirmText(Contact contact) => string.IsNullOrWhiteSpace(contact.CustomerNumber) ? contact.Name : contact.CustomerNumber;

    private static bool Confirmed(Contact contact, string? confirm) =>
        !string.IsNullOrWhiteSpace(confirm) && string.Equals(confirm.Trim(), ConfirmText(contact).Trim(), StringComparison.OrdinalIgnoreCase);

    public static async Task<List<CustomerReference>> FindReferencesAsync(
        IEnumerable<ICustomerReferenceSource> sources, Guid id, CancellationToken cancellationToken)
    {
        var references = new List<CustomerReference>();
        foreach (var source in sources)
        {
            references.AddRange((await source.FindAsync(id, cancellationToken)).Where(r => r.Count > 0));
        }

        return references;
    }

    private static async Task<Results<Ok<CustomerReferencesResponse>, ProblemHttpResult>> ReferencesAsync(
        Guid id, EstrichOneDbContext db, IEnumerable<ICustomerReferenceSource> sources, CancellationToken cancellationToken)
    {
        var contact = await db.Set<Contact>().AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return NotFound();
        }

        var references = await FindReferencesAsync(sources, id, cancellationToken);
        return TypedResults.Ok(new CustomerReferencesResponse(contact.Id, contact.Name, contact.CustomerNumber, references.Count == 0,
            ConfirmText(contact), contact.ArchivedAt is not null, contact.AnonymizedAt is not null, references));
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(
        Guid id, EstrichOneDbContext db, IEnumerable<ICustomerReferenceSource> sources, IAuditLog audit, ICurrentUser currentUser,
        TimeProvider clock, CancellationToken cancellationToken, [FromQuery] string? confirm = null)
    {
        var contact = await db.Set<Contact>().Include(c => c.Persons).SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return NotFound();
        }

        if (!Confirmed(contact, confirm))
        {
            return ConfirmationMismatch(contact);
        }

        var referenceSources = sources.ToList();
        var references = await FindReferencesAsync(referenceSources, id, cancellationToken);
        if (references.Count > 0)
        {
            return HasReferences(references);
        }

        var number = contact.CustomerNumber;
        var persons = contact.Persons.Count;
        foreach (var source in referenceSources)
        {
            await source.AnonymizeAsync(id, "Gelöschter Kunde", cancellationToken);
        }

        contact.Purge(clock.GetUtcNow());
        audit.Record("contact.deleted", currentUser.UserId, "contact", id.ToString(), new Dictionary<string, string?>
        {
            ["customerNumber"] = number, ["persons"] = persons.ToString(CultureInfo.InvariantCulture),
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ContactResponse>, ProblemHttpResult>> ArchiveAsync(
        Guid id, EstrichOneDbContext db, IAuditLog audit, ICurrentUser currentUser, TimeProvider clock, CancellationToken cancellationToken)
    {
        var contact = await db.Set<Contact>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return NotFound();
        }

        if (contact.ArchivedAt is null)
        {
            contact.Archive(clock.GetUtcNow());
            audit.Record("contact.archived", currentUser.UserId, "contact", id.ToString(),
                new Dictionary<string, string?> { ["customerNumber"] = contact.CustomerNumber });
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(ContactEndpoints.ToResponse(contact));
    }

    private static async Task<Results<Ok<ContactResponse>, ProblemHttpResult>> ReactivateAsync(
        Guid id, EstrichOneDbContext db, IAuditLog audit, ICurrentUser currentUser, CancellationToken cancellationToken)
    {
        var contact = await db.Set<Contact>().SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return NotFound();
        }

        if (contact.AnonymizedAt is not null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Kunde ist anonymisiert",
                detail: "Ein anonymisierter Kunde kann nicht wieder aktiv gesetzt werden. Bitte bei Bedarf neu anlegen.",
                extensions: new Dictionary<string, object?> { ["code"] = "CUSTOMER_ANONYMIZED" });
        }

        if (contact.ArchivedAt is not null)
        {
            contact.Reactivate();
            audit.Record("contact.reactivated", currentUser.UserId, "contact", id.ToString(),
                new Dictionary<string, string?> { ["customerNumber"] = contact.CustomerNumber });
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Ok(ContactEndpoints.ToResponse(contact));
    }

    private static async Task<Results<Ok<ContactResponse>, ProblemHttpResult>> AnonymizeAsync(
        Guid id, AnonymizeContactRequest request, EstrichOneDbContext db, IEnumerable<ICustomerReferenceSource> sources, IAuditLog audit,
        ICurrentUser currentUser, TimeProvider clock, CancellationToken cancellationToken)
    {
        var contact = await db.Set<Contact>().Include(c => c.Persons).SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return NotFound();
        }

        if (contact.AnonymizedAt is not null)
        {
            return TypedResults.Ok(ContactEndpoints.ToResponse(contact));
        }

        if (!Confirmed(contact, request.Confirm))
        {
            return ConfirmationMismatch(contact);
        }

        if (!request.Irreversible)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Bestätigung fehlt",
                detail: "Bitte bestätigen, dass die Anonymisierung nicht rückgängig gemacht werden kann.",
                extensions: new Dictionary<string, object?> { ["code"] = ConfirmationCode });
        }

        var references = await FindReferencesAsync(sources, id, cancellationToken);
        contact.Anonymize(clock.GetUtcNow());
        foreach (var source in sources)
        {
            await source.AnonymizeAsync(id, contact.Name, cancellationToken);
        }

        audit.Record("contact.anonymized", currentUser.UserId, "contact", id.ToString(), new Dictionary<string, string?>
        {
            ["customerNumber"] = contact.CustomerNumber,
            ["references"] = string.Join(", ", references.Select(r => $"{r.Count.ToString(CultureInfo.InvariantCulture)} {r.Label}")),
        });
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ContactEndpoints.ToResponse(contact));
    }

    private static ProblemHttpResult NotFound() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Kontakt nicht gefunden",
            extensions: new Dictionary<string, object?> { ["code"] = "CONTACT_NOT_FOUND" });

    private static ProblemHttpResult ConfirmationMismatch(Contact contact) =>
        TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Bestätigung stimmt nicht",
            detail: string.IsNullOrWhiteSpace(contact.CustomerNumber)
                ? "Bitte zur Bestätigung den Namen des Kunden eintippen."
                : "Bitte zur Bestätigung die Kundennummer eintippen.",
            extensions: new Dictionary<string, object?> { ["code"] = ConfirmationCode });

    /// <summary>409 mit Liste der Bezüge, z. B. „3 Bauakten, 2 Anrufe und Anfragen“.</summary>
    public static ProblemHttpResult HasReferences(IReadOnlyList<CustomerReference> references) =>
        TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Kunde hat verknüpfte Daten",
            detail: $"Der Kunde kann nicht gelöscht werden. Noch verknüpft: {Describe(references)}. Stattdessen archivieren (inaktiv setzen) oder – mit Berechtigung – anonymisieren.",
            extensions: new Dictionary<string, object?> { ["code"] = HasReferencesCode, ["references"] = references });

    public static string Describe(IReadOnlyList<CustomerReference> references)
    {
        var parts = references.Select(r => $"{r.Count.ToString(CultureInfo.InvariantCulture)} {r.Label}").ToList();
        return parts.Count <= 1 ? string.Concat(parts) : $"{string.Join(", ", parts[..^1])} und {parts[^1]}";
    }
}
