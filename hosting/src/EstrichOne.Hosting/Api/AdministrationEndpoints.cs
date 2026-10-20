using System.Globalization;
using System.Text;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Docker;
using EstrichOne.Hosting.Domain;
using EstrichOne.Hosting.Infrastructure;
using EstrichOne.Hosting.Operations;
using EstrichOne.Hosting.Proxy;
using EstrichOne.Hosting.Status;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Api;

/// <summary>Tarife, Übersicht, Ereignisprotokoll und Einstellungen.</summary>
public static class AdministrationEndpoints
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var plans = app.MapGroup("/api/plans").WithTags("Tarife").RequireAuthorization();
        plans.MapGet("/", async (HostingDbContext db, CancellationToken ct) =>
        {
            var counts = await db.Tenants.Where(t => t.State != TenantState.Deleted).GroupBy(t => t.PlanId)
                .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            return Results.Ok((await db.Plans.AsNoTracking().ToListAsync(ct)).OrderBy(p => p.MonthlyPrice ?? 0).ThenBy(p => p.Name)
                .Select(p => PlanDto.From(p, counts.GetValueOrDefault(p.Id))));
        });
        plans.MapPost("/", CreatePlanAsync);
        plans.MapPut("/{id:guid}", UpdatePlanAsync);
        plans.MapDelete("/{id:guid}", DeletePlanAsync);

        app.MapGet("/api/dashboard", DashboardAsync).RequireAuthorization().WithTags("Übersicht");
        app.MapGet("/api/audit", AuditAsync).RequireAuthorization().WithTags("Ereignisse");
        app.MapGet("/api/audit/export", AuditExportAsync).RequireAuthorization().WithTags("Ereignisse");
        app.MapGet("/api/operations", async (HostingDbContext db, CancellationToken ct) =>
        {
            var operations = await db.Operations.AsNoTracking().OrderByDescending(o => o.QueuedAt).Take(100).ToListAsync(ct);
            var ids = operations.Select(o => o.TenantId).Distinct().ToList();
            var names = await db.Tenants.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.CompanyName, ct);
            return Results.Ok(operations.Select(o => new { operation = TenantService.ToSummary(o), tenantId = o.TenantId, companyName = names.GetValueOrDefault(o.TenantId) }));
        }).RequireAuthorization().WithTags("Vorgänge");

        var system = app.MapGroup("/api/system").WithTags("Einstellungen").RequireAuthorization();
        system.MapGet("/console-update", async (ConsoleUpdater updater, VersionCatalog catalog, CancellationToken ct) =>
        {
            var versions = await catalog.GetAsync([], ct, "estrichone-hosting");
            return Results.Ok(new
            {
                current = ConsoleVersion.Current,
                versions.Versions,
                versions.Latest,
                updateAvailable = SemanticVersion.IsOlder(ConsoleVersion.Current, versions.Latest),
                state = updater.State,
            });
        });
        system.MapPost("/console-update", async (SettingsInput input, ConsoleUpdater updater, AuditLog audit, CancellationToken ct) =>
        {
            var version = input.DefaultVersion?.Trim();
            if (version != "latest" && Validation.CheckVersion(version) is { } error)
            {
                return ApiErrors.BadRequest(error);
            }

            var state = await updater.StartAsync(version!, ct);
            await audit.WriteAsync("Konsole aktualisieren", details: $"Zielversion {version}: {state.Message}", success: state.Success != false, cancellationToken: ct);
            return state.Success == false ? ApiErrors.Conflict(state.Message) : Results.Accepted(value: state);
        });

        var settings = app.MapGroup("/api/settings").WithTags("Einstellungen").RequireAuthorization();
        settings.MapGet("/", SettingsAsync);
        settings.MapPut("/", SaveSettingsAsync);
        app.MapGet("/api/versions", async (HostingDbContext db, VersionCatalog catalog, CancellationToken ct) =>
            Results.Ok(await catalog.GetAsync(await db.Tenants.Where(t => t.State != TenantState.Deleted).Select(t => t.Version).Distinct().ToListAsync(ct), ct)))
            .RequireAuthorization().WithTags("Einstellungen");
    }

    private static Dictionary<string, string> ValidatePlan(PlanInput input)
    {
        var errors = new Dictionary<string, string>();
        if (Validation.CheckText(input.Name?.Trim(), "Der Name", 80, true) is { } nameError) errors["name"] = nameError;
        if (Validation.CheckText(input.Description, "Die Beschreibung", 400, false) is { } descriptionError) errors["description"] = descriptionError;
        if (input.MaxUsers is < 0 or > 100_000) errors["maxUsers"] = "Benutzerlimit 0 (unbegrenzt) bis 100000.";
        if (input.StorageGb is < 0 or > 100_000) errors["storageGb"] = "Speicher 0 (unbegrenzt) bis 100000 GB.";
        if (input.MemoryMb is < 0 or (> 0 and < 256) or > 262_144) errors["memoryMb"] = "Arbeitsspeicher 0 (keine Grenze) oder 256 bis 262144 MB.";
        if (input.CpuLimit is < 0 or > 128) errors["cpuLimit"] = "CPU-Grenze 0 (keine) bis 128 Kerne.";
        if (input.MonthlyPrice is < 0 or > 1_000_000) errors["monthlyPrice"] = "Ungültiger Preis.";
        return errors;
    }

    private static void Apply(Plan plan, PlanInput input)
    {
        plan.Name = input.Name!.Trim();
        plan.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        plan.MaxUsers = input.MaxUsers ?? 0;
        plan.StorageGb = input.StorageGb ?? 0;
        plan.MemoryMb = input.MemoryMb ?? 0;
        plan.CpuLimit = Math.Round(input.CpuLimit ?? 0, 2);
        plan.MonthlyPrice = input.MonthlyPrice;
    }

    private static string Describe(Plan plan) => string.Create(CultureInfo.InvariantCulture,
        $"{plan.Name}: {plan.MaxUsers} Benutzer, {plan.StorageGb} GB, {plan.MemoryMb} MB RAM, {plan.CpuLimit} CPU");

    private static async Task<IResult> CreatePlanAsync(PlanInput input, HostingDbContext db, AuditLog audit, CancellationToken cancellationToken)
    {
        var errors = ValidatePlan(input);
        if (errors.Count == 0 && await db.Plans.AnyAsync(p => p.Name == input.Name!.Trim(), cancellationToken))
        {
            errors["name"] = "Einen Tarif mit diesem Namen gibt es bereits.";
        }

        if (errors.Count > 0)
        {
            return ApiErrors.Validation(errors);
        }

        var plan = new Plan { Name = string.Empty };
        Apply(plan, input);
        db.Plans.Add(plan);
        await audit.WriteAsync("Tarif angelegt", details: Describe(plan), cancellationToken: cancellationToken);
        return Results.Created($"/api/plans/{plan.Id}", PlanDto.From(plan, 0));
    }

    private static async Task<IResult> UpdatePlanAsync(Guid id, PlanInput input, HostingDbContext db, AuditLog audit,
        OperationService operations, CancellationToken cancellationToken)
    {
        var plan = await db.Plans.FindAsync([id], cancellationToken);
        if (plan is null)
        {
            return ApiErrors.NotFound("Tarif nicht gefunden.");
        }

        var errors = ValidatePlan(input);
        if (errors.Count == 0 && await db.Plans.AnyAsync(p => p.Name == input.Name!.Trim() && p.Id != id, cancellationToken))
        {
            errors["name"] = "Einen Tarif mit diesem Namen gibt es bereits.";
        }

        if (errors.Count > 0)
        {
            return ApiErrors.Validation(errors);
        }

        var limitsChanged = plan.MemoryMb != (input.MemoryMb ?? 0) || plan.CpuLimit != Math.Round(input.CpuLimit ?? 0, 2);
        Apply(plan, input);
        await audit.WriteAsync("Tarif geändert", details: Describe(plan), cancellationToken: cancellationToken);

        // Neue Ressourcengrenzen auf alle laufenden Instanzen dieses Tarifs anwenden.
        var applied = 0;
        if (limitsChanged)
        {
            var tenants = await db.Tenants.Where(t => t.PlanId == id && (t.State == TenantState.Running || t.State == TenantState.Stopped || t.State == TenantState.Locked))
                .ToListAsync(cancellationToken);
            foreach (var tenant in tenants)
            {
                try
                {
                    await operations.EnqueueAsync(tenant, OperationKind.Reconfigure, null, cancellationToken);
                    applied++;
                }
                catch (OperationConflictException)
                {
                    // Wird beim nächsten Vorgang dieses Mandanten ohnehin übernommen.
                }
            }
        }

        return Results.Ok(new { reconfigured = applied });
    }

    private static async Task<IResult> DeletePlanAsync(Guid id, HostingDbContext db, AuditLog audit, CancellationToken cancellationToken)
    {
        var plan = await db.Plans.FindAsync([id], cancellationToken);
        if (plan is null)
        {
            return ApiErrors.NotFound("Tarif nicht gefunden.");
        }

        if (await db.Tenants.AnyAsync(t => t.PlanId == id && t.State != TenantState.Deleted, cancellationToken))
        {
            return ApiErrors.Conflict("Der Tarif ist noch Mandanten zugeordnet.");
        }

        // Gelöschte Mandanten behalten ihren Tarifverweis nicht.
        foreach (var deleted in await db.Tenants.Where(t => t.PlanId == id).ToListAsync(cancellationToken))
        {
            db.Tenants.Remove(deleted);
        }

        db.Plans.Remove(plan);
        await audit.WriteAsync("Tarif gelöscht", details: plan.Name, cancellationToken: cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DashboardAsync(HostingDbContext db, StatusService status, ConsoleSettings settings,
        IDockerHost docker, ProxyManager proxy, IOptions<HostingOptions> options, CancellationToken cancellationToken)
    {
        var defaultVersion = await settings.GetDefaultVersionAsync(cancellationToken);
        var tenants = await db.Tenants.AsNoTracking().Include(t => t.Plan).Where(t => t.State != TenantState.Deleted).ToListAsync(cancellationToken);
        var statuses = tenants.ToDictionary(t => t.Id, t => status.Get(t.Slug));
        var problems = tenants
            .Where(t => t.State == TenantState.Failed || statuses[t.Id].Health is HealthLevel.Unhealthy or HealthLevel.Degraded || t.LastError is not null)
            .Select(t => new
            {
                tenantId = t.Id,
                t.Slug,
                t.CompanyName,
                state = t.State,
                health = statuses[t.Id].Health,
                problem = t.State == TenantState.Failed && t.LastError is not null ? t.LastError : (statuses[t.Id].Problems.Count > 0 ? statuses[t.Id].Problems[0] : t.LastError),
            })
            .OrderByDescending(p => p.health)
            .Take(20)
            .ToList();

        long? free = null, total = null;
        try
        {
            var drive = new DriveInfo(options.Value.Root);
            free = drive.AvailableFreeSpace;
            total = drive.TotalSize;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Root existiert (noch) nicht
        }

        var events = await db.AuditEvents.AsNoTracking().OrderByDescending(a => a.Id).Take(10).ToListAsync(cancellationToken);
        var runningOperations = await db.Operations.CountAsync(o => o.Status == OperationStatus.Running || o.Status == OperationStatus.Queued, cancellationToken);
        var info = await docker.GetSystemInfoAsync(cancellationToken);
        return Results.Ok(new
        {
            defaultVersion,
            tenants = new
            {
                total = tenants.Count,
                running = tenants.Count(t => t.State == TenantState.Running),
                stopped = tenants.Count(t => t.State == TenantState.Stopped),
                locked = tenants.Count(t => t.State == TenantState.Locked),
                failed = tenants.Count(t => t.State == TenantState.Failed),
                provisioning = tenants.Count(t => t.State == TenantState.Provisioning),
            },
            health = new
            {
                healthy = statuses.Values.Count(s => s.Health == HealthLevel.Healthy),
                degraded = statuses.Values.Count(s => s.Health == HealthLevel.Degraded),
                unhealthy = statuses.Values.Count(s => s.Health == HealthLevel.Unhealthy),
                stopped = statuses.Values.Count(s => s.Health == HealthLevel.Stopped),
                unknown = statuses.Values.Count(s => s.Health == HealthLevel.Unknown),
            },
            outdated = tenants.Count(t => SemanticVersion.IsOlder(t.Version, defaultVersion)),
            versions = tenants.GroupBy(t => t.Version).Select(g => new { version = g.Key, count = g.Count() }).OrderByDescending(v => v.count).ToList(),
            storageBytes = statuses.Values.Sum(s => s.DiskBytes ?? 0),
            storageLimitBytes = tenants.Sum(t => (long)(t.Plan?.StorageGb ?? 0) * 1024 * 1024 * 1024),
            memoryBytes = statuses.Values.Sum(s => s.MemoryBytes),
            cpuPercent = Math.Round(statuses.Values.Sum(s => s.CpuPercent), 1),
            activeUsers = statuses.Values.Sum(s => s.ActiveUsers ?? 0),
            backupsStale = tenants.Count(t => statuses[t.Id].Backup is { Configured: true, Healthy: false } && DateTime.UtcNow - t.CreatedAt > BackupState.MaxAge),
            setupPending = statuses.Values.Count(s => s.SetupRequired == true),
            runningOperations,
            problems,
            recentEvents = events,
            system = new
            {
                docker = info,
                diskFreeBytes = free,
                diskTotalBytes = total,
                proxyMode = options.Value.Proxy,
                proxyError = proxy.LastError,
                proxyReloadedAt = proxy.LastReload,
                consoleVersion = ConsoleVersion.Current,
                simulation = options.Value.IsFake,
            },
        });
    }

    private static IQueryable<AuditEvent> FilterAudit(HostingDbContext db, string? q, Guid? tenant, bool? success, DateTime? from, DateTime? to)
    {
        var query = db.AuditEvents.AsNoTracking();
        if (tenant is not null) query = query.Where(a => a.TenantId == tenant);
        if (success is not null) query = query.Where(a => a.Success == success);
        if (from is not null) query = query.Where(a => a.At >= from.Value.ToUniversalTime());
        if (to is not null) query = query.Where(a => a.At <= to.Value.ToUniversalTime());
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(a => EF.Functions.Like(a.Action, $"%{term}%") || EF.Functions.Like(a.Actor, $"%{term}%")
                || (a.TenantSlug != null && EF.Functions.Like(a.TenantSlug, $"%{term}%"))
                || (a.Details != null && EF.Functions.Like(a.Details, $"%{term}%")));
        }

        return query;
    }

    private static async Task<IResult> AuditAsync(string? q, Guid? tenant, bool? success, DateTime? from, DateTime? to, int? skip, int? take,
        HostingDbContext db, CancellationToken cancellationToken)
    {
        var query = FilterAudit(db, q, tenant, success, from, to);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(a => a.Id).Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? 50, 1, 500)).ToListAsync(cancellationToken);
        return Results.Ok(new { total, items });
    }

    private static async Task<IResult> AuditExportAsync(string? q, Guid? tenant, bool? success, DateTime? from, DateTime? to,
        HostingDbContext db, AuditLog audit, CancellationToken cancellationToken)
    {
        var items = await FilterAudit(db, q, tenant, success, from, to).OrderByDescending(a => a.Id).Take(50_000).ToListAsync(cancellationToken);
        var csv = new StringBuilder("Zeitpunkt (UTC);Benutzer;Aktion;Mandant;Ergebnis;Details;Adresse\n");
        static string Cell(string? value)
        {
            var text = (value ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal);
            // Formel-Injektion in Tabellenkalkulationen verhindern.
            if (text.Length > 0 && "=+-@".Contains(text[0], StringComparison.Ordinal))
            {
                text = "'" + text;
            }

            return $"\"{text}\"";
        }

        foreach (var item in items)
        {
            csv.Append(Cell(item.At.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(';')
                .Append(Cell(item.Actor)).Append(';').Append(Cell(item.Action)).Append(';').Append(Cell(item.TenantSlug)).Append(';')
                .Append(Cell(item.Success ? "erfolgreich" : "fehlgeschlagen")).Append(';').Append(Cell(item.Details)).Append(';')
                .Append(Cell(item.IpAddress)).Append('\n');
        }

        await audit.WriteAsync("Ereignisprotokoll exportiert", details: $"{items.Count} Einträge", cancellationToken: cancellationToken);
        return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8",
            $"ereignisprotokoll-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }

    private static async Task<IResult> SettingsAsync(ConsoleSettings settings, IOptions<HostingOptions> options, CancellationToken cancellationToken)
    {
        var o = options.Value;
        return Results.Ok(new
        {
            defaultVersion = await settings.GetDefaultVersionAsync(cancellationToken),
            baseDomain = o.BaseDomain,
            consoleDomain = o.ConsoleDomain,
            proxy = o.Proxy,
            proxyContainer = o.ProxyContainer,
            tls = o.Tls,
            registry = o.Registry,
            root = o.Root,
            backupRoot = o.BackupRoot,
            simulation = o.IsFake,
            consoleVersion = ConsoleVersion.Current,
        });
    }

    public sealed record SettingsInput(string? DefaultVersion);

    private static async Task<IResult> SaveSettingsAsync(SettingsInput input, ConsoleSettings settings, AuditLog audit, CancellationToken cancellationToken)
    {
        var version = input.DefaultVersion?.Trim();
        if (Validation.CheckVersion(version) is { } error)
        {
            return ApiErrors.BadRequest(error);
        }

        await settings.SetDefaultVersionAsync(version!, cancellationToken);
        await audit.WriteAsync("Standardversion geändert", details: version, cancellationToken: cancellationToken);
        return Results.NoContent();
    }
}
