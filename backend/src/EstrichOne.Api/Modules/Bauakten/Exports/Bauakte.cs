using EstrichOne.Api.Infrastructure.Persistence;

namespace EstrichOne.Api.Modules.Bauakten.Exports;

public enum BauakteStatus
{
    /// <summary>Anfrage / noch nicht beauftragt.</summary>
    Inquiry,
    Planned,
    InProgress,
    /// <summary>Estrich eingebracht, Trocknung bis zur Belegreife.</summary>
    Drying,
    Completed,
    Cancelled,
}

/// <summary>
/// Bauakte = Bauvorhaben mit Baustellenadresse (ADR 0005). Einziges Pflichtfeld ist <see cref="Title"/>,
/// damit eine Bauakte auf der Baustelle in Sekunden angelegt werden kann (docs/reengineering/09).
/// Die <see cref="Number"/> vergibt ausschließlich der Server.
/// </summary>
public sealed class Bauakte : IVersionedEntity, ISoftDeletable
{
    public const int MaxTitleLength = 200;

    public Guid Id { get; private set; }

    /// <summary>Bauaktennummer <c>JJJJ-NNN</c>; <c>null</c>, solange eine offline angelegte Akte nicht übertragen ist.</summary>
    public string? Number { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public BauakteStatus Status { get; private set; } = BauakteStatus.Planned;
    public string? Street { get; private set; }
    public string? PostalCode { get; private set; }
    public string? City { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public string? Description { get; private set; }
    public Guid? CustomerId { get; private set; }
    public string? TemplateKey { get; private set; }
    public Guid? ResponsibleUserId { get; private set; }
    public Guid CreatedByUserId { get; private set; }

    public List<BauakteSection> Sections { get; private set; } = [];
    public List<BauakteMember> Members { get; private set; } = [];

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private Bauakte()
    {
    }

    public static Bauakte Create(Guid id, string title, Guid createdByUserId)
    {
        var bauakte = new Bauakte { Id = id, CreatedByUserId = createdByUserId, ResponsibleUserId = createdByUserId };
        bauakte.Rename(title);
        bauakte.Members.Add(new BauakteMember(id, createdByUserId, BauakteMemberRole.Responsible));
        return bauakte;
    }

    public void Rename(string title)
    {
        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Das Bauvorhaben braucht eine Bezeichnung.", nameof(title));
        }

        Title = trimmed.Length > MaxTitleLength ? trimmed[..MaxTitleLength] : trimmed;
    }

    public void AssignNumber(string number)
    {
        if (Number is not null)
        {
            throw new InvalidOperationException($"Die Bauakte hat bereits die Nummer {Number}.");
        }

        Number = number;
    }

    public void ChangeAddress(string? street, string? postalCode, string? city, double? latitude, double? longitude)
    {
        Street = street;
        PostalCode = postalCode;
        City = city;
        Latitude = latitude;
        Longitude = longitude;
    }

    public void ChangeStatus(BauakteStatus status) => Status = status;

    public void Describe(string? description) => Description = description;

    public void AssignCustomer(Guid? customerId) => CustomerId = customerId;

    public void ApplyTemplate(string? templateKey) => TemplateKey = templateKey;

    public void AssignResponsible(Guid? userId) => ResponsibleUserId = userId;

    public void Delete(DateTimeOffset at) => DeletedAt ??= at;

    public void Restore() => DeletedAt = null;
}

/// <summary>Bauabschnitt innerhalb einer Bauakte (Haus A/B, Geschoss, Wohnung).</summary>
public sealed class BauakteSection
{
    public Guid Id { get; private set; }
    public Guid BauakteId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }

    private BauakteSection()
    {
    }

    public BauakteSection(Guid id, Guid bauakteId, string name, int sortOrder)
    {
        Id = id;
        BauakteId = bauakteId;
        Name = name.Trim();
        SortOrder = sortOrder;
    }
}

public enum BauakteMemberRole
{
    Responsible,
    Member,
}

/// <summary>Benutzer mit Zugriff auf eine Bauakte, auch ohne das Recht „alle Bauakten sehen“.</summary>
public sealed class BauakteMember(Guid bauakteId, Guid userId, BauakteMemberRole role)
{
    public Guid BauakteId { get; private set; } = bauakteId;
    public Guid UserId { get; private set; } = userId;
    public BauakteMemberRole Role { get; private set; } = role;
}
