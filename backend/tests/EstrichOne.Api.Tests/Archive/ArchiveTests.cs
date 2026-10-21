// Integrationstests laufen unter Linux (CI); kurze Aufrufe ohne Abbruch-Token.
#pragma warning disable xUnit1051, CA1416
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Archive;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Archive;

/// <summary>Suite ↔ echter Archivserver über HTTPS: Kopplung, automatische Archivierung, Unveränderbarkeit, Löschsperre, Prüfung, Export.</summary>
public sealed class ArchiveTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private ArchiveServerHost _archive = null!;
    private HttpClient _admin = null!;
    private Guid _bauakteId;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string> { ["EstrichOne:Archive:Worker"] = "false" });
        _archive = await ArchiveServerHost.StartAsync();
        _admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        using var response = await _admin.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title = "EFH Archiv" });
        _bauakteId = (await response.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options))!.Id;
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _archive.DisposeAsync();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Pairing_WithWrongCode_IsRejected_WithRightCode_PinsCertificate()
    {
        var before = (await _admin.GetFromJsonAsync<ArchiveOverview>("/api/v1/archive/status", TestJson.Options))!;
        before.Connected.ShouldBeFalse();
        (await _admin.GetFromJsonAsync<List<ArchiveRuleDto>>("/api/v1/archive/rules", TestJson.Options))!.Count.ShouldBe(12);
        var code = _archive.NewCode();
        using var wrong = await _admin.PostAsJsonAsync("/api/v1/archive/pair", new { address = _archive.Address, code = "AAAA-BBBB-CCCC" });
        wrong.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await wrong.Content.ReadAsStringAsync()).ShouldContain("PEER_CODE_WRONG");

        using var paired = await _admin.PostAsJsonAsync("/api/v1/archive/pair", new { address = _archive.Address, code });
        paired.StatusCode.ShouldBe(HttpStatusCode.OK, await paired.Content.ReadAsStringAsync());
        var connection = (await paired.Content.ReadFromJsonAsync<ArchiveConnectionDto>(TestJson.Options))!;
        connection.ServerName.ShouldBe("Archiv Test");
        connection.CaFingerprint.Length.ShouldBe(64);

        // Code ist verbraucht.
        using var again = await _admin.PostAsJsonAsync("/api/v1/archive/pair", new { address = _archive.Address, code });
        (await again.Content.ReadAsStringAsync()).ShouldContain("PEER_PAIRING_CLOSED");

        var status = (await _admin.GetFromJsonAsync<ArchiveOverview>("/api/v1/archive/status", TestJson.Options))!;
        status.Connected.ShouldBeTrue();
        status.Server!.Name.ShouldBe("Archiv Test");

        // Token liegt nur verschlüsselt in der Datenbank.
        await using var scope = _factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<ArchiveConnection>().SingleAsync();
        stored.ProtectedToken.ShouldStartWith("CfDJ8");
    }

    [Fact]
    public async Task Invoice_IsArchivedAutomatically_Immutable_AndProtectedUntilRetentionEnds()
    {
        await PairAsync();
        var (fileId, content) = await UploadDocumentAsync("invoice", "Rechnung-4711.pdf");

        await RunArchiveAsync();

        var record = (await _admin.GetFromJsonAsync<ArchiveRecordDto>($"/api/v1/archive/files/{fileId}", TestJson.Options))!;
        record.Status.ShouldBe(ArchiveRecordStatus.Archived);
        record.RetentionYears.ShouldBe(10);
        record.RetainUntil.ShouldBe(new DateOnly(DateTime.UtcNow.Year + 10, 12, 31));
        record.LedgerHash!.Length.ShouldBe(64);
        record.Sha256.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(content)));

        var bauakte = (await _admin.GetFromJsonAsync<List<ArchiveRecordDto>>($"/api/v1/archive/bauakten/{_bauakteId}", TestJson.Options))!;
        bauakte.ShouldContain(r => r.FileId == fileId);

        using var http = await ArchiveClientAsync();
        // Inhalt im Archiv identisch
        (await http.GetByteArrayAsync(new Uri($"/api/v1/objects/{fileId}", UriKind.Relative))).ShouldBe(content);

        // Unveränderbar: Überschreiben und abweichender Inhalt unter derselben ID werden abgelehnt.
        using var put = await http.PutAsync(new Uri($"/api/v1/objects/{fileId}", UriKind.Relative), new ByteArrayContent([1, 2, 3]));
        put.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var other = Encoding.UTF8.GetBytes("%PDF-1.4 gefälscht");
        using var overwrite = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/objects/{fileId}")
        {
            Content = new ByteArrayContent(other),
        };
        overwrite.Headers.Add("X-Archive-Sha256", Convert.ToHexStringLower(SHA256.HashData(other)));
        overwrite.Headers.Add("X-Archive-Metadata", Convert.ToBase64String(Encoding.UTF8.GetBytes("""{"name":"x.pdf","retentionYears":10}""")));
        using var overwritten = await http.SendAsync(overwrite);
        overwritten.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await overwritten.Content.ReadAsStringAsync()).ShouldContain("ARCHIVE_IMMUTABLE");

        // Löschsperre bis Fristende
        using var delete = await http.DeleteAsync(new Uri($"/api/v1/objects/{fileId}", UriKind.Relative));
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await delete.Content.ReadAsStringAsync()).ShouldContain("ARCHIVE_RETENTION_ACTIVE");

        // Prüfprotokoll: in Ordnung …
        using var verify = await _admin.PostAsync(new Uri("/api/v1/archive/verify", UriKind.Relative), null);
        var report = (await verify.Content.ReadFromJsonAsync<ArchiveVerification>(TestJson.Options))!;
        report.Ok.ShouldBeTrue();
        report.ObjectsChecked.ShouldBe(1);

        // … Export für den Betriebsprüfer enthält Dokument, Journal, Prüfsummen und Prüfskript.
        await using (var zip = await _admin.GetStreamAsync(new Uri("/api/v1/archive/export", UriKind.Relative)))
        {
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
            archive.Entries.Select(e => e.FullName).ShouldContain($"dokumente/{fileId}_Rechnung-4711.pdf");
            archive.Entries.Select(e => e.FullName).ShouldContain("journal.jsonl");
            archive.Entries.Select(e => e.FullName).ShouldContain("journal_pruefen.py");
            using var sums = new StreamReader(archive.GetEntry("sha256sums.txt")!.Open());
            (await sums.ReadToEndAsync()).ShouldContain(record.Sha256);
        }

        // … und erkennt eine Manipulation auf der Festplatte des Archivrechners.
        var file = Directory.EnumerateFiles(Path.Combine(_archive.Options.DataPath, "objects"), $"{fileId}.bin", SearchOption.AllDirectories).Single();
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await File.WriteAllBytesAsync(file, other);
        using var verifyAgain = await _admin.PostAsync(new Uri("/api/v1/archive/verify", UriKind.Relative), null);
        var broken = (await verifyAgain.Content.ReadFromJsonAsync<ArchiveVerification>(TestJson.Options))!;
        broken.Ok.ShouldBeFalse();
        broken.Errors.ShouldContain(e => e.Contains("Inhalt verändert", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rules_ControlWhatIsArchived_ManualArchivingOfBauakte_Works()
    {
        await PairAsync();
        var (protocolId, _) = await UploadDocumentAsync("protocol", "Abnahme.pdf");
        await RunArchiveAsync();
        (await RecordAsync(protocolId)).ShouldBeNull(); // Protokolle erst bei Abschluss der Bauakte

        // Regel ändern: Protokolle sofort, 12 Jahre
        var rules = (await _admin.GetFromJsonAsync<List<ArchiveRuleDto>>("/api/v1/archive/rules", TestJson.Options))!;
        var changed = rules.Select(r => r.DocumentType == "protocol" ? r with { Trigger = ArchiveTrigger.OnUpload, RetentionYears = 12 } : r).ToList();
        using var put = await _admin.PutAsJsonAsync("/api/v1/archive/rules", changed, TestJson.Options);
        put.StatusCode.ShouldBe(HttpStatusCode.OK);
        await RunArchiveAsync();
        var protocol = await RecordAsync(protocolId);
        protocol!.Status.ShouldBe(ArchiveRecordStatus.Archived);
        protocol.RetentionYears.ShouldBe(12);

        // Manuell: ganze Bauakte (auch Dokumentarten ohne aktive Regel)
        var (otherId, _) = await UploadDocumentAsync("other", "Notiz.pdf");
        using var queue = await _admin.PostAsync(new Uri($"/api/v1/archive/bauakten/{_bauakteId}", UriKind.Relative), null);
        (await queue.Content.ReadFromJsonAsync<QueueResult>(TestJson.Options))!.Queued.ShouldBe(1);
        await RunArchiveAsync();
        (await RecordAsync(otherId))!.Status.ShouldBe(ArchiveRecordStatus.Archived);
    }

    [Fact]
    public async Task UnreachableArchive_MarksRecordForRetry()
    {
        await PairAsync();
        var (fileId, _) = await UploadDocumentAsync("invoice", "R-1.pdf");
        await _archive.DisposeAsync();
        _archive = await ArchiveServerHost.StartAsync(); // anderer Port, alte Adresse nicht mehr erreichbar

        await RunArchiveAsync();

        var record = await RecordAsync(fileId);
        record!.Status.ShouldBe(ArchiveRecordStatus.Pending);
        record.Attempts.ShouldBe(1);
        record.LastError.ShouldNotBeNull();
        using var retry = await _admin.PostAsync(new Uri($"/api/v1/archive/records/{record.Id}/retry", UriKind.Relative), null);
        retry.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task PairAsync()
    {
        using var paired = await _admin.PostAsJsonAsync("/api/v1/archive/pair", new { address = _archive.Address, code = _archive.NewCode() });
        paired.EnsureSuccessStatusCode();
    }

    private async Task<ArchiveRecordDto?> RecordAsync(Guid fileId)
    {
        using var response = await _admin.GetAsync(new Uri($"/api/v1/archive/files/{fileId}", UriKind.Relative));
        return response.StatusCode == HttpStatusCode.NoContent ? null : await response.Content.ReadFromJsonAsync<ArchiveRecordDto>(TestJson.Options);
    }

    private async Task RunArchiveAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ArchiveService>();
        await service.EnqueueByRulesAsync(CancellationToken.None);
        await service.ProcessDueAsync(CancellationToken.None);
    }

    private async Task<HttpClient> ArchiveClientAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ArchiveService>();
        var connection = await service.ConnectionAsync(CancellationToken.None);
        return service.Client(connection!, TimeSpan.FromSeconds(30));
    }

    private async Task<(Guid Id, byte[] Content)> UploadDocumentAsync(string category, string name)
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
            category,
        });
        announce.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var upload = await _admin.PutAsync(new Uri($"/api/v1/files/{id}/content", UriKind.Relative), body);
        upload.EnsureSuccessStatusCode();
        return (id, content);
    }
}
