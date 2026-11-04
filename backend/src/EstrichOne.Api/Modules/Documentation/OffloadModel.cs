using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Documentation;

/// <summary>Zustand eines Originals in der Speicherstufe Storage.</summary>
public enum OffloadState
{
    /// <summary>Verschlüsselt auf dem Storage, Prüfsumme bestätigt; lokal noch vorhanden – wartet auf die Sicherungskopie.</summary>
    Pending,

    /// <summary>Lokal gelöscht, liegt nur noch (verschlüsselt) auf dem Storage und in der Sicherung.</summary>
    Offloaded,

    /// <summary>Zurückgeholt (wieder lokal); die Kopie auf dem Storage wird noch in den Papierkorb gelegt.</summary>
    Recalled,

    /// <summary>Weder lokal noch auf dem Storage auffindbar – aus der Sicherung zurückholen (docs/speicher.md).</summary>
    Missing,
}

/// <summary>
/// Kopplung mit dem Storage (Kombiserver bzw. Archivrechner, Dienst „estrichone-offload“), Regeln und Hauptschlüssel der
/// Auslagerung. Eine Zeile. Der Hauptschlüssel bleibt beim Entkoppeln erhalten (ausgelagerte Objekte bleiben lesbar).
/// </summary>
public sealed class OffloadConnection : Infrastructure.Persistence.IAuditedEntity
{
    public static readonly Guid SingletonId = new("0000a5c1-0000-7000-8000-0000000000f1");

    public Guid Id { get; private set; } = SingletonId;
    public string? Address { get; private set; }
    public string? ServerName { get; private set; }
    public string? ServerVersion { get; private set; }
    public string? CaCertificatePem { get; private set; }
    public string? CaFingerprint { get; private set; }
    public string? ClientId { get; private set; }

    /// <summary>Zugangstoken, verschlüsselt mit ASP.NET Data Protection.</summary>
    public string? ProtectedToken { get; private set; }

    /// <summary>Hauptschlüssel (32 Byte) für die Verschlüsselung der Objekte, mit ASP.NET Data Protection verschlüsselt.</summary>
    public string ProtectedMasterKey { get; private set; } = string.Empty;
    public DateTimeOffset? PairedAt { get; private set; }
    public Guid? PairedByUserId { get; private set; }
    public DateTimeOffset? LastContactAt { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Automatisch auslagern, wenn der freie Platz unter die Schwelle fällt.</summary>
    public bool AutoWhenLow { get; private set; } = true;

    /// <summary>Zusätzlich regelmäßig nach den Regeln auslagern (auch ohne Platznot).</summary>
    public bool AutoByRules { get; private set; }

    /// <summary>Schwelle: weniger als so viel Prozent frei = knapp.</summary>
    public int ThresholdPercent { get; private set; } = 15;

    /// <summary>Optional zusätzlich: weniger als so viele GB frei = knapp.</summary>
    public int? ThresholdFreeGb { get; private set; }

    /// <summary>Nur Dateien, die älter als so viele Monate sind (0 = jedes Alter).</summary>
    public int MinAgeMonths { get; private set; } = 6;

    /// <summary>Nur Dateien abgeschlossener Bauakten.</summary>
    public bool OnlyCompletedBauakten { get; private set; }

    public bool IncludePhotos { get; private set; } = true;
    public bool IncludeVideos { get; private set; } = true;
    public bool IncludeDocuments { get; private set; } = true;

    /// <summary>Erst lokal löschen, wenn die Originale zusätzlich in einer Sicherung liegen (dringend empfohlen).</summary>
    public bool RequireBackup { get; private set; } = true;

    /// <summary>Höchstgröße des Zwischenspeichers für vom Storage geladene Originale (MB).</summary>
    public int CacheLimitMb { get; private set; } = 2048;

    public DateTimeOffset? LastRunAt { get; private set; }
    public string? LastRunMessage { get; private set; }

    /// <summary>Wann zuletzt „Platz knapp“ gemeldet wurde (einmal täglich).</summary>
    public DateTimeOffset? LowSpaceNotifiedAt { get; private set; }
    public DateTimeOffset? UnreachableNotifiedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private OffloadConnection()
    {
    }

    public static OffloadConnection Create(string protectedMasterKey) => new() { ProtectedMasterKey = protectedMasterKey };

    public bool Paired => ProtectedToken is not null && Address is not null;

    public void Pair(string address, string serverName, string serverVersion, string caPem, string fingerprint, string clientId, string protectedToken,
        Guid userId, DateTimeOffset at)
    {
        Address = address;
        ServerName = serverName;
        ServerVersion = serverVersion;
        CaCertificatePem = caPem;
        CaFingerprint = fingerprint;
        ClientId = clientId;
        ProtectedToken = protectedToken;
        PairedByUserId = userId;
        PairedAt = at;
        LastContactAt = at;
        LastError = null;
    }

    public void Unpair()
    {
        Address = ServerName = ServerVersion = CaCertificatePem = CaFingerprint = ClientId = ProtectedToken = null;
        PairedAt = null;
        PairedByUserId = null;
        LastError = null;
    }

    public void Configure(bool autoWhenLow, bool autoByRules, int thresholdPercent, int? thresholdFreeGb, int minAgeMonths, bool onlyCompleted,
        bool photos, bool videos, bool documents, bool requireBackup, int cacheLimitMb)
    {
        AutoWhenLow = autoWhenLow;
        AutoByRules = autoByRules;
        ThresholdPercent = Math.Clamp(thresholdPercent, 1, 90);
        ThresholdFreeGb = thresholdFreeGb is > 0 ? Math.Min(thresholdFreeGb.Value, 100_000) : null;
        MinAgeMonths = Math.Clamp(minAgeMonths, 0, 240);
        OnlyCompletedBauakten = onlyCompleted;
        IncludePhotos = photos;
        IncludeVideos = videos;
        IncludeDocuments = documents;
        RequireBackup = requireBackup;
        CacheLimitMb = Math.Clamp(cacheLimitMb, 100, 1_000_000);
    }

    public void Contacted(DateTimeOffset at, string? error, string? serverVersion = null)
    {
        LastError = error is null ? null : error[..Math.Min(error.Length, 1000)];
        if (error is null)
        {
            LastContactAt = at;
            UnreachableNotifiedAt = null;
        }

        if (serverVersion is not null)
        {
            ServerVersion = serverVersion;
        }
    }

    public void Ran(DateTimeOffset at, string message)
    {
        LastRunAt = at;
        LastRunMessage = message[..Math.Min(message.Length, 1000)];
    }

    public void NotifiedLowSpace(DateTimeOffset? at) => LowSpaceNotifiedAt = at;

    public void NotifiedUnreachable(DateTimeOffset at) => UnreachableNotifiedAt = at;
}

/// <summary>Ein ausgelagertes Original (eine Ablagedatei; Dubletten teilen sich die Ablage und damit diesen Eintrag).</summary>
public sealed class OffloadedBlob
{
    public string StorageKey { get; private set; } = string.Empty;

    /// <summary>Kennung auf dem Storage (zufällig, verrät nichts über die Datei).</summary>
    public Guid ObjectId { get; private set; }

    /// <summary>SHA-256 und Größe des Originals (Klartext).</summary>
    public string Sha256 { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }

    /// <summary>SHA-256 und Größe des verschlüsselten Objekts auf dem Storage.</summary>
    public string CipherSha256 { get; private set; } = string.Empty;
    public long CipherBytes { get; private set; }
    public OffloadState State { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }
    public DateTimeOffset? OffloadedAt { get; private set; }

    /// <summary>Anforderung der Sicherungskopie (Backup-Dienst, <c>requests/offload-backup.list</c>).</summary>
    public string? BackupRequestId { get; private set; }
    public DateTimeOffset? BackedUpAt { get; private set; }
    public DateTimeOffset? LastAccessAt { get; private set; }
    public string? LastError { get; private set; }

    private OffloadedBlob()
    {
    }

    public static OffloadedBlob Uploaded(string storageKey, Guid objectId, string sha256, long size, string cipherSha256, long cipherBytes,
        DateTimeOffset at) => new()
    {
        StorageKey = storageKey, ObjectId = objectId, Sha256 = sha256, SizeBytes = size, CipherSha256 = cipherSha256, CipherBytes = cipherBytes,
        State = OffloadState.Pending, UploadedAt = at,
    };

    public void AwaitBackup(string? requestId) => BackupRequestId = requestId;

    public void MarkOffloaded(DateTimeOffset at, DateTimeOffset? backedUpAt)
    {
        State = OffloadState.Offloaded;
        OffloadedAt = at;
        BackedUpAt = backedUpAt;
        LastError = null;
    }

    public void MarkRecalled() => State = OffloadState.Recalled;

    public void MarkMissing(string error)
    {
        State = OffloadState.Missing;
        LastError = error;
    }

    public void Accessed(DateTimeOffset at) => LastAccessAt = at;

    public void Failed(string? error) => LastError = error is null ? null : error[..Math.Min(error.Length, 500)];
}

internal sealed class OffloadConnectionConfiguration : IEntityTypeConfiguration<OffloadConnection>
{
    public void Configure(EntityTypeBuilder<OffloadConnection> builder)
    {
        builder.ToTable("offload_connection", table => table.HasCheckConstraint("ck_offload_threshold", "threshold_percent BETWEEN 1 AND 90"));
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Address).HasMaxLength(300);
        builder.Property(c => c.ServerName).HasMaxLength(200);
        builder.Property(c => c.ServerVersion).HasMaxLength(50);
        builder.Property(c => c.CaFingerprint).HasMaxLength(64);
        builder.Property(c => c.ClientId).HasMaxLength(64);
        builder.Property(c => c.LastError).HasMaxLength(1000);
        builder.Property(c => c.LastRunMessage).HasMaxLength(1000);
    }
}

internal sealed class OffloadedBlobConfiguration : IEntityTypeConfiguration<OffloadedBlob>
{
    public void Configure(EntityTypeBuilder<OffloadedBlob> builder)
    {
        builder.ToTable("offloaded_blobs");
        builder.HasKey(b => b.StorageKey);
        builder.Property(b => b.StorageKey).HasMaxLength(200);
        builder.Property(b => b.Sha256).HasMaxLength(64).IsFixedLength();
        builder.Property(b => b.CipherSha256).HasMaxLength(64).IsFixedLength();
        builder.Property(b => b.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.BackupRequestId).HasMaxLength(32);
        builder.Property(b => b.LastError).HasMaxLength(500);
        builder.HasIndex(b => b.ObjectId).IsUnique();
        builder.HasIndex(b => b.State);
    }
}
