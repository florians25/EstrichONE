using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Documentation;

public enum FileKind
{
    Photo,
    Video,
    Document,
}

public enum VariantState
{
    /// <summary>Kein Bild – keine Vorschau nötig.</summary>
    NotApplicable,
    Pending,
    Ready,
    /// <summary>Format nicht verarbeitbar (z. B. HEIC ohne Decoder); ausgeliefert wird das Original.</summary>
    Failed,
}

public enum FileState
{
    /// <summary>Metadaten bekannt (z. B. offline angelegt und synchronisiert), Inhalt noch nicht hochgeladen.</summary>
    AwaitingUpload,
    /// <summary>Inhalt vollständig hochgeladen, Prüfsumme bestätigt.</summary>
    Stored,
}

/// <summary>
/// Foto, Video oder Dokument. Dateien sind unveränderlich (nur anlegen und löschen) und damit
/// offline konfliktfrei (docs/reengineering/15). Der Inhalt liegt außerhalb der Datenbank (Upload: AP-07).
/// </summary>
public sealed class FileRecord : IVersionedEntity, ISoftDeletable
{
    public Guid Id { get; private set; }
    public Guid? BauakteId { get; private set; }
    public FileKind Kind { get; private set; }
    public FileState State { get; private set; } = FileState.AwaitingUpload;
    public string OriginalName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }

    /// <summary>SHA-256 des Inhalts als Kleinbuchstaben-Hex (64 Zeichen), vom Client vorab gemeldet und beim Upload geprüft.</summary>
    public string Sha256 { get; private set; } = string.Empty;
    public string? StorageKey { get; private set; }

    /// <summary>Aus dem Inhalt ermittelter Typ (maßgeblich für die Auslieferung, nicht die Client-Angabe).</summary>
    public string? DetectedContentType { get; private set; }
    public VariantState Variants { get; private set; } = VariantState.NotApplicable;
    public string? Caption { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public Guid UploadedByUserId { get; private set; }

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private FileRecord()
    {
    }

    public static FileRecord Announce(Guid id, Guid? bauakteId, FileKind kind, string originalName, string contentType,
        long sizeBytes, string sha256, Guid uploadedByUserId, DateTimeOffset? capturedAt = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("SHA-256 muss als 64-stellige Hex-Zeichenfolge angegeben werden.", nameof(sha256));
        }

        return new FileRecord
        {
            Id = id,
            BauakteId = bauakteId,
            Kind = kind,
            OriginalName = originalName,
            ContentType = contentType,
            SizeBytes = sizeBytes,
            Sha256 = sha256.ToLowerInvariant(),
            UploadedByUserId = uploadedByUserId,
            CapturedAt = capturedAt,
        };
    }

    public void MarkStored(string storageKey, string detectedContentType, bool needsVariants)
    {
        StorageKey = storageKey;
        DetectedContentType = detectedContentType;
        State = FileState.Stored;
        Variants = needsVariants ? VariantState.Pending : VariantState.NotApplicable;
    }

    public void SetVariants(VariantState state) => Variants = state;

    /// <summary>Gleiche ID, gleicher Inhalt → wiederholte Ankündigung (z. B. nach Verbindungsabbruch).</summary>
    public bool IsSameAnnouncement(Guid? bauakteId, FileKind kind, string sha256, long sizeBytes) =>
        BauakteId == bauakteId && Kind == kind && string.Equals(Sha256, sha256, StringComparison.OrdinalIgnoreCase) && SizeBytes == sizeBytes;

    public void Describe(string? caption) => Caption = caption;

    public void SetLocation(double? latitude, double? longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public void Delete(DateTimeOffset at) => DeletedAt ??= at;
}

internal sealed class FileRecordConfiguration : IEntityTypeConfiguration<FileRecord>
{
    public void Configure(EntityTypeBuilder<FileRecord> builder)
    {
        builder.ToTable("files", table =>
        {
            table.HasCheckConstraint("ck_files_size_not_negative", "size_bytes >= 0");
            table.HasCheckConstraint("ck_files_sha256_hex", "sha256 ~ '^[0-9a-f]{64}$'");
        });
        builder.ConfigureVersioned();
        builder.Property(file => file.Id).ValueGeneratedNever();
        builder.Property(file => file.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(file => file.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(file => file.OriginalName).HasMaxLength(255);
        builder.Property(file => file.ContentType).HasMaxLength(100);
        builder.Property(file => file.Sha256).HasMaxLength(64).IsFixedLength();
        builder.Property(file => file.StorageKey).HasMaxLength(200);
        builder.Property(file => file.DetectedContentType).HasMaxLength(100);
        builder.Property(file => file.Variants).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(file => file.Variants).HasFilter("variants = 'Pending'");
        builder.Property(file => file.Caption).HasMaxLength(500);
        builder.HasOne<Bauakte>().WithMany().HasForeignKey(file => file.BauakteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(file => file.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(file => new { file.BauakteId, file.Kind });
        builder.HasIndex(file => file.Sha256);
    }
}
