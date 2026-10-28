using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Peers;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Documentation.Exports;
using EstrichOne.Api.Modules.Mail.Exports;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Archive;

public sealed record ArchiveRuleDto(string DocumentType, string Label, bool Enabled, int RetentionYears, ArchiveTrigger Trigger);

/// <summary>Quittung des Archivservers (siehe backend/src/EstrichOne.Archive).</summary>
internal sealed record ArchiveReceiptResponse(string ObjectId, string Sha256, long Size, DateTimeOffset ArchivedAt, DateOnly RetainUntil,
    long LedgerSeq, string LedgerHash, bool Deleted, string? Name, string? DocumentType);

public sealed record ArchiveServerStatus(
    string Role, string Name, string Version, DateTimeOffset Now, string ClientId, string ClientName, DateTimeOffset PairedAt,
    ArchiveServerStatistics? Archive, ArchiveVerification? LastVerification, long? DiskFreeBytes, long? DiskTotalBytes);

public sealed record ArchiveServerStatistics(int Objects, long Bytes, int Deleted, long LedgerEntries, string LedgerHead);

public sealed record ArchiveVerification(
    DateTimeOffset At, bool Ok, long LedgerEntries, string LedgerHead, int ObjectsChecked, long BytesChecked, int Deleted,
    IReadOnlyList<string> Errors);

/// <summary>Kopplung, Regeln, Warteschlange und Übertragung zum Archivserver.</summary>
internal sealed partial class ArchiveService(
    EstrichOneDbContext db, IArchivableDocuments documents, IDataProtectionProvider protection, TimeProvider clock, IMailService mail,
    IOptions<EstrichOneOptions> options, IConfiguration configuration, ILogger<ArchiveService> logger)
{
    public const string ProtectorPurpose = "EstrichOne.Archive.Token";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Vorschläge (Fristen nach § 147 AO / § 257 HGB, aufgerundet); maßgeblich ist die steuerliche Beratung.</summary>
    public static IReadOnlyList<ArchiveRuleDto> DefaultRules { get; } =
    [
        new("invoice", "Rechnungen", true, 10, ArchiveTrigger.OnUpload),
        new("order", "Aufträge", true, 6, ArchiveTrigger.OnUpload),
        new("offer", "Angebote", true, 6, ArchiveTrigger.OnApproval),
        new("delivery", "Lieferscheine", true, 6, ArchiveTrigger.OnBauakteCompleted),
        new("acceptance", "Abnahmen", true, 10, ArchiveTrigger.OnUpload),
        new("protocol", "Protokolle", true, 10, ArchiveTrigger.OnBauakteCompleted),
        new("plan", "Pläne", true, 10, ArchiveTrigger.OnBauakteCompleted),
        new("customer", "Kundenschreiben", true, 6, ArchiveTrigger.OnBauakteCompleted),
        new("office", "Büro", false, 6, ArchiveTrigger.Manual),
        new("other", "Sonstige Dokumente", false, 10, ArchiveTrigger.Manual),
        new("photo", "Fotos", false, 10, ArchiveTrigger.OnBauakteCompleted),
        new("video", "Videos", false, 10, ArchiveTrigger.Manual),
    ];

    public static string LabelOf(string documentType) => DefaultRules.FirstOrDefault(r => r.DocumentType == documentType)?.Label ?? documentType;

    public static string DocumentTypeOf(ArchivableDocument document) =>
        document.Kind == "document" ? document.Category ?? "other" : document.Kind;

    public Task<ArchiveConnection?> ConnectionAsync(CancellationToken cancellationToken) =>
        db.Set<ArchiveConnection>().SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ArchiveRuleDto>> RulesAsync(CancellationToken cancellationToken)
    {
        var stored = await db.Set<ArchiveRule>().AsNoTracking().ToDictionaryAsync(r => r.DocumentType, cancellationToken);
        return [.. DefaultRules.Select(d => stored.TryGetValue(d.DocumentType, out var r)
            ? d with { Enabled = r.Enabled, RetentionYears = r.RetentionYears, Trigger = r.Trigger }
            : d)];
    }

    public async Task<IReadOnlyList<ArchiveRuleDto>> UpdateRulesAsync(IReadOnlyList<ArchiveRuleDto> rules, CancellationToken cancellationToken)
    {
        var stored = await db.Set<ArchiveRule>().ToDictionaryAsync(r => r.DocumentType, cancellationToken);
        foreach (var rule in rules)
        {
            if (DefaultRules.All(d => d.DocumentType != rule.DocumentType))
            {
                throw new ArgumentException($"Unbekannte Dokumentart '{rule.DocumentType}'.");
            }

            if (rule.RetentionYears is < 1 or > 30)
            {
                throw new ArgumentException("Die Aufbewahrungsfrist muss zwischen 1 und 30 Jahren liegen.");
            }

            if (stored.TryGetValue(rule.DocumentType, out var existing))
            {
                existing.Update(rule.Enabled, rule.RetentionYears, rule.Trigger);
            }
            else
            {
                db.Add(ArchiveRule.Create(rule.DocumentType, rule.Enabled, rule.RetentionYears, rule.Trigger));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await RulesAsync(cancellationToken);
    }

    public async Task<ArchiveConnection> PairAsync(string address, string code, Guid userId, CancellationToken cancellationToken)
    {
        var uri = PeerClient.NormalizeAddress(address);
        var pairing = await PeerClient.PairAsync(uri, code, $"EstrichONE {options.Value.ServerName}", "archive", cancellationToken);
        var existing = await ConnectionAsync(cancellationToken);
        if (existing is not null)
        {
            db.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
        }

        var connection = ArchiveConnection.Create(uri.ToString().TrimEnd('/'), pairing.Name, pairing.Version, pairing.CaCertificatePem,
            pairing.CaFingerprint, pairing.ClientId, protection.CreateProtector(ProtectorPurpose).Protect(pairing.Token), userId, clock.GetUtcNow());
        db.Add(connection);
        await db.SaveChangesAsync(cancellationToken);
        return connection;
    }

    public async Task UnpairAsync(CancellationToken cancellationToken)
    {
        var connection = await ConnectionAsync(cancellationToken);
        if (connection is null)
        {
            return;
        }

        try
        {
            using var http = Client(connection, TimeSpan.FromSeconds(10));
            using var _ = await http.DeleteAsync(new Uri("/api/v1/pairing", UriKind.Relative), cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // Archivserver nicht erreichbar: lokal trotzdem entkoppeln.
        }

        db.Remove(connection);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Gekoppelter Archivserver für die Fernaktualisierung (Backup-Modul, docs/backup.md).</summary>
    public async Task<IReadOnlyList<Backup.Exports.UpdatablePeer>> UpdatablePeersAsync(CancellationToken cancellationToken)
    {
        if (await ConnectionAsync(cancellationToken) is not { } connection)
        {
            return [];
        }

        var token = protection.CreateProtector(ProtectorPurpose).Unprotect(connection.ProtectedToken);
        return [new Backup.Exports.UpdatablePeer("archive", "archive", connection.ServerName, "archive", connection.ClientId,
            Infrastructure.Peers.PeerUpdateOrders.Sha256(token), timeout => Client(connection, timeout), connection.ServerVersion, "/system/archiv")];
    }

    public HttpClient Client(ArchiveConnection connection, TimeSpan timeout) =>
        PeerClient.CreateClient(new Uri(connection.Address), connection.CaCertificatePem,
            protection.CreateProtector(ProtectorPurpose).Unprotect(connection.ProtectedToken), timeout);

    public async Task<ArchiveServerStatus?> ServerStatusAsync(ArchiveConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            using var http = Client(connection, TimeSpan.FromSeconds(15));
            var status = await http.GetFromJsonAsync<ArchiveServerStatus>("/api/v1/status", Json, cancellationToken);
            connection.Contacted(clock.GetUtcNow(), null, status?.Version);
            await db.SaveChangesAsync(cancellationToken);
            return status;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            connection.Contacted(clock.GetUtcNow(), Describe(exception));
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
    }

    /// <summary>Neue Kandidaten nach den Regeln einreihen (nur wenn gekoppelt und automatische Archivierung an).</summary>
    public async Task<int> EnqueueByRulesAsync(CancellationToken cancellationToken)
    {
        var connection = await ConnectionAsync(cancellationToken);
        if (connection is not { AutoArchive: true })
        {
            return 0;
        }

        var queued = 0;
        foreach (var rule in (await RulesAsync(cancellationToken)).Where(r => r.Enabled && r.Trigger != ArchiveTrigger.Manual))
        {
            var query = Candidates(rule.DocumentType);
            query = rule.Trigger switch
            {
                ArchiveTrigger.OnApproval => query.Where(d => d.ApprovedAt != null),
                ArchiveTrigger.OnBauakteCompleted => query.Where(d => d.BauakteId != null
                    && db.Set<Bauakte>().Any(b => b.Id == d.BauakteId && b.Status == BauakteStatus.Completed)),
                _ => query,
            };
            foreach (var document in await query.OrderBy(d => d.CreatedAt).Take(200).ToListAsync(cancellationToken))
            {
                db.Add(ArchiveRecord.Queue(document.FileId, document.BauakteId, rule.DocumentType, document.OriginalName, document.Sha256,
                    document.SizeBytes, rule.RetentionYears, rule.Trigger, null, clock.GetUtcNow()));
                queued++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return queued;
    }

    private IQueryable<ArchivableDocument> Candidates(string documentType)
    {
        var query = documents.Query().Where(d => !db.Set<ArchiveRecord>().Any(r => r.FileId == d.FileId));
        return documentType is "photo" or "video"
            ? query.Where(d => d.Kind == documentType)
            : documentType == "other"
                ? query.Where(d => d.Kind == "document" && (d.Category == null || d.Category == "other"))
                : query.Where(d => d.Kind == "document" && d.Category == documentType);
    }

    /// <summary>Manuell: einzelne Datei (auch ohne aktive Regel; Frist aus der Regel der Dokumentart).</summary>
    public async Task<ArchiveRecord?> QueueFileAsync(Guid fileId, Guid userId, CancellationToken cancellationToken)
    {
        var existing = await db.Set<ArchiveRecord>().SingleOrDefaultAsync(r => r.FileId == fileId, cancellationToken);
        if (existing is not null)
        {
            existing.Retry(clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var document = await documents.Query().SingleOrDefaultAsync(d => d.FileId == fileId, cancellationToken);
        if (document is null)
        {
            return null;
        }

        var rules = await RulesAsync(cancellationToken);
        var type = DocumentTypeOf(document);
        var record = ArchiveRecord.Queue(document.FileId, document.BauakteId, type, document.OriginalName, document.Sha256, document.SizeBytes,
            rules.FirstOrDefault(r => r.DocumentType == type)?.RetentionYears ?? 10, ArchiveTrigger.Manual, userId, clock.GetUtcNow());
        db.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    /// <summary>Manuell: alle Dokumente einer Bauakte (Fotos/Videos nur, wenn ihre Regel aktiv ist).</summary>
    public async Task<int> QueueBauakteAsync(Guid bauakteId, Guid userId, CancellationToken cancellationToken)
    {
        var rules = await RulesAsync(cancellationToken);
        var media = rules.Where(r => r.Enabled && r.DocumentType is "photo" or "video").Select(r => r.DocumentType).ToList();
        var candidates = await documents.Query()
            .Where(d => d.BauakteId == bauakteId && (d.Kind == "document" || media.Contains(d.Kind)))
            .Where(d => !db.Set<ArchiveRecord>().Any(r => r.FileId == d.FileId))
            .ToListAsync(cancellationToken);
        foreach (var document in candidates)
        {
            var type = DocumentTypeOf(document);
            db.Add(ArchiveRecord.Queue(document.FileId, document.BauakteId, type, document.OriginalName, document.Sha256, document.SizeBytes,
                rules.FirstOrDefault(r => r.DocumentType == type)?.RetentionYears ?? 10, ArchiveTrigger.Manual, userId, clock.GetUtcNow()));
        }

        await db.Set<ArchiveRecord>().Where(r => r.BauakteId == bauakteId && r.Status == ArchiveRecordStatus.Failed)
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.Status, ArchiveRecordStatus.Pending).SetProperty(r => r.NextAttemptAt, clock.GetUtcNow()),
                cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return candidates.Count;
    }

    /// <summary>Fällige Einträge übertragen. Rückgabe: Anzahl bearbeiteter Einträge.</summary>
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken)
    {
        var connection = await ConnectionAsync(cancellationToken);
        if (connection is null)
        {
            return 0;
        }

        var now = clock.GetUtcNow();
        var due = await db.Set<ArchiveRecord>()
            .Where(r => r.Status != ArchiveRecordStatus.Archived && r.NextAttemptAt <= now)
            .OrderBy(r => r.NextAttemptAt).Take(20).ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        using var http = Client(connection, TimeSpan.FromMinutes(30));
        foreach (var record in due)
        {
            var wasFailed = record.Status == ArchiveRecordStatus.Failed;
            string? error;
            var permanent = false;
            try
            {
                (error, permanent) = await UploadAsync(http, connection, record, cancellationToken);
                connection.Contacted(clock.GetUtcNow(), null);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or JsonException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                error = Describe(exception);
                connection.Contacted(clock.GetUtcNow(), error);
            }

            if (error is not null)
            {
                record.Failed(error, clock.GetUtcNow(), permanent);
                LogArchiveFailed(logger, record.FileId, error);
                if (!wasFailed && record.Status == ArchiveRecordStatus.Failed && connection.NotifyEmail is { } notify)
                {
                    await NotifyAsync(notify, record, error, cancellationToken);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return due.Count;
    }

    private async Task<(string? Error, bool Permanent)> UploadAsync(HttpClient http, ArchiveConnection connection, ArchiveRecord record,
        CancellationToken cancellationToken)
    {
        var document = await documents.Query().SingleOrDefaultAsync(d => d.FileId == record.FileId, cancellationToken);
        await using var content = document is null ? null : await documents.OpenAsync(record.FileId, cancellationToken);
        if (document is null || content is null)
        {
            return ("Datei nicht mehr vorhanden (gelöscht oder Upload unvollständig).", true);
        }

        var bauakte = document.BauakteId is null
            ? null
            : await db.Set<Bauakte>().AsNoTracking().Where(b => b.Id == document.BauakteId)
                .Select(b => new { b.Number, b.Title }).SingleOrDefaultAsync(cancellationToken);
        var source = new Dictionary<string, string>
        {
            ["system"] = "EstrichONE",
            ["datei_id"] = document.FileId.ToString(),
            ["dokumentart"] = LabelOf(record.DocumentType),
        };
        if (document.BauakteId is { } bauakteId)
        {
            source["bauakte_id"] = bauakteId.ToString();
        }

        if (bauakte is not null)
        {
            source["bauakte"] = $"{bauakte.Number} {bauakte.Title}".Trim();
        }

        var metadata = new
        {
            name = document.OriginalName,
            contentType = document.ContentType,
            documentType = record.DocumentType,
            retentionYears = record.RetentionYears,
            documentDate = DateOnly.FromDateTime(document.CreatedAt.UtcDateTime),
            source,
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/objects/{record.FileId}");
        request.Headers.Add("X-Archive-Sha256", document.Sha256);
        request.Headers.Add("X-Archive-Metadata", Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(metadata, Json)));
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentLength = document.SizeBytes;
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            var receipt = await response.Content.ReadFromJsonAsync<ArchiveReceiptResponse>(Json, cancellationToken)
                ?? throw new JsonException("Leere Quittung.");
            if (!string.Equals(receipt.Sha256, document.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return ("Quittung mit abweichender Prüfsumme – bitte Archivserver prüfen.", true);
            }

            record.Succeeded(receipt.ArchivedAt, receipt.RetainUntil, receipt.LedgerSeq, receipt.LedgerHash, connection.ServerName);
            return (null, false);
        }

        var problem = await response.Content.ReadAsStringAsync(cancellationToken);
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => ("Archivserver lehnt den Zugang ab – bitte neu koppeln.", false),
            HttpStatusCode.Conflict => ("Unter dieser ID liegt im Archiv bereits ein anderer Inhalt (unveränderbar).", true),
            HttpStatusCode.UnprocessableEntity => ($"Archivserver lehnt ab: {Title(problem)}", true),
            HttpStatusCode.RequestEntityTooLarge => ("Datei zu groß für den Archivserver.", true),
            _ => ($"Archivserver meldet HTTP {(int)response.StatusCode}: {Title(problem)}", false),
        };
    }

    private async Task NotifyAsync(string to, ArchiveRecord record, string error, CancellationToken cancellationToken)
    {
        try
        {
            var bauakte = record.BauakteId is null
                ? "–"
                : await db.Set<Bauakte>().Where(b => b.Id == record.BauakteId).Select(b => b.Number ?? b.Title).SingleOrDefaultAsync(cancellationToken) ?? "–";
            await mail.QueueTemplateAsync(MailTemplateKeys.ArchiveFailed, to, new Dictionary<string, string?>
            {
                ["dokument"] = record.Name,
                ["bauakte"] = bauakte,
                ["fehler"] = error,
                ["link"] = $"{options.Value.PublicUrl?.ToString().TrimEnd('/')}/system/archiv",
            }, $"archive_record:{record.Id}", cancellationToken: cancellationToken);
        }
        catch (ArgumentException)
        {
            // Ungültige Empfängeradresse: Archivierung nicht blockieren.
        }
    }

    private static string Title(string problem)
    {
        try
        {
            using var json = JsonDocument.Parse(problem);
            return json.RootElement.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty;
        }
        catch (JsonException)
        {
            return problem.Length > 200 ? problem[..200] : problem;
        }
    }

    /// <summary>
    /// Zustand für die Hosting-Konsole (je Mandant): <c>&lt;Backup-Statusverzeichnis&gt;/archive.json</c>, ohne Geheimnisse.
    /// </summary>
    public async Task WriteStatusFileAsync(CancellationToken cancellationToken)
    {
        var dir = configuration["EstrichOne:Backup:StatusPath"];
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return;
        }

        var connection = await ConnectionAsync(cancellationToken);
        var counts = await db.Set<ArchiveRecord>().GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        var state = new
        {
            connected = connection is not null,
            serverName = connection?.ServerName,
            address = connection?.Address,
            lastContactAt = connection?.LastContactAt,
            lastError = connection?.LastError,
            archived = counts.GetValueOrDefault(ArchiveRecordStatus.Archived),
            pending = counts.GetValueOrDefault(ArchiveRecordStatus.Pending),
            failed = counts.GetValueOrDefault(ArchiveRecordStatus.Failed),
            at = clock.GetUtcNow(),
        };
        var path = Path.Combine(dir, "archive.json");
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(state, Json), cancellationToken);
        File.Move(path + ".tmp", path, overwrite: true);
    }

    public static string Describe(Exception exception) => exception switch
    {
        HttpRequestException { InnerException: System.Security.Authentication.AuthenticationException } =>
            "Zertifikat des Archivservers stimmt nicht mehr mit der Kopplung überein – bitte neu koppeln.",
        HttpRequestException request => $"Archivserver nicht erreichbar: {request.Message}",
        TaskCanceledException => "Zeitüberschreitung beim Archivserver.",
        _ => exception.Message,
    };

    public static string ProblemText(Exception exception) => exception is PeerException peer ? peer.Message : Describe(exception);

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Archivierung von {FileId} fehlgeschlagen: {Error}")]
    private static partial void LogArchiveFailed(ILogger logger, Guid fileId, string error);
}
