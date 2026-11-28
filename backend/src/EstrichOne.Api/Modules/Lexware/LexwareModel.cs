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

    /// <summary>Neue Kunden standardmäßig mit Lexware Office abgleichen (Vorgabe für <c>Contact.LexwareSync</c>).</summary>
    public bool NewCustomersSync { get; set; } = true;

    /// <summary>Neue Kunden mit eingeschaltetem Abgleich ohne ähnlichen Kontakt automatisch in Lexware Office anlegen.</summary>
    public bool AutoCreate { get; set; } = true;

    /// <summary>Richtung, in der spätere Änderungen an verknüpften Kunden abgeglichen werden.</summary>
    public LexwareChangeSync ChangeSync { get; set; } = LexwareChangeSync.Both;

    /// <summary>Letzter vollständiger Änderungsabgleich und sein Fehler (vom Hintergrunddienst ohne Versionswechsel geschrieben).</summary>
    public DateTimeOffset? LastChangeSyncAt { get; set; }
    public string? LastChangeSyncError { get; set; }

    /// <summary>Richtung des Artikelabgleichs (Modul „Lexware Office: Artikel“); Vorgabe: aus Lexware Office nach EstrichONE.</summary>
    public LexwareArticleSync ArticleSync { get; set; } = LexwareArticleSync.FromLexware;

    /// <summary>Letzter Artikelabgleich und sein Fehler (ohne Versionswechsel geschrieben).</summary>
    public DateTimeOffset? LastArticleSyncAt { get; set; }
    public string? LastArticleSyncError { get; set; }

    /// <summary>Regel des Moduls „Status der Bauakte automatisch“: angenommenes Angebot setzt eine Anfrage auf „Geplant“.</summary>
    public bool StatusOnQuotationAccepted { get; set; } = true;

    /// <summary>Regel: alle Rechnungen bezahlt stellt die Bauakte zur Abschlussprüfung.</summary>
    public bool StatusOnInvoicesPaid { get; set; } = true;

    /// <summary>Belege automatisch Bauakten zuordnen, wenn die Zuordnung sicher ist (Vorgabe an).</summary>
    public bool AutoAssign { get; set; } = true;

    /// <summary>Zusätzliche Regel: einzige offene Bauakte des Kunden mit Belegdatum im Zeitraum (weniger sicher, Vorgabe aus).</summary>
    public bool AutoAssignSingleOpen { get; set; }

    /// <summary>Kleinunternehmer und Besteuerungsart laut <c>GET /v1/profile</c> und Zeitpunkt der Prüfung (ohne Versionswechsel geschrieben).</summary>
    public bool? ProfileSmallBusiness { get; set; }
    public string? ProfileTaxType { get; set; }
    public DateTimeOffset? TaxTypesCheckedAt { get; set; }

    /// <summary>Von Lexware Office abgelehnte Steuerarten je Belegart als JSON (<see cref="LexwareTaxTypeRejection"/>; ohne Versionswechsel geschrieben).</summary>
    public string? RejectedTaxTypes { get; set; }

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

    /// <summary>
    /// Zuletzt abgeglichener Stand der Felder (<see cref="LexwareSyncFields"/>) als JSON; leer, solange noch kein
    /// Änderungsabgleich lief. Gegen ihn wird erkannt, welche Seite ein Feld geändert hat.
    /// </summary>
    public string? SyncedFields { get; set; }

    /// <summary><c>version</c> und <c>updatedDate</c> des Lexware-Kontakts beim letzten Abgleich.</summary>
    public int? LexwareVersion { get; set; }
    public DateTimeOffset? LexwareUpdatedAt { get; set; }

    /// <summary>Version des Kunden beim letzten Abgleich; weicht sie ab, wurde der Kunde in EstrichONE geändert.</summary>
    public long? LocalVersion { get; set; }

    /// <summary>Zeitpunkt des letzten erfolgreichen Abgleichs.</summary>
    public DateTimeOffset? SyncedAt { get; set; }

    /// <summary>Offene Abweichungen (<see cref="LexwareFieldDeviation"/>) als JSON und ihre Anzahl.</summary>
    public string? Deviations { get; set; }
    public int DeviationCount { get; set; }

    /// <summary>Zustand des Lexware-Kontakts: leer = vorhanden, sonst <see cref="RemoteMissing"/> oder <see cref="RemoteArchived"/>.</summary>
    public string? RemoteState { get; set; }

    /// <summary>Fehler des letzten Abgleichs, Zahl der Fehlversuche und frühester nächster Versuch.</summary>
    public string? SyncError { get; set; }
    public int SyncAttempts { get; set; }
    public DateTimeOffset? NextSyncAt { get; set; }

    public const string RemoteMissing = "missing";
    public const string RemoteArchived = "archived";
}

/// <summary>
/// Beleg aus Lexware Office (Rechnung, Angebot, Gutschrift, Auftragsbestätigung), der in EstrichONE einer Bauakte zugeordnet
/// ist. Die Zuordnung liegt nur hier; in Lexware Office wird dafür nichts geschrieben.
/// </summary>
public sealed class LexwareVoucherLink
{
    public const string InvoiceType = "invoice";

    /// <summary>Von Hand zugeordnet (Vorgabe).</summary>
    public const string OriginManual = "manual";

    /// <summary>Automatisch zugeordnet (<see cref="AutoReason"/> nennt den Grund).</summary>
    public const string OriginAuto = "auto";

    /// <summary><see cref="OriginManual"/> oder <see cref="OriginAuto"/>.</summary>
    public string Origin { get; set; } = OriginManual;

    /// <summary>Grund der automatischen Zuordnung (<see cref="LexwareAutoAssignRules"/>).</summary>
    public string? AutoReason { get; set; }

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

    /// <summary>
    /// Status laut Lexware: draft, open, paid, paidoff, voided, bei Angeboten auch accepted und rejected. „Überfällig“ wird
    /// bei Rechnungen aus Fälligkeit und Status abgeleitet.
    /// </summary>
    public string Status { get; set; } = "draft";

    public string? ContactName { get; set; }
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
        builder.Property(s => s.OrganizationId).HasMaxLength(100);
        builder.Property(s => s.OrganizationName).HasMaxLength(LexwareContactLink.MaxName);
        builder.Property(s => s.LastTestError).HasMaxLength(2000);
        builder.Property(s => s.ChangeSync).HasConversion<string>().HasMaxLength(20).HasDefaultValue(LexwareChangeSync.Both);
        builder.Property(s => s.LastChangeSyncError).HasMaxLength(2000);
        builder.Property(s => s.AutoCreate).HasDefaultValue(true);
        builder.Property(s => s.ArticleSync).HasConversion<string>().HasMaxLength(20).HasDefaultValue(LexwareArticleSync.FromLexware);
        builder.Property(s => s.LastArticleSyncError).HasMaxLength(2000);
        builder.Property(s => s.StatusOnQuotationAccepted).HasDefaultValue(true);
        builder.Property(s => s.StatusOnInvoicesPaid).HasDefaultValue(true);
        builder.Property(s => s.AutoAssign).HasDefaultValue(true);
        builder.Property(s => s.ProfileTaxType).HasMaxLength(30);
        builder.Property(s => s.RejectedTaxTypes).HasMaxLength(4000);
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
        builder.Property(l => l.RemoteState).HasMaxLength(20);
        builder.Property(l => l.SyncError).HasMaxLength(2000);
        builder.Property(l => l.DeviationCount).HasDefaultValue(0);
        builder.Property(l => l.SyncAttempts).HasDefaultValue(0);
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
        builder.Property(l => l.ContactName).HasMaxLength(LexwareContactLink.MaxName);
        builder.Property(l => l.NetAmount).HasPrecision(14, 2);
        builder.Property(l => l.GrossAmount).HasPrecision(14, 2);
        builder.Property(l => l.Currency).HasMaxLength(3);
        builder.Property(l => l.Status).HasMaxLength(20);
        builder.Property(l => l.SyncError).HasMaxLength(1000);
        builder.Property(l => l.Origin).HasMaxLength(10).HasDefaultValue(LexwareVoucherLink.OriginManual);
        builder.Property(l => l.AutoReason).HasMaxLength(20);
    }
}
