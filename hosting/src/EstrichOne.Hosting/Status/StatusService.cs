using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Docker;
using EstrichOne.Hosting.Infrastructure;
using EstrichOne.Hosting.Instances;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Status;

/// <summary>Erhebt den Betriebszustand der Instanzen und hält die letzte Momentaufnahme im Speicher.</summary>
public sealed partial class StatusService(
    IDockerHost docker,
    IInstanceProbe probe,
    InstanceLayout layout,
    TimeProvider clock,
    IOptions<HostingOptions> options,
    ILogger<StatusService> logger)
{
    private readonly ConcurrentDictionary<string, TenantStatus> snapshots = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, UsageSample> usage = new(StringComparer.Ordinal);

    public TenantStatus Get(string slug) => snapshots.TryGetValue(slug, out var status) ? status : TenantStatus.Empty(slug);

    public IReadOnlyDictionary<string, TenantStatus> All => snapshots;

    public void Forget(string slug)
    {
        snapshots.TryRemove(slug, out _);
        usage.TryRemove(slug, out _);
    }

    public async Task<TenantStatus> CollectAsync(Tenant tenant, Plan plan, bool forceUsage, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        try
        {
            var project = InstanceLayout.ProjectName(tenant.Slug);
            var containers = await docker.ListContainersAsync(project, cancellationToken);
            var running = containers.Where(c => c.State == "running").ToList();
            var stats = await docker.GetUsageAsync(running.Select(c => c.Id).ToList(), cancellationToken);
            var expectedRunning = tenant.State is TenantState.Running or TenantState.Failed
                || (tenant.State == TenantState.Locked && tenant.StateBeforeLock == TenantState.Running && running.Count > 0);

            var probeResult = running.Any(c => c.Service == "caddy") ? await probe.ProbeAsync(tenant.Slug, cancellationToken) : null;

            string? setupCode = null;
            if (probeResult?.SetupRequired == true)
            {
                setupCode = await ReadSetupCodeAsync(tenant.Slug, cancellationToken);
            }

            var sample = usage.GetValueOrDefault(tenant.Slug);
            if (forceUsage || sample is null || now - sample.MeasuredAt > TimeSpan.FromMinutes(options.Value.UsageIntervalMinutes))
            {
                sample = await MeasureUsageAsync(tenant.Slug, running.Any(c => c.Service == "postgres"), now, cancellationToken);
                usage[tenant.Slug] = sample;
            }

            var backup = BackupState.Read(Path.Combine(layout.DataDirectory(tenant.Slug), "backup-status"), now);
            var (level, problems) = HealthEvaluator.Evaluate(
                expectedRunning, containers, probeResult, backup,
                backupExpected: now - tenant.CreatedAt > BackupState.MaxAge,
                sample.DiskBytes, (long)plan.StorageGb * 1024 * 1024 * 1024, sample.ActiveUsers, plan.MaxUsers);

            var containerStatus = containers.Select(c =>
            {
                var s = stats.FirstOrDefault(x => x.Id.StartsWith(c.Id, StringComparison.Ordinal) || c.Id.StartsWith(x.Id, StringComparison.Ordinal));
                return new ContainerStatus(c.Service, c.Name, c.State, c.Health, c.Status, c.Image, c.ExitCode, s?.CpuPercent, s?.MemoryBytes, s?.MemoryLimitBytes);
            }).ToList();

            var status = new TenantStatus(tenant.Slug, now, level, containerStatus, probeResult, probeResult?.ServerVersion,
                probeResult?.SetupRequired, setupCode, Math.Round(stats.Sum(s => s.CpuPercent), 2), stats.Sum(s => s.MemoryBytes),
                sample.DiskBytes, sample.MeasuredAt, sample.ActiveUsers, backup, problems, null);
            snapshots[tenant.Slug] = status;
            return status;
        }
        catch (Exception exception) when (exception is DockerException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Status von {Slug} nicht ermittelbar: {Message}", tenant.Slug, exception.Message);
            var previous = Get(tenant.Slug);
            var failed = previous with { CollectedAt = now, Health = HealthLevel.Unknown, Error = exception.Message, Problems = [exception.Message] };
            snapshots[tenant.Slug] = failed;
            return failed;
        }
    }

    /// <summary>Der Einrichtungscode steht nur im Protokoll der API (wie beim Suite-Installer).</summary>
    public async Task<string?> ReadSetupCodeAsync(string slug, CancellationToken cancellationToken)
    {
        try
        {
            var logs = await docker.GetLogsAsync(layout.Project(slug), "api", 2000, cancellationToken);
            var matches = SetupCodePattern().Matches(logs);
            return matches.Count == 0 ? null : matches[^1].Groups[1].Value;
        }
        catch (DockerException)
        {
            return null;
        }
    }

    private async Task<UsageSample> MeasureUsageAsync(string slug, bool postgresRunning, DateTime now, CancellationToken cancellationToken)
    {
        long? disk = null;
        try
        {
            disk = DirectorySize(layout.Directory(slug));
            var backupDirectory = layout.BackupDirectory(slug);
            if (!backupDirectory.StartsWith(layout.Directory(slug), StringComparison.Ordinal))
            {
                disk += DirectorySize(backupDirectory);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Speicherplatz von {Slug} nicht messbar: {Message}", slug, exception.Message);
        }

        int? users = null;
        if (postgresRunning)
        {
            var result = await docker.ExecAsync(InstanceLayout.PostgresContainer(slug),
                ["psql", "-U", "estrichone", "-d", "estrichone", "-tAc", "SELECT count(*) FROM users WHERE is_active"],
                TimeSpan.FromSeconds(20), cancellationToken);
            if (result.Success && int.TryParse(result.Output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                users = count;
            }
        }

        return new UsageSample(disk, users ?? usage.GetValueOrDefault(slug)?.ActiveUsers, now);
    }

    public static long DirectorySize(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            ReturnSpecialDirectories = false,
        };
        long total = 0;
        foreach (var file in new DirectoryInfo(directory).EnumerateFiles("*", options))
        {
            total += file.Length;
        }

        return total;
    }

    [GeneratedRegex(@"Einrichtungscode: ([A-Z0-9]{4}(?:-[A-Z0-9]{4})+)", RegexOptions.CultureInvariant)]
    private static partial Regex SetupCodePattern();
}

/// <summary>Erhebt den Zustand aller Instanzen in festen Abständen.</summary>
public sealed class StatusCollector(
    IServiceScopeFactory scopes,
    StatusService status,
    IOptions<HostingOptions> options,
    ILogger<StatusCollector> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.StatusIntervalSeconds)));
        do
        {
            try
            {
                await CollectAllAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Statuserhebung fehlgeschlagen.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task CollectAllAsync(CancellationToken cancellationToken)
    {
        List<Tenant> tenants;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostingDbContext>();
            tenants = await db.Tenants.AsNoTracking().Include(t => t.Plan)
                .Where(t => t.State != TenantState.Deleted && t.State != TenantState.Deleting)
                .ToListAsync(cancellationToken);
        }

        await Parallel.ForEachAsync(tenants, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
            async (tenant, token) => await status.CollectAsync(tenant, tenant.Plan!, forceUsage: false, token));
    }
}
