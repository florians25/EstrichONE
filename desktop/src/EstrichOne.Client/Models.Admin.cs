using System.Text.Json;

namespace EstrichOne.Client;

// Verwaltung: Benutzer und Rollen, Geräte, Sicherung, Protokoll (contracts/openapi/v1.json).

public sealed record UserListItem(
    Guid Id, string DisplayName, string UserName, string? Email, string? Phone, bool IsActive, DateTimeOffset? LastLoginAt,
    IReadOnlyList<Guid> RoleIds, long Version);

public sealed record CreateUserRequest(string DisplayName, string UserName, string? Email, string? Phone, IReadOnlyList<Guid> RoleIds);

public sealed record UpdateUserRequest(string DisplayName, string? Email, string? Phone);

/// <summary>Neu angelegter oder zurückgesetzter Benutzer mit Einmalpasswort (wird nur einmal angezeigt).</summary>
public sealed record UserWithPassword(UserListItem User, string OneTimePassword);

public sealed record Role(Guid Id, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions, long Version)
{
    public override string ToString() => Name;
}

public sealed record PermissionInfo(string Code, string Group, string Description);

public enum DevicePlatform { Ios, Android, Windows, Other }

public enum DeviceKind { Personal, Pool }

public enum DeviceStatus { Pending, Active, Locked, Retired }

public sealed record Device(
    Guid Id, string Name, DevicePlatform Platform, DeviceKind Kind, DeviceStatus Status, string? Model, string? OsVersion, string? AppVersion,
    Guid? AssignedUserId, DateTimeOffset RegisteredAt, DateTimeOffset? ApprovedAt, DateTimeOffset? LastSeenAt, long Version);

/// <summary>Einmal-Einrichtungscode; <see cref="Link"/> ist der Inhalt des QR-Codes (nur mit öffentlicher Serveradresse).</summary>
public sealed record EnrollmentCode(string Code, DeviceKind Kind, string? DeviceName, DateTimeOffset ExpiresAt, string? Link);

public sealed record BackupRunInfo(string Status, string Message, DateTimeOffset At, JsonElement? Details = null)
{
    public bool Succeeded => Status == "succeeded";
}

public sealed record BackupStatus(
    bool Configured, bool Healthy, string Summary, BackupRunInfo? LastRun, BackupRunInfo? LastSuccess, BackupRunInfo? LastCheck,
    BackupRunInfo? LastRestoreTest, bool RunRequested);

public sealed record AuditLogItem(
    Guid Id, DateTimeOffset OccurredAt, string Action, Guid? ActorUserId, string? ActorName, string? EntityType, string? EntityId,
    string? IpAddress, IReadOnlyDictionary<string, string?> Details);

public sealed record AuditLogPage(IReadOnlyList<AuditLogItem> Items, string? NextCursor);

/// <summary>Filter für das Protokoll; <see cref="Action"/> filtert per Präfix (z. B. „auth.“).</summary>
public sealed record AuditQuery(string? Action = null, Guid? ActorUserId = null, string? EntityType = null, string? EntityId = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null, string? Cursor = null, int Limit = 100);
