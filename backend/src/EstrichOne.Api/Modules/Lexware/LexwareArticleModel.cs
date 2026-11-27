using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Lexware;

/// <summary>Richtung des Artikelabgleichs mit Lexware Office.</summary>
public enum LexwareArticleSync
{
    /// <summary>Nur aus Lexware Office nach EstrichONE (Vorgabe): übernehmen und Änderungen nachziehen, in Lexware wird nichts geschrieben.</summary>
    FromLexware,

    /// <summary>Beide Richtungen: zusätzlich Artikel aus EstrichONE in Lexware Office anlegen und Änderungen dorthin übertragen.</summary>
    Both,

    /// <summary>Kein Abgleich.</summary>
    Off,
}

/// <summary>Art eines Eintrags in „Artikel und Leistungen“ (wie in Lexware Office).</summary>
public enum CatalogItemKind
{
    /// <summary>Material bzw. Ware (<c>PRODUCT</c>).</summary>
    Product,

    /// <summary>Leistung, z. B. Estrich einbringen je m² (<c>SERVICE</c>).</summary>
    Service,
}

/// <summary>
/// Eintrag in „Artikel und Leistungen“: Verkaufsartikel bzw. Leistung mit Verkaufspreis (netto) und Steuersatz, wie sie in
/// Angeboten stehen. Unabhängig vom Materialstamm (der Einkaufsdaten führt); <see cref="MaterialId"/> verknüpft optional
/// mit einem Artikel des Materialstamms. Mit Lexware Office verknüpft über <see cref="LexwareId"/>; der Abgleichstand
/// liegt am Eintrag selbst.
/// </summary>
public sealed class CatalogItem : IVersionedEntity
{
    public const int MaxTitle = 255;
    public const int MaxDescription = 2000;

    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public CatalogItemKind Kind { get; set; } = CatalogItemKind.Service;
    public string? ArticleNumber { get; set; }
    public string? Gtin { get; set; }
    public string? Unit { get; set; }

    /// <summary>Verkaufspreis je Einheit netto.</summary>
    public decimal NetPrice { get; set; }

    /// <summary>Bruttopreis laut Lexware Office (nur Anzeige; EstrichONE rechnet netto).</summary>
    public decimal? GrossPrice { get; set; }

    /// <summary>Steuersatz in Prozent (19, 7 oder 0).</summary>
    public decimal TaxRate { get; set; } = 19m;

    /// <summary>Verknüpfter Artikel des Materialstamms (optional, ohne Fremdschlüssel über Modulgrenzen).</summary>
    public Guid? MaterialId { get; set; }

    /// <summary>In Angeboten auswählbar. In Lexware Office archivierte Artikel werden inaktiv.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Herkunft: <c>estrichOne</c> oder <c>lexware</c> (aus Lexware Office übernommen).</summary>
    public string Origin { get; set; } = OriginEstrichOne;

    public string? LexwareId { get; set; }
    public int? LexwareVersion { get; set; }

    /// <summary>Zuletzt abgeglichene Feldwerte (JSON), Grundlage des Dreiwege-Abgleichs.</summary>
    public string? SyncedFields { get; set; }

    public DateTimeOffset? SyncedAt { get; set; }

    /// <summary>Offene Abweichungen (<see cref="LexwareFieldDeviation"/>) als JSON und ihre Anzahl.</summary>
    public string? Deviations { get; set; }
    public int DeviationCount { get; set; }

    /// <summary>Zustand des Lexware-Artikels: leer = vorhanden, sonst <c>missing</c> oder <c>archived</c>.</summary>
    public string? RemoteState { get; set; }
    public string? SyncError { get; set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public const string OriginEstrichOne = "estrichOne";
    public const string OriginLexware = "lexware";
}

internal sealed class CatalogItemConfiguration : IEntityTypeConfiguration<CatalogItem>
{
    public const string UniqueLexwareArticle = "ix_catalog_items_lexware_id";

    public void Configure(EntityTypeBuilder<CatalogItem> builder)
    {
        builder.ToTable("catalog_items");
        builder.ConfigureVersioned();
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Title).HasMaxLength(CatalogItem.MaxTitle);
        builder.Property(c => c.Description).HasMaxLength(CatalogItem.MaxDescription);
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.ArticleNumber).HasMaxLength(50);
        builder.Property(c => c.Gtin).HasMaxLength(20);
        builder.Property(c => c.Unit).HasMaxLength(50);
        builder.Property(c => c.NetPrice).HasPrecision(14, 4);
        builder.Property(c => c.GrossPrice).HasPrecision(14, 4);
        builder.Property(c => c.TaxRate).HasPrecision(5, 2);
        builder.Property(c => c.Origin).HasMaxLength(20);
        builder.Property(c => c.LexwareId).HasMaxLength(100);
        builder.HasIndex(c => c.LexwareId).IsUnique().HasFilter("lexware_id IS NOT NULL").HasDatabaseName(UniqueLexwareArticle);
        builder.Property(c => c.RemoteState).HasMaxLength(20);
        builder.Property(c => c.SyncError).HasMaxLength(2000);
        builder.Property(c => c.DeviationCount).HasDefaultValue(0);
        builder.HasIndex(c => c.Title);
        builder.HasIndex(c => c.ArticleNumber);
    }
}
