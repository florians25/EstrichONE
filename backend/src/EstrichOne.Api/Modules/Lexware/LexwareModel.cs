using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Einstellungen der Lexware-Office-Anbindung (eine Zeile). Der API-Schlüssel liegt nur verschlüsselt vor (Data Protection).</summary>
public sealed class LexwareSettingsRow : IVersionedEntity
{
    public const int SingletonId = 1;
    public const int MaxUrl = 300;
    public const int MaxLabel = 300;

    public int Id { get; private set; } = SingletonId;

    public string? ProtectedApiKey { get; set; }

    /// <summary>Abweichende Basis-URL der API; leer = Vorgabe (<see cref="LexwareOptions.DefaultBaseUrl"/>).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Zahlungsziel in Tagen für neue Rechnungsentwürfe.</summary>
    public int PaymentTermDays { get; set; } = 14;

    /// <summary>Text der Zahlungsbedingung; leer = Standardtext mit dem Zahlungsziel.</summary>
    public string? PaymentTermLabel { get; set; }

    /// <summary>Neue Kunden standardmäßig mit Lexware Office abgleichen (Vorgabe für <c>Contact.LexwareSync</c>).</summary>
    public bool NewCustomersSync { get; set; } = true;

    public string? OrganizationId { get; set; }
    public string? OrganizationName { get; set; }
    public DateTimeOffset? LastTestAt { get; set; }
    public bool? LastTestOk { get; set; }
    public string? LastTestError { get; set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool Configured => ProtectedApiKey is not null;
}

/// <summary>Wie eine Verknüpfung entstanden ist.</summary>
public enum LexwareLinkOrigin
{
    /// <summary>Vorhandene Einträge beider Seiten verknüpft.</summary>
    Linked,

    /// <summary>Aus EstrichONE in Lexware Office angelegt.</summary>
    CreatedInLexware,

    /// <summary>Aus Lexware Office nach EstrichONE übernommen.</summary>
    ImportedFromLexware,
}

/// <summary>Verknüpfung Kunde (EstrichONE) mit Kontakt (Lexware Office); je Seite höchstens eine.</summary>
public sealed class LexwareContactLink
{
    public const int MaxName = 300;

    public Guid ContactId { get; set; }
    public string LexwareContactId { get; set; } = string.Empty;

    /// <summary>Name und Kundennummer in Lexware zum Zeitpunkt des letzten Abgleichs (Anzeige ohne Aufruf).</summary>
    public string? LexwareName { get; set; }
    public string? LexwareNumber { get; set; }
    public LexwareLinkOrigin Origin { get; set; }
    public Guid LinkedByUserId { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
}

/// <summary>Beleg in Lexware Office, der zu einer Bauakte gehört (derzeit nur Rechnungen).</summary>
public sealed class LexwareVoucherLink
{
    public const string InvoiceType = "invoice";

    public Guid Id { get; set; }
    public Guid BauakteId { get; set; }
    public string VoucherType { get; set; } = InvoiceType;
    public string LexwareId { get; set; } = string.Empty;
    public string? Number { get; set; }
    public string? Title { get; set; }
    public DateOnly? VoucherDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal? GrossAmount { get; set; }
    public string Currency { get; set; } = "EUR";

    /// <summary>Status laut Lexware: draft, open, paid, paidoff, voided. „Überfällig“ wird aus Fälligkeit und Status abgeleitet.</summary>
    public string Status { get; set; } = "draft";

    /// <summary>Von EstrichONE als Entwurf angelegt (sonst nachträglich zugeordnet).</summary>
    public bool Created { get; set; }
    public Guid LinkedByUserId { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
    public DateTimeOffset? SyncedAt { get; set; }
    public string? SyncError { get; set; }
}

internal sealed class LexwareSettingsRowConfiguration : IEntityTypeConfiguration<LexwareSettingsRow>
{
    public void Configure(EntityTypeBuilder<LexwareSettingsRow> builder)
    {
        builder.ToTable("lexware_settings");
        builder.ConfigureVersioned();
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.BaseUrl).HasMaxLength(LexwareSettingsRow.MaxUrl);
        builder.Property(s => s.PaymentTermLabel).HasMaxLength(LexwareSettingsRow.MaxLabel);
        builder.Property(s => s.OrganizationId).HasMaxLength(100);
        builder.Property(s => s.OrganizationName).HasMaxLength(LexwareContactLink.MaxName);
        builder.Property(s => s.LastTestError).HasMaxLength(2000);
        builder.Ignore(s => s.Configured);
    }
}

internal sealed class LexwareContactLinkConfiguration : IEntityTypeConfiguration<LexwareContactLink>
{
    public const string UniqueLexwareContact = "ix_lexware_contact_links_lexware_contact_id";

    public void Configure(EntityTypeBuilder<LexwareContactLink> builder)
    {
        builder.ToTable("lexware_contact_links");
        builder.HasKey(l => l.ContactId);
        builder.Property(l => l.LexwareContactId).HasMaxLength(100);
        builder.HasIndex(l => l.LexwareContactId).IsUnique().HasDatabaseName(UniqueLexwareContact);
        builder.Property(l => l.LexwareName).HasMaxLength(LexwareContactLink.MaxName);
        builder.Property(l => l.LexwareNumber).HasMaxLength(50);
        builder.Property(l => l.Origin).HasConversion<string>().HasMaxLength(30);
    }
}

internal sealed class LexwareVoucherLinkConfiguration : IEntityTypeConfiguration<LexwareVoucherLink>
{
    public const string UniqueVoucher = "ix_lexware_voucher_links_lexware_id";

    public void Configure(EntityTypeBuilder<LexwareVoucherLink> builder)
    {
        builder.ToTable("lexware_voucher_links");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.VoucherType).HasMaxLength(30);
        builder.Property(l => l.LexwareId).HasMaxLength(100);
        builder.HasIndex(l => l.LexwareId).IsUnique().HasDatabaseName(UniqueVoucher);
        builder.HasIndex(l => l.BauakteId);
        builder.Property(l => l.Number).HasMaxLength(100);
        builder.Property(l => l.Title).HasMaxLength(300);
        builder.Property(l => l.NetAmount).HasPrecision(14, 2);
        builder.Property(l => l.GrossAmount).HasPrecision(14, 2);
        builder.Property(l => l.Currency).HasMaxLength(3);
        builder.Property(l => l.Status).HasMaxLength(20);
        builder.Property(l => l.SyncError).HasMaxLength(1000);
    }
}
