using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Stand eines hochgeladenen Eingangsbelegs.</summary>
public enum PurchaseDocumentState
{
    /// <summary>Hochgeladen und gelesen, noch nicht an Lexware Office übergeben.</summary>
    Uploaded,

    /// <summary>Übergabe läuft (gegen doppeltes Senden).</summary>
    Sending,

    /// <summary>An Lexware Office übergeben (Beleg „zu prüfen“ in Lexware Office).</summary>
    Sent,

    /// <summary>Übergabe fehlgeschlagen; erneut senden möglich.</summary>
    Failed,
}

/// <summary>
/// Hochgeladener Eingangsbeleg (PDF, Foto, XRechnung, ZUGFeRD). Die Datei bleibt bis zur Übergabe an Lexware Office in
/// EstrichONE; Lexware Office ist danach Belegarchiv und Buchhaltung. Strukturierte Daten einer E-Rechnung werden beim
/// Hochladen gelesen (<see cref="EInvoiceReader"/>) und als JSON gespeichert.
/// </summary>
public sealed class PurchaseDocument
{
    public const int MaxBytes = 5 * 1024 * 1024;
    public const int MaxName = 255;

    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/pdf";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public byte[] Content { get; set; } = [];
    public Guid? BauakteId { get; set; }
    public string? Note { get; set; }

    /// <summary><c>xrechnung-ubl</c>, <c>xrechnung-cii</c>, <c>zugferd</c> oder <c>none</c>.</summary>
    public string Format { get; set; } = "none";

    /// <summary><see cref="EInvoiceData"/> als JSON (auch bei <c>none</c>, dann ohne Werte).</summary>
    public string DataJson { get; set; } = "{}";

    /// <summary>Kurzangaben für die Liste (aus der E-Rechnung, sonst leer).</summary>
    public string? SupplierName { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public decimal? GrossAmount { get; set; }

    /// <summary><c>web</c> oder <c>app</c>.</summary>
    public string Source { get; set; } = "web";
    public Guid UploadedByUserId { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public PurchaseDocumentState State { get; set; } = PurchaseDocumentState.Uploaded;
    public string? LexwareFileId { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public Guid? SentByUserId { get; set; }
    public string? SendError { get; set; }
    public DateTimeOffset? SendStartedAt { get; set; }
}

/// <summary>Eingangsbeleg aus Lexware Office, einer Bauakte zugeordnet (Kosten der Bauakte).</summary>
public sealed class PurchaseVoucherLink
{
    public Guid Id { get; set; }
    public string LexwareId { get; set; } = string.Empty;

    /// <summary><c>purchaseinvoice</c> oder <c>purchasecreditnote</c>.</summary>
    public string VoucherType { get; set; } = LexwareVoucherTypes.PurchaseInvoice;
    public Guid BauakteId { get; set; }
    public string? Number { get; set; }
    public DateOnly? VoucherDate { get; set; }
    public decimal? GrossAmount { get; set; }
    public string? ContactName { get; set; }
    public string? LexwareContactId { get; set; }
    public string? Status { get; set; }
    public Guid AssignedByUserId { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
}

/// <summary>Lieferant aus Lexware Office mit dem Lieferanten im Materialstamm verknüpft.</summary>
public sealed class PurchaseSupplierLink
{
    public string LexwareContactId { get; set; } = string.Empty;
    public Guid SupplierId { get; set; }
    public string? ContactName { get; set; }
    public DateTimeOffset LinkedAt { get; set; }
}

internal sealed class PurchaseDocumentConfiguration : IEntityTypeConfiguration<PurchaseDocument>
{
    public void Configure(EntityTypeBuilder<PurchaseDocument> builder)
    {
        builder.ToTable("purchase_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.FileName).HasMaxLength(PurchaseDocument.MaxName);
        builder.Property(d => d.ContentType).HasMaxLength(100);
        builder.Property(d => d.Sha256).HasMaxLength(64);
        builder.HasIndex(d => d.Sha256).IsUnique();
        builder.HasIndex(d => d.BauakteId);
        builder.Property(d => d.Note).HasMaxLength(1000);
        builder.Property(d => d.Format).HasMaxLength(20);
        builder.Property(d => d.DataJson).HasColumnType("jsonb");
        builder.Property(d => d.SupplierName).HasMaxLength(255);
        builder.Property(d => d.InvoiceNumber).HasMaxLength(100);
        builder.Property(d => d.GrossAmount).HasPrecision(14, 2);
        builder.Property(d => d.Source).HasMaxLength(10);
        builder.Property(d => d.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.LexwareFileId).HasMaxLength(100);
        builder.Property(d => d.SendError).HasMaxLength(1000);
    }
}

internal sealed class PurchaseVoucherLinkConfiguration : IEntityTypeConfiguration<PurchaseVoucherLink>
{
    public void Configure(EntityTypeBuilder<PurchaseVoucherLink> builder)
    {
        builder.ToTable("purchase_voucher_links");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.LexwareId).HasMaxLength(100);
        builder.HasIndex(l => l.LexwareId).IsUnique();
        builder.HasIndex(l => l.BauakteId);
        builder.Property(l => l.VoucherType).HasMaxLength(30);
        builder.Property(l => l.Number).HasMaxLength(100);
        builder.Property(l => l.GrossAmount).HasPrecision(14, 2);
        builder.Property(l => l.ContactName).HasMaxLength(255);
        builder.Property(l => l.LexwareContactId).HasMaxLength(100);
        builder.Property(l => l.Status).HasMaxLength(20);
    }
}

internal sealed class PurchaseSupplierLinkConfiguration : IEntityTypeConfiguration<PurchaseSupplierLink>
{
    public void Configure(EntityTypeBuilder<PurchaseSupplierLink> builder)
    {
        builder.ToTable("purchase_supplier_links");
        builder.HasKey(l => l.LexwareContactId);
        builder.Property(l => l.LexwareContactId).HasMaxLength(100);
        builder.Property(l => l.ContactName).HasMaxLength(255);
    }
}
