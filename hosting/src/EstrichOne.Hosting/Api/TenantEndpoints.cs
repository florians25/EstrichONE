using System.Security.Claims;
using System.Text.RegularExpressions;
using EstrichOne.Hosting.Auth;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Docker;
using EstrichOne.Hosting.Domain;
using EstrichOne.Hosting.Infrastructure;
using EstrichOne.Hosting.Instances;
using EstrichOne.Hosting.Operations;
using EstrichOne.Hosting.Status;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Api;

public static partial class TenantEndpoints
{
    /// <summary>Dienste, deren Protokoll angezeigt werden darf (feste Liste – keine freien Eingaben an Docker).</summary>
    public static readonly IReadOnlyList<string> LogServices = ["api", "caddy", "postgres", "backup", "migrate"];

    public static void Map(IEndpointRouteBuilder app)
    {
        var tenants = app.MapGroup("/api/tenants").WithTags("Mandanten").RequireAuthorization();
        tenants.MapGet("/", ListAsync);
        tenants.MapPost("/", CreateAsync);
        tenants.MapGet("/suggest", SuggestAsync);
        tenants.MapPost("/update-all", UpdateAllAsync);
        tenants.MapGet("/{id:guid}", GetAsync);
        tenants.MapPut("/{id:guid}", UpdateAsync);
        tenants.MapPost("/{id:guid}/refresh", RefreshAsync);
        tenants.MapPost("/{id:guid}/actions/{action}", ActionAsync);
        tenants.MapGet("/{id:guid}/logs", LogsAsync);
        tenants.MapGet("/{id:guid}/operations", async (Guid id, HostingDbContext db, CancellationToken ct) =>
            Results.Ok((await db.Operations.AsNoTracking().Where(o => o.TenantId == id).OrderByDescending(o => o.QueuedAt).Take(100)
                .ToListAsync(ct)).Select(TenantService.ToSummary)));
        tenants.MapPost("/{id:guid}/backup-password", BackupPasswordAsync);

        var operations = app.MapGroup("/api/operations").WithTags("Vorgänge").RequireAuthorization();
        operations.MapGet("/{id:guid}", async (Guid id, HostingDbContext db, CancellationToken ct) =>
            await db.Operations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct) is { } operation
                ? Results.Ok(TenantService.ToDetail(operation))
                : ApiErrors.NotFound());
    }

    private static async Task<IResult> ListAsync(HostingDbContext db, TenantService service, ConsoleSettings settings, CancellationToken cancellationToken)
    {
        var defaultVersion = await settings.GetDefaultVersionAsync(cancellationToken);
        var open = await service.OpenOperationsAsync(cancellationToken);
        var tenants = await db.Tenants.AsNoTracking().Include(t => t.Plan).Where(t => t.State != TenantState.Deleted)
            .OrderBy(t => t.CompanyName).ToListAsync(cancellationToken);
        return Results.Ok(tenants.Select(t => service.ToListItem(t, t.Plan!, defaultVersion, open.GetValueOrDefault(t.Id))));
    }

    private static async Task<IResult> SuggestAsync(string? company, string? slug, string? domain, HostingDbContext db, TenantService service,
        InstanceLayout layout, CancellationToken cancellationToken)
    {
        var suggested = Validation.IsValidSlug(slug) ? slug! : Validation.SuggestSlug(company);
        var candidate = suggested;
        for (var i = 2; await db.Tenants.AnyAsync(t => t.Slug == candidate, cancellationToken) || Directory.Exists(layout.Directory(candidate)); i++)
        {
            candidate = $"{suggested[..Math.Min(suggested.Length, Validation.SlugMaxLength - 3)]}-{i}";
        }

        var normalizedDomain = string.IsNullOrWhiteSpace(domain) ? service.DefaultDomain(candidate) : Validation.NormalizeDomain(domain);
        var slugError = slug is null ? null : Validation.CheckSlug(slug)
            ?? (await db.Tenants.AnyAsync(t => t.Slug == slug, cancellationToken) ? $"Das Kürzel „{slug}“ ist bereits vergeben." : null);
        var domainError = Validation.CheckDomain(normalizedDomain)
            ?? (await db.Tenants.AnyAsync(t => t.Domain == normalizedDomain, cancellationToken) ? "Die Domain ist bereits vergeben." : null);
        return Results.Ok(new { slug = candidate, domain = normalizedDomain, slugError, domainError });
    }

    private static async Task<IResult> CreateAsync(TenantInput input, HostingDbContext db, TenantService service, OperationService operations,
        AuditLog audit, ConsoleSettings settings, CancellationToken cancellationToken)
    {
        var (value, errors) = await service.ValidateAsync(input, null, cancellationToken);
        if (value is null)
        {
            return ApiErrors.Validation(errors);
        }

        var tenant = service.Create(value);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("Mandant angelegt", tenant,
            $"{tenant.CompanyName}, Domain {tenant.Domain}, Version {tenant.Version}", cancellationToken: cancellationToken);
        Operation? operation = null;
        if (input.ProvisionNow != false)
        {
            operation = await operations.EnqueueAsync(tenant, OperationKind.Provision, null, cancellationToken);
        }

        var plan = await db.Plans.FindAsync([tenant.PlanId], cancellationToken);
        return Results.Created($"/api/tenants/{tenant.Id}",
            service.ToListItem(tenant, plan!, await settings.GetDefaultVersionAsync(cancellationToken), operation));
    }

    private static async Task<IResult> GetAsync(Guid id, HostingDbContext db, TenantService service, ConsoleSettings settings,
        StatusService status, InstanceLayout layout, InstanceFiles files, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().Include(t => t.Plan).FirstOrDefaultAsync(t => t.Id == id && t.State != TenantState.Deleted, cancellationToken);
        if (tenant is null)
        {
            return ApiErrors.NotFound("Mandant nicht gefunden.");
        }

        var open = (await service.OpenOperationsAsync(cancellationToken)).GetValueOrDefault(id);
        var recent = await db.Operations.AsNoTracking().Where(o => o.TenantId == id).OrderByDescending(o => o.QueuedAt).Take(20).ToListAsync(cancellationToken);
        var tenantCount = await db.Tenants.CountAsync(t => t.PlanId == tenant.PlanId && t.State != TenantState.Deleted, cancellationToken);
        var env = await files.ReadEnvAsync(tenant.Slug, cancellationToken);
        return Results.Ok(new TenantDetail(
            service.ToListItem(tenant, tenant.Plan!, await settings.GetDefaultVersionAsync(cancellationToken), open),
            tenant.ContactPhone, tenant.Notes, tenant.LockedAt, tenant.StateBeforeLock,
            PlanDto.From(tenant.Plan!, tenantCount), status.Get(tenant.Slug), layout.Directory(tenant.Slug),
            InstanceLayout.ProjectName(tenant.Slug), InstanceLayout.EdgeNetwork(tenant.Slug),
            $"http://{InstanceLayout.WebContainer(tenant.Slug)}:{InstanceLayout.InstanceHttpPort}",
            env.GetValueOrDefault("BACKUP_TIME"), recent.Select(TenantService.ToSummary).ToList()));
    }

    private static async Task<IResult> UpdateAsync(Guid id, TenantInput input, HostingDbContext db, TenantService service,
        OperationService operations, AuditLog audit, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id && t.State != TenantState.Deleted, cancellationToken);
        if (tenant is null)
        {
            return ApiErrors.NotFound("Mandant nicht gefunden.");
        }

        var (value, errors) = await service.ValidateAsync(input, tenant, cancellationToken);
        if (value is null)
        {
            return ApiErrors.Validation(errors);
        }

        var needsReconfigure = (tenant.Domain != value.Domain || tenant.PlanId != value.PlanId || tenant.CompanyName != value.CompanyName)
            && tenant.State is not TenantState.Provisioning and not TenantState.Deleting;
        if (needsReconfigure && await operations.HasOpenOperationAsync(tenant.Id, cancellationToken))
        {
            return ApiErrors.Conflict("Domain, Tarif und Firmenname können erst nach Abschluss des laufenden Vorgangs geändert werden.");
        }

        var before = $"{tenant.CompanyName} | {tenant.Domain} | Tarif {tenant.PlanId}";
        TenantService.Apply(tenant, value);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync("Stammdaten geändert", tenant, $"vorher: {before}", cancellationToken: cancellationToken);
        if (needsReconfigure)
        {
            await operations.EnqueueAsync(tenant, OperationKind.Reconfigure, null, cancellationToken);
        }

        return Results.NoContent();
    }

    private static async Task<IResult> RefreshAsync(Guid id, HostingDbContext db, StatusService status, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().Include(t => t.Plan).FirstOrDefaultAsync(t => t.Id == id && t.State != TenantState.Deleted, cancellationToken);
        if (tenant is null)
        {
            return ApiErrors.NotFound("Mandant nicht gefunden.");
        }

        return Results.Ok(await status.CollectAsync(tenant, tenant.Plan!, forceUsage: true, cancellationToken));
    }

    private static async Task<IResult> ActionAsync(Guid id, string action, ActionRequest? request, HostingDbContext db,
        OperationService operations, AuditLog audit, CancellationToken cancellationToken)
    {
        request ??= new ActionRequest(null, null, null, null, null, null, null);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id && t.State != TenantState.Deleted, cancellationToken);
        if (tenant is null)
        {
            return ApiErrors.NotFound("Mandant nicht gefunden.");
        }

        var state = tenant.State;
        (OperationKind Kind, OperationParameters? Parameters)? plan;
        string? error = null;
        switch (action)
        {
            case "provision":
                plan = (OperationKind.Provision, null);
                if (state is not (TenantState.Provisioning or TenantState.Failed)) error = "Bereitstellen ist nur für neue oder fehlgeschlagene Instanzen möglich.";
                break;
            case "start":
                plan = (OperationKind.Start, null);
                if (state is TenantState.Deleting or TenantState.Provisioning) error = "Die Instanz kann in diesem Zustand nicht gestartet werden.";
                break;
            case "stop":
                plan = (OperationKind.Stop, null);
                if (state is TenantState.Deleting or TenantState.Provisioning or TenantState.Stopped) error = "Die Instanz läuft nicht.";
                break;
            case "restart":
                plan = (OperationKind.Restart, null);
                if (state is not (TenantState.Running or TenantState.Failed or TenantState.Locked)) error = "Nur laufende Instanzen können neu gestartet werden.";
                break;
            case "update":
                plan = (OperationKind.Update, new OperationParameters(Version: request.Version?.Trim(), BackupFirst: request.BackupFirst != false));
                error = Validation.CheckVersion(request.Version?.Trim());
                if (error is null && request.Version!.Trim() == tenant.Version) error = $"Die Instanz hat bereits Version {tenant.Version}.";
                if (state is TenantState.Deleting or TenantState.Provisioning) error = "Die Instanz kann in diesem Zustand nicht aktualisiert werden.";
                break;
            case "lock":
                plan = (OperationKind.Lock, new OperationParameters(Reason: request.Reason?.Trim(), StopInstance: request.StopInstance == true));
                error = Validation.CheckText(request.Reason?.Trim(), "Der Sperrgrund", 200, required: true);
                if (state is TenantState.Locked) error = "Die Instanz ist bereits gesperrt.";
                if (state is TenantState.Deleting or TenantState.Provisioning) error = "Die Instanz kann in diesem Zustand nicht gesperrt werden.";
                break;
            case "unlock":
                plan = (OperationKind.Unlock, null);
                if (state != TenantState.Locked) error = "Die Instanz ist nicht gesperrt.";
                break;
            case "backup":
                plan = (OperationKind.Backup, null);
                if (state is not (TenantState.Running or TenantState.Failed or TenantState.Locked)) error = "Sichern ist nur bei laufender Instanz möglich.";
                break;
            case "delete":
                plan = (OperationKind.Delete, new OperationParameters(FinalBackup: request.FinalBackup == true));
                // Doppelte Bestätigung: ausdrückliches „Ja“ UND das exakt eingetippte Kürzel.
                if (request.Confirm != true || !string.Equals(request.ConfirmSlug?.Trim(), tenant.Slug, StringComparison.Ordinal))
                {
                    error = $"Zum Löschen bitte bestätigen und das Kürzel „{tenant.Slug}“ exakt eingeben.";
                }

                if (state is TenantState.Deleting) error = "Die Instanz wird bereits gelöscht.";
                break;
            default:
                return ApiErrors.NotFound($"Unbekannte Aktion „{action}“.");
        }

        if (error is not null)
        {
            await audit.WriteAsync($"{OperationService.Labels[plan.Value.Kind]} abgewiesen", tenant, error, success: false, cancellationToken: cancellationToken);
            return ApiErrors.BadRequest(error);
        }

        try
        {
            var operation = await operations.EnqueueAsync(tenant, plan.Value.Kind, plan.Value.Parameters, cancellationToken);
            return Results.Accepted($"/api/operations/{operation.Id}", TenantService.ToSummary(operation));
        }
        catch (OperationConflictException exception)
        {
            return ApiErrors.Conflict(exception.Message);
        }
    }

    private static async Task<IResult> UpdateAllAsync(UpdateAllRequest request, HostingDbContext db, OperationService operations,
        AuditLog audit, CancellationToken cancellationToken)
    {
        var version = request.Version?.Trim();
        if (Validation.CheckVersion(version) is { } error)
        {
            return ApiErrors.BadRequest(error);
        }

        var candidates = (await db.Tenants.Where(t => t.State != TenantState.Deleted && t.State != TenantState.Deleting && t.State != TenantState.Provisioning)
                .ToListAsync(cancellationToken))
            .Where(t => SemanticVersion.IsOlder(t.Version, version))
            .Where(t => request.IncludeStopped == true || t.State != TenantState.Stopped)
            .ToList();
        var queued = 0;
        var skipped = new List<string>();
        foreach (var tenant in candidates)
        {
            try
            {
                await operations.EnqueueAsync(tenant, OperationKind.Update, new OperationParameters(Version: version, BackupFirst: request.BackupFirst != false), cancellationToken);
                queued++;
            }
            catch (OperationConflictException)
            {
                skipped.Add(tenant.Slug);
            }
        }

        await audit.WriteAsync("Alle aktualisieren", details: $"Zielversion {version}: {queued} eingereiht, {skipped.Count} übersprungen (laufender Vorgang)",
            cancellationToken: cancellationToken);
        return Results.Ok(new { queued, skipped });
    }

    private static async Task<IResult> LogsAsync(Guid id, string? service, int? lines, string? filter, string? level, HostingDbContext db,
        IDockerHost docker, InstanceLayout layout, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.State != TenantState.Deleted, cancellationToken);
        if (tenant is null)
        {
            return ApiErrors.NotFound("Mandant nicht gefunden.");
        }

        service ??= "api";
        if (!LogServices.Contains(service))
        {
            return ApiErrors.BadRequest("Unbekannter Dienst.");
        }

        var tail = Math.Clamp(lines ?? 200, 10, 5000);
        string text;
        try
        {
            text = Directory.Exists(layout.Directory(tenant.Slug))
                ? await docker.GetLogsAsync(layout.Project(tenant.Slug), service, tail, cancellationToken)
                : string.Empty;
        }
        catch (DockerException exception)
        {
            return ApiErrors.Conflict(exception.Message);
        }

        var result = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).AsEnumerable();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var needle = filter.Trim();
            result = result.Where(l => l.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        if (level is "warning" or "error")
        {
            result = result.Where(l => level == "error" ? ErrorPattern().IsMatch(l) : WarningPattern().IsMatch(l) || ErrorPattern().IsMatch(l));
        }

        var list = result.ToList();
        return Results.Ok(new { service, lines = list, count = list.Count, tail });
    }

    private static async Task<IResult> BackupPasswordAsync(Guid id, SecretRequest request, HttpContext context, HostingDbContext db,
        InstanceFiles files, AuditLog audit, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.State != TenantState.Deleted, cancellationToken);
        if (tenant is null)
        {
            return ApiErrors.NotFound("Mandant nicht gefunden.");
        }

        // Erneute Passwortabfrage: Der Sicherungsschlüssel ist das sensibelste Geheimnis einer Instanz.
        var adminId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var admin = Guid.TryParse(adminId, out var guid) ? await db.Admins.FindAsync([guid], cancellationToken) : null;
        if (admin is null || !AuthEndpoints.VerifyPassword(admin, request.Password ?? string.Empty))
        {
            await audit.WriteAsync("Sicherungsschlüssel angezeigt", tenant, "Passwort falsch – abgewiesen", success: false, cancellationToken: cancellationToken);
            return ApiErrors.BadRequest("Das Passwort ist falsch.");
        }

        var env = await files.ReadEnvAsync(tenant.Slug, cancellationToken);
        if (!env.TryGetValue("RESTIC_PASSWORD", out var password) || string.IsNullOrEmpty(password))
        {
            return ApiErrors.NotFound("Für diese Instanz ist noch kein Sicherungsschlüssel angelegt.");
        }

        await audit.WriteAsync("Sicherungsschlüssel angezeigt", tenant, cancellationToken: cancellationToken);
        return Results.Ok(new { password });
    }

    [GeneratedRegex(@"""@l"":""(Error|Fatal)""|\b(ERROR|FATAL|PANIC|FEHLER|error)\b", RegexOptions.CultureInvariant)]
    private static partial Regex ErrorPattern();

    [GeneratedRegex(@"""@l"":""Warning""|\b(WARN|WARNING|warn)\b", RegexOptions.CultureInvariant)]
    private static partial Regex WarningPattern();
}
