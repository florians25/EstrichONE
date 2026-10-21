using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Modules.Sync.Exports;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Contacts;

/// <summary>
/// Kunden offline anlegen (docs/sync-protokoll.md, Abschnitt 4): typisch bei der Schnellanlage einer Bauakte auf der
/// Baustelle. Die Operation steht in der Outbox vor <c>bauakte.create</c>, sodass die Bauakte ihren Kunden bereits
/// vorfindet. Erlaubt mit <c>contacts.edit</c> oder – für die Baustelle – mit <c>bauakten.create</c> und
/// <c>contacts.read</c>. Kundennummern vergibt das Büro; hier wird keine gesetzt.
/// </summary>
internal sealed class ContactSync(EstrichOneDbContext db, IUserPermissions permissions) : ISyncOperationHandler
{
    public IReadOnlyCollection<string> OperationTypes { get; } = ["contact.create"];

    public async Task<SyncOperationResult> ApplyAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        var granted = await permissions.GetAsync(cancellationToken);
        if (!granted.Contains(Permissions.ContactsEdit)
            && !(granted.Contains(Permissions.BauaktenCreate) && granted.Contains(Permissions.ContactsRead)))
        {
            return SyncOperationResult.Rejected(op.OpId, "PERMISSION_DENIED", "Für diese Aktion fehlt die Berechtigung.");
        }

        var p = op.Payload;
        var name = Clean(SyncPayload.Read<string>(p, "name"));
        if (name is null)
        {
            throw new SyncValidationException("Der Kunde braucht einen Namen.");
        }

        var kind = p.ContainsKey("kind") ? SyncPayload.Read<ContactKind>(p, "kind") : ContactKind.Person;
        var street = Clean(SyncPayload.Read<string>(p, "street"));
        var postalCode = Clean(SyncPayload.Read<string>(p, "postalCode"));
        var city = Clean(SyncPayload.Read<string>(p, "city"));
        var phone = Clean(SyncPayload.Read<string>(p, "phone"));
        var email = Clean(SyncPayload.Read<string>(p, "email"));
        var notes = Clean(SyncPayload.Read<string>(p, "notes"));
        Validate(name, street, postalCode, city, phone, email, notes);

        var existing = await db.Set<Contact>().IgnoreQueryFilters([QueryFilters.NotDeleted]).AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == op.EntityId, cancellationToken);
        if (existing is not null)
        {
            // Gleiche Anlage erneut (z. B. nach verlorener Antwort mit neuer opId): unverändert bestätigen.
            return existing.Name == name && existing.DeletedAt is null
                ? new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: existing.Version,
                    Entity: SyncPayload.ToNode(ContactEndpoints.ToResponse(existing)))
                : SyncOperationResult.Rejected(op.OpId, "ID_CONFLICT", "Unter dieser ID existiert bereits ein anderer Kontakt.");
        }

        var contact = Contact.Create(op.EntityId, kind, name);
        contact.Update(kind, name, null, street, postalCode, city, phone, email, notes);
        // Erweiterte Felder (neuere Apps); ältere senden nur phone/email.
        var details = new SyncContactDetails(
            p.ContainsKey("phones") ? SyncPayload.Read<List<ContactPhone>>(p, "phones") : null,
            p.ContainsKey("emails") ? SyncPayload.Read<List<ContactEmail>>(p, "emails") : null,
            Clean(SyncPayload.Read<string>(p, "salutation")), Clean(SyncPayload.Read<string>(p, "title")),
            p.ContainsKey("billingAddress") ? SyncPayload.Read<ContactAddress>(p, "billingAddress") : null,
            p.ContainsKey("deliveryAddress") ? SyncPayload.Read<ContactAddress>(p, "deliveryAddress") : null,
            Clean(SyncPayload.Read<string>(p, "vatId")), Clean(SyncPayload.Read<string>(p, "taxNumber")),
            p.ContainsKey("preferredChannel") ? SyncPayload.Read<ContactChannel?>(p, "preferredChannel") : null,
            p.ContainsKey("birthday") ? SyncPayload.Read<DateOnly?>(p, "birthday") : null,
            Clean(SyncPayload.Read<string>(p, "website")));
        if ((details.Phones ?? []).Count > 10 || (details.Emails ?? []).Count > 10
            || (details.Phones ?? []).Any(x => x.Number is { Length: > 50 }) || (details.Emails ?? []).Any(x => x.Address is { Length: > 320 }))
        {
            throw new SyncValidationException("Zu viele oder zu lange Telefonnummern/E-Mail-Adressen.");
        }

        ContactEndpoints.ApplyDetails(contact, details);
        db.Add(contact);
        await db.SaveChangesAsync(cancellationToken);
        return new SyncOperationResult(op.OpId, SyncOperationStatus.Applied, Version: contact.Version,
            Entity: SyncPayload.ToNode(ContactEndpoints.ToResponse(contact)));
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Validate(string name, string? street, string? postalCode, string? city, string? phone, string? email, string? notes)
    {
        var problem = name.Length > 200 ? "Der Name ist länger als 200 Zeichen."
            : street is { Length: > 200 } ? "Die Straße ist länger als 200 Zeichen."
            : postalCode is { Length: > 10 } ? "Die PLZ ist länger als 10 Zeichen."
            : city is { Length: > 100 } ? "Der Ort ist länger als 100 Zeichen."
            : phone is { Length: > 50 } ? "Die Telefonnummer ist länger als 50 Zeichen."
            : email is not null && (email.Length > 320 || !email.Contains('@', StringComparison.Ordinal) || email.Contains(' ', StringComparison.Ordinal))
                ? $"Ungültige E-Mail-Adresse „{email}“."
            : notes is { Length: > 4000 } ? "Die Notiz ist länger als 4000 Zeichen."
            : null;
        if (problem is not null)
        {
            throw new SyncValidationException(problem);
        }
    }
}

internal sealed record SyncContactDetails(
    IReadOnlyList<ContactPhone>? Phones, IReadOnlyList<ContactEmail>? Emails, string? Salutation, string? Title,
    ContactAddress? BillingAddress, ContactAddress? DeliveryAddress, string? VatId, string? TaxNumber,
    ContactChannel? PreferredChannel, DateOnly? Birthday, string? Website) : IContactDetailsRequest;
