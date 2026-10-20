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
    bool? ProvisionNow);

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
    DateTime? StatusAt);

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
    IReadOnlyList<OperationSummary> Operations);

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
    bool? FinalBackup);

public sealed record UpdateAllRequest(string? Version, bool? IncludeStopped);

public sealed record SecretRequest(string? Password);
