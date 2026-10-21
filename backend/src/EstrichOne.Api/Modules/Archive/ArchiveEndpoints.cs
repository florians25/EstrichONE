using EstrichOne.Api.Infrastructure.Peers;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace EstrichOne.Api.Modules.Archive;

public sealed record ArchiveConnectionDto(
    string Address, string ServerName, string ServerVersion, string CaFingerprint, string FingerprintDisplay, DateTimeOffset PairedAt,
    DateTimeOffset? LastContactAt, string? LastError, bool AutoArchive, string? NotifyEmail);

public sealed record ArchiveCounts(int Pending, int Archived, int Failed);

public sealed record ArchiveOverview(bool Connected, ArchiveConnectionDto? Connection, ArchiveServerStatus? Server, ArchiveCounts Counts);

public sealed record PairArchiveRequest(string Address, string Code);

public sealed record ArchiveSettingsRequest(bool AutoArchive, string? NotifyEmail);

public sealed record ArchiveRecordDto(
    Guid Id, Guid FileId, Guid? BauakteId, string DocumentType, string DocumentTypeLabel, string Name, string Sha256, long SizeBytes,
    int RetentionYears, ArchiveTrigger Trigger, ArchiveRecordStatus Status, int Attempts, DateTimeOffset NextAttemptAt, string? LastError,
    DateTimeOffset QueuedAt, DateTimeOffset? ArchivedAt, DateOnly? RetainUntil, long? LedgerSeq, string? LedgerHash, string? ArchiveServerName);

public sealed record QueueResult(int Queued);

internal static class ArchiveEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        var archive = api.MapGroup("/archive").WithTags("Archiv");
        archive.MapGet("/status", StatusAsync).WithName("GetArchiveStatus").RequirePermission(Permissions.SettingsManage);
        archive.MapPost("/pair", PairAsync).WithName("PairArchive")
            .WithSummary("Archivserver per Adresse und Kopplungscode verbinden (Zertifikat wird gepinnt)")
            .RequirePermission(Permissions.SettingsManage);
        archive.MapDelete("/connection", UnpairAsync).WithName("UnpairArchive").RequirePermission(Permissions.SettingsManage);
        archive.MapPut("/settings", SettingsAsync).WithName("UpdateArchiveSettings").RequirePermission(Permissions.SettingsManage);
        archive.MapGet("/rules", (ArchiveService service, CancellationToken ct) => RulesAsync(service, ct))
            .WithName("GetArchiveRules").RequirePermission(Permissions.SettingsManage);
        archive.MapPut("/rules", UpdateRulesAsync).WithName("UpdateArchiveRules").RequirePermission(Permissions.SettingsManage);
        archive.MapGet("/records", RecordsAsync).WithName("GetArchiveRecords").RequirePermission(Permissions.SettingsManage);
        archive.MapPost("/records/{id:guid}/retry", RetryAsync).WithName("RetryArchiveRecord").RequirePermission(Permissions.SettingsManage);
        archive.MapPost("/verify", VerifyAsync).WithName("VerifyArchive")
            .WithSummary("Prüflauf auf dem Archivserver (Hash-Kette, alle Prüfsummen) – Ergebnis = Prüfprotokoll")
            .RequirePermission(Permissions.SettingsManage);
        archive.MapGet("/export", ExportAsync).WithName("ExportArchive")
            .WithSummary("Export für Betriebsprüfer (ZIP: Dokumente, Metadaten, Journal, Prüfsummen, Prüfprotokoll)")
            .RequirePermission(Permissions.SettingsManage);

        // Nachweis an Dokument und Bauakte – für alle, die die Bauakte sehen dürfen.
        archive.MapGet("/bauakten/{bauakteId:guid}", BauakteRecordsAsync).WithName("GetBauakteArchiveRecords")
            .RequirePermission(Permissions.BauaktenRead);
        archive.MapPost("/bauakten/{bauakteId:guid}", QueueBauakteAsync).WithName("ArchiveBauakte")
            .WithSummary("Alle Dokumente der Bauakte archivieren (bzw. Fehlgeschlagene erneut versuchen)")
            .RequirePermission(Permissions.BauaktenEdit);
        archive.MapGet("/files/{fileId:guid}", FileRecordAsync).WithName("GetFileArchiveRecord").RequirePermission(Permissions.BauaktenRead);
        archive.MapPost("/files/{fileId:guid}", QueueFileAsync).WithName("ArchiveFile").RequirePermission(Permissions.BauaktenEdit);
    }

    internal static ArchiveRecordDto ToDto(ArchiveRecord r) => new(r.Id, r.FileId, r.BauakteId, r.DocumentType,
        ArchiveService.LabelOf(r.DocumentType), r.Name, r.Sha256, r.SizeBytes, r.RetentionYears, r.Trigger, r.Status, r.Attempts, r.NextAttemptAt,
        r.LastError, r.CreatedAt, r.ArchivedAt, r.RetainUntil, r.LedgerSeq, r.LedgerHash, r.ArchiveServerName);

    private static ArchiveConnectionDto ToDto(ArchiveConnection c) => new(c.Address, c.ServerName, c.ServerVersion, c.CaFingerprint,
        PeerClient.FormatFingerprint(c.CaFingerprint), c.PairedAt, c.LastContactAt, c.LastError, c.AutoArchive, c.NotifyEmail);

    private static async Task<Ok<ArchiveOverview>> StatusAsync(ArchiveService service, EstrichOneDbContext db, bool? live,
        CancellationToken cancellationToken)
    {
        var connection = await service.ConnectionAsync(cancellationToken);
        var server = connection is not null && live != false ? await service.ServerStatusAsync(connection, cancellationToken) : null;
        var counts = await db.Set<ArchiveRecord>().GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return TypedResults.Ok(new ArchiveOverview(connection is not null, connection is null ? null : ToDto(connection), server,
            new ArchiveCounts(counts.GetValueOrDefault(ArchiveRecordStatus.Pending), counts.GetValueOrDefault(ArchiveRecordStatus.Archived),
                counts.GetValueOrDefault(ArchiveRecordStatus.Failed))));
    }

    private static async Task<Results<Ok<ArchiveConnectionDto>, ProblemHttpResult>> PairAsync(PairArchiveRequest request,
        ArchiveService service, IAuditLog audit, ICurrentUser user, EstrichOneDbContext db, ArchiveSignal signal, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await service.PairAsync(request.Address, request.Code, user.UserId, cancellationToken);
            audit.Record("archive.paired", user.UserId, "archive_connection", connection.Id.ToString(),
                new Dictionary<string, string?> { ["address"] = connection.Address, ["fingerprint"] = connection.CaFingerprint });
            await db.SaveChangesAsync(cancellationToken);
            signal.Wake();
            return TypedResults.Ok(ToDto(connection));
        }
        catch (PeerException exception)
        {
            return Problem(exception.Code is "PEER_UNREACHABLE" ? 502 : 422, exception.Code, "Kopplung fehlgeschlagen", exception.Message);
        }
    }

    private static async Task<NoContent> UnpairAsync(ArchiveService service, IAuditLog audit, ICurrentUser user, EstrichOneDbContext db,
        CancellationToken cancellationToken)
    {
        await service.UnpairAsync(cancellationToken);
        audit.Record("archive.unpaired", user.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<ArchiveConnectionDto>, ProblemHttpResult>> SettingsAsync(ArchiveSettingsRequest request,
        ArchiveService service, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var connection = await service.ConnectionAsync(cancellationToken);
        if (connection is null)
        {
            return NotConnected();
        }

        if (!string.IsNullOrWhiteSpace(request.NotifyEmail) && !MimeKit.MailboxAddress.TryParse(request.NotifyEmail.Trim(), out _))
        {
            return Problem(422, "VALIDATION_FAILED", "Ungültige Angaben", "Die E-Mail-Adresse für Benachrichtigungen ist ungültig.");
        }

        connection.Configure(request.AutoArchive, request.NotifyEmail);
        await db.SaveChangesAsync(cancellationToken);
        return TypedResults.Ok(ToDto(connection));
    }

    private static async Task<Ok<IReadOnlyList<ArchiveRuleDto>>> RulesAsync(ArchiveService service, CancellationToken cancellationToken) =>
        TypedResults.Ok(await service.RulesAsync(cancellationToken));

    private static async Task<Results<Ok<IReadOnlyList<ArchiveRuleDto>>, ProblemHttpResult>> UpdateRulesAsync(
        List<ArchiveRuleDto> rules, ArchiveService service, IAuditLog audit, ICurrentUser user, EstrichOneDbContext db, ArchiveSignal signal,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.UpdateRulesAsync(rules, cancellationToken);
            audit.Record("archive.rules.changed", user.UserId);
            await db.SaveChangesAsync(cancellationToken);
            signal.Wake();
            return TypedResults.Ok(result);
        }
        catch (ArgumentException exception)
        {
            return Problem(422, "VALIDATION_FAILED", "Ungültige Angaben", exception.Message);
        }
    }

    private static async Task<Ok<List<ArchiveRecordDto>>> RecordsAsync(EstrichOneDbContext db, ArchiveRecordStatus? status, Guid? bauakteId,
        int? take, CancellationToken cancellationToken)
    {
        var query = db.Set<ArchiveRecord>().AsNoTracking();
        if (status is not null)
        {
            query = query.Where(r => r.Status == status);
        }

        if (bauakteId is not null)
        {
            query = query.Where(r => r.BauakteId == bauakteId);
        }

        var records = await query.OrderByDescending(r => r.CreatedAt).Take(Math.Clamp(take ?? 300, 1, 2000)).ToListAsync(cancellationToken);
        return TypedResults.Ok(records.Select(ToDto).ToList());
    }

    private static async Task<Results<Ok<ArchiveRecordDto>, ProblemHttpResult>> RetryAsync(Guid id, EstrichOneDbContext db, TimeProvider clock,
        ArchiveSignal signal, CancellationToken cancellationToken)
    {
        var record = await db.Set<ArchiveRecord>().SingleOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (record is null)
        {
            return Problem(404, "NOT_FOUND", "Nicht gefunden", "Archivierungseintrag nicht gefunden.");
        }

        record.Retry(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        signal.Wake();
        return TypedResults.Ok(ToDto(record));
    }

    private static async Task<Results<Ok<ArchiveVerification>, ProblemHttpResult>> VerifyAsync(ArchiveService service, IAuditLog audit,
        ICurrentUser user, EstrichOneDbContext db, CancellationToken cancellationToken)
    {
        var connection = await service.ConnectionAsync(cancellationToken);
        if (connection is null)
        {
            return NotConnected();
        }

        try
        {
            using var http = service.Client(connection, TimeSpan.FromMinutes(30));
            using var response = await http.PostAsync(new Uri("/api/v1/verify", UriKind.Relative), null, cancellationToken);
            response.EnsureSuccessStatusCode();
            var report = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<ArchiveVerification>(response.Content,
                cancellationToken) ?? throw new HttpRequestException("Leere Antwort");
            audit.Record("archive.verified", user.UserId, details: new Dictionary<string, string?> { ["ok"] = report.Ok ? "true" : "false" });
            await db.SaveChangesAsync(cancellationToken);
            return TypedResults.Ok(report);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return Problem(502, "ARCHIVE_UNREACHABLE", "Archivserver nicht erreichbar", ArchiveService.Describe(exception));
        }
    }

    private static async Task<Results<FileStreamHttpResult, ProblemHttpResult>> ExportAsync(HttpContext http, ArchiveService service,
        IAuditLog audit, ICurrentUser user, EstrichOneDbContext db, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var connection = await service.ConnectionAsync(cancellationToken);
        if (connection is null)
        {
            return NotConnected();
        }

        var query = (from, to) switch
        {
            (null, null) => string.Empty,
            _ => "?" + string.Join('&', new[] { from is null ? null : $"from={from:yyyy-MM-dd}", to is null ? null : $"to={to:yyyy-MM-dd}" }
                .Where(part => part is not null)),
        };
        var client = service.Client(connection, TimeSpan.FromHours(2));
        try
        {
            var response = await client.GetAsync(new Uri("/api/v1/export" + query, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            audit.Record("archive.exported", user.UserId);
            await db.SaveChangesAsync(cancellationToken);
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            http.Response.RegisterForDispose(response);
            http.Response.RegisterForDispose(client);
            return TypedResults.File(stream, "application/zip", $"archiv-export-{DateTime.UtcNow:yyyyMMdd}.zip");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            client.Dispose();
            return Problem(502, "ARCHIVE_UNREACHABLE", "Archivserver nicht erreichbar", ArchiveService.Describe(exception));
        }
    }

    private static async Task<Results<Ok<List<ArchiveRecordDto>>, ProblemHttpResult>> BauakteRecordsAsync(Guid bauakteId,
        EstrichOneDbContext db, IBauakteAccess access, CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(bauakteId, cancellationToken))
        {
            return Problem(404, "BAUAKTE_NOT_FOUND", "Bauakte nicht gefunden", "Die Bauakte existiert nicht oder ist nicht sichtbar.");
        }

        var records = await db.Set<ArchiveRecord>().AsNoTracking().Where(r => r.BauakteId == bauakteId)
            .OrderBy(r => r.Name).ToListAsync(cancellationToken);
        return TypedResults.Ok(records.Select(ToDto).ToList());
    }

    private static async Task<Results<Ok<ArchiveRecordDto>, NoContent, ProblemHttpResult>> FileRecordAsync(Guid fileId,
        EstrichOneDbContext db, IBauakteAccess access, CancellationToken cancellationToken)
    {
        var record = await db.Set<ArchiveRecord>().AsNoTracking().SingleOrDefaultAsync(r => r.FileId == fileId, cancellationToken);
        if (record is null)
        {
            return TypedResults.NoContent();
        }

        if (record.BauakteId is { } bauakteId && !await access.CanReadAsync(bauakteId, cancellationToken))
        {
            return Problem(404, "NOT_FOUND", "Nicht gefunden", "Datei nicht gefunden.");
        }

        return TypedResults.Ok(ToDto(record));
    }

    private static async Task<Results<Ok<QueueResult>, ProblemHttpResult>> QueueBauakteAsync(Guid bauakteId, ArchiveService service,
        IBauakteAccess access, ICurrentUser user, IAuditLog audit, EstrichOneDbContext db, ArchiveSignal signal, CancellationToken cancellationToken)
    {
        if (!await access.CanReadAsync(bauakteId, cancellationToken))
        {
            return Problem(404, "BAUAKTE_NOT_FOUND", "Bauakte nicht gefunden", "Die Bauakte existiert nicht oder ist nicht sichtbar.");
        }

        if (await service.ConnectionAsync(cancellationToken) is null)
        {
            return NotConnected();
        }

        var queued = await service.QueueBauakteAsync(bauakteId, user.UserId, cancellationToken);
        audit.Record("archive.bauakte_queued", user.UserId, "bauakte", bauakteId.ToString(),
            new Dictionary<string, string?> { ["count"] = queued.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        await db.SaveChangesAsync(cancellationToken);
        signal.Wake();
        return TypedResults.Ok(new QueueResult(queued));
    }

    private static async Task<Results<Ok<ArchiveRecordDto>, ProblemHttpResult>> QueueFileAsync(Guid fileId, ArchiveService service,
        IBauakteAccess access, ICurrentUser user, ArchiveSignal signal, CancellationToken cancellationToken)
    {
        if (await service.ConnectionAsync(cancellationToken) is null)
        {
            return NotConnected();
        }

        var record = await service.QueueFileAsync(fileId, user.UserId, cancellationToken);
        if (record is null || (record.BauakteId is { } bauakteId && !await access.CanReadAsync(bauakteId, cancellationToken)))
        {
            return Problem(404, "NOT_FOUND", "Nicht gefunden", "Datei nicht gefunden oder noch nicht vollständig hochgeladen.");
        }

        signal.Wake();
        return TypedResults.Ok(ToDto(record));
    }

    private static ProblemHttpResult NotConnected() =>
        Problem(409, "ARCHIVE_NOT_CONNECTED", "Kein Archivserver gekoppelt", "Bitte zuerst unter System → Archiv einen Archivserver koppeln.");

    private static ProblemHttpResult Problem(int status, string code, string title, string detail) =>
        TypedResults.Problem(statusCode: status, title: title, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });
}
