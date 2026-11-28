using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Stand eines Verkaufsbelegs in EstrichONE.</summary>
public enum SalesVoucherState
{
    /// <summary>In Bearbeitung in EstrichONE.</summary>
    Draft,

    /// <summary>Wird gerade an Lexware Office gesendet (Sperre gegen doppeltes Senden).</summary>
    Sending,

    /// <summary>In Lexware Office angelegt (als Entwurf oder festgeschrieben); ab jetzt gilt der Beleg dort, hier schreibgeschützt.</summary>
    Sent,

    /// <summary>Senden fehlgeschlagen; bearbeiten und erneut senden möglich.</summary>
    Failed,
}

/// <summary>Belegarten des Moduls „Verkauf“ mit Modul, Rechten und Texten.</summary>
public static class SalesTypes
{
    public const string Quotation = LexwareVoucherTypes.Quotation;
    public const string OrderConfirmation = LexwareVoucherTypes.OrderConfirmation;
    public const string Invoice = LexwareVoucherTypes.Invoice;
    public const string CreditNote = LexwareVoucherTypes.CreditNote;

    /// <summary>Zweck einer Rechnung: normale Rechnung, Abschlagsrechnung, Schlussrechnung.</summary>
    public const string PurposeStandard = "standard";
    public const string PurposeDownPayment = "downPayment";
    public const string PurposeFinal = "final";

    public static IReadOnlyList<string> All { get; } = [Quotation, OrderConfirmation, Invoice, CreditNote];

    public static bool Known(string? type) => type is Quotation or OrderConfirmation or Invoice or CreditNote;

    /// <summary>Untermodul der Belegart.</summary>
    public static string Module(string type) => type switch
    {
        OrderConfirmation => AppModules.SalesOrderConfirmations,
        Invoice => AppModules.SalesInvoices,
        CreditNote => AppModules.SalesCreditNotes,
        _ => AppModules.Quotations,
    };

    public static string ReadPermission(string type) => type switch
    {
        OrderConfirmation => Permissions.OrderConfirmationsRead,
        Invoice => Permissions.SalesInvoicesRead,
        CreditNote => Permissions.CreditNotesRead,
        _ => Permissions.QuotationsRead,
    };

    public static string EditPermission(string type) => type switch
    {
        OrderConfirmation => Permissions.OrderConfirmationsEdit,
        Invoice => Permissions.SalesInvoicesEdit,
        CreditNote => Permissions.CreditNotesEdit,
        _ => Permissions.QuotationsPrepare,
    };

    public static string FinalizePermission(string type) => type switch
    {
        OrderConfirmation => Permissions.OrderConfirmationsFinalize,
        Invoice => Permissions.SalesInvoicesFinalize,
        CreditNote => Permissions.CreditNotesFinalize,
        _ => Permissions.QuotationsFinalize,
    };

    public static string Name(string type, string? purpose = null) => type switch
    {
        OrderConfirmation => "Auftragsbestätigung",
        Invoice when purpose == PurposeDownPayment => "Abschlagsrechnung",
        Invoice when purpose == PurposeFinal => "Schlussrechnung",
        Invoice => "Rechnung",
        CreditNote => "Gutschrift",
        _ => "Angebot",
    };

    /// <summary>Belegart im Akkusativ mit Artikel, für Meldungen („senden Sie das Angebot …“).</summary>
    public static string Accusative(string type) => type switch
    {
        OrderConfirmation => "die Auftragsbestätigung",
        Invoice => "die Rechnung",
        CreditNote => "die Gutschrift",
        _ => "das Angebot",
    };

    /// <summary>Welche Belegarten aus einer Belegart entstehen können (<c>precedingSalesVoucherId</c>).</summary>
    public static IReadOnlyList<string> Followers(string type) => type switch
    {
        Quotation => [OrderConfirmation, Invoice],
        OrderConfirmation => [Invoice],
        Invoice => [CreditNote],
        _ => [],
    };
}

/// <summary>
/// Verkaufsbeleg (Angebot, Auftragsbestätigung, Rechnung, Gutschrift), erfasst in EstrichONE und an Lexware Office gesendet.
/// Lexware Office bleibt Buchhaltung, Nummernkreis und Belegarchiv: Nach dem Senden gilt der Beleg dort (<see cref="LexwareId"/>),
/// der Beleg hier ist schreibgeschützt. Früher „Angebotsentwurf“ (Tabelle <c>quotation_drafts</c>).
/// </summary>
public sealed class SalesVoucher : IVersionedEntity
{
    public const int MaxTitle = 200;
    public const int MaxText = 4000;

    public const string TaxNet = LexwareTaxTypes.Net;
    public const string TaxConstruction13b = LexwareTaxTypes.Construction13b;
    public const string TaxVatfree = LexwareTaxTypes.Vatfree;

    public Guid Id { get; set; }

    /// <summary><see cref="SalesTypes"/>: quotation, orderconfirmation, invoice, creditnote.</summary>
    public string VoucherType { get; set; } = SalesTypes.Quotation;

    /// <summary>Bei Rechnungen: <see cref="SalesTypes.PurposeStandard"/>, <see cref="SalesTypes.PurposeDownPayment"/> oder <see cref="SalesTypes.PurposeFinal"/>.</summary>
    public string Purpose { get; set; } = SalesTypes.PurposeStandard;

    public Guid ContactId { get; set; }
    public Guid? BauakteId { get; set; }
    public string Title { get; set; } = "Angebot";
    public string? Introduction { get; set; }
    public string? Remark { get; set; }

    /// <summary>Belegdatum; leer heißt: Tag des Sendens.</summary>
    public DateOnly? VoucherDate { get; set; }

    /// <summary>Angebot: gültig bis.</summary>
    public DateOnly? ValidUntil { get; set; }

    /// <summary>Rechnung, Auftragsbestätigung, Gutschrift: Leistungsdatum bzw. Beginn des Leistungszeitraums.</summary>
    public DateOnly? ServiceDate { get; set; }

    /// <summary>Ende des Leistungszeitraums (leer: einzelnes Leistungsdatum).</summary>
    public DateOnly? ServiceEndDate { get; set; }

    /// <summary>Vorhergehender Beleg in EstrichONE (Belegkette) und seine ID in Lexware Office (<c>precedingSalesVoucherId</c>).</summary>
    public Guid? PrecedingId { get; set; }
    public string? PrecedingLexwareId { get; set; }

    /// <summary>Abschlagsrechnung: Prozent der Auftragssumme, aus der der Betrag vorgeschlagen wurde (nur Anzeige).</summary>
    public decimal? DownPaymentPercent { get; set; }

    /// <summary><see cref="TaxNet"/>, <see cref="TaxConstruction13b"/> oder <see cref="TaxVatfree"/>.</summary>
    public string TaxType { get; set; } = TaxNet;

    /// <summary>Gewählte Zahlungsbedingung aus Lexware Office (Kopie der Werte, damit der Beleg ohne Abruf lesbar bleibt).</summary>
    public string? PaymentConditionId { get; set; }
    public string? PaymentLabel { get; set; }
    public int? PaymentDuration { get; set; }
    public decimal? PaymentDiscountPercent { get; set; }
    public int? PaymentDiscountRange { get; set; }

    public SalesVoucherState State { get; set; } = SalesVoucherState.Draft;
    public string? LexwareId { get; set; }

    /// <summary>Beim Senden festgeschrieben (<c>finalize=true</c>, nur mit Modul, Recht und Bestätigung).</summary>
    public bool Finalized { get; set; }

    /// <summary>Stand aus Lexware Office: Nummer, Status, offener Betrag, Zahlstatus, Mahnstufe (zuletzt gelesen).</summary>
    public string? LexwareNumber { get; set; }
    public string? LexwareStatus { get; set; }
    public decimal? OpenAmount { get; set; }
    public string? PaymentStatus { get; set; }
    public int? DunningLevel { get; set; }
    public DateTimeOffset? LexwareSyncedAt { get; set; }

    public DateTimeOffset? SendStartedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public Guid? SentByUserId { get; set; }
    public string? SendError { get; set; }

    /// <summary>Originaltext von Lexware Office zum letzten Fehler (englisch) für „Technische Einzelheiten“.</summary>
    public string? SendErrorTechnical { get; set; }

    /// <summary>Bekannte Behebung des letzten Fehlers (<see cref="LexwareErrors.FixTaxTypeNet"/> usw.).</summary>
    public string? SendErrorFix { get; set; }

    /// <summary>
    /// Der letzte Versuch ist ohne eindeutige Antwort abgebrochen (Zeitüberschreitung, keine Verbindung): Lexware Office
    /// kann den Beleg trotzdem angelegt haben. Vor dem nächsten Senden wird deshalb dort nach ihm gesucht.
    /// </summary>
    public bool SendUncertain { get; set; }

    public Guid CreatedByUserId { get; set; }
    public List<SalesVoucherLine> Lines { get; set; } = [];

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool Editable => State is SalesVoucherState.Draft or SalesVoucherState.Failed;
}

/// <summary>Position eines Verkaufsbelegs.</summary>
public sealed class SalesVoucherLine
{
    /// <summary>Artikel, Leistung oder freie Position.</summary>
    public const string KindItem = "item";

    /// <summary>Textposition ohne Betrag.</summary>
    public const string KindText = "text";

    /// <summary>Zwischensumme der Positionen darüber (nur Anzeige; an Lexware Office als Textposition).</summary>
    public const string KindSubtotal = "subtotal";

    /// <summary>Abzug einer Abschlagsrechnung in der Schlussrechnung (negativer Betrag).</summary>
    public const string KindDeduction = "deduction";

    public static readonly string[] Kinds = [KindItem, KindText, KindSubtotal, KindDeduction];

    public Guid Id { get; set; }
    public Guid DraftId { get; set; }
    public int Position { get; set; }

    public string Kind { get; set; } = KindItem;

    /// <summary>Eintrag aus „Artikel und Leistungen“ (sonst freie Position).</summary>
    public Guid? CatalogItemId { get; set; }

    /// <summary>Abzugszeile: abgezogene Abschlagsrechnung (Verkaufsbeleg in EstrichONE).</summary>
    public Guid? DeductionVoucherId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal? NetPrice { get; set; }
    public decimal? TaxRate { get; set; }
    public decimal? DiscountPercent { get; set; }

    /// <summary>Zählt in die Summen (Position oder Abzug).</summary>
    public bool Priced => Kind is KindItem or KindDeduction;
}

internal sealed class SalesVoucherConfiguration : IEntityTypeConfiguration<SalesVoucher>
{
    public const string UniqueLexwareVoucher = "ix_sales_vouchers_lexware_id";

    public void Configure(EntityTypeBuilder<SalesVoucher> builder)
    {
        builder.ToTable("sales_vouchers");
        builder.ConfigureVersioned();
        builder.Property(q => q.Id).ValueGeneratedNever();
        builder.Property(q => q.VoucherType).HasMaxLength(30).HasDefaultValue(SalesTypes.Quotation);
        builder.Property(q => q.Purpose).HasMaxLength(20).HasDefaultValue(SalesTypes.PurposeStandard);
        builder.Property(q => q.Title).HasMaxLength(SalesVoucher.MaxTitle);
        builder.Property(q => q.Introduction).HasMaxLength(SalesVoucher.MaxText);
        builder.Property(q => q.Remark).HasMaxLength(SalesVoucher.MaxText);
        builder.Property(q => q.TaxType).HasMaxLength(30);
        builder.Property(q => q.PaymentConditionId).HasMaxLength(100);
        builder.Property(q => q.PaymentLabel).HasMaxLength(300);
        builder.Property(q => q.PaymentDiscountPercent).HasPrecision(5, 2);
        builder.Property(q => q.DownPaymentPercent).HasPrecision(5, 2);
        builder.Property(q => q.OpenAmount).HasPrecision(14, 2);
        builder.Property(q => q.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(q => q.LexwareId).HasMaxLength(100);
        builder.Property(q => q.PrecedingLexwareId).HasMaxLength(100);
        builder.Property(q => q.LexwareNumber).HasMaxLength(100);
        builder.Property(q => q.LexwareStatus).HasMaxLength(20);
        builder.Property(q => q.PaymentStatus).HasMaxLength(30);
        builder.HasIndex(q => q.LexwareId).IsUnique().HasFilter("lexware_id IS NOT NULL").HasDatabaseName(UniqueLexwareVoucher);
        builder.Property(q => q.SendError).HasMaxLength(2000);
        builder.Property(q => q.SendErrorTechnical).HasMaxLength(1000);
        builder.Property(q => q.SendErrorFix).HasMaxLength(40);
        builder.HasIndex(q => q.BauakteId);
        builder.HasIndex(q => q.ContactId);
        builder.HasIndex(q => q.PrecedingId);
        builder.Ignore(q => q.Editable);
        builder.HasMany(q => q.Lines).WithOne().HasForeignKey(l => l.DraftId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(q => q.Lines).AutoInclude();
    }
}

internal sealed class SalesVoucherLineConfiguration : IEntityTypeConfiguration<SalesVoucherLine>
{
    public void Configure(EntityTypeBuilder<SalesVoucherLine> builder)
    {
        builder.ToTable("sales_voucher_lines");
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Kind).HasMaxLength(10);
        builder.Property(l => l.Name).HasMaxLength(255);
        builder.Property(l => l.Description).HasMaxLength(2000);
        builder.Property(l => l.Quantity).HasPrecision(14, 3);
        builder.Property(l => l.Unit).HasMaxLength(50);
        builder.Property(l => l.NetPrice).HasPrecision(14, 4);
        builder.Property(l => l.TaxRate).HasPrecision(5, 2);
        builder.Property(l => l.DiscountPercent).HasPrecision(5, 2);
        builder.HasIndex(l => new { l.DraftId, l.Position });
        builder.Ignore(l => l.Priced);
    }
}
