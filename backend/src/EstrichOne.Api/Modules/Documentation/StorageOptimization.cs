using System.Security.Cryptography;
using System.Threading.Channels;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Documentation.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Documentation;

public sealed record StorageKindStats(FileKind Kind, int Files, long Bytes, long UploadedBytes, int Optimized);

/// <param name="SavedBytes">Ersparnis durch Verdichtung (hochgeladen minus gespeichert).</param>
/// <param name="DuplicateFiles">Dateien, die sich die Ablage mit einer gleichen Datei teilen.</param>
/// <param name="DiskBytes">Tatsächlich belegter Platz der Ablage (Originale, Vorschaustufen) laut Dateisystem.</param>
public sealed record StorageStats(
    IReadOnlyList<StorageKindStats> Kinds, long StoredBytes, long SavedBytes, int DuplicateFiles, long DuplicateBytes, long OriginalsDiskBytes,
    long VariantsDiskBytes, long? FreeDiskBytes, int Candidates, StorageJobStatus Job);

public sealed record StorageJobStatus(bool Running, int Processed, int Optimized, long SavedBytes, int Skipped, int Failed, DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt, string? Message);

/// <summary>
/// Nachverdichten bestehender Dateien (Einstellungen → Speicher): Fotos (JPEG) und PDFs, die noch nicht verdichtet sind.
/// Archivierte oder zur Archivierung vorgemerkte Dateien bleiben unangetastet (Prüfsumme im Archiv).
/// </summary>
internal sealed partial class StorageOptimizationJob(IServiceScopeFactory scopes, LocalFileStorage storage, MediaOptimizer optimizer,
    Channel<Guid> variantQueue, TimeProvider clock, ILogger<StorageOptimizationJob> logger) : BackgroundService
{
    private readonly SemaphoreSlim _start = new(0, 1);
    private readonly Lock _lock = new();
    private StorageJobStatus _status = new(false, 0, 0, 0, 0, 0, null, null, null);

    public StorageJobStatus Status
    {
        get
        {
            lock (_lock)
            {
                return _status;
            }
        }
    }

    public bool TryStart()
    {
        lock (_lock)
        {
            if (_status.Running)
            {
                return false;
            }

            _status = new StorageJobStatus(true, 0, 0, 0, 0, 0, clock.GetUtcNow(), null, "Gestartet");
        }

        _start.Release();
        return true;
    }

    public override void Dispose()
    {
        _start.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _start.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                await RunAsync(stoppingToken);
                Update(s => s with { Running = false, FinishedAt = clock.GetUtcNow(), Message = "Fertig" });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
                Update(s => s with { Running = false, FinishedAt = clock.GetUtcNow(), Message = "Abgebrochen: " + exception.Message });
            }
        }
    }

    private void Update(Func<StorageJobStatus, StorageJobStatus> change)
    {
        lock (_lock)
        {
            _status = change(_status);
        }
    }

    internal static IQueryable<FileRecord> Candidates(EstrichOneDbContext db) =>
        db.Set<FileRecord>().Where(f => f.State == FileState.Stored && f.StorageKey != null && f.UploadSha256 == null
            && (f.DetectedContentType == "image/jpeg" || f.DetectedContentType == "application/pdf"));

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var lastId = Guid.Empty;
        while (true)
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
            var media = (await scope.ServiceProvider.GetRequiredService<ISystemSettings>().GetAsync(cancellationToken)).Media;
            var batch = await Candidates(db).Where(f => f.Id.CompareTo(lastId) > 0).OrderBy(f => f.Id).Take(20).ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return;
            }

            lastId = batch[^1].Id;
            var archived = new HashSet<Guid>();
            foreach (var state in scope.ServiceProvider.GetServices<IFileArchiveState>())
            {
                archived.UnionWith(await state.ArchivedOrQueuedAsync(batch.Select(f => f.Id).ToList(), cancellationToken));
            }

            foreach (var file in batch)
            {
                var path = storage.PathOf(file.StorageKey!);
                var shared = await db.Set<FileRecord>().IgnoreQueryFilters().AnyAsync(f => f.StorageKey == file.StorageKey && f.Id != file.Id, cancellationToken);
                if (archived.Contains(file.Id) || shared || !File.Exists(path))
                {
                    Update(s => s with { Processed = s.Processed + 1, Skipped = s.Skipped + 1 });
                    continue;
                }

                try
                {
                    var original = await File.ReadAllBytesAsync(path, cancellationToken);
                    if (optimizer.Optimize(original, file.DetectedContentType!, file.Kind, media) is not { } optimized)
                    {
                        Update(s => s with { Processed = s.Processed + 1, Skipped = s.Skipped + 1 });
                        continue;
                    }

                    // Erst daneben schreiben, dann atomar ersetzen; die Datenbank folgt sofort.
                    var temporary = path + ".opt";
                    await File.WriteAllBytesAsync(temporary, optimized.Content, cancellationToken);
                    file.ReplaceContent(Convert.ToHexStringLower(SHA256.HashData(optimized.Content)), optimized.Content.Length, optimized.ContentType);
                    var regenerate = file.Variants != VariantState.NotApplicable;
                    if (regenerate)
                    {
                        file.SetVariants(VariantState.Pending);
                    }

                    File.Move(temporary, path, overwrite: true);
                    await db.SaveChangesAsync(cancellationToken);
                    if (regenerate)
                    {
                        variantQueue.Writer.TryWrite(file.Id);
                    }

                    var saved = original.Length - optimized.Content.Length;
                    Update(s => s with { Processed = s.Processed + 1, Optimized = s.Optimized + 1, SavedBytes = s.SavedBytes + saved });
                }
                catch (Exception exception) when (exception is IOException or DbUpdateException or UnauthorizedAccessException)
                {
                    LogFileFailed(logger, file.Id, exception.Message);
                    db.ChangeTracker.Clear();
                    Update(s => s with { Processed = s.Processed + 1, Failed = s.Failed + 1 });
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Nachverdichten abgebrochen")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Datei {FileId} nicht nachverdichtet: {Reason}")]
    private static partial void LogFileFailed(ILogger logger, Guid fileId, string reason);
}

/// <summary>Speicherstatistik und Nachverdichten (Einstellungen → Speicher, Recht „settings.manage“).</summary>
internal static class StorageEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/files/storage").WithTags("Dateien");
        group.MapGet("/", StatsAsync).WithName("GetStorageStats").WithSummary("Speicherbelegung je Art, Ersparnis durch Verdichtung und Dubletten")
            .RequirePermission(Permissions.SettingsManage);
        group.MapPost("/optimize", Start).WithName("StartStorageOptimization")
            .WithSummary("Bestehende Fotos und PDFs im Hintergrund nachverdichten (archivierte Dateien bleiben unverändert)")
            .ProducesProblem(StatusCodes.Status409Conflict).RequirePermission(Permissions.SettingsManage);
    }

    private static async Task<Ok<StorageStats>> StatsAsync(EstrichOneDbContext db, LocalFileStorage storage, StorageOptimizationJob job,
        Microsoft.Extensions.Options.IOptions<FileStorageSettings> options, CancellationToken cancellationToken)
    {
        var stored = db.Set<FileRecord>().AsNoTracking().Where(f => f.State == FileState.Stored);
        var kinds = await stored.GroupBy(f => f.Kind).Select(g => new StorageKindStats(g.Key, g.Count(), g.Sum(f => f.SizeBytes),
            g.Sum(f => f.UploadSizeBytes ?? f.SizeBytes), g.Count(f => f.UploadSha256 != null))).ToListAsync(cancellationToken);
        var shared = await db.Set<FileRecord>().IgnoreQueryFilters().AsNoTracking().Where(f => f.State == FileState.Stored && f.StorageKey != null)
            .GroupBy(f => f.StorageKey).Where(g => g.Count() > 1).Select(g => new { Count = g.Count() - 1, Bytes = (g.Count() - 1) * g.Max(f => f.SizeBytes) })
            .ToListAsync(cancellationToken);
        var root = options.Value.RootPath;
        long? free = null;
        try
        {
            free = new DriveInfo(Path.GetFullPath(root)).AvailableFreeSpace;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            free = null;
        }

        return TypedResults.Ok(new StorageStats(kinds.OrderBy(k => k.Kind).ToList(), kinds.Sum(k => k.Bytes), kinds.Sum(k => k.UploadedBytes - k.Bytes),
            shared.Sum(s => s.Count), shared.Sum(s => s.Bytes), DirectoryBytes(Path.Combine(root, "originals")), DirectoryBytes(Path.Combine(root, "variants")),
            free, await StorageOptimizationJob.Candidates(db).CountAsync(cancellationToken), job.Status));
    }

    private static long DirectoryBytes(string path)
    {
        try
        {
            return Directory.Exists(path) ? new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static async Task<Results<Accepted<StorageJobStatus>, ProblemHttpResult>> Start(StorageOptimizationJob job, IAuditLog audit, ICurrentUser user,
        EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (!job.TryStart())
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Läuft bereits",
                extensions: new Dictionary<string, object?> { ["code"] = "OPTIMIZATION_RUNNING" });
        }

        audit.Record("files.storage_optimization_started", user.UserId, "settings", "storage");
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Accepted("/api/v1/files/storage", job.Status);
    }
}
