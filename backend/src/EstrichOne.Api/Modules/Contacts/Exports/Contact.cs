using EstrichOne.Api.Infrastructure.Persistence;

namespace EstrichOne.Api.Modules.Contacts.Exports;

public enum ContactKind
{
    Company,
    Person,
}

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
        Phone = phone;
        Email = email;
        Notes = notes;
    }

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
