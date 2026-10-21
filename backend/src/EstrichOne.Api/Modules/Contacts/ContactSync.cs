using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// <summary>Felder, die <c>contact.update</c> ändern darf (feldweise zusammengeführt wie bei der Bauakte).</summary>
    private static readonly HashSet<string> EditableFields =
        ["kind", "name", "street", "postalCode", "city", "notes", "phones", "emails", "salutation", "title", "preferredChannel", "website"];

    public IReadOnlyCollection<string> OperationTypes { get; } = ["contact.create", "contact.update"];

    public async Task<SyncOperationResult> ApplyAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        if (op.Type == "contact.update")
        {
            return await UpdateAsync(op, cancellationToken);
        }

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

    /// <summary>
    /// Kunden in der App bearbeiten (Recht <c>contacts.edit</c>): <c>changes</c>/<c>base</c> wie bei <c>bauakte.update</c>;
    /// zwischenzeitlich im Büro geänderte Felder werden nicht überschrieben (FIELD_CONFLICT). Kundennummer, Steuerdaten,
    /// abweichende Anschriften und Ansprechpartner pflegt weiterhin das Büro.
    /// </summary>
    private async Task<SyncOperationResult> UpdateAsync(SyncOperation op, CancellationToken cancellationToken)
    {
        if (!await permissions.HasAsync(Permissions.ContactsEdit, cancellationToken))
        {
            return SyncOperationResult.Rejected(op.OpId, "PERMISSION_DENIED", "Für diese Aktion fehlt die Berechtigung.");
        }

        var contact = await db.Set<Contact>().SingleOrDefaultAsync(c => c.Id == op.EntityId, cancellationToken);
        if (contact is null)
        {
            return SyncOperationResult.Rejected(op.OpId, "NOT_FOUND", "Der Kontakt existiert nicht (mehr).");
        }

        var current = (JsonObject)SyncPayload.ToNode(ContactEndpoints.ToResponse(contact))!;
        var (accepted, conflicts) = SyncPayload.Merge(op.Payload, current.ToDictionary(pair => pair.Key, pair => pair.Value), EditableFields);
        var changes = (JsonObject)op.Payload["changes"]!;
        T Value<T>(string field, T fallback) => accepted.Contains(field) ? changes[field].Deserialize<T>(SyncPayload.Json) ?? fallback : fallback;
        string? Text(string field, string? fallback) => accepted.Contains(field) ? Clean(changes[field]?.GetValue<string>()) : fallback;

        var name = Text("name", contact.Name) ?? throw new SyncValidationException("Bitte einen Namen angeben.");
        var kind = Value("kind", contact.Kind);
        var street = Text("street", contact.Street);
        var postalCode = Text("postalCode", contact.PostalCode);
        var city = Text("city", contact.City);
        var notes = Text("notes", contact.Notes);
        Validate(name, street, postalCode, city, null, null, notes);
        var phones = accepted.Contains("phones") ? Value<List<ContactPhone>>("phones", []) : contact.Phones;
        var emails = accepted.Contains("emails") ? Value<List<ContactEmail>>("emails", []) : contact.Emails;
        if (phones.Count > 10 || emails.Count > 10 || phones.Any(x => x.Number is { Length: > 50 } || x.Label is { Length: > 50 })
            || emails.Any(x => x.Address is { Length: > 320 } || x.Label is { Length: > 50 }
                || (!string.IsNullOrWhiteSpace(x.Address) && !x.Address.Contains('@', StringComparison.Ordinal))))
        {
            throw new SyncValidationException("Zu viele, zu lange oder ungültige Telefonnummern/E-Mail-Adressen.");
        }

        var salutation = Text("salutation", contact.Salutation);
        var title = Text("title", contact.Title);
        var website = Text("website", contact.Website);
        if (salutation is { Length: > 50 } || title is { Length: > 50 } || website is { Length: > 300 })
        {
            throw new SyncValidationException("Anrede, Titel oder Website ist zu lang.");
        }

        var channel = accepted.Contains("preferredChannel") ? Value<ContactChannel?>("preferredChannel", null) : contact.PreferredChannel;
        contact.Update(kind, name, contact.CustomerNumber, street, postalCode, city, contact.Phone, contact.Email, notes);
        contact.UpdateDetails(new ContactDetails(phones, emails, salutation, title, contact.BillingAddress, contact.DeliveryAddress,
            contact.VatId, contact.TaxNumber, channel, contact.Birthday, website));
        await db.SaveChangesAsync(cancellationToken);
        return new SyncOperationResult(op.OpId, conflicts.Count == 0 ? SyncOperationStatus.Applied : SyncOperationStatus.Conflict,
            Code: conflicts.Count == 0 ? null : "FIELD_CONFLICT",
            Message: conflicts.Count == 0 ? null : "Einige Felder wurden inzwischen von jemand anderem geändert.",
            Version: contact.Version, Conflicts: conflicts.Count == 0 ? null : conflicts,
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
