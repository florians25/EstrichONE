using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Materials;

public enum MaterialCategory
{
    Screed,
    Binder,
    Aggregate,
    Insulation,
    Membrane,
    EdgeStrip,
    Additive,
    Other,
}

public enum OrderStatus
{
    /// <summary>Entwurf: noch nicht beim Lieferanten.</summary>
    Draft,
    Ordered,
    Confirmed,
    Delivered,
    Cancelled,
}

/// <summary>Lieferant (Baustoffhandel, Werk, Transportbeton).</summary>
public sealed class Supplier : IVersionedEntity, ISoftDeletable
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>Unsere Kundennummer beim Lieferanten (steht auf der Bestellung).</summary>
    public string? CustomerNumber { get; private set; }
    public string? ContactName { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Street { get; private set; }
    public string? PostalCode { get; private set; }
    public string? City { get; private set; }

    /// <summary>Hinweise zum Bestellen, z. B. „bis 12 Uhr für Lieferung am Folgetag“.</summary>
    public string? OrderNotes { get; private set; }
    public bool IsActive { get; private set; } = true;

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private Supplier()
    {
    }

    public static Supplier Create(Guid id) => new() { Id = id };

    public void Update(string name, string? customerNumber, string? contactName, string? phone, string? email, string? street, string? postalCode,
        string? city, string? orderNotes, bool isActive)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Bitte den Namen des Lieferanten angeben.", nameof(name)) : name.Trim();
        CustomerNumber = Text.Blank(customerNumber);
        ContactName = Text.Blank(contactName);
        Phone = Text.Blank(phone);
        Email = Text.Blank(email);
        Street = Text.Blank(street);
        PostalCode = Text.Blank(postalCode);
        City = Text.Blank(city);
        OrderNotes = Text.Blank(orderNotes);
        IsActive = isActive;
    }

    public void Delete(DateTimeOffset at) => DeletedAt ??= at;
}

/// <summary>Artikel im Materialstamm (Estrichmörtel, Dämmung, Folie, Randdämmstreifen …).</summary>
public sealed class Material : IVersionedEntity, ISoftDeletable
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? ArticleNumber { get; private set; }
    public MaterialCategory Category { get; private set; }
    public string Unit { get; private set; } = "Stk";
    public Guid? SupplierId { get; private set; }

    /// <summary>Einkaufspreis je Einheit (netto), optional.</summary>
    public decimal? Price { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private Material()
    {
    }

    public static Material Create(Guid id) => new() { Id = id };

    public void Update(string name, string? articleNumber, MaterialCategory category, string unit, Guid? supplierId, decimal? price, string? notes,
        bool isActive)
    {
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Bitte die Bezeichnung angeben.", nameof(name)) : name.Trim();
        Unit = string.IsNullOrWhiteSpace(unit) ? throw new ArgumentException("Bitte die Einheit angeben.", nameof(unit)) : unit.Trim();
        ArticleNumber = Text.Blank(articleNumber);
        Category = category;
        SupplierId = supplierId;
        Price = price;
        Notes = Text.Blank(notes);
        IsActive = isActive;
    }

    public void Delete(DateTimeOffset at) => DeletedAt ??= at;
}

/// <summary>Materialbestellung beim Lieferanten, meist für eine Bauakte und Lieferung auf die Baustelle.</summary>
public sealed class MaterialOrder : IVersionedEntity, ISoftDeletable
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> Transitions = new()
    {
        [OrderStatus.Draft] = [OrderStatus.Ordered, OrderStatus.Cancelled],
        [OrderStatus.Ordered] = [OrderStatus.Confirmed, OrderStatus.Delivered, OrderStatus.Cancelled, OrderStatus.Draft],
        [OrderStatus.Confirmed] = [OrderStatus.Delivered, OrderStatus.Cancelled, OrderStatus.Ordered],
        [OrderStatus.Delivered] = [OrderStatus.Confirmed],
        [OrderStatus.Cancelled] = [OrderStatus.Draft],
    };

    public Guid Id { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public Guid SupplierId { get; private set; }
    public Guid? BauakteId { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Draft;
    public DateOnly? DeliveryDate { get; private set; }

    /// <summary>Zeitfenster als Text, z. B. „7:00–8:00“ oder „vormittags“.</summary>
    public string? DeliveryTime { get; private set; }
    public string? DeliveryStreet { get; private set; }
    public string? DeliveryPostalCode { get; private set; }
    public string? DeliveryCity { get; private set; }

    /// <summary>Ansprechpartner vor Ort für den Fahrer.</summary>
    public string? SiteContact { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset? OrderedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public List<MaterialOrderLine> Lines { get; private set; } = [];

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private MaterialOrder()
    {
    }

    public static MaterialOrder Create(Guid id, string number, Guid createdByUserId) =>
        new() { Id = id, Number = number, CreatedByUserId = createdByUserId };

    public bool IsEditable => Status is OrderStatus.Draft or OrderStatus.Ordered or OrderStatus.Confirmed;

    public void Update(Guid supplierId, Guid? bauakteId, DateOnly? deliveryDate, string? deliveryTime, string? street, string? postalCode, string? city,
        string? siteContact, string? notes, IReadOnlyList<MaterialOrderLine> lines)
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException("Gelieferte oder stornierte Bestellungen können nicht mehr geändert werden.");
        }

        if (Status != OrderStatus.Draft && lines.Count == 0)
        {
            throw new ArgumentException("Eine abgeschickte Bestellung braucht mindestens eine Position.", nameof(lines));
        }

        SupplierId = supplierId;
        BauakteId = bauakteId;
        DeliveryDate = deliveryDate;
        DeliveryTime = Text.Blank(deliveryTime);
        DeliveryStreet = Text.Blank(street);
        DeliveryPostalCode = Text.Blank(postalCode);
        DeliveryCity = Text.Blank(city);
        SiteContact = Text.Blank(siteContact);
        Notes = Text.Blank(notes);
        Lines.Clear();
        Lines.AddRange(lines);
    }

    public void ChangeStatus(OrderStatus status, DateTimeOffset now)
    {
        if (status == Status)
        {
            return;
        }

        if (!Transitions[Status].Contains(status))
        {
            throw new InvalidOperationException($"Von „{Labels.Of(Status)}“ geht es nicht zu „{Labels.Of(status)}“.");
        }

        if (status == OrderStatus.Ordered && Lines.Count == 0)
        {
            throw new InvalidOperationException("Bitte zuerst mindestens eine Position erfassen.");
        }

        if (status == OrderStatus.Ordered && OrderedAt is null)
        {
            OrderedAt = now;
        }

        DeliveredAt = status == OrderStatus.Delivered ? now : null;
        Status = status;
    }

    public void Delete(DateTimeOffset at)
    {
        if (Status != OrderStatus.Draft && Status != OrderStatus.Cancelled)
        {
            throw new InvalidOperationException("Nur Entwürfe und stornierte Bestellungen lassen sich löschen.");
        }

        DeletedAt ??= at;
    }

    internal static class Labels
    {
        public static string Of(OrderStatus status) => status switch
        {
            OrderStatus.Draft => "Entwurf",
            OrderStatus.Ordered => "Bestellt",
            OrderStatus.Confirmed => "Bestätigt",
            OrderStatus.Delivered => "Geliefert",
            _ => "Storniert",
        };
    }
}

public sealed class MaterialOrderLine
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public int Position { get; private set; }
    public Guid? MaterialId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal Quantity { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public decimal? UnitPrice { get; private set; }

    private MaterialOrderLine()
    {
    }

    public MaterialOrderLine(Guid id, Guid orderId, int position, Guid? materialId, string description, decimal quantity, string unit, decimal? unitPrice)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException($"Position {position}: Bitte die Bezeichnung angeben.", nameof(description));
        }

        if (quantity <= 0)
        {
            throw new ArgumentException($"Position {position}: Die Menge muss größer als 0 sein.", nameof(quantity));
        }

        if (string.IsNullOrWhiteSpace(unit))
        {
            throw new ArgumentException($"Position {position}: Bitte die Einheit angeben.", nameof(unit));
        }

        Id = id;
        OrderId = orderId;
        Position = position;
        MaterialId = materialId;
        Description = description.Trim();
        Quantity = quantity;
        Unit = unit.Trim();
        UnitPrice = unitPrice;
    }
}

/// <summary>Nummernkreis je Jahr für Bestellnummern (<c>B-JJJJ-NNNN</c>).</summary>
public sealed class OrderNumberSequence
{
    public int Year { get; private set; }
    public int LastNumber { get; private set; }
}

internal static class Text
{
    public static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("suppliers");
        builder.ConfigureVersioned();
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Name).HasMaxLength(200);
        builder.Property(s => s.CustomerNumber).HasMaxLength(50);
        builder.Property(s => s.ContactName).HasMaxLength(200);
        builder.Property(s => s.Phone).HasMaxLength(80);
        builder.Property(s => s.Email).HasMaxLength(320);
        builder.Property(s => s.Street).HasMaxLength(200);
        builder.Property(s => s.PostalCode).HasMaxLength(10);
        builder.Property(s => s.City).HasMaxLength(100);
        builder.Property(s => s.OrderNotes).HasMaxLength(2000);
        builder.HasIndex(s => s.Name);
    }
}

internal sealed class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("materials");
        builder.ConfigureVersioned();
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Name).HasMaxLength(200);
        builder.Property(m => m.ArticleNumber).HasMaxLength(50);
        builder.Property(m => m.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Unit).HasMaxLength(20);
        builder.Property(m => m.Price).HasPrecision(12, 2);
        builder.Property(m => m.Notes).HasMaxLength(2000);
        builder.HasOne<Supplier>().WithMany().HasForeignKey(m => m.SupplierId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(m => m.Name);
    }
}

internal sealed class MaterialOrderConfiguration : IEntityTypeConfiguration<MaterialOrder>
{
    public void Configure(EntityTypeBuilder<MaterialOrder> builder)
    {
        builder.ToTable("material_orders");
        builder.ConfigureVersioned();
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Number).HasMaxLength(20);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.DeliveryTime).HasMaxLength(50);
        builder.Property(o => o.DeliveryStreet).HasMaxLength(200);
        builder.Property(o => o.DeliveryPostalCode).HasMaxLength(10);
        builder.Property(o => o.DeliveryCity).HasMaxLength(100);
        builder.Property(o => o.SiteContact).HasMaxLength(200);
        builder.Property(o => o.Notes).HasMaxLength(4000);
        builder.Ignore(o => o.IsEditable);
        builder.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Lines).AutoInclude();
        builder.HasOne<Supplier>().WithMany().HasForeignKey(o => o.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Bauakte>().WithMany().HasForeignKey(o => o.BauakteId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(o => o.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(o => o.Number).IsUnique();
        builder.HasIndex(o => new { o.Status, o.DeliveryDate });
        builder.HasIndex(o => o.BauakteId);
    }
}

internal sealed class MaterialOrderLineConfiguration : IEntityTypeConfiguration<MaterialOrderLine>
{
    public void Configure(EntityTypeBuilder<MaterialOrderLine> builder)
    {
        builder.ToTable("material_order_lines");
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Description).HasMaxLength(300);
        builder.Property(l => l.Quantity).HasPrecision(12, 3);
        builder.Property(l => l.Unit).HasMaxLength(20);
        builder.Property(l => l.UnitPrice).HasPrecision(12, 2);
        builder.HasOne<Material>().WithMany().HasForeignKey(l => l.MaterialId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(l => new { l.OrderId, l.Position });
    }
}

internal sealed class OrderNumberSequenceConfiguration : IEntityTypeConfiguration<OrderNumberSequence>
{
    public void Configure(EntityTypeBuilder<OrderNumberSequence> builder)
    {
        builder.ToTable("order_number_sequences");
        builder.HasKey(s => s.Year);
        builder.Property(s => s.Year).ValueGeneratedNever();
    }
}
