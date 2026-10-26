using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Archive;

/// <summary>Wann ein Dokument automatisch ins Archiv geht.</summary>
public enum ArchiveTrigger
{
    /// <summary>Sobald die Datei vollständig hochgeladen ist.</summary>
    OnUpload,

    /// <summary>Sobald das Dokument freigegeben wurde.</summary>
    OnApproval,

    /// <summary>Sobald die Bauakte abgeschlossen ist.</summary>
    OnBauakteCompleted,

    /// <summary>Nur auf Knopfdruck (Dokument oder ganze Bauakte).</summary>
    Manual,
}

public enum ArchiveRecordStatus
{
    Pending,
    Archived,
    Failed,
}

/// <summary>Gekoppelter Archivserver (höchstens einer je Installation).</summary>
public sealed class ArchiveConnection : IAuditedEntity
{
    public static readonly Guid SingletonId = new("0000a5c1-0000-7000-8000-000000000001");

    public Guid Id { get; private set; } = SingletonId;
    public string Address { get; private set; } = string.Empty;
    public string ServerName { get; private set; } = string.Empty;
    public string ServerVersion { get; private set; } = string.Empty;
    public string CaCertificatePem { get; private set; } = string.Empty;
    public string CaFingerprint { get; private set; } = string.Empty;
    public string ClientId { get; private set; } = string.Empty;

    /// <summary>Zugangstoken, verschlüsselt mit ASP.NET Data Protection.</summary>
    public string ProtectedToken { get; private set; } = string.Empty;
    public DateTimeOffset PairedAt { get; private set; }
    public Guid PairedByUserId { get; private set; }
    public DateTimeOffset? LastContactAt { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Automatische Archivierung nach den Regeln (aus = nur manuell).</summary>
    public bool AutoArchive { get; private set; } = true;

    /// <summary>Benachrichtigung bei Fehlern (E-Mail), optional.</summary>
    public string? NotifyEmail { get; private set; }

    /// <summary>Abgeschlossene Bauakten nach so vielen Monaten komplett auslagern (null = nur manuell).</summary>
    public int? DossierAfterMonths { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private ArchiveConnection()
    {
    }

    public static ArchiveConnection Create(string address, string serverName, string serverVersion, string caPem, string fingerprint,
        string clientId, string protectedToken, Guid userId, DateTimeOffset at) => new()
    {
        Address = address, ServerName = serverName, ServerVersion = serverVersion, CaCertificatePem = caPem, CaFingerprint = fingerprint,
        ClientId = clientId, ProtectedToken = protectedToken, PairedByUserId = userId, PairedAt = at, LastContactAt = at,
    };

    public void Configure(bool autoArchive, string? notifyEmail, int? dossierAfterMonths)
    {
        DossierAfterMonths = dossierAfterMonths is > 0 and <= 240 ? dossierAfterMonths : null;
        AutoArchive = autoArchive;
        NotifyEmail = string.IsNullOrWhiteSpace(notifyEmail) ? null : notifyEmail.Trim();
    }

    public void Contacted(DateTimeOffset at, string? error, string? serverVersion = null)
    {
        LastError = error;
        if (error is null)
        {
            LastContactAt = at;
        }

        if (serverVersion is not null)
        {
            ServerVersion = serverVersion;
        }
    }
}

/// <summary>Regel je Dokumentart: Frist in Jahren und Auslöser.</summary>
public sealed class ArchiveRule : IAuditedEntity
{
    /// <summary>Dokumentart (offer, invoice, …) oder „photo“/„video“.</summary>
    public string DocumentType { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public int RetentionYears { get; private set; }
    public ArchiveTrigger Trigger { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private ArchiveRule()
    {
    }

    public static ArchiveRule Create(string documentType, bool enabled, int retentionYears, ArchiveTrigger trigger)
    {
        var rule = new ArchiveRule { DocumentType = documentType };
        rule.Update(enabled, retentionYears, trigger);
        return rule;
    }

    public void Update(bool enabled, int retentionYears, ArchiveTrigger trigger)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(retentionYears, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(retentionYears, 30);
        Enabled = enabled;
        RetentionYears = retentionYears;
        Trigger = trigger;
    }
}

/// <summary>Archivierung einer Datei: Warteschlange, Wiederholung und Nachweis (Quittung des Archivservers).</summary>
public sealed class ArchiveRecord : IAuditedEntity
{
    public Guid Id { get; private set; }
    public Guid FileId { get; private set; }
    public Guid? BauakteId { get; private set; }
    public string DocumentType { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Sha256 { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public int RetentionYears { get; private set; }
    public ArchiveTrigger Trigger { get; private set; }
    public ArchiveRecordStatus Status { get; private set; } = ArchiveRecordStatus.Pending;
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public string? LastError { get; private set; }
    public Guid? RequestedByUserId { get; private set; }

    // Nachweis (Quittung des Archivservers)
    public DateTimeOffset? ArchivedAt { get; private set; }
    public DateOnly? RetainUntil { get; private set; }
    public long? LedgerSeq { get; private set; }
    public string? LedgerHash { get; private set; }
    public string? ArchiveServerName { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private ArchiveRecord()
    {
    }

    public static ArchiveRecord Queue(Guid fileId, Guid? bauakteId, string documentType, string name, string sha256, long size,
        int retentionYears, ArchiveTrigger trigger, Guid? requestedBy, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now), FileId = fileId, BauakteId = bauakteId, DocumentType = documentType,
        Name = name.Length > 255 ? name[..255] : name, Sha256 = sha256, SizeBytes = size, RetentionYears = retentionYears,
        Trigger = trigger, RequestedByUserId = requestedBy, NextAttemptAt = now,
    };

    public void Succeeded(DateTimeOffset archivedAt, DateOnly retainUntil, long ledgerSeq, string ledgerHash, string serverName)
    {
        Status = ArchiveRecordStatus.Archived;
        ArchivedAt = archivedAt;
        RetainUntil = retainUntil;
        LedgerSeq = ledgerSeq;
        LedgerHash = ledgerHash;
        ArchiveServerName = serverName;
        LastError = null;
        Attempts++;
    }

    /// <summary>Wiederholung mit wachsendem Abstand: 1, 5, 15, 60 Minuten, danach alle 6 Stunden.</summary>
    public void Failed(string error, DateTimeOffset now, bool permanent)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        Status = permanent || Attempts >= 3 ? ArchiveRecordStatus.Failed : ArchiveRecordStatus.Pending;
        var delay = Attempts switch
        {
            1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(15),
            4 => TimeSpan.FromHours(1),
            _ => TimeSpan.FromHours(6),
        };
        NextAttemptAt = now + delay;
    }

    /// <summary>Manuell erneut versuchen (sofort).</summary>
    public void Retry(DateTimeOffset now)
    {
        if (Status != ArchiveRecordStatus.Archived)
        {
            Status = ArchiveRecordStatus.Pending;
            NextAttemptAt = now;
        }
    }

    /// <summary>Fehlgeschlagene werden automatisch weiter versucht (seltener), damit nichts liegen bleibt.</summary>
    public bool IsDue(DateTimeOffset now) => Status != ArchiveRecordStatus.Archived && NextAttemptAt <= now;
}

internal sealed class ArchiveConnectionConfiguration : IEntityTypeConfiguration<ArchiveConnection>
{
    public void Configure(EntityTypeBuilder<ArchiveConnection> builder)
    {
        builder.ToTable("archive_connection");
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Address).HasMaxLength(300);
        builder.Property(c => c.ServerName).HasMaxLength(200);
        builder.Property(c => c.ServerVersion).HasMaxLength(50);
        builder.Property(c => c.CaFingerprint).HasMaxLength(64);
        builder.Property(c => c.ClientId).HasMaxLength(64);
        builder.Property(c => c.LastError).HasMaxLength(1000);
        builder.Property(c => c.NotifyEmail).HasMaxLength(254);
    }
}

internal sealed class ArchiveRuleConfiguration : IEntityTypeConfiguration<ArchiveRule>
{
    public void Configure(EntityTypeBuilder<ArchiveRule> builder)
    {
        builder.ToTable("archive_rules", table => table.HasCheckConstraint("ck_archive_rules_years", "retention_years BETWEEN 1 AND 30"));
        builder.HasKey(rule => rule.DocumentType);
        builder.Property(rule => rule.DocumentType).HasMaxLength(40);
        builder.Property(rule => rule.Trigger).HasConversion<string>().HasMaxLength(30);
    }
}

internal sealed class ArchiveRecordConfiguration : IEntityTypeConfiguration<ArchiveRecord>
{
    public void Configure(EntityTypeBuilder<ArchiveRecord> builder)
    {
        builder.ToTable("archive_records");
        builder.Property(record => record.Id).ValueGeneratedNever();
        builder.HasIndex(record => record.FileId).IsUnique();
        builder.HasIndex(record => record.BauakteId);
        builder.HasIndex(record => new { record.Status, record.NextAttemptAt });
        builder.Property(record => record.DocumentType).HasMaxLength(40);
        builder.Property(record => record.Name).HasMaxLength(255);
        builder.Property(record => record.Sha256).HasMaxLength(64);
        builder.Property(record => record.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(record => record.Trigger).HasConversion<string>().HasMaxLength(30);
        builder.Property(record => record.LastError).HasMaxLength(1000);
        builder.Property(record => record.LedgerHash).HasMaxLength(64);
        builder.Property(record => record.ArchiveServerName).HasMaxLength(200);
    }
}
