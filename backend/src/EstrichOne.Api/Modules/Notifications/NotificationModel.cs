using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Notifications.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Notifications;

/// <summary>Zustellung als Pushnachricht (das Postfach hat jede Benachrichtigung unabhängig davon).</summary>
public enum PushState
{
    /// <summary>Wartet auf den Versand.</summary>
    Pending,

    /// <summary>An mindestens ein Gerät zugestellt (vom Push-Dienst angenommen).</summary>
    Sent,

    /// <summary>Kein Gerät mit Pushnachrichten registriert – nur im Postfach.</summary>
    NoDevice,

    /// <summary>Nur im Postfach: Push für die registrierten Geräte nicht eingerichtet (z. B. APNs ohne Schlüssel).</summary>
    InAppOnly,

    /// <summary>Alle Zustellversuche abgelehnt oder nicht erreichbar.</summary>
    Failed,

    /// <summary>Zu alt für einen Versand (länger als 24 Stunden liegen geblieben).</summary>
    Expired,
}

public enum PushPlatform
{
    Ios,
    Android,
    Web,
}

/// <summary>Eintrag im Postfach eines Benutzers.</summary>
public sealed class Notification
{
    public const int MaxTitle = 200;
    public const int MaxBody = 2000;
    public const int MaxLink = 500;

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public NotificationCategory Category { get; private set; }
    public NotificationUrgency Urgency { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Body { get; private set; }
    public string? Link { get; private set; }
    public string? ObjectType { get; private set; }
    public Guid? ObjectId { get; private set; }
    public Guid? BroadcastId { get; private set; }
    public Guid? SenderUserId { get; private set; }
    public string? DedupKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ReadAt { get; private set; }
    public PushState PushState { get; private set; }
    public DateTimeOffset? PushedAt { get; private set; }
    public string? PushError { get; private set; }

    public static Notification Create(Guid userId, NotificationRequest request, DateTimeOffset now, Guid? senderUserId, Guid? broadcastId = null) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        Category = request.Category,
        Urgency = request.Urgency,
        Title = Clip(request.Title, MaxTitle) ?? "Benachrichtigung",
        Body = Clip(request.Body, MaxBody),
        Link = NotificationLinks.Normalize(request.Link),
        ObjectType = Clip(request.ObjectType, 50),
        ObjectId = request.ObjectId,
        BroadcastId = broadcastId,
        SenderUserId = senderUserId,
        DedupKey = Clip(request.DedupKey, 200),
        CreatedAt = now,
        PushState = PushState.Pending,
    };

    public void MarkRead(DateTimeOffset now) => ReadAt ??= now;

    public void MarkUnread() => ReadAt = null;

    public void SetPush(PushState state, DateTimeOffset now, string? error = null)
    {
        PushState = state;
        PushedAt = state == PushState.Sent ? now : PushedAt;
        PushError = Clip(error, 300);
    }

    internal static string? Clip(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > max ? value.Trim()[..max] : value.Trim();
}

/// <summary>Von Hand gesendete Nachricht (Verlauf „Gesendet“).</summary>
public sealed class NotificationBroadcast
{
    public Guid Id { get; private set; }
    public Guid SenderUserId { get; private set; }
    public string SenderName { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Body { get; private set; }
    public string? Link { get; private set; }
    public NotificationUrgency Urgency { get; private set; }
    public string Audience { get; private set; } = string.Empty;
    public int RecipientCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static NotificationBroadcast Create(Guid sender, string senderName, NotificationRequest request, string audience, int recipients, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        SenderUserId = sender,
        SenderName = senderName,
        Title = Notification.Clip(request.Title, Notification.MaxTitle) ?? string.Empty,
        Body = Notification.Clip(request.Body, Notification.MaxBody),
        Link = NotificationLinks.Normalize(request.Link),
        Urgency = request.Urgency,
        Audience = Notification.Clip(audience, 500) ?? string.Empty,
        RecipientCount = recipients,
        CreatedAt = now,
    };
}

/// <summary>Gerät bzw. Browser, das Pushnachrichten empfängt (APNs-Token, FCM-Token oder Web-Push-Abo).</summary>
public sealed class PushDevice
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public PushPlatform Platform { get; private set; }

    /// <summary>APNs-/FCM-Token bzw. Endpunkt-URL des Web-Push-Abos.</summary>
    public string Token { get; private set; } = string.Empty;

    /// <summary>SHA-256 des Tokens (eindeutig; ein Token gehört immer nur zum zuletzt angemeldeten Benutzer).</summary>
    public string TokenHash { get; private set; } = string.Empty;

    public string? P256dh { get; private set; }
    public string? AuthSecret { get; private set; }

    /// <summary>Nur iOS: Token aus der Entwicklungsumgebung (Xcode/TestFlight-Debug) → APNs-Sandbox.</summary>
    public bool Sandbox { get; private set; }

    public string? BundleId { get; private set; }
    public string? DeviceName { get; private set; }
    public string? AppVersion { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset? LastSuccessAt { get; private set; }
    public int FailureCount { get; private set; }
    public string? LastError { get; private set; }

    public static PushDevice Create(Guid userId, PushPlatform platform, string token, string tokenHash, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(), UserId = userId, Platform = platform, Token = token, TokenHash = tokenHash, CreatedAt = now, LastSeenAt = now,
    };

    public void Refresh(Guid userId, string? p256dh, string? auth, bool sandbox, string? bundleId, string? deviceName, string? appVersion, DateTimeOffset now)
    {
        UserId = userId;
        P256dh = Notification.Clip(p256dh, 200);
        AuthSecret = Notification.Clip(auth, 100);
        Sandbox = sandbox;
        BundleId = Notification.Clip(bundleId, 200);
        DeviceName = Notification.Clip(deviceName, 200);
        AppVersion = Notification.Clip(appVersion, 50);
        LastSeenAt = now;
        FailureCount = 0;
        LastError = null;
    }

    public void Succeeded(DateTimeOffset now)
    {
        LastSuccessAt = now;
        FailureCount = 0;
        LastError = null;
    }

    public void Failed(string error)
    {
        FailureCount++;
        LastError = Notification.Clip(error, 300);
    }
}

/// <summary>Push-Einrichtung der Installation (eine Zeile). Private Schlüssel nur verschlüsselt (Data Protection, AES-256-GCM).</summary>
public sealed class PushSettingsRow : IVersionedEntity
{
    public const int SingletonId = 1;

    public int Id { get; private set; } = SingletonId;
    public bool WebPushEnabled { get; set; } = true;
    public string? VapidPublicKey { get; set; }
    public string? ProtectedVapidPrivateKey { get; set; }
    public string? VapidSubject { get; set; }
    public bool ApnsEnabled { get; set; }
    public string? ApnsKeyId { get; set; }
    public string? ApnsTeamId { get; set; }
    public string? ApnsBundleId { get; set; }
    public bool ApnsProduction { get; set; } = true;
    public string? ProtectedApnsKey { get; set; }

    /// <summary>Text der Benachrichtigung in der Pushnachricht zeigen; sonst nur „Neue Benachrichtigung“ (Inhalt im Postfach).</summary>
    public bool ShowContent { get; set; } = true;

    public long Version { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Links nur als relative Pfade der eigenen Oberfläche (kein offener Weiterleiter, keine fremden Adressen).</summary>
public static class NotificationLinks
{
    public static string? Normalize(string? link)
    {
        var value = Notification.Clip(link, Notification.MaxLink);
        if (value is null)
        {
            return null;
        }

        return value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal) && !value.Contains('\\', StringComparison.Ordinal)
            && !value.Contains("://", StringComparison.Ordinal) ? value : null;
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Category).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.Urgency).HasConversion<string>().HasMaxLength(10);
        builder.Property(n => n.PushState).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.Title).HasMaxLength(Notification.MaxTitle);
        builder.Property(n => n.Body).HasMaxLength(Notification.MaxBody);
        builder.Property(n => n.Link).HasMaxLength(Notification.MaxLink);
        builder.Property(n => n.ObjectType).HasMaxLength(50);
        builder.Property(n => n.DedupKey).HasMaxLength(200);
        builder.Property(n => n.PushError).HasMaxLength(300);
        builder.HasIndex(n => new { n.UserId, n.CreatedAt });
        builder.HasIndex(n => new { n.UserId, n.DedupKey }).IsUnique().HasFilter("dedup_key IS NOT NULL");
        builder.HasIndex(n => n.PushState).HasFilter("push_state = 'Pending'");
        builder.HasIndex(n => n.BroadcastId);
    }
}

internal sealed class NotificationBroadcastConfiguration : IEntityTypeConfiguration<NotificationBroadcast>
{
    public void Configure(EntityTypeBuilder<NotificationBroadcast> builder)
    {
        builder.ToTable("notification_broadcasts");
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.SenderName).HasMaxLength(200);
        builder.Property(b => b.Title).HasMaxLength(Notification.MaxTitle);
        builder.Property(b => b.Body).HasMaxLength(Notification.MaxBody);
        builder.Property(b => b.Link).HasMaxLength(Notification.MaxLink);
        builder.Property(b => b.Urgency).HasConversion<string>().HasMaxLength(10);
        builder.Property(b => b.Audience).HasMaxLength(500);
        builder.HasIndex(b => b.CreatedAt);
    }
}

internal sealed class PushDeviceConfiguration : IEntityTypeConfiguration<PushDevice>
{
    public void Configure(EntityTypeBuilder<PushDevice> builder)
    {
        builder.ToTable("push_devices");
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Platform).HasConversion<string>().HasMaxLength(10);
        builder.Property(d => d.Token).HasMaxLength(2000);
        builder.Property(d => d.TokenHash).HasMaxLength(64);
        builder.Property(d => d.P256dh).HasMaxLength(200);
        builder.Property(d => d.AuthSecret).HasMaxLength(100);
        builder.Property(d => d.BundleId).HasMaxLength(200);
        builder.Property(d => d.DeviceName).HasMaxLength(200);
        builder.Property(d => d.AppVersion).HasMaxLength(50);
        builder.Property(d => d.LastError).HasMaxLength(300);
        builder.HasIndex(d => d.TokenHash).IsUnique();
        builder.HasIndex(d => d.UserId);
    }
}

internal sealed class PushSettingsRowConfiguration : IEntityTypeConfiguration<PushSettingsRow>
{
    public void Configure(EntityTypeBuilder<PushSettingsRow> builder)
    {
        builder.ToTable("push_settings");
        builder.ConfigureVersioned();
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.VapidPublicKey).HasMaxLength(200);
        builder.Property(s => s.VapidSubject).HasMaxLength(200);
        builder.Property(s => s.ApnsKeyId).HasMaxLength(20);
        builder.Property(s => s.ApnsTeamId).HasMaxLength(20);
        builder.Property(s => s.ApnsBundleId).HasMaxLength(200);
    }
}
