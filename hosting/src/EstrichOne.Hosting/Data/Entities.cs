namespace EstrichOne.Hosting.Data;

/// <summary>Lebenszustand einer Instanz aus Sicht der Konsole (der tatsächliche Container-Zustand kommt aus Docker).</summary>
public enum TenantState
{
    /// <summary>Angelegt, Bereitstellung läuft oder steht aus.</summary>
    Provisioning,
    Running,
    Stopped,
    /// <summary>Gesperrt (z. B. Zahlungsverzug): Proxy zeigt die Wartungsseite.</summary>
    Locked,
    /// <summary>Letzter Vorgang fehlgeschlagen.</summary>
    Failed,
    Deleting,
    Deleted,
}

public sealed class Plan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Description { get; set; }
    /// <summary>Höchstzahl aktiver Benutzer (0 = unbegrenzt).</summary>
    public int MaxUsers { get; set; }
    /// <summary>Speicherkontingent für Datenbank und Dateien in GB (0 = unbegrenzt).</summary>
    public int StorageGb { get; set; }
    /// <summary>Arbeitsspeichergrenze des API-Containers in MB (0 = keine Grenze).</summary>
    public int MemoryMb { get; set; }
    /// <summary>CPU-Grenze des API-Containers in Kernen (0 = keine Grenze).</summary>
    public decimal CpuLimit { get; set; }
    /// <summary>Monatspreis netto in Euro (nur zur Anzeige).</summary>
    public decimal? MonthlyPrice { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Slug { get; set; }
    public required string CompanyName { get; set; }
    public string? CustomerNumber { get; set; }
    public string? ContactName { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public required string Domain { get; set; }
    public Guid PlanId { get; set; }
    public Plan? Plan { get; set; }
    public required string Version { get; set; }
    public TenantState State { get; set; } = TenantState.Provisioning;
    /// <summary>Zustand vor der Sperre (für das Entsperren).</summary>
    public TenantState? StateBeforeLock { get; set; }
    public string? LockReason { get; set; }
    public DateTime? LockedAt { get; set; }
    public string? Notes { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    /// <summary>Erste Admin-Person der Suite (legt die Konsole bei der Bereitstellung an, Zugangsbrief).</summary>
    public string? FirstAdminName { get; set; }
    public string? FirstAdminUserName { get; set; }
    public string? FirstAdminEmail { get; set; }

    /// <summary>Version vor der letzten Aktualisierung (Rückkehr per „Zurücksetzen“ möglich).</summary>
    public string? PreviousVersion { get; set; }

    /// <summary>Snapshot der Vorab-Sicherung der letzten Aktualisierung (Daten für das Zurücksetzen).</summary>
    public string? RollbackSnapshot { get; set; }
}

/// <summary>Berechtigung eines Kontos der Konsole.</summary>
public enum AdminRole
{
    /// <summary>Darf alles: Instanzen anlegen, ändern, aktualisieren, löschen; Konten und Einstellungen verwalten.</summary>
    Administrator,
    /// <summary>Nur lesen (Bereitschaft, Leitstand, Support): Zustand, Protokolle, Ereignisse – keine Änderungen.</summary>
    Viewer,
}

public sealed class AdminUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string UserName { get; set; }
    public string? DisplayName { get; set; }
    public AdminRole Role { get; set; } = AdminRole.Administrator;
    public required string PasswordHash { get; set; }
    /// <summary>Base32-Geheimnis für TOTP; gesetzt, aber erst nach Bestätigung aktiv.</summary>
    public string? TotpSecret { get; set; }
    public bool TotpEnabled { get; set; }
    /// <summary>Zuletzt verwendeter TOTP-Zeitschritt (verhindert Wiederverwendung eines Codes).</summary>
    public long TotpLastStep { get; set; }
    public int FailedLogins { get; set; }
    public DateTime? LockedUntil { get; set; }
    /// <summary>Erhöht sich bei Passwortänderung – beendet alle anderen Sitzungen.</summary>
    public int SecurityStamp { get; set; }
    public string? Preferences { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Vom Installer angelegt bzw. zurückgesetzt: Passwort bei der ersten Anmeldung ändern.</summary>
    public bool MustChangePassword { get; set; }
}

public sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public required string Actor { get; set; }
    public required string Action { get; set; }
    public Guid? TenantId { get; set; }
    public string? TenantSlug { get; set; }
    public string? Details { get; set; }
    public bool Success { get; set; } = true;
    public string? IpAddress { get; set; }
}

public enum OperationKind
{
    Provision,
    Start,
    Stop,
    Restart,
    Update,
    Lock,
    Unlock,
    Backup,
    Delete,
    Reconfigure,
    /// <summary>Daten aus einer Sicherung (Snapshot) zurückspielen.</summary>
    Restore,
    /// <summary>Letzte Aktualisierung zurücknehmen: vorherige Version und Daten der Vorab-Sicherung.</summary>
    Rollback,
    /// <summary>Erste Admin-Person in der Suite anlegen (Ersteinrichtung über die Konsole).</summary>
    InitialSetup,
}

public enum OperationStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
}

/// <summary>Ein länger laufender Vorgang an einer Instanz (Bereitstellung, Aktualisierung …) mit Protokoll.</summary>
public sealed class Operation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public OperationKind Kind { get; set; }
    public OperationStatus Status { get; set; } = OperationStatus.Queued;
    /// <summary>Parameter als JSON (z. B. Zielversion).</summary>
    public string? Parameters { get; set; }
    public required string RequestedBy { get; set; }
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string Log { get; set; } = string.Empty;
    public string? Error { get; set; }
}

/// <summary>Einstellungen der Konsole als Schlüssel/Wert (z. B. Standardversion, Einrichtungscode-Hash).</summary>
public sealed class Setting
{
    public required string Key { get; set; }
    public required string Value { get; set; }
}

public enum NoticeKind
{
    /// <summary>Ankündigung (Banner in der Suite).</summary>
    Announcement,
    /// <summary>Wartungszeitraum: Wartungsseite, Anmeldung nur für Administratoren.</summary>
    Maintenance,
}

/// <summary>Systemhinweis, den die Konsole an ausgewählte oder alle Instanzen verteilt.</summary>
public sealed class Notice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public NoticeKind Kind { get; set; }
    public required string Title { get; set; }
    public string Text { get; set; } = string.Empty;
    /// <summary><c>info</c>, <c>warning</c>, <c>critical</c>.</summary>
    public string Severity { get; set; } = "info";
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    /// <summary>Nur Wartung: Administratoren dürfen weiterarbeiten.</summary>
    public bool AdminsMayWork { get; set; } = true;
    public bool AllTenants { get; set; } = true;
    /// <summary>Ziel-Mandanten (IDs, kommagetrennt), wenn nicht alle.</summary>
    public string TenantIds { get; set; } = string.Empty;
    public required string CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public IReadOnlySet<Guid> Targets() =>
        TenantIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty).ToHashSet();

    public bool AppliesTo(Guid tenantId) => AllTenants || Targets().Contains(tenantId);
}

/// <summary>Benachrichtigung über ein Problem (Instanz gestört, Sicherung veraltet, Zertifikat läuft ab …).</summary>
public sealed class Notification
{
    public long Id { get; set; }
    /// <summary>Eindeutiger Schlüssel des Befunds (z. B. <c>tenant:firma:health</c>); je Schlüssel höchstens ein offener Eintrag.</summary>
    public required string Key { get; set; }
    /// <summary><c>info</c>, <c>warning</c>, <c>critical</c>.</summary>
    public string Severity { get; set; } = "warning";
    public required string Title { get; set; }
    public string Text { get; set; } = string.Empty;
    public Guid? TenantId { get; set; }
    public string? TenantSlug { get; set; }
    public DateTime RaisedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public DateTime? ReadAt { get; set; }
    /// <summary>Zustellung (E-Mail/Webhook) – Ergebnis zur Anzeige.</summary>
    public string? Delivery { get; set; }
}
