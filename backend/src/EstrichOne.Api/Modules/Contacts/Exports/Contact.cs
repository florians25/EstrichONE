using EstrichOne.Api.Infrastructure.Persistence;

namespace EstrichOne.Api.Modules.Contacts.Exports;

public enum ContactKind
{
    Company,
    Person,
}

public enum ContactPhoneKind
{
    Mobile,
    Landline,
    Office,
    Fax,
    Private,
}

public enum ContactChannel
{
    Phone,
    Mobile,
    Email,
    Letter,
}

/// <param name="Label">Optionale Bezeichnung, z. B. „Bauleitung“ oder „nur vormittags“.</param>
public sealed record ContactPhone(ContactPhoneKind Kind, string Number, string? Label = null);

public sealed record ContactEmail(string Address, string? Label = null);

/// <summary>Abweichende Rechnungs- oder Lieferanschrift.</summary>
public sealed record ContactAddress(string? Name, string? Street, string? PostalCode, string? City);

/// <summary>Erweiterte Kundendaten (Runde 3b); Telefon und E-Mail als Listen.</summary>
public sealed record ContactDetails(
    IReadOnlyList<ContactPhone> Phones, IReadOnlyList<ContactEmail> Emails, string? Salutation, string? Title,
    ContactAddress? BillingAddress, ContactAddress? DeliveryAddress, string? VatId, string? TaxNumber,
    ContactChannel? PreferredChannel, DateOnly? Birthday, string? Website);

/// <summary>Kunde oder Projektbeteiligter (Bauherr, Bauträger, Architekt, Generalunternehmer …).</summary>
public sealed class Contact : IVersionedEntity, ISoftDeletable
{
    public Guid Id { get; private set; }
    public ContactKind Kind { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? CustomerNumber { get; private set; }
    public string? Street { get; private set; }
    public string? PostalCode { get; private set; }
    public string? City { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>Alle Telefonnummern; die erste steht zusätzlich in <see cref="Phone"/> (ältere Apps).</summary>
    public List<ContactPhone> Phones { get; private set; } = [];

    /// <summary>Alle E-Mail-Adressen; die erste steht zusätzlich in <see cref="Email"/>.</summary>
    public List<ContactEmail> Emails { get; private set; } = [];

    /// <summary>Anrede, z. B. „Herr“, „Frau“, „Familie“, „Firma“.</summary>
    public string? Salutation { get; private set; }

    /// <summary>Titel, z. B. „Dr.“.</summary>
    public string? Title { get; private set; }
    public ContactAddress? BillingAddress { get; private set; }
    public ContactAddress? DeliveryAddress { get; private set; }
    public string? VatId { get; private set; }
    public string? TaxNumber { get; private set; }
    public ContactChannel? PreferredChannel { get; private set; }

    /// <summary>Geburtstag (nur bei Privatkunden, optional).</summary>
    public DateOnly? Birthday { get; private set; }
    public string? Website { get; private set; }
    public List<ContactPerson> Persons { get; private set; } = [];

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private Contact()
    {
    }

    public static Contact Create(Guid id, ContactKind kind, string name) =>
        new() { Id = id, Kind = kind, Name = Required(name) };

    public void Update(ContactKind kind, string name, string? customerNumber, string? street, string? postalCode,
        string? city, string? phone, string? email, string? notes)
    {
        Kind = kind;
        Name = Required(name);
        CustomerNumber = customerNumber;
        Street = street;
        PostalCode = postalCode;
        City = city;
        Notes = notes;

        // Einzelfelder (ältere Apps, Import): ersetzen die erste Nummer bzw. Adresse, weitere bleiben erhalten.
        if (phone != Phone)
        {
            var rest = Phones.Skip(Phones.Count > 0 && Phones[0].Number == Phone ? 1 : 0).ToList();
            Phones = phone is null ? rest : [new ContactPhone(Phones.FirstOrDefault()?.Kind ?? ContactPhoneKind.Landline, phone, Phones.FirstOrDefault()?.Label), .. rest];
        }

        if (email != Email)
        {
            var rest = Emails.Skip(Emails.Count > 0 && Emails[0].Address == Email ? 1 : 0).ToList();
            Emails = email is null ? rest : [new ContactEmail(email, Emails.FirstOrDefault()?.Label), .. rest];
        }

        Phone = Phones.FirstOrDefault()?.Number;
        Email = Emails.FirstOrDefault()?.Address;
    }

    /// <summary>Erweiterte Daten setzen; <see cref="Phone"/> und <see cref="Email"/> folgen der ersten Nummer/Adresse.</summary>
    public void UpdateDetails(ContactDetails details)
    {
        Phones = details.Phones.Where(p => !string.IsNullOrWhiteSpace(p.Number))
            .Select(p => p with { Number = p.Number.Trim(), Label = Clean(p.Label) }).DistinctBy(p => p.Number).ToList();
        Emails = details.Emails.Where(e => !string.IsNullOrWhiteSpace(e.Address))
            .Select(e => e with { Address = e.Address.Trim(), Label = Clean(e.Label) }).DistinctBy(e => e.Address, StringComparer.OrdinalIgnoreCase).ToList();
        Phone = Phones.FirstOrDefault()?.Number;
        Email = Emails.FirstOrDefault()?.Address;
        Salutation = Clean(details.Salutation);
        Title = Clean(details.Title);
        BillingAddress = Address(details.BillingAddress);
        DeliveryAddress = Address(details.DeliveryAddress);
        VatId = Clean(details.VatId);
        TaxNumber = Clean(details.TaxNumber);
        PreferredChannel = details.PreferredChannel;
        Birthday = Kind == ContactKind.Person ? details.Birthday : null;
        Website = Clean(details.Website);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ContactAddress? Address(ContactAddress? a) =>
        a is null || (Clean(a.Street) ?? Clean(a.PostalCode) ?? Clean(a.City) ?? Clean(a.Name)) is null
            ? null : new ContactAddress(Clean(a.Name), Clean(a.Street), Clean(a.PostalCode), Clean(a.City));

    public void Delete(DateTimeOffset at) => DeletedAt ??= at;

    private static string Required(string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Name fehlt.", nameof(value)) : value.Trim();
}

/// <summary>Ansprechpartner eines Kontakts.</summary>
public sealed class ContactPerson
{
    public Guid Id { get; private set; }
    public Guid ContactId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Function { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }

    private ContactPerson()
    {
    }

    public ContactPerson(Guid id, Guid contactId, string name, string? function, string? phone, string? email)
    {
        Id = id;
        ContactId = contactId;
        Name = name.Trim();
        Function = function;
        Phone = phone;
        Email = email;
    }
}
