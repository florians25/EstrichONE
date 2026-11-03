using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using EstrichOne.Hosting.Auth;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Infrastructure;
using EstrichOne.Hosting.Instances;
using EstrichOne.Hosting.Operations;
using EstrichOne.Hosting.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Api;

public sealed record BulkActionRequest(IReadOnlyList<Guid>? TenantIds, string? Action);

public sealed record ArchiveItem(string Name, string Slug, DateTime? ArchivedAt, long SizeBytes, bool HasKey);

/// <summary>Sicherungen, Wiederherstellung, Zugangsbrief, Export, Archiv der Endsicherungen und Massenaktionen.</summary>
public static partial class RecoveryEndpoints
{
    /// <summary>Massenaktionen: nur ungefährliche, wiederholbare Vorgänge.</summary>
    private static readonly Dictionary<string, OperationKind> BulkActions = new Dictionary<string, OperationKind>(StringComparer.Ordinal)
    {
        ["start"] = OperationKind.Start,
        ["stop"] = OperationKind.Stop,
        ["restart"] = OperationKind.Restart,
        ["backup"] = OperationKind.Backup,
    };

    public static void Map(IEndpointRouteBuilder app)
    {
        var tenants = app.MapGroup("/api/tenants").WithTags("Mandanten").RequireAuthorization();
        tenants.MapGet("/{id:guid}/snapshots", SnapshotsAsync);
        tenants.MapPost("/{id:guid}/access-letter", AccessLetterAsync);
        tenants.MapDelete("/{id:guid}/access-letter", DiscardAccessLetterAsync);
        tenants.MapPost("/{id:guid}/export", ExportTenantAsync);
        tenants.MapPost("/bulk", BulkAsync);

        var archives = app.MapGroup("/api/archives").WithTags("Archiv").RequireAuthorization();
        archives.MapGet("/", (IOptions<HostingOptions> options) => Results.Ok(ListArchives(options.Value)));
        archives.MapPost("/{name}/export", ExportArchiveAsync);
        archives.MapDelete("/{name}", DeleteArchiveAsync);

        app.MapGet("/api/downloads/{token}", DownloadAsync).RequireAuthorization();
    }

    private static async Task<Tenant?> FindAsync(HostingDbContext db, Guid id, CancellationToken cancellationToken) =>
        await db.Tenants.FirstOrDefaultAsync(t => t.Id == id && t.State != TenantState.Deleted, cancellationToken);

    /// <summary>Erneute Passwortabfrage vor dem Herausgeben von Geheimnissen oder Daten.</summary>
    private static async Task<bool> ConfirmPasswordAsync(HttpContext context, HostingDbContext db, string? password, CancellationToken cancellationToken)
    {
        var adminId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var admin = Guid.TryParse(adminId, out var guid) ? await db.Admins.FindAsync([guid], cancellationToken) : null;
        return admin is not null && AuthEndpoints.VerifyPassword(admin, password ?? string.Empty);
    }

    private static async Task<IResult> SnapshotsAsync(Guid id, HostingDbContext db, InstanceLayout layout, CancellationToken cancellationToken)
    {
        var tenant = await FindAsync(db, id, cancellationToken);
        if (tenant is null)
        {
            return ApiErrors.NotFound("Mandant nicht gefunden.");
        }

        var snapshots = SnapshotReader.Read(Path.Combine(layout.DataDirectory(tenant.Slug), "backup-status"));
        return Results.Ok(new
        {
            items = snapshots,
            previousVersion = tenant.PreviousVersion,
            rollbackSnapshot = tenant.RollbackSnapshot,
        });
    }

    private static async Task<IResult> AccessLetterAsync(Guid id, SecretRequest request, HttpContext context, HostingDbContext db,
        AccessLetters letters, AuditLog audit, CancellationToken cancellationToken)
    {
        var tenant = await FindAsync(db, id, cancellationToken);
        if (tenant is null)
        {
            return ApiErrors.NotFound("Mandant nicht gefunden.");
        }

        if (!await ConfirmPasswordAsync(context, db, request.Password, cancellationToken))
        {
            await audit.WriteAsync("Zugangsbrief geöffnet", tenant, "Passwort falsch – abgewiesen", success: false, cancellationToken: cancellationToken);
            return ApiErrors.BadRequest("Das Passwort ist falsch.");
        }

        var letter = await letters.ReadAsync(tenant.Slug, cancellationToken);
        if (letter is null)
        {
            return ApiErrors.NotFound("Für diese Instanz liegt kein Zugangsbrief (mehr) vor.");
        }

        await audit.WriteAsync("Zugangsbrief geöffnet", tenant, $"Admin-Person {letter.UserName}", cancellationToken: cancellationToken);
        return Results.Ok(letter);
    }

    private static async Task<IResult> DiscardAccessLetterAsync(Guid id, HostingDbContext db, AccessLetters letters, AuditLog audit,
        CancellationToken cancellationToken)
    {
        var tenant = await FindAsync(db, id, cancellationToken);
        if (tenant is null)
        {
            return ApiErrors.NotFound("Mandant nicht gefunden.");
        }

        letters.Discard(tenant.Slug);
        await audit.WriteAsync("Zugangsbrief verworfen", tenant, "Startpasswort gelöscht", cancellationToken: cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ExportTenantAsync(Guid id, SecretRequest request, HttpContext context, HostingDbContext db,
        InstanceLayout layout, Downloads downloads, AuditLog audit, TimeProvider clock, CancellationToken cancellationToken)
    {
        var tenant = await FindAsync(db, id, cancellationToken);
        if (tenant is null)
        {
            return ApiErrors.NotFound("Mandant nicht gefunden.");
        }

        if (!await ConfirmPasswordAsync(context, db, request.Password, cancellationToken))
        {
            await audit.WriteAsync("Export angefordert", tenant, "Passwort falsch – abgewiesen", success: false, cancellationToken: cancellationToken);
            return ApiErrors.BadRequest("Das Passwort ist falsch.");
        }

        var repository = layout.BackupDirectory(tenant.Slug);
        if (!Directory.Exists(repository) || !Directory.EnumerateFileSystemEntries(repository).Any())
        {
            return ApiErrors.Conflict("Es gibt noch keine Sicherung. Bitte zuerst „Jetzt sichern“ ausführen.");
        }

        var directory = layout.Directory(tenant.Slug);
        var parts = new List<ExportPart> { new(repository, "backup-repository"), new(Path.Combine(directory, ".env"), "instanz.env") };
        parts.AddRange(InstanceLayout.ComposeFiles.Select(f => new ExportPart(Path.Combine(directory, f), f)));
        var name = $"estrichone-{tenant.Slug}-{clock.GetUtcNow().UtcDateTime.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}.tar.gz";
        var token = downloads.Issue(name, parts, audit.CurrentActor);
        await audit.WriteAsync("Export angefordert", tenant, $"{name} (Sicherungs-Repository und Schlüssel)", cancellationToken: cancellationToken);
        return Results.Ok(new { url = $"/api/downloads/{token}", fileName = name, sizeBytes = Downloads.SizeOf(repository) });
    }

    public static IReadOnlyList<ArchiveItem> ListArchives(HostingOptions options)
    {
        if (!Directory.Exists(options.ArchiveDirectory))
        {
            return [];
        }

        return Directory.EnumerateDirectories(options.ArchiveDirectory)
            .Select(path =>
            {
                var name = Path.GetFileName(path);
                var match = ArchiveName().Match(name);
                DateTime? at = match.Success && DateTime.TryParseExact(match.Groups[2].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;
                return new ArchiveItem(name, match.Success ? match.Groups[1].Value : name, at, Downloads.SizeOf(path),
                    File.Exists(Path.Combine(path, "instanz.env")));
            })
            .Where(a => ArchiveName().IsMatch(a.Name))
            .OrderByDescending(a => a.ArchivedAt)
            .ToList();
    }

    private static string? ArchivePath(HostingOptions options, string name) =>
        ArchiveName().IsMatch(name) && Directory.Exists(Path.Combine(options.ArchiveDirectory, name)) ? Path.Combine(options.ArchiveDirectory, name) : null;

    private static async Task<IResult> ExportArchiveAsync(string name, SecretRequest request, HttpContext context, HostingDbContext db,
        IOptions<HostingOptions> options, Downloads downloads, AuditLog audit, CancellationToken cancellationToken)
    {
        var path = ArchivePath(options.Value, name);
        if (path is null)
        {
            return ApiErrors.NotFound("Archiv nicht gefunden.");
        }

        if (!await ConfirmPasswordAsync(context, db, request.Password, cancellationToken))
        {
            await audit.WriteAsync("Archiv heruntergeladen", details: $"{name}: Passwort falsch – abgewiesen", success: false, cancellationToken: cancellationToken);
            return ApiErrors.BadRequest("Das Passwort ist falsch.");
        }

        var token = downloads.Issue($"{name}.tar.gz", [new ExportPart(path, name)], audit.CurrentActor);
        await audit.WriteAsync("Archiv heruntergeladen", details: name, cancellationToken: cancellationToken);
        return Results.Ok(new { url = $"/api/downloads/{token}", fileName = $"{name}.tar.gz", sizeBytes = Downloads.SizeOf(path) });
    }

    private static async Task<IResult> DeleteArchiveAsync(string name, string? confirm, IOptions<HostingOptions> options, AuditLog audit,
        CancellationToken cancellationToken)
    {
        var path = ArchivePath(options.Value, name);
        if (path is null)
        {
            return ApiErrors.NotFound("Archiv nicht gefunden.");
        }

        if (!string.Equals(confirm, name, StringComparison.Ordinal))
        {
            return ApiErrors.BadRequest($"Zum Löschen bitte den Namen „{name}“ exakt eingeben.");
        }

        Directory.Delete(path, recursive: true);
        await audit.WriteAsync("Archiv gelöscht", details: $"{name} (Endsicherung endgültig entfernt)", cancellationToken: cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> DownloadAsync(string token, HttpContext context, Downloads downloads, AuditLog audit,
        CancellationToken cancellationToken)
    {
        if (downloads.Redeem(token) is not { } ticket || ticket.Actor != audit.CurrentActor)
        {
            return ApiErrors.NotFound("Der Download-Link ist abgelaufen oder wurde bereits verwendet.");
        }

        context.Response.ContentType = "application/gzip";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{ticket.FileName}\"";
        context.Response.Headers.CacheControl = "no-store";
        await Downloads.WriteTarGzAsync(context.Response.Body, ticket.Parts, cancellationToken);
        return Results.Empty;
    }

    private static async Task<IResult> BulkAsync(BulkActionRequest request, HostingDbContext db, OperationService operations, AuditLog audit,
        CancellationToken cancellationToken)
    {
        if (request.Action is null || !BulkActions.TryGetValue(request.Action, out var kind))
        {
            return ApiErrors.BadRequest("Massenaktion muss start, stop, restart oder backup sein.");
        }

        var ids = request.TenantIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0)
        {
            return ApiErrors.BadRequest("Bitte mindestens einen Mandanten auswählen.");
        }

        var tenants = await db.Tenants.Where(t => ids.Contains(t.Id) && t.State != TenantState.Deleted).ToListAsync(cancellationToken);
        var queued = new List<string>();
        var skipped = new List<string>();
        foreach (var tenant in tenants)
        {
            var allowed = kind switch
            {
                OperationKind.Start => tenant.State is TenantState.Stopped or TenantState.Failed,
                OperationKind.Stop => tenant.State is TenantState.Running or TenantState.Failed,
                _ => tenant.State is TenantState.Running or TenantState.Failed or TenantState.Locked,
            };
            if (!allowed)
            {
                skipped.Add(tenant.Slug);
                continue;
            }

            try
            {
                await operations.EnqueueAsync(tenant, kind, null, cancellationToken);
                queued.Add(tenant.Slug);
            }
            catch (OperationConflictException)
            {
                skipped.Add(tenant.Slug);
            }
        }

        await audit.WriteAsync($"Massenaktion {OperationService.Labels[kind]}",
            details: $"{queued.Count} eingereiht ({string.Join(", ", queued)}), {skipped.Count} übersprungen", cancellationToken: cancellationToken);
        return Results.Ok(new { queued, skipped });
    }

    [GeneratedRegex(@"^([a-z0-9][a-z0-9-]{0,30}[a-z0-9]?)-(\d{8}-\d{6})$", RegexOptions.CultureInvariant)]
    private static partial Regex ArchiveName();
}
