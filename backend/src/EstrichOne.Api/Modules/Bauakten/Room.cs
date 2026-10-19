using EstrichOne.Api.Infrastructure.Persistence;

namespace EstrichOne.Api.Modules.Bauakten;

public enum RoomStatus
{
    Planned,
    /// <summary>Estrich eingebracht.</summary>
    Laid,
    /// <summary>Belegreif (CM-Messung bestanden).</summary>
    Ready,
}

/// <summary>Raum mit Fläche und Estrichaufbau.</summary>
public sealed class Room : IVersionedEntity, ISoftDeletable
{
    public Guid Id { get; private set; }
    public Guid BauakteId { get; private set; }
    public Guid? SectionId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Floor { get; private set; }
    public decimal? AreaSquareMeters { get; private set; }
    public Guid? ScreedTypeId { get; private set; }
    public int? ThicknessMillimeters { get; private set; }
    public int? InsulationMillimeters { get; private set; }
    public bool UnderfloorHeating { get; private set; }
    public RoomStatus Status { get; private set; } = RoomStatus.Planned;
    public string? Notes { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private Room()
    {
    }

    public static Room Create(Guid id, Guid bauakteId, string name) =>
        new() { Id = id, BauakteId = bauakteId, Name = name.Trim() };

    public void Update(Guid? sectionId, string name, string? floor, decimal? areaSquareMeters, string? notes)
    {
        if (areaSquareMeters is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(areaSquareMeters), "Die Fläche darf nicht negativ sein.");
        }

        SectionId = sectionId;
        Name = name.Trim();
        Floor = floor;
        AreaSquareMeters = areaSquareMeters is null ? null : decimal.Round(areaSquareMeters.Value, 2);
        Notes = notes;
    }

    public void ChangeBuildup(Guid? screedTypeId, int? thicknessMillimeters, int? insulationMillimeters, bool underfloorHeating)
    {
        ScreedTypeId = screedTypeId;
        ThicknessMillimeters = thicknessMillimeters;
        InsulationMillimeters = insulationMillimeters;
        UnderfloorHeating = underfloorHeating;
    }

    public void ChangeStatus(RoomStatus status) => Status = status;

    public void Delete(DateTimeOffset at) => DeletedAt ??= at;
}

public enum ScreedBinder
{
    /// <summary>CT – Zementestrich.</summary>
    Cement,
    /// <summary>CA – Calciumsulfatestrich (Anhydrit).</summary>
    CalciumSulfate,
    /// <summary>AS – Gussasphaltestrich.</summary>
    MasticAsphalt,
    /// <summary>MA – Magnesiaestrich.</summary>
    Magnesia,
    /// <summary>SR – Kunstharzestrich.</summary>
    SyntheticResin,
}

/// <summary>Estrichart als Stammdatum, z. B. „Zementestrich CT-C25-F4“.</summary>
public sealed class ScreedType : IVersionedEntity
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>Kurzbezeichnung nach DIN EN 13813, z. B. <c>CT-C25-F4</c>.</summary>
    public string? Designation { get; private set; }
    public ScreedBinder Binder { get; private set; }
    public bool IsActive { get; private set; } = true;

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private ScreedType()
    {
    }

    public static ScreedType Create(Guid id, string name, string? designation, ScreedBinder binder) =>
        new() { Id = id, Name = name.Trim(), Designation = designation, Binder = binder };

    public void Deactivate() => IsActive = false;
}
