using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Status;

namespace EstrichOne.Hosting.Api;

public sealed record TenantInput(
    string? CompanyName,
    string? CustomerNumber,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? Slug,
    string? Domain,
    Guid? PlanId,
    string? Version,
    string? Notes,
    bool? ProvisionNow,
    string? FirstAdminName = null,
    string? FirstAdminUserName = null,
    string? FirstAdminEmail = null);

public sealed record OperationSummary(Guid Id, OperationKind Kind, string Label, OperationStatus Status, string RequestedBy,
    DateTime QueuedAt, DateTime? StartedAt, DateTime? FinishedAt, string? Error);

public sealed record OperationDetail(Guid Id, Guid TenantId, OperationKind Kind, string Label, OperationStatus Status, string RequestedBy,
    DateTime QueuedAt, DateTime? StartedAt, DateTime? FinishedAt, string? Error, string Log);

public sealed record TenantListItem(
    Guid Id,
    string Slug,
    string CompanyName,
    string? CustomerNumber,
    string? ContactName,
    string? ContactEmail,
    string Domain,
    string Url,
    Guid PlanId,
    string PlanName,
    string Version,
    bool Outdated,
    TenantState State,
    string? LockReason,
    HealthLevel Health,
    IReadOnlyList<string> Problems,
    double CpuPercent,
    long MemoryBytes,
    long? DiskBytes,
    long StorageLimitBytes,
    int? ActiveUsers,
    int MaxUsers,
    bool? BackupHealthy,
    DateTime? BackupLastSuccess,
    bool? SetupRequired,
    OperationSummary? OpenOperation,
    string? LastError,
    DateTime CreatedAt,
    DateTime? StatusAt,
    string? PreviousVersion = null,
    CertificateInfo? Certificate = null,
    bool AccessLetterAvailable = false,
    string? FirstAdminUserName = null);

public sealed record TenantDetail(
    TenantListItem Summary,
    string? ContactPhone,
    string? Notes,
    DateTime? LockedAt,
    TenantState? StateBeforeLock,
    PlanDto Plan,
    TenantStatus Status,
    string Directory,
    string ComposeProject,
    string EdgeNetwork,
    string ProxyTarget,
    string? BackupTime,
    IReadOnlyList<OperationSummary> Operations,
    SuiteInfo? Suite = null,
    string? LatestVersion = null);

public sealed record PlanDto(Guid Id, string Name, string? Description, int MaxUsers, int StorageGb, int MemoryMb, decimal CpuLimit,
    decimal? MonthlyPrice, int TenantCount)
{
    public static PlanDto From(Plan plan, int tenantCount) =>
        new(plan.Id, plan.Name, plan.Description, plan.MaxUsers, plan.StorageGb, plan.MemoryMb, plan.CpuLimit, plan.MonthlyPrice, tenantCount);
}

public sealed record PlanInput(string? Name, string? Description, int? MaxUsers, int? StorageGb, int? MemoryMb, decimal? CpuLimit, decimal? MonthlyPrice);

public sealed record ActionRequest(
    string? Version,
    string? Reason,
    bool? StopInstance,
    string? ConfirmSlug,
    bool? Confirm,
    bool? FinalBackup,
    bool? BackupFirst,
    string? Snapshot = null);

/// <param name="DryRun">Nur ermitteln, welche Instanzen betroffen wären (für die Vorschau im Dialog), nichts einreihen.</param>
public sealed record UpdateAllRequest(string? Version, bool? IncludeStopped, bool? BackupFirst, bool? DryRun = null);

public sealed record SecretRequest(string? Password);
