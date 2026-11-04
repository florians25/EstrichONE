// Integrationstests laufen unter Linux (CI); kurze Aufrufe ohne Abbruch-Token.
#pragma warning disable xUnit1051, CA1416
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Archive;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Documentation;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Archive;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Offload;

/// <summary>
/// Speicher-Auslagerung gegen einen echten Storage (EstrichOne.Archive, Rolle offload) über HTTPS: Kopplung, Auslagern erst nach
/// geprüftem Upload und bestätigter Sicherungskopie, Abruf (entschlüsselt, geprüft, Zwischenspeicher), Ausfall, Manipulation,
/// Rückholen, Abgleich und Wiederherstellung aus der Sicherung.
/// </summary>
public sealed class OffloadTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private ApiFactory _factory = null!;
    private ArchiveServerHost _storage = null!;
    private HttpClient _admin = null!;
    private Guid _bauakteId;
    private string _status = null!;

    public async ValueTask InitializeAsync()
    {
        _status = Path.Combine(Path.GetTempPath(), "estrichone-offload-status", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_status);
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
        {
            ["EstrichOne:Offload:Worker"] = "false",
            ["EstrichOne:Archive:Worker"] = "false",
            ["EstrichOne:Health:Worker"] = "false",
            ["EstrichOne:Backup:StatusPath"] = _status,
        });
        _storage = await ArchiveServerHost.StartAsync(ServerRole.Offload);
        _admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        using var response = await _admin.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title = "EFH Auslagerung" });
        _bauakteId = (await response.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options))!.Id;
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _storage.DisposeAsync();
        await _factory.DisposeAsync();
        Directory.Delete(_status, recursive: true);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(OffloadCrypto.ChunkSize)]
    [InlineData(OffloadCrypto.ChunkSize + 1)]
    [InlineData((3 * OffloadCrypto.ChunkSize) - 7)]
    public async Task Crypto_RoundTrips_AndDetectsTamperingAndTruncation(int length)
    {
        var plain = RandomNumberGenerator.GetBytes(length);
        var master = RandomNumberGenerator.GetBytes(32);
        var id = Guid.NewGuid();
        var key = OffloadCrypto.DeriveKey(master, id);
        using var cipher = new MemoryStream();
        await OffloadCrypto.EncryptAsync(new MemoryStream(plain), cipher, key, RandomNumberGenerator.GetBytes(8), CancellationToken.None);
        cipher.Length.ShouldBe(OffloadCrypto.CipherLength(length));

        using var back = new MemoryStream();
        await OffloadCrypto.DecryptAsync(new MemoryStream(cipher.ToArray()), back, key, CancellationToken.None);
        back.ToArray().ShouldBe(plain);

        // Falscher Schlüssel (andere Objekt-ID), ein gekipptes Bit, abgeschnittener letzter Block: alles fällt auf.
        await Should.ThrowAsync<CryptographicException>(() => OffloadCrypto.DecryptAsync(new MemoryStream(cipher.ToArray()), Stream.Null,
            OffloadCrypto.DeriveKey(master, Guid.NewGuid()), CancellationToken.None));
        var flipped = cipher.ToArray();
        flipped[^1] ^= 0x01;
        await Should.ThrowAsync<CryptographicException>(() => OffloadCrypto.DecryptAsync(new MemoryStream(flipped), Stream.Null, key, CancellationToken.None));
        if (length > OffloadCrypto.ChunkSize)
        {
            var truncated = cipher.ToArray()[..(OffloadCrypto.HeaderSize + OffloadCrypto.ChunkSize + 16)];
            await Should.ThrowAsync<CryptographicException>(() => OffloadCrypto.DecryptAsync(new MemoryStream(truncated), Stream.Null, key, CancellationToken.None));
        }
    }

    [Fact]
    public async Task Offload_DeletesLocallyOnlyAfterVerifiedUploadAndBackupCopy_ThenServesFromStorage_AndRecalls()
    {
        var (fileId, content) = await UploadDocumentAsync("Aufmass-alt.pdf");
        var path = await LocalPathAsync(fileId);
        File.Exists(path).ShouldBeTrue();

        // Ohne Kopplung: klare Meldung.
        using (var notPaired = await _admin.PostAsJsonAsync("/api/v1/files/offload/run", new { }))
        {
            notPaired.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        await PairAsync();
        await ConfigureAsync(minAgeMonths: 0);
        (await OverviewAsync()).GetProperty("counts").GetProperty("candidateFiles").GetInt32().ShouldBe(1);

        using (var run = await _admin.PostAsJsonAsync("/api/v1/files/offload/run", new { bauakteId = _bauakteId }))
        {
            run.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        await TickAsync();

        // Übertragen und geprüft, aber noch nicht gesichert: lokal bleibt alles, Sicherungskopie ist angefordert.
        var blob = await BlobAsync(fileId);
        blob.State.ShouldBe(OffloadState.Pending);
        File.Exists(path).ShouldBeTrue();
        var request = Path.Combine(_status, "requests", "offload-backup.list");
        var lines = await File.ReadAllLinesAsync(request);
        lines[0].ShouldBe($"id={blob.BackupRequestId}");
        lines.ShouldContain(blob.StorageKey);

        // Auf dem Storage liegt nur Chiffretext (kein Klartext, andere Prüfsumme).
        var stored = Directory.EnumerateFiles(Path.Combine(_storage.Options.DataPath, "offload"), "*", SearchOption.AllDirectories)
            .Single(f => Path.GetFileName(f) == blob.ObjectId.ToString("N"));
        var cipher = await File.ReadAllBytesAsync(stored);
        Encoding.Latin1.GetString(cipher).ShouldNotContain("Aufmass-alt.pdf");
        Convert.ToHexStringLower(SHA256.HashData(cipher)).ShouldBe(blob.CipherSha256);

        // Sicherungskopie fehlgeschlagen → bleibt lokal, wird erneut angefordert.
        File.Delete(request);
        await WriteBackupResultAsync(blob.BackupRequestId!, "failed", "Ziel nicht erreichbar");
        await TickAsync();
        File.Exists(path).ShouldBeTrue();
        blob = await BlobAsync(fileId);
        blob.State.ShouldBe(OffloadState.Pending);
        File.Exists(request).ShouldBeTrue();
        blob.BackupRequestId.ShouldNotBeNull();

        // Backup-Dienst bestätigt → jetzt erst lokal gelöscht.
        File.Delete(request);
        await WriteBackupResultAsync(blob.BackupRequestId!, "succeeded", "In allen Zielen gesichert");
        await TickAsync();
        (await BlobAsync(fileId)).State.ShouldBe(OffloadState.Offloaded);
        File.Exists(path).ShouldBeFalse();

        // Abruf: transparent vom Storage, Inhalt bitgenau, Hinweis im Kopf; die Liste der Orte nennt die Datei.
        using (var download = await _admin.GetAsync(new Uri($"/api/v1/files/{fileId}/content", UriKind.Relative)))
        {
            download.StatusCode.ShouldBe(HttpStatusCode.OK);
            download.Headers.GetValues("X-EstrichOne-Storage").ShouldContain("offload");
            (await download.Content.ReadAsByteArrayAsync()).ShouldBe(content);
        }

        var locations = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/files/offload/locations?ids={fileId}", Json);
        locations.GetArrayLength().ShouldBe(1);
        var overview = await OverviewAsync();
        overview.GetProperty("counts").GetProperty("offloadedFiles").GetInt32().ShouldBe(1);
        // „ok“ – oder „warning“ nur, weil die Platte des Testrechners knapp ist (nie wegen der Auslagerung selbst).
        var line = overview.GetProperty("status");
        if (line.GetProperty("level").GetString() != "ok")
        {
            line.GetProperty("text").GetString()!.ShouldStartWith("Speicher knapp");
        }
        overview.GetProperty("server").GetProperty("offload").GetProperty("objects").GetInt32().ShouldBe(1);

        // Entkoppeln ist gesperrt, solange etwas ausgelagert ist.
        using (var unpair = await _admin.DeleteAsync(new Uri("/api/v1/files/offload/pairing", UriKind.Relative)))
        {
            unpair.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        // Rückholen der Bauakte: wieder lokal (Prüfsumme geprüft), Objekt auf dem Storage im Papierkorb, Eintrag erledigt.
        using (var recall = await _admin.PostAsJsonAsync("/api/v1/files/offload/recall", new { bauakteId = _bauakteId }))
        {
            recall.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        await TickAsync();
        File.Exists(path).ShouldBeTrue();
        (await File.ReadAllBytesAsync(path)).ShouldBe(content);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            (await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<OffloadedBlob>().CountAsync()).ShouldBe(0);
        }

        var store = _storage.Services.GetRequiredService<OffloadStore>();
        store.Statistics(null).Objects.ShouldBe(0);
        store.Statistics(null).TrashObjects.ShouldBe(1);

        using (var unpair = await _admin.DeleteAsync(new Uri("/api/v1/files/offload/pairing", UriKind.Relative)))
        {
            unpair.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }
    }

    [Fact]
    public async Task StorageDown_GivesClearMessage_AndAdminOnlyBanner_NothingLost()
    {
        var (fileId, content) = await OffloadOneAsync();
        _factory.Services.GetRequiredService<OffloadCache>().Clear();
        await _storage.StopAsync();

        using (var download = await _admin.GetAsync(new Uri($"/api/v1/files/{fileId}/content", UriKind.Relative)))
        {
            download.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            var problem = await download.Content.ReadAsStringAsync();
            problem.ShouldContain("OFFLOAD_UNAVAILABLE");
            problem.ShouldContain("Nichts ist verloren");
        }

        var health = await _factory.Services.GetRequiredService<SystemHealthMonitor>().RefreshAsync(notify: false, CancellationToken.None);
        health.Banners.ShouldContain(b => b.Key == "offload" && b.Severity == "warning" && b.AdminOnly);
        var adminBanner = await _admin.GetFromJsonAsync<JsonElement>("/api/v1/system/health-banner", Json);
        adminBanner.GetProperty("banners").EnumerateArray().ShouldContain(b => b.GetProperty("key").GetString() == "offload");
        using var worker = await _factory.CreateClientAsAsync("monteur", StandardRoles.SiteWorkerId);
        var workerBanner = await worker.GetFromJsonAsync<JsonElement>("/api/v1/system/health-banner", Json);
        workerBanner.GetProperty("banners").EnumerateArray().ShouldNotContain(b => b.GetProperty("key").GetString() == "offload");
        var down = (await OverviewAsync()).GetProperty("status");
        down.GetProperty("level").GetString().ShouldBe("warning");
        down.GetProperty("text").GetString()!.ShouldContain("nicht erreichbar");

        // Storage wieder da (gleiche Daten, gleiche Kopplung): Datei wieder abrufbar.
        await _storage.StartAgainAsync();
        using var again = await _admin.GetAsync(new Uri($"/api/v1/files/{fileId}/content", UriKind.Relative));
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await again.Content.ReadAsByteArrayAsync()).ShouldBe(content);
    }

    [Fact]
    public async Task TamperedObject_IsRejected_LostObject_IsRecoveredFromBackup()
    {
        var (fileId, content) = await OffloadOneAsync();
        var blob = await BlobAsync(fileId);
        _factory.Services.GetRequiredService<OffloadCache>().Clear();
        var stored = Directory.EnumerateFiles(Path.Combine(_storage.Options.DataPath, "offload"), blob.ObjectId.ToString("N"), SearchOption.AllDirectories).Single();
        var bytes = await File.ReadAllBytesAsync(stored);
        bytes[40] ^= 0xFF;
        await File.WriteAllBytesAsync(stored, bytes);

        using (var download = await _admin.GetAsync(new Uri($"/api/v1/files/{fileId}/content", UriKind.Relative)))
        {
            download.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
            (await download.Content.ReadAsStringAsync()).ShouldContain("beschädigt");
        }

        (await _storage.Services.GetRequiredService<OffloadStore>().VerifyAsync(CancellationToken.None)).Ok.ShouldBeFalse();

        // Objekt verschwindet ganz → Abgleich meldet „fehlt“, Rückholen aus der Sicherung wird beim Backup-Dienst angefordert.
        File.Delete(stored);
        File.Delete(stored + ".json");
        using (var reconcile = await _admin.PostAsync(new Uri("/api/v1/files/offload/reconcile", UriKind.Relative), null))
        {
            reconcile.EnsureSuccessStatusCode();
        }

        (await BlobAsync(fileId)).State.ShouldBe(OffloadState.Missing);
        (await OverviewAsync()).GetProperty("status").GetProperty("level").GetString().ShouldBe("critical");
        using (var recover = await _admin.PostAsync(new Uri("/api/v1/files/offload/recover-from-backup", UriKind.Relative), null))
        {
            recover.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var lines = await File.ReadAllLinesAsync(Path.Combine(_status, "requests", "offload-recover.list"));
        lines.ShouldContain(blob.StorageKey);

        // Backup-Dienst spielt das Original zurück (hier: Inhalt aus der „Sicherung“) → Abgleich: wieder lokal, Eintrag erledigt.
        var path = await LocalPathAsync(fileId);
        await File.WriteAllBytesAsync(path, content);
        using (var reconcile = await _admin.PostAsync(new Uri("/api/v1/files/offload/reconcile", UriKind.Relative), null))
        {
            reconcile.EnsureSuccessStatusCode();
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<OffloadedBlob>().CountAsync()).ShouldBe(0);
        using var download2 = await _admin.GetAsync(new Uri($"/api/v1/files/{fileId}/content", UriKind.Relative));
        (await download2.Content.ReadAsByteArrayAsync()).ShouldBe(content);
    }

    [Fact]
    public async Task Rules_LowSpaceThreshold_AndValidation()
    {
        await PairAsync();
        var (fileId, _) = await UploadDocumentAsync("Neu.pdf");

        // Regel „älter als 6 Monate“: frische Datei ist kein Kandidat.
        await ConfigureAsync(minAgeMonths: 6);
        (await OverviewAsync()).GetProperty("counts").GetProperty("candidateFiles").GetInt32().ShouldBe(0);
        await using (var age = _factory.Services.CreateAsyncScope())
        {
            await age.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<FileRecord>().Where(f => f.Id == fileId)
                .ExecuteUpdateAsync(u => u.SetProperty(f => f.CreatedAt, DateTimeOffset.UtcNow.AddDays(-200)));
        }

        (await OverviewAsync()).GetProperty("counts").GetProperty("candidateFiles").GetInt32().ShouldBe(1);

        // Nur abgeschlossene Bauakten: offene Bauakte zählt nicht.
        await ConfigureAsync(minAgeMonths: 6, onlyCompleted: true);
        (await OverviewAsync()).GetProperty("counts").GetProperty("candidateFiles").GetInt32().ShouldBe(0);

        // Schwelle: 99 % ist ungültig; knapp = weniger als Schwelle frei, Ziel = Schwelle + 5 %.
        using (var invalid = await _admin.PutAsJsonAsync("/api/v1/files/offload/settings", Settings(thresholdPercent: 99)))
        {
            invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<OffloadService>();
        var settings = await service.ConnectionAsync(CancellationToken.None);
        OffloadService.IsLow(settings, 10, 100).ShouldBeTrue();
        OffloadService.IsLow(settings, 20, 100).ShouldBeFalse();
        OffloadService.BytesToFree(settings, 10, 100).ShouldBe(10);
        _ = fileId;
    }

    // ------------------------------------------------------------------------------------------------------------------

    private async Task<(Guid FileId, byte[] Content)> OffloadOneAsync()
    {
        var uploaded = await UploadDocumentAsync($"Plan-{Guid.NewGuid():N}.pdf");
        await PairAsync();
        await ConfigureAsync(minAgeMonths: 0, requireBackup: false);
        using (var run = await _admin.PostAsJsonAsync("/api/v1/files/offload/run", new { }))
        {
            run.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        await TickAsync();
        (await BlobAsync(uploaded.Id)).State.ShouldBe(OffloadState.Offloaded);
        File.Exists(await LocalPathAsync(uploaded.Id)).ShouldBeFalse();
        return uploaded;
    }

    private async Task PairAsync()
    {
        using var paired = await _admin.PostAsJsonAsync("/api/v1/files/offload/pair", new { address = _storage.Address, code = _storage.NewCode() });
        paired.StatusCode.ShouldBe(HttpStatusCode.OK, await paired.Content.ReadAsStringAsync());
    }

    private static object Settings(int thresholdPercent = 15, int minAgeMonths = 0, bool onlyCompleted = false, bool requireBackup = true) => new
    {
        autoWhenLow = true, autoByRules = false, thresholdPercent, thresholdFreeGb = (int?)null, minAgeMonths, onlyCompletedBauakten = onlyCompleted,
        includePhotos = true, includeVideos = true, includeDocuments = true, requireBackup, cacheLimitMb = 500,
    };

    private async Task ConfigureAsync(int minAgeMonths, bool onlyCompleted = false, bool requireBackup = true)
    {
        using var response = await _admin.PutAsJsonAsync("/api/v1/files/offload/settings",
            Settings(minAgeMonths: minAgeMonths, onlyCompleted: onlyCompleted, requireBackup: requireBackup));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<JsonElement> OverviewAsync() => await _admin.GetFromJsonAsync<JsonElement>("/api/v1/files/offload", Json);

    private Task TickAsync() => _factory.Services.GetRequiredService<OffloadWorker>().TickAsync(CancellationToken.None);

    private async Task WriteBackupResultAsync(string id, string status, string message) =>
        await File.WriteAllTextAsync(Path.Combine(_status, "offload-backup.json"),
            $"{{\"requestId\":\"{id}\",\"status\":\"{status}\",\"message\":\"{message}\",\"at\":\"{DateTimeOffset.UtcNow:O}\",\"files\":1}}");

    private async Task<OffloadedBlob> BlobAsync(Guid fileId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var key = await db.Set<FileRecord>().Where(f => f.Id == fileId).Select(f => f.StorageKey).SingleAsync();
        return await db.Set<OffloadedBlob>().AsNoTracking().SingleAsync(b => b.StorageKey == key);
    }

    private async Task<string> LocalPathAsync(Guid fileId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var key = await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<FileRecord>().Where(f => f.Id == fileId)
            .Select(f => f.StorageKey).SingleAsync();
        return scope.ServiceProvider.GetRequiredService<LocalFileStorage>().PathOf(key!);
    }

    private async Task<(Guid Id, byte[] Content)> UploadDocumentAsync(string name)
    {
        var content = Encoding.UTF8.GetBytes($"%PDF-1.4\n% {name} {Guid.NewGuid()}\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n");
        var id = Guid.CreateVersion7();
        using var announce = await _admin.PostAsJsonAsync("/api/v1/files", new
        {
            id,
            bauakteId = _bauakteId,
            kind = "document",
            originalName = name,
            contentType = "application/pdf",
            sizeBytes = (long)content.Length,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
            category = "plan",
        });
        announce.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var upload = await _admin.PutAsync(new Uri($"/api/v1/files/{id}/content", UriKind.Relative), body);
        upload.EnsureSuccessStatusCode();
        await using var scope = _factory.Services.CreateAsyncScope();
        var key = await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<FileRecord>().Where(f => f.Id == id).Select(f => f.StorageKey).SingleAsync();
        return (id, await File.ReadAllBytesAsync(scope.ServiceProvider.GetRequiredService<LocalFileStorage>().PathOf(key!)));
    }
}
