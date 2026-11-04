using EstrichOne.Api.Infrastructure.Peers;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Backup.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using EstrichOne.Api.Modules.Organization.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Documentation;

public sealed record OffloadConnectionDto(string Address, string ServerName, string? ServerVersion, string CaFingerprint, string FingerprintDisplay,
    DateTimeOffset? PairedAt, DateTimeOffset? LastContactAt, string? LastError, bool Reachable);

/// <param name="ThresholdPercent">Knapp, wenn weniger als so viel Prozent frei sind (1–90).</param>
/// <param name="ThresholdFreeGb">Zusätzlich knapp, wenn weniger als so viele GB frei sind (leer = aus).</param>
/// <param name="MinAgeMonths">Nur Dateien, die älter als so viele Monate sind (0 = jedes Alter).</param>
/// <param name="RequireBackup">Erst lokal löschen, wenn die Originale zusätzlich gesichert sind (dringend empfohlen).</param>
public sealed record OffloadSettingsDto(bool AutoWhenLow, bool AutoByRules, int ThresholdPercent, int? ThresholdFreeGb, int MinAgeMonths,
    bool OnlyCompletedBauakten, bool IncludePhotos, bool IncludeVideos, bool IncludeDocuments, bool RequireBackup, int CacheLimitMb);

public sealed record OffloadDiskDto(long? FreeBytes, long? TotalBytes, double? FreePercent, bool Low, long BytesToFree);

/// <param name="OffloadedFiles">Dateien (inkl. Dubletten), deren Original nur noch auf dem Storage liegt.</param>
/// <param name="PendingBlobs">Übertragen und geprüft, lokal noch vorhanden (wartet auf die Sicherungskopie).</param>
/// <param name="MissingBlobs">Weder lokal noch auf dem Storage – aus der Sicherung zurückholen.</param>
public sealed record OffloadCountsDto(int OffloadedFiles, int OffloadedBlobs, long OffloadedBytes, int PendingBlobs, long PendingBytes, int MissingBlobs,
    int CandidateFiles, long CandidateBytes, long CacheBytes);

/// <param name="Level"><c>ok</c>, <c>warning</c> (Handlungsbedarf) oder <c>critical</c>.</param>
public sealed record OffloadStatusLine(string Level, string Text);

/// <param name="BackupAvailable">Backup-Dienst eingerichtet (Sicherungskopie vor dem Löschen möglich).</param>
/// <param name="LastCopy">Letzte Anforderung der Sicherungskopie.</param>
public sealed record OffloadOverview(OffloadStatusLine Status, bool Paired, OffloadConnectionDto? Connection, OffloadServerStatus? Server, OffloadSettingsDto Settings,
    OffloadDiskDto Disk, OffloadCountsDto Counts, bool BackupAvailable, OffloadBackupResult? LastCopy, OffloadJobStatus Job, DateTimeOffset? LastRunAt,
    string? LastRunMessage);

public sealed record PairOffloadRequest(string Address, string Code);

/// <param name="BauakteId">Nur diese Bauakte (dann ohne Alters- und Abschlussregel).</param>
public sealed record OffloadRunRequest(Guid? BauakteId = null);

/// <param name="FileIds">Diese Dateien zurückholen.</param>
/// <param name="BauakteId">Alle ausgelagerten Dateien dieser Bauakte.</param>
/// <param name="All">Alles zurückholen (Platz wird vorher geprüft).</param>
public sealed record OffloadRecallRequest(IReadOnlyList<Guid>? FileIds = null, Guid? BauakteId = null, bool All = false);

public sealed record OffloadedFileDto(Guid FileId, string Name, FileKind Kind, Guid? BauakteId, string? BauakteTitle, long SizeBytes, OffloadState State,
    DateTimeOffset UploadedAt, DateTimeOffset? OffloadedAt, DateTimeOffset? BackedUpAt, string? LastError);

public sealed record OffloadedFilesPage(IReadOnlyList<OffloadedFileDto> Items, int Total);

public sealed record OffloadedBauakteDto(Guid BauakteId, string Title, int Files, long Bytes);

/// <summary>Welche der angefragten Dateien vom Storage geladen werden („wird vom Storage geladen“).</summary>
public sealed record OffloadLocation(Guid FileId, OffloadState State);

public sealed record OffloadAccepted(string Message, string? RequestId = null);

internal static class OffloadEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/files/offload").WithTags("Speicher-Auslagerung");
        group.MapGet("/", OverviewAsync).WithName("GetOffloadOverview")
            .WithSummary("Speicher-Auslagerung: Belegung Server/Storage, Schwelle, Regeln, Fortschritt, Statuszeile")
            .RequirePermission(Permissions.SettingsManage);
        group.MapPut("/settings", SettingsAsync).WithName("UpdateOffloadSettings").RequirePermission(Permissions.SettingsManage);
        group.MapPost("/pair", PairAsync).WithName("PairOffloadStorage")
            .WithSummary("Storage (Dienst estrichone-offload auf Kombiserver/Archivrechner) per Adresse und Kopplungscode verbinden")
            .RequirePermission(Permissions.SettingsManage);
        group.MapDelete("/pairing", UnpairAsync).WithName("UnpairOffloadStorage")
            .WithSummary("Entkoppeln – nur, wenn nichts mehr ausgelagert ist (sonst 409)")
            .RequirePermission(Permissions.SettingsManage);
        group.MapPost("/run", Run).WithName("RunOffload").WithSummary("Jetzt auslagern (nach Regeln bzw. eine Bauakte)")
            .RequirePermission(Permissions.SettingsManage);
        group.MapPost("/recall", RecallAsync).WithName("RecallOffloadedFiles")
            .WithSummary("Wieder lokal speichern: Dateien, eine Bauakte oder alles").RequirePermission(Permissions.SettingsManage);
        group.MapGet("/files", FilesAsync).WithName("GetOffloadedFiles").RequirePermission(Permissions.SettingsManage);
        group.MapGet("/bauakten", BauaktenAsync).WithName("GetOffloadedBauakten").RequirePermission(Permissions.SettingsManage);
        group.MapPost("/reconcile", ReconcileAsync).WithName("ReconcileOffload")
            .WithSummary("Abgleich Server ↔ Storage (nach Wiederherstellung oder Ausfall)").RequirePermission(Permissions.SettingsManage);
        group.MapPost("/recover-from-backup", RecoverAsync).WithName("RecoverOffloadFromBackup")
            .WithSummary("Fehlende Originale aus den Auslagerungs-Snapshots der Sicherung zurückspielen").RequirePermission(Permissions.SettingsManage);
        group.MapGet("/locations", LocationsAsync).WithName("GetOffloadLocations")
            .WithSummary("Welche der Dateien (ids=…) ihr Original vom Storage laden – Hinweis „wird vom Storage geladen“")
            .RequirePermission(Permissions.BauaktenRead);
    }

    private static async Task<Ok<OffloadOverview>> OverviewAsync(OffloadService service, OffloadProgress progress, OffloadCache cache, EstrichOneDbContext db,
        IOffloadBackup backup, CancellationToken cancellationToken)
    {
        var settings = await service.ConnectionAsync(cancellationToken);
        var server = settings.Paired ? await service.ServerStatusAsync(settings, cancellationToken) : null;
        settings = await service.ConnectionAsync(cancellationToken);
        var blobs = db.Set<OffloadedBlob>().AsNoTracking();
        var offloaded = await blobs.Where(b => b.State == OffloadState.Offloaded).GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Bytes = g.Sum(b => b.SizeBytes) }).FirstOrDefaultAsync(cancellationToken);
        var pending = await blobs.Where(b => b.State == OffloadState.Pending).GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Bytes = g.Sum(b => b.SizeBytes) }).FirstOrDefaultAsync(cancellationToken);
        var missing = await blobs.CountAsync(b => b.State == OffloadState.Missing, cancellationToken);
        var offloadedFiles = await db.Set<FileRecord>().AsNoTracking()
            .CountAsync(f => db.Set<OffloadedBlob>().Any(b => b.StorageKey == f.StorageKey && b.State == OffloadState.Offloaded), cancellationToken);
        var candidates = await service.Candidates(settings, null, false).GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Bytes = g.Sum(f => f.SizeBytes) }).FirstOrDefaultAsync(cancellationToken);
        var lastCopyId = await blobs.Where(b => b.BackupRequestId != null).OrderByDescending(b => b.UploadedAt).Select(b => b.BackupRequestId)
            .FirstOrDefaultAsync(cancellationToken);
        var lastCopy = lastCopyId is null ? null : backup.CopyResult(lastCopyId);

        var disk = service.Disk();
        var low = disk is { } d && OffloadService.IsLow(settings, d.Free, d.Total);
        var diskDto = new OffloadDiskDto(disk?.Free, disk?.Total, disk is { Total: > 0 } t ? Math.Round(t.Free * 100.0 / t.Total, 1) : null, low,
            disk is { } x ? OffloadService.BytesToFree(settings, x.Free, x.Total) : 0);
        var counts = new OffloadCountsDto(offloadedFiles, offloaded?.Count ?? 0, offloaded?.Bytes ?? 0, pending?.Count ?? 0, pending?.Bytes ?? 0, missing,
            candidates?.Count ?? 0, candidates?.Bytes ?? 0, cache.SizeBytes());
        var status = Line(settings, server is not null, diskDto, counts, backup.Available, lastCopy);
        return TypedResults.Ok(new OffloadOverview(status, settings.Paired, ToDto(settings, server is not null), server, ToSettings(settings), diskDto, counts,
            backup.Available, lastCopy, progress.Status, settings.LastRunAt, settings.LastRunMessage));
    }

    private static OffloadStatusLine Line(OffloadConnection settings, bool reachable, OffloadDiskDto disk, OffloadCountsDto counts, bool backupAvailable,
        OffloadBackupResult? lastCopy)
    {
        if (counts.MissingBlobs > 0)
        {
            return new("critical", $"{counts.MissingBlobs} ausgelagerte Original(e) fehlen auf dem Storage – bitte „Aus Sicherung zurückholen“.");
        }

        if (settings.Paired && !reachable)
        {
            return new("warning", $"Storage „{settings.ServerName}“ nicht erreichbar – ausgelagerte Originale lassen sich gerade nicht öffnen. Nichts ist verloren.");
        }

        if (!settings.Paired)
        {
            return disk.Low
                ? new("warning", "Speicher knapp – für die Auslagerung zuerst einen Storage koppeln.")
                : new("ok", "Kein Storage gekoppelt. Alle Originale liegen auf diesem Server.");
        }

        if (settings.RequireBackup && !backupAvailable)
        {
            return new("warning", "Kein Backup-Dienst eingerichtet – Originale werden erst gelöscht, wenn eine Sicherungskopie möglich ist.");
        }

        if (lastCopy is { Status: "failed" })
        {
            return new("warning", $"Sicherungskopie der ausgelagerten Originale fehlgeschlagen: {lastCopy.Message}. Wird erneut versucht.");
        }

        if (disk.Low)
        {
            return settings.AutoWhenLow
                ? new("warning", counts.CandidateFiles > 0
                    ? "Speicher knapp – Originale werden automatisch ausgelagert."
                    : "Speicher knapp – keine Dateien entsprechen den Regeln. Regeln lockern (Alter, Arten) oder Platz erweitern.")
                : new("warning", "Speicher knapp – automatische Auslagerung ist aus.");
        }

        return new("ok", counts.OffloadedBlobs > 0
            ? "Alles in Ordnung. Ausgelagerte Originale sind verschlüsselt auf dem Storage und in der Sicherung."
            : "Alles in Ordnung. Storage gekoppelt, noch nichts ausgelagert.");
    }

    private static OffloadConnectionDto? ToDto(OffloadConnection c, bool reachable) => c.Paired
        ? new OffloadConnectionDto(c.Address!, c.ServerName ?? "Storage", c.ServerVersion, c.CaFingerprint!, PeerClient.FormatFingerprint(c.CaFingerprint!), c.PairedAt,
            c.LastContactAt, c.LastError, reachable)
        : null;

    private static OffloadSettingsDto ToSettings(OffloadConnection c) => new(c.AutoWhenLow, c.AutoByRules, c.ThresholdPercent, c.ThresholdFreeGb, c.MinAgeMonths,
        c.OnlyCompletedBauakten, c.IncludePhotos, c.IncludeVideos, c.IncludeDocuments, c.RequireBackup, c.CacheLimitMb);

    private static async Task<Results<Ok<OffloadSettingsDto>, ValidationProblem>> SettingsAsync(OffloadSettingsDto request, OffloadService service,
        EstrichOneDbContext db, IAuditLog audit, ICurrentUser user, OffloadProgress progress, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.ThresholdPercent is < 1 or > 90) errors["thresholdPercent"] = ["1 bis 90 Prozent."];
        if (request.ThresholdFreeGb is < 0 or > 100_000) errors["thresholdFreeGb"] = ["0 (aus) bis 100000 GB."];
        if (request.MinAgeMonths is < 0 or > 240) errors["minAgeMonths"] = ["0 bis 240 Monate."];
        if (request.CacheLimitMb is < 100 or > 1_000_000) errors["cacheLimitMb"] = ["100 MB bis 1 000 000 MB."];
        if (!request.IncludePhotos && !request.IncludeVideos && !request.IncludeDocuments) errors["includePhotos"] = ["Mindestens eine Dateiart wählen."];
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var connection = await service.ConnectionAsync(cancellationToken);
        connection.Configure(request.AutoWhenLow, request.AutoByRules, request.ThresholdPercent, request.ThresholdFreeGb, request.MinAgeMonths,
            request.OnlyCompletedBauakten, request.IncludePhotos, request.IncludeVideos, request.IncludeDocuments, request.RequireBackup, request.CacheLimitMb);
        audit.Record("files.offload.settings_changed", user.UserId, "offload", null, new Dictionary<string, string?>
        {
            ["thresholdPercent"] = request.ThresholdPercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["requireBackup"] = request.RequireBackup ? "true" : "false",
        });
        await db.SaveChangesAsync(cancellationToken);
        progress.Wake();
        return TypedResults.Ok(ToSettings(connection));
    }

    private static async Task<Results<Ok<OffloadConnectionDto>, ProblemHttpResult>> PairAsync(PairOffloadRequest request, OffloadService service,
        EstrichOneDbContext db, IAuditLog audit, ICurrentUser user, ICompanyProfiles companies, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await service.PairAsync(request.Address, request.Code, $"EstrichONE {await companies.GetDisplayNameAsync(cancellationToken)}",
                user.UserId, cancellationToken);
            audit.Record("files.offload.paired", user.UserId, "offload", null,
                new Dictionary<string, string?> { ["address"] = connection.Address, ["fingerprint"] = connection.CaFingerprint });
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(ToDto(connection, true)!);
        }
        catch (PeerException exception)
        {
            return TypedResults.Problem(statusCode: exception.Code switch { "PEER_UNREACHABLE" => 502, "OFFLOAD_STILL_IN_USE" => 409, _ => 422 },
                title: "Kopplung fehlgeschlagen", detail: exception.Message, extensions: new Dictionary<string, object?> { ["code"] = exception.Code });
        }
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> UnpairAsync(OffloadService service, EstrichOneDbContext db, IAuditLog audit,
        ICurrentUser user, CancellationToken cancellationToken)
    {
        if (!await service.UnpairAsync(cancellationToken))
        {
            return TypedResults.Problem(statusCode: 409, title: "Noch Dateien ausgelagert",
                detail: "Bitte zuerst alle ausgelagerten Dateien zurückholen („Alles wieder lokal speichern“) – dann entkoppeln.",
                extensions: new Dictionary<string, object?> { ["code"] = "OFFLOAD_STILL_IN_USE" });
        }

        audit.Record("files.offload.unpaired", user.UserId, "offload");
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Accepted<OffloadAccepted>, ProblemHttpResult>> Run(OffloadRunRequest? request, OffloadService service,
        OffloadProgress progress, ICurrentUser user, CancellationToken cancellationToken)
    {
        if (await service.FindConnectionAsync(cancellationToken) is not { Paired: true })
        {
            return TypedResults.Problem(statusCode: 409, title: "Kein Storage gekoppelt",
                extensions: new Dictionary<string, object?> { ["code"] = "OFFLOAD_NOT_PAIRED" });
        }

        progress.Requests.Enqueue(new OffloadRequest(false, request?.BauakteId, null, request?.BauakteId is not null, user.UserId));
        progress.Wake();
        return TypedResults.Accepted("/api/v1/files/offload", new OffloadAccepted("Auslagerung gestartet."));
    }

    private static async Task<Results<Accepted<OffloadAccepted>, ProblemHttpResult>> RecallAsync(OffloadRecallRequest request, EstrichOneDbContext db,
        OffloadProgress progress, ICurrentUser user, CancellationToken cancellationToken)
    {
        List<string>? keys = null;
        if (request.FileIds is { Count: > 0 } ids)
        {
            if (ids.Count > 1000)
            {
                return TypedResults.Problem(statusCode: 400, title: "Höchstens 1000 Dateien je Auftrag.");
            }

            keys = await db.Set<FileRecord>().AsNoTracking().Where(f => ids.Contains(f.Id) && f.StorageKey != null).Select(f => f.StorageKey!).Distinct()
                .ToListAsync(cancellationToken);
            if (keys.Count == 0)
            {
                return TypedResults.Problem(statusCode: 404, title: "Dateien nicht gefunden.");
            }
        }
        else if (request.BauakteId is null && !request.All)
        {
            return TypedResults.Problem(statusCode: 400, title: "Bitte Dateien, eine Bauakte oder „alles“ angeben.");
        }

        progress.Requests.Enqueue(new OffloadRequest(true, request.BauakteId, keys, true, user.UserId));
        progress.Wake();
        return TypedResults.Accepted("/api/v1/files/offload", new OffloadAccepted("Rückholen gestartet."));
    }

    private static async Task<Ok<OffloadedFilesPage>> FilesAsync(EstrichOneDbContext db, Guid? bauakteId, string? state, int? skip, int? take,
        CancellationToken cancellationToken)
    {
        var query = from file in db.Set<FileRecord>().AsNoTracking()
                    join blob in db.Set<OffloadedBlob>().AsNoTracking() on file.StorageKey equals blob.StorageKey
                    where blob.State != OffloadState.Recalled
                    select new { file, blob };
        if (bauakteId is { } id)
        {
            query = query.Where(x => x.file.BauakteId == id);
        }

        if (Enum.TryParse<OffloadState>(state, ignoreCase: true, out var wanted))
        {
            query = query.Where(x => x.blob.State == wanted);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.blob.UploadedAt).ThenBy(x => x.file.Id).Skip(Math.Max(0, skip ?? 0)).Take(Math.Clamp(take ?? 50, 1, 200))
            .Select(x => new
            {
                x.file.Id, x.file.OriginalName, x.file.Kind, x.file.BauakteId, x.file.SizeBytes, x.blob.State, x.blob.UploadedAt, x.blob.OffloadedAt, x.blob.BackedUpAt,
                x.blob.LastError,
                Title = db.Set<Bauakte>().Where(b => b.Id == x.file.BauakteId).Select(b => b.Title).FirstOrDefault(),
            }).ToListAsync(cancellationToken);
        return TypedResults.Ok(new OffloadedFilesPage([.. rows.Select(r => new OffloadedFileDto(r.Id, r.OriginalName, r.Kind, r.BauakteId, r.Title, r.SizeBytes, r.State,
            r.UploadedAt, r.OffloadedAt, r.BackedUpAt, r.LastError))], total));
    }

    private static async Task<Ok<List<OffloadedBauakteDto>>> BauaktenAsync(EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var rows = await (from file in db.Set<FileRecord>().AsNoTracking()
                          join blob in db.Set<OffloadedBlob>().AsNoTracking() on file.StorageKey equals blob.StorageKey
                          where blob.State == OffloadState.Offloaded && file.BauakteId != null
                          group file by file.BauakteId into g
                          select new { BauakteId = g.Key!.Value, Files = g.Count(), Bytes = g.Sum(f => f.SizeBytes) })
            .OrderByDescending(x => x.Bytes).Take(200).ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.BauakteId).ToList();
        var titles = await db.Set<Bauakte>().AsNoTracking().Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.Title, cancellationToken);
        return TypedResults.Ok(rows.Select(r => new OffloadedBauakteDto(r.BauakteId, titles.GetValueOrDefault(r.BauakteId) ?? "Bauakte", r.Files, r.Bytes)).ToList());
    }

    private static async Task<Ok<OffloadAccepted>> ReconcileAsync(OffloadService service, OffloadProgress progress, CancellationToken cancellationToken)
    {
        await progress.Gate.WaitAsync(cancellationToken);
        try
        {
            var settings = await service.ConnectionAsync(cancellationToken);
            var changed = await service.ReconcileAsync(settings, cancellationToken);
            await service.CleanupRemoteAsync(settings, cancellationToken);
            return TypedResults.Ok(new OffloadAccepted($"Abgleich abgeschlossen ({changed} Änderung{(changed == 1 ? "" : "en")})."));
        }
        finally
        {
            progress.Gate.Release();
        }
    }

    private static async Task<Results<Accepted<OffloadAccepted>, ProblemHttpResult>> RecoverAsync(OffloadService service, IOffloadBackup backup, IAuditLog audit,
        ICurrentUser user, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        if (!backup.Available)
        {
            return TypedResults.Problem(statusCode: 409, title: "Kein Backup-Dienst eingerichtet",
                extensions: new Dictionary<string, object?> { ["code"] = "BACKUP_NOT_CONFIGURED" });
        }

        var id = await service.RequestRecoveryFromBackupAsync(cancellationToken);
        if (id is null)
        {
            return TypedResults.Problem(statusCode: 409, title: "Nichts zurückzuholen oder bereits angefordert",
                extensions: new Dictionary<string, object?> { ["code"] = "OFFLOAD_NOTHING_MISSING" });
        }

        audit.Record("files.offload.recovery_requested", user.UserId, "offload", id);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Accepted("/api/v1/files/offload",
            new OffloadAccepted("Der Backup-Dienst spielt die fehlenden Originale zurück (binnen einer Minute, danach Abgleich).", id));
    }

    private static async Task<Results<Ok<List<OffloadLocation>>, ProblemHttpResult>> LocationsAsync(string? ids, EstrichOneDbContext db, FileService files,
        CancellationToken cancellationToken)
    {
        var wanted = (ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => Guid.TryParse(v, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().Take(500).ToList();
        var rows = await (from file in db.Set<FileRecord>().AsNoTracking()
                          join blob in db.Set<OffloadedBlob>().AsNoTracking() on file.StorageKey equals blob.StorageKey
                          where wanted.Contains(file.Id) && (blob.State == OffloadState.Offloaded || blob.State == OffloadState.Missing)
                          select new { file.Id, blob.State }).ToListAsync(cancellationToken);
        var result = new List<OffloadLocation>();
        foreach (var row in rows)
        {
            if (await files.FindReadableAsync(row.Id, cancellationToken) is not null)
            {
                result.Add(new OffloadLocation(row.Id, row.State));
            }
        }

        return TypedResults.Ok(result);
    }
}
