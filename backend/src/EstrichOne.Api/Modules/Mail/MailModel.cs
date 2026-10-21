using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Mail;

public enum MailSecurity
{
    /// <summary>STARTTLS (Port 587) – Standard bei den meisten Anbietern.</summary>
    StartTls,

    /// <summary>SSL/TLS ab Verbindungsbeginn (Port 465).</summary>
    SslOnConnect,

    /// <summary>Unverschlüsselt (nur interne Relays, z. B. im Firmennetz).</summary>
    None,
}

public enum MailStatus
{
    Queued,
    Sent,
    Failed,
}

/// <summary>SMTP-Konto (eines je Installation). Das Passwort liegt nur verschlüsselt (Data Protection) in der Datenbank.</summary>
public sealed class MailAccount : IAuditedEntity
{
    public static readonly Guid SingletonId = new("00006a11-0000-7000-8000-000000000001");

    public Guid Id { get; private set; } = SingletonId;
    public bool Enabled { get; private set; }
    public string Host { get; private set; } = string.Empty;
    public int Port { get; private set; } = 587;
    public MailSecurity Security { get; private set; } = MailSecurity.StartTls;
    public string? UserName { get; private set; }
    public string? ProtectedPassword { get; private set; }
    public string FromAddress { get; private set; } = string.Empty;
    public string? FromName { get; private set; }
    public string? ReplyTo { get; private set; }
    public DateTimeOffset? LastTestAt { get; private set; }
    public bool? LastTestOk { get; private set; }
    public string? LastTestError { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static MailAccount Create() => new();

    public void Update(bool enabled, string host, int port, MailSecurity security, string? userName, string fromAddress, string? fromName,
        string? replyTo)
    {
        Enabled = enabled;
        Host = host.Trim();
        Port = port;
        Security = security;
        UserName = string.IsNullOrWhiteSpace(userName) ? null : userName.Trim();
        FromAddress = fromAddress.Trim();
        FromName = string.IsNullOrWhiteSpace(fromName) ? null : fromName.Trim();
        ReplyTo = string.IsNullOrWhiteSpace(replyTo) ? null : replyTo.Trim();
    }

    public void SetProtectedPassword(string? protectedPassword) => ProtectedPassword = protectedPassword;

    public void Tested(DateTimeOffset at, string? error)
    {
        LastTestAt = at;
        LastTestOk = error is null;
        LastTestError = error;
    }

    public bool IsUsable => Enabled && Host.Length > 0 && FromAddress.Length > 0;
}

/// <summary>Angepasste Vorlage; ohne Eintrag gilt der eingebaute Text (<see cref="DefaultMailTemplates"/>).</summary>
public sealed class MailTemplate : IAuditedEntity
{
    public string Key { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static MailTemplate Create(string key) => new() { Key = key };

    public void Update(string subject, string body)
    {
        Subject = subject.Trim();
        Body = body;
    }
}

/// <summary>Versandprotokoll; zugleich Warteschlange (Status Queued) mit Wiederholung.</summary>
public sealed class MailLogEntry : IAuditedEntity
{
    public Guid Id { get; private set; }
    public string To { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public string? TemplateKey { get; private set; }
    public string? Category { get; private set; }
    public string? RelatedEntity { get; private set; }

    /// <summary>Anhänge (JSON mit Base64) bis zum Versand; danach geleert, nur die Namen bleiben.</summary>
    public string? AttachmentsJson { get; private set; }
    public string? AttachmentNames { get; private set; }
    public MailStatus Status { get; private set; } = MailStatus.Queued;
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? Error { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private MailLogEntry()
    {
    }

    public static MailLogEntry Queue(string to, string subject, string body, string? templateKey, string? category, string? relatedEntity,
        string? attachmentsJson, string? attachmentNames, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now), To = to, Subject = subject, Body = body, TemplateKey = templateKey, Category = category,
        RelatedEntity = relatedEntity, AttachmentsJson = attachmentsJson, AttachmentNames = attachmentNames, NextAttemptAt = now,
    };

    public void Sent(DateTimeOffset at)
    {
        Attempts++;
        Status = MailStatus.Sent;
        SentAt = at;
        Error = null;
        AttachmentsJson = null;
        // Inhalt nach dem Versand nicht aufbewahren (kann Einmalpasswörter enthalten); Protokoll behält Empfänger und Betreff.
        Body = string.Empty;
    }

    /// <summary>Wiederholung nach 1, 5, 30 Minuten und 2 Stunden; danach endgültig fehlgeschlagen.</summary>
    public void Failed(string error, DateTimeOffset now, bool permanent)
    {
        Attempts++;
        Error = error.Length > 1000 ? error[..1000] : error;
        TimeSpan? delay = permanent ? null : Attempts switch
        {
            1 => TimeSpan.FromMinutes(1),
            2 => TimeSpan.FromMinutes(5),
            3 => TimeSpan.FromMinutes(30),
            4 => TimeSpan.FromHours(2),
            _ => null,
        };
        if (delay is { } wait)
        {
            NextAttemptAt = now + wait;
        }
        else
        {
            Status = MailStatus.Failed;
        }
    }

    public void Retry(DateTimeOffset now)
    {
        if (Status == MailStatus.Failed)
        {
            Status = MailStatus.Queued;
            NextAttemptAt = now;
        }
    }
}

internal sealed class MailAccountConfiguration : IEntityTypeConfiguration<MailAccount>
{
    public void Configure(EntityTypeBuilder<MailAccount> builder)
    {
        builder.ToTable("mail_account");
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Host).HasMaxLength(253);
        builder.Property(a => a.Security).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.UserName).HasMaxLength(254);
        builder.Property(a => a.FromAddress).HasMaxLength(254);
        builder.Property(a => a.FromName).HasMaxLength(200);
        builder.Property(a => a.ReplyTo).HasMaxLength(254);
        builder.Property(a => a.LastTestError).HasMaxLength(1000);
    }
}

internal sealed class MailTemplateConfiguration : IEntityTypeConfiguration<MailTemplate>
{
    public void Configure(EntityTypeBuilder<MailTemplate> builder)
    {
        builder.ToTable("mail_templates");
        builder.HasKey(t => t.Key);
        builder.Property(t => t.Key).HasMaxLength(60);
        builder.Property(t => t.Subject).HasMaxLength(300);
        builder.Property(t => t.Body).HasMaxLength(20_000);
    }
}

internal sealed class MailLogEntryConfiguration : IEntityTypeConfiguration<MailLogEntry>
{
    public void Configure(EntityTypeBuilder<MailLogEntry> builder)
    {
        builder.ToTable("mail_log");
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.To).HasMaxLength(254);
        builder.Property(m => m.Subject).HasMaxLength(300);
        builder.Property(m => m.TemplateKey).HasMaxLength(60);
        builder.Property(m => m.Category).HasMaxLength(60);
        builder.Property(m => m.RelatedEntity).HasMaxLength(100);
        builder.Property(m => m.AttachmentNames).HasMaxLength(1000);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Error).HasMaxLength(1000);
        builder.HasIndex(m => new { m.Status, m.NextAttemptAt });
        builder.HasIndex(m => m.CreatedAt);
    }
}
