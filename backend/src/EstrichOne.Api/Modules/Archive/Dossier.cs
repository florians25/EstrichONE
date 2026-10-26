using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Documentation.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EstrichOne.Api.Modules.Archive;

public enum DossierStatus
{
    /// <summary>Wird übertragen (alle Dateien + Datenstand), danach im Produktivsystem entfernt.</summary>
    Archiving,

    /// <summary>Nur auf dem Archivserver; im Produktivsystem bleibt der Platzhalter.</summary>
    Archived,

    /// <summary>Wird vom Archivserver geholt (Fortschritt).</summary>
    Restoring,

    /// <summary>Vorübergehend im Produktivsystem (nur Ansicht) bis <see cref="Dossier.AvailableUntil"/>.</summary>
    Available,

    /// <summary>Mit Passwort entsperrt: Änderungen erlaubt; danach als neue Fassung wieder archiviert.</summary>
    Unlocked,
}

/// <summary>Komplett ausgelagerte Bauakte (Aktenarchivierung). Bleibt immer auf dem Archivserver.</summary>
public sealed class Dossier : IAuditedEntity
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromDays(5);

    public Guid BauakteId { get; private set; }
    public DossierStatus Status { get; private set; }
    public int Version { get; private set; } = 1;
    public int FilesTotal { get; private set; }
    public int FilesDone { get; private set; }
    public DateTimeOffset? ArchivedAt { get; private set; }
    public DateTimeOffset? AvailableUntil { get; private set; }
    public DateTimeOffset? UnlockedUntil { get; private set; }
    public Guid? UnlockedByUserId { get; private set; }
    public string? ManifestHash { get; private set; }
    public string? LastError { get; private set; }
    public Guid RequestedByUserId { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private Dossier()
    {
    }

    public static Dossier Start(Guid bauakteId, Guid requestedBy) =>
        new() { BauakteId = bauakteId, Status = DossierStatus.Archiving, RequestedByUserId = requestedBy };

    /// <summary>Änderungen sind nur entsperrt erlaubt.</summary>
    public bool IsReadOnly => Status != DossierStatus.Unlocked;

    public void Progress(int done, int total, string? error)
    {
        FilesDone = done;
        FilesTotal = total;
        LastError = error;
    }

    public void Archived(DateTimeOffset at, string manifestHash)
    {
        Status = DossierStatus.Archived;
        ArchivedAt = at;
        ManifestHash = manifestHash;
        AvailableUntil = null;
        UnlockedUntil = null;
        LastError = null;
    }

    public void Restore()
    {
        if (Status == DossierStatus.Archived)
        {
            Status = DossierStatus.Restoring;
            FilesDone = 0;
        }
    }

    public void Restored(DateTimeOffset now)
    {
        Status = DossierStatus.Available;
        AvailableUntil = now + CacheDuration;
        LastError = null;
    }

    public void Evicted() => Status = DossierStatus.Archived;

    public void Unlock(Guid userId, DateTimeOffset until)
    {
        Status = DossierStatus.Unlocked;
        UnlockedByUserId = userId;
        UnlockedUntil = until;
    }

    /// <summary>Nach Änderungen (oder neuen Dateien) als neue Fassung erneut archivieren.</summary>
    public void Rearchive()
    {
        Status = DossierStatus.Archiving;
        Version++;
        UnlockedUntil = null;
    }
}

internal sealed class DossierConfiguration : IEntityTypeConfiguration<Dossier>
{
    public void Configure(EntityTypeBuilder<Dossier> builder)
    {
        builder.ToTable("archive_dossiers");
        builder.HasKey(d => d.BauakteId);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.ManifestHash).HasMaxLength(64);
        builder.Property(d => d.LastError).HasMaxLength(1000);
    }
}

public sealed record DossierDto(
    Guid BauakteId, string? Number, string Title, DossierStatus Status, int Version, int FilesDone, int FilesTotal, DateTimeOffset? ArchivedAt,
    DateTimeOffset? AvailableUntil, DateTimeOffset? UnlockedUntil, string? LastError, bool ReadOnly);

/// <summary>Aktenarchivierung: auslagern, abrufen (5 Tage Cache), entsperren, neue Fassung archivieren.</summary>
internal sealed partial class DossierService(
    EstrichOneDbContext db, ArchiveService archive, IArchivableDocuments documents, TimeProvider clock, ILogger<DossierService> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<Dossier?> FindAsync(Guid bauakteId, CancellationToken cancellationToken) =>
        db.Set<Dossier>().SingleOrDefaultAsync(d => d.BauakteId == bauakteId, cancellationToken);

    public async Task<List<DossierDto>> ListAsync(IReadOnlyCollection<Guid>? ids, CancellationToken cancellationToken)
    {
        var query = db.Set<Dossier>().AsNoTracking();
        if (ids is { Count: > 0 })
        {
            query = query.Where(d => ids.Contains(d.BauakteId));
        }

        var list = await query.Join(db.Set<Bauakte>().IgnoreQueryFilters(), d => d.BauakteId, b => b.Id,
                (d, b) => new DossierDto(d.BauakteId, b.Number, b.Title, d.Status, d.Version, d.FilesDone, d.FilesTotal, d.ArchivedAt,
                    d.AvailableUntil, d.UnlockedUntil, d.LastError, d.Status != DossierStatus.Unlocked))
            .ToListAsync(cancellationToken);
        return [.. list.OrderBy(d => d.Number, StringComparer.Ordinal)];
    }

    public async Task<Dossier> StartAsync(Guid bauakteId, Guid userId, CancellationToken cancellationToken)
    {
        var dossier = await FindAsync(bauakteId, cancellationToken);
        if (dossier is null)
        {
            dossier = Dossier.Start(bauakteId, userId);
            db.Add(dossier);
        }
        else if (dossier.Status == DossierStatus.Unlocked)
        {
            dossier.Rearchive();
        }

        await db.SaveChangesAsync(cancellationToken);
        return dossier;
    }

    /// <summary>Regel: abgeschlossene Bauakten nach X Monaten automatisch auslagern.</summary>
    public async Task<int> StartByRuleAsync(CancellationToken cancellationToken)
    {
        var connection = await archive.ConnectionAsync(cancellationToken);
        if (connection?.DossierAfterMonths is not { } months)
        {
            return 0;
        }

        var cutoff = clock.GetUtcNow().AddMonths(-months);
        var due = await db.Set<Bauakte>().Where(b => b.Status == BauakteStatus.Completed && b.UpdatedAt < cutoff)
            .Where(b => !db.Set<Dossier>().Any(d => d.BauakteId == b.Id)).Select(b => b.Id).Take(20).ToListAsync(cancellationToken);
        foreach (var id in due)
        {
            db.Add(Dossier.Start(id, Guid.Empty));
        }

        await db.SaveChangesAsync(cancellationToken);
        return due.Count;
    }

    /// <summary>Ein Arbeitsschritt für alle Akten (Hintergrunddienst bzw. Tests).</summary>
    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var connection = await archive.ConnectionAsync(cancellationToken);
        if (connection is null)
        {
            return;
        }

        foreach (var dossier in await db.Set<Dossier>().Where(d => d.Status != DossierStatus.Archived).ToListAsync(cancellationToken))
        {
            try
            {
                await StepAsync(connection, dossier, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or JsonException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                dossier.Progress(dossier.FilesDone, dossier.FilesTotal, ArchiveService.Describe(exception));
                LogFailed(logger, dossier.BauakteId, dossier.LastError!);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        // Neue Dateien in bereits ausgelagerten Akten (z. B. über die App-Synchronisation) → neue Fassung.
        var archived = await db.Set<Dossier>().Where(d => d.Status == DossierStatus.Archived).Select(d => d.BauakteId).ToListAsync(cancellationToken);
        foreach (var id in archived)
        {
            if (await documents.Query().AnyAsync(f => f.BauakteId == id && !db.Set<ArchiveRecord>().Any(r => r.FileId == f.FileId), cancellationToken))
            {
                (await FindAsync(id, cancellationToken))!.Rearchive();
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task StepAsync(ArchiveConnection connection, Dossier dossier, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        switch (dossier.Status)
        {
            case DossierStatus.Archiving:
                await ArchiveStepAsync(connection, dossier, cancellationToken);
                break;
            case DossierStatus.Restoring:
                await RestoreStepAsync(connection, dossier, cancellationToken);
                break;
            case DossierStatus.Available when dossier.AvailableUntil <= now:
                await EvictAsync(dossier, cancellationToken);
                dossier.Evicted();
                break;
            case DossierStatus.Unlocked when dossier.UnlockedUntil <= now:
                dossier.Rearchive();
                break;
        }
    }

    private async Task ArchiveStepAsync(ArchiveConnection connection, Dossier dossier, CancellationToken cancellationToken)
    {
        // 1. Alle Dateien der Akte (auch Fotos/Videos) müssen einzeln archiviert sein.
        var files = await documents.Query().Where(f => f.BauakteId == dossier.BauakteId).ToListAsync(cancellationToken);
        var records = await db.Set<ArchiveRecord>().Where(r => r.BauakteId == dossier.BauakteId).ToListAsync(cancellationToken);
        var rules = await archive.RulesAsync(cancellationToken);
        foreach (var file in files.Where(f => records.All(r => r.FileId != f.FileId)))
        {
            var type = ArchiveService.DocumentTypeOf(file);
            var record = ArchiveRecord.Queue(file.FileId, file.BauakteId, type, file.OriginalName, file.Sha256, file.SizeBytes,
                rules.FirstOrDefault(r => r.DocumentType == type)?.RetentionYears ?? 10, ArchiveTrigger.Manual, dossier.RequestedByUserId,
                clock.GetUtcNow());
            db.Add(record);
            records.Add(record);
        }

        await db.SaveChangesAsync(cancellationToken);
        await archive.ProcessDueAsync(cancellationToken);
        var done = records.Count(r => r.Status == ArchiveRecordStatus.Archived);
        dossier.Progress(done, files.Count, records.FirstOrDefault(r => r.LastError is not null)?.LastError);
        if (files.Any(f => records.All(r => r.FileId != f.FileId || r.Status != ArchiveRecordStatus.Archived)))
        {
            return;
        }

        // 2. Datenstand der Akte als eigenes Objekt (Fassung) – unveränderbar wie jedes Dokument.
        var bauakte = await db.Set<Bauakte>().IgnoreQueryFilters().AsNoTracking().SingleAsync(b => b.Id == dossier.BauakteId, cancellationToken);
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            format = "estrichone-akte-v1",
            version = dossier.Version,
            archivedAt = clock.GetUtcNow(),
            bauakte,
            files = files.Select(f => new { f.FileId, f.Kind, f.Category, f.OriginalName, f.ContentType, f.SizeBytes, f.Sha256, f.CreatedAt }),
        }, Json);
        var sha = Convert.ToHexStringLower(SHA256.HashData(manifest));
        var objectId = ManifestId(dossier.BauakteId, dossier.Version);
        using var http = archive.Client(connection, TimeSpan.FromMinutes(5));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/objects/{objectId}") { Content = new ByteArrayContent(manifest) };
        request.Headers.Add("X-Archive-Sha256", sha);
        request.Headers.Add("X-Archive-Metadata", Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new
        {
            name = $"Bauakte {bauakte.Number ?? bauakte.Title} Fassung {dossier.Version}.json",
            contentType = "application/json",
            documentType = "dossier",
            retentionYears = records.Count == 0 ? 10 : records.Max(r => r.RetentionYears),
            source = new Dictionary<string, string> { ["system"] = "EstrichONE", ["bauakte_id"] = dossier.BauakteId.ToString() },
        }, Json)));
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            dossier.Progress(done, files.Count, $"Datenstand nicht archiviert (HTTP {(int)response.StatusCode}).");
            return;
        }

        // 3. Erst jetzt aus dem Produktivsystem entfernen.
        await EvictAsync(dossier, cancellationToken);
        dossier.Archived(clock.GetUtcNow(), sha);
        LogArchived(logger, dossier.BauakteId, dossier.Version, files.Count);
    }

    private async Task EvictAsync(Dossier dossier, CancellationToken cancellationToken)
    {
        var archived = await db.Set<ArchiveRecord>()
            .Where(r => r.BauakteId == dossier.BauakteId && r.Status == ArchiveRecordStatus.Archived).Select(r => new { r.FileId, r.Sha256 })
            .ToListAsync(cancellationToken);
        var files = await documents.Query().Where(f => f.BauakteId == dossier.BauakteId).Select(f => new { f.FileId, f.Sha256 })
            .ToListAsync(cancellationToken);
        foreach (var file in files.Where(f => archived.Any(a => a.FileId == f.FileId && a.Sha256 == f.Sha256)))
        {
            await documents.EvictAsync(file.FileId, cancellationToken);
        }
    }

    private async Task RestoreStepAsync(ArchiveConnection connection, Dossier dossier, CancellationToken cancellationToken)
    {
        var files = await documents.Query().Where(f => f.BauakteId == dossier.BauakteId).Select(f => f.FileId).ToListAsync(cancellationToken);
        using var http = archive.Client(connection, TimeSpan.FromMinutes(30));
        var done = 0;
        foreach (var fileId in files)
        {
            if (!await documents.HasContentAsync(fileId, cancellationToken))
            {
                using var response = await http.GetAsync(new Uri($"/api/v1/objects/{fileId}", UriKind.Relative), HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    dossier.Progress(done, files.Count, $"Datei {fileId} nicht abrufbar (HTTP {(int)response.StatusCode}).");
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                if (!await documents.RestoreAsync(fileId, stream, cancellationToken))
                {
                    dossier.Progress(done, files.Count, $"Datei {fileId}: Prüfsumme stimmt nicht.");
                    continue;
                }
            }

            done++;
            dossier.Progress(done, files.Count, null);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (done == files.Count)
        {
            dossier.Restored(clock.GetUtcNow());
        }
    }

    /// <summary>Feste Objekt-ID je Akte und Fassung (wiederholbar, ohne Doppelungen).</summary>
    public static Guid ManifestId(Guid bauakteId, int version)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"estrichone-akte|{bauakteId:N}|{version}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Aktenarchivierung {BauakteId}: {Error}")]
    private static partial void LogFailed(ILogger logger, Guid bauakteId, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Bauakte {BauakteId} Fassung {Version} archiviert und ausgelagert ({Files} Dateien).")]
    private static partial void LogArchived(ILogger logger, Guid bauakteId, int version, int files);
}

/// <summary>
/// Schreibschutz für archivierte Akten: Änderungen an Bauakte und Dateien nur nach Entsperren (423 ARCHIVE_READ_ONLY);
/// ausgelagerte Inhalte liefern einen verständlichen Hinweis statt eines Fehlers (409 ARCHIVE_EVICTED).
/// </summary>
internal sealed class DossierLockMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, EstrichOneDbContext db, IArchivableDocuments documents)
    {
        var segments = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (segments.Length >= 4 && segments[0] == "api" && segments[1] == "v1" && Guid.TryParse(segments[3], out var id)
            && segments[2] is "bauakten" or "files")
        {
            var write = !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
                && !HttpMethods.IsOptions(context.Request.Method);
            var bauakteId = segments[2] == "bauakten"
                ? id
                : await documents.Query().Where(f => f.FileId == id).Select(f => f.BauakteId).FirstOrDefaultAsync(context.RequestAborted);
            var status = bauakteId is null
                ? null
                : await db.Set<Dossier>().Where(d => d.BauakteId == bauakteId).Select(d => (DossierStatus?)d.Status)
                    .FirstOrDefaultAsync(context.RequestAborted);
            if (status is { } s && s != DossierStatus.Unlocked)
            {
                if (write)
                {
                    await Problem(context, 423, "ARCHIVE_READ_ONLY", "Bauakte ist archiviert",
                        "Archivierte Bauakten sind nur zur Ansicht. Zum Ändern unter Aktenarchiv entsperren (Passwort erforderlich).");
                    return;
                }

                if (segments[2] == "files" && segments.Length >= 5 && segments[4] is "content" or "variants" or "download"
                    && !await documents.HasContentAsync(id, context.RequestAborted))
                {
                    await Problem(context, 409, "ARCHIVE_EVICTED", "Auf dem Archivserver",
                        "Die Akte ist ausgelagert. Bitte „Akte abrufen“ wählen – danach ist sie 5 Tage verfügbar.");
                    return;
                }
            }
        }

        await next(context);
    }

    private static Task Problem(HttpContext context, int status, string code, string title, string detail)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { status, title, detail, code }, (JsonSerializerOptions?)null, "application/problem+json");
    }
}

internal sealed class DossierLockStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<DossierLockMiddleware>();
        next(app);
    };
}
