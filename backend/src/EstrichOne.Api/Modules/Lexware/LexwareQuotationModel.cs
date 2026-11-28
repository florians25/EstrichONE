using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Stand eines Angebotsentwurfs in EstrichONE.</summary>
public enum QuotationDraftState
{
    /// <summary>In Bearbeitung in EstrichONE.</summary>
    Draft,

    /// <summary>Wird gerade an Lexware Office gesendet (Sperre gegen doppeltes Senden).</summary>
    Sending,

    /// <summary>Als Entwurf in Lexware Office angelegt; ab jetzt gilt der Beleg dort, der Entwurf hier ist schreibgeschützt.</summary>
    Sent,

    /// <summary>Senden fehlgeschlagen; bearbeiten und erneut senden möglich.</summary>
    Failed,
}

/// <summary>
/// Angebot, das in EstrichONE vorbereitet und als Entwurf an Lexware Office gesendet wird (Beta). Nach dem Senden gilt
/// der Beleg in Lexware Office (<see cref="LexwareId"/>); Nummer, Festschreiben und Versand bleiben dort.
/// </summary>
public sealed class QuotationDraft : IVersionedEntity
{
    public const int MaxTitle = 200;
    public const int MaxText = 4000;

    public const string TaxNet = LexwareTaxTypes.Net;
    public const string TaxConstruction13b = LexwareTaxTypes.Construction13b;
    public const string TaxVatfree = LexwareTaxTypes.Vatfree;

    public Guid Id { get; set; }
    public Guid ContactId { get; set; }
    public Guid? BauakteId { get; set; }
    public string Title { get; set; } = "Angebot";
    public string? Introduction { get; set; }
    public string? Remark { get; set; }
    public DateOnly? ValidUntil { get; set; }

    /// <summary><see cref="TaxNet"/>, <see cref="TaxConstruction13b"/> oder <see cref="TaxVatfree"/>.</summary>
    public string TaxType { get; set; } = TaxNet;

    /// <summary>Gewählte Zahlungsbedingung aus Lexware Office (Kopie der Werte, damit der Entwurf ohne Abruf lesbar bleibt).</summary>
    public string? PaymentConditionId { get; set; }
    public string? PaymentLabel { get; set; }
    public int? PaymentDuration { get; set; }
    public decimal? PaymentDiscountPercent { get; set; }
    public int? PaymentDiscountRange { get; set; }

    public QuotationDraftState State { get; set; } = QuotationDraftState.Draft;
    public string? LexwareId { get; set; }
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
    /// kann das Angebot trotzdem angelegt haben. Vor dem nächsten Senden wird deshalb dort nach ihm gesucht.
    /// </summary>
    public bool SendUncertain { get; set; }

    public Guid CreatedByUserId { get; set; }
    public List<QuotationDraftLine> Lines { get; set; } = [];

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool Editable => State is QuotationDraftState.Draft or QuotationDraftState.Failed;
}

/// <summary>Position eines Angebotsentwurfs: Artikel bzw. Leistung (aus dem Katalog oder frei) oder Textposition.</summary>
public sealed class QuotationDraftLine
{
    public const string KindItem = "item";
    public const string KindText = "text";

    public Guid Id { get; set; }
    public Guid DraftId { get; set; }
    public int Position { get; set; }

    /// <summary><see cref="KindItem"/> oder <see cref="KindText"/>.</summary>
    public string Kind { get; set; } = KindItem;

    /// <summary>Eintrag aus „Artikel und Leistungen“ (sonst freie Position).</summary>
    public Guid? CatalogItemId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal? NetPrice { get; set; }
    public decimal? TaxRate { get; set; }
    public decimal? DiscountPercent { get; set; }
}

internal sealed class QuotationDraftConfiguration : IEntityTypeConfiguration<QuotationDraft>
{
    public const string UniqueLexwareQuotation = "ix_quotation_drafts_lexware_id";

    public void Configure(EntityTypeBuilder<QuotationDraft> builder)
    {
        builder.ToTable("quotation_drafts");
        builder.ConfigureVersioned();
        builder.Property(q => q.Id).ValueGeneratedNever();
        builder.Property(q => q.Title).HasMaxLength(QuotationDraft.MaxTitle);
        builder.Property(q => q.Introduction).HasMaxLength(QuotationDraft.MaxText);
        builder.Property(q => q.Remark).HasMaxLength(QuotationDraft.MaxText);
        builder.Property(q => q.TaxType).HasMaxLength(30);
        builder.Property(q => q.PaymentConditionId).HasMaxLength(100);
        builder.Property(q => q.PaymentLabel).HasMaxLength(300);
        builder.Property(q => q.PaymentDiscountPercent).HasPrecision(5, 2);
        builder.Property(q => q.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(q => q.LexwareId).HasMaxLength(100);
        builder.HasIndex(q => q.LexwareId).IsUnique().HasFilter("lexware_id IS NOT NULL").HasDatabaseName(UniqueLexwareQuotation);
        builder.Property(q => q.SendError).HasMaxLength(2000);
        builder.Property(q => q.SendErrorTechnical).HasMaxLength(1000);
        builder.Property(q => q.SendErrorFix).HasMaxLength(40);
        builder.HasIndex(q => q.BauakteId);
        builder.HasIndex(q => q.ContactId);
        builder.Ignore(q => q.Editable);
        builder.HasMany(q => q.Lines).WithOne().HasForeignKey(l => l.DraftId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(q => q.Lines).AutoInclude();
    }
}

internal sealed class QuotationDraftLineConfiguration : IEntityTypeConfiguration<QuotationDraftLine>
{
    public void Configure(EntityTypeBuilder<QuotationDraftLine> builder)
    {
        builder.ToTable("quotation_draft_lines");
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
    }
}
