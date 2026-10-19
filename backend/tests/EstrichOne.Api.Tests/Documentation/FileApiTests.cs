using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Documentation;
using EstrichOne.Api.Tests.Support;
using NetVips;

namespace EstrichOne.Api.Tests.Documentation;

public sealed class FileApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _worker = null!;
    private Guid _bauakteId;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres);
        _worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var response = await _worker.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title = "EFH Fotos" });
        _bauakteId = (await response.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options))!.Id;
    }

    public async ValueTask DisposeAsync()
    {
        _worker.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Photo_UploadedInOnePiece_IsStoredAndGetsVariants()
    {
        var jpeg = CreateJpeg(3000, 2000);
        var id = await AnnounceAsync(jpeg, FileKind.Photo, "IMG_0001.jpg");

        using var upload = await UploadAsync(id, jpeg);
        upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await upload.Content.ReadFromJsonAsync<FileResponse>(TestJson.Options, TestContext.Current.CancellationToken))!.State.ShouldBe(FileState.Stored);

        var original = await _worker.GetByteArrayAsync(new Uri($"/api/v1/files/{id}/content", UriKind.Relative), TestContext.Current.CancellationToken);
        original.ShouldBe(jpeg);

        var thumb = await WaitForVariantAsync(id, "thumb");
        using var image = Image.NewFromBuffer(thumb);
        Math.Max(image.Width, image.Height).ShouldBe(400);
        image.GetFields().ShouldNotContain("exif-data", "Varianten enthalten keine Metadaten (z. B. GPS).");
    }

    [Fact]
    public async Task Announce_IsIdempotent_ButConflictsOnDifferentContent()
    {
        var jpeg = CreateJpeg(100, 100);
        var id = Guid.CreateVersion7();

        using var first = await AnnounceRawAsync(id, jpeg, FileKind.Photo);
        using var repeat = await AnnounceRawAsync(id, jpeg, FileKind.Photo);
        using var conflict = await AnnounceRawAsync(id, CreateJpeg(120, 100), FileKind.Photo);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        repeat.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await conflict.ReadProblemAsync()).Code.ShouldBe("ID_CONFLICT");
    }

    [Fact]
    public async Task Upload_WithWrongContent_IsRejectedAndCanBeRetried()
    {
        var jpeg = CreateJpeg(200, 200);
        var id = await AnnounceAsync(jpeg, FileKind.Photo, "a.jpg");
        var corrupted = jpeg.ToArray();
        corrupted[^10] ^= 0xFF;

        using var rejected = await UploadAsync(id, corrupted);
        (await rejected.ReadProblemAsync()).Code.ShouldBe("CHECKSUM_MISMATCH");

        using var retried = await UploadAsync(id, jpeg);
        retried.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Upload_WithDisguisedContent_IsRejected()
    {
        var html = "<html><script>alert(1)</script></html>"u8.ToArray();
        var id = await AnnounceAsync(html, FileKind.Photo, "foto.jpg", contentType: "image/jpeg");

        using var response = await UploadAsync(id, html);

        (await response.ReadProblemAsync()).Code.ShouldBe("FILE_TYPE_NOT_ALLOWED");
    }

    [Fact]
    public async Task Upload_InChunks_CanResumeAfterInterruption()
    {
        var video = CreateFakeMp4(1_000_000);
        var id = await AnnounceAsync(video, FileKind.Video, "baustelle.mp4", contentType: "video/mp4");

        using var part1 = await UploadAsync(id, video[..400_000], from: 0, total: video.Length);
        part1.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        // Verbindung abgebrochen – App fragt nach dem Stand und setzt fort.
        var status = await _worker.GetFromJsonAsync<UploadStatus>($"/api/v1/files/{id}/upload-status", TestJson.Options, TestContext.Current.CancellationToken);
        status!.ReceivedBytes.ShouldBe(400_000);

        using var wrongOffset = await UploadAsync(id, video[300_000..], from: 300_000, total: video.Length);
        wrongOffset.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using var part2 = await UploadAsync(id, video[400_000..], from: 400_000, total: video.Length);
        part2.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/files/{id}/content");
        request.Headers.Range = new RangeHeaderValue(0, 99);
        using var range = await _worker.SendAsync(request, TestContext.Current.CancellationToken);
        range.StatusCode.ShouldBe(HttpStatusCode.PartialContent);
        (await range.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBe(video[..100]);
    }

    [Fact]
    public async Task Files_AreVisibleOnlyWithAccessToTheBauakte()
    {
        var jpeg = CreateJpeg(100, 100);
        var id = await AnnounceAsync(jpeg, FileKind.Photo, "x.jpg");
        (await UploadAsync(id, jpeg)).EnsureSuccessStatusCode();
        using var colleague = await _factory.CreateClientAsAsync("kollege", StandardRoles.SiteWorkerId);
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);

        (await colleague.GetAsync(new Uri($"/api/v1/files/{id}/content", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await office.GetAsync(new Uri($"/api/v1/files/{id}/content", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await office.GetFromJsonAsync<List<FileResponse>>($"/api/v1/bauakten/{_bauakteId}/files", TestJson.Options, TestContext.Current.CancellationToken);
        list.ShouldNotBeNull();
        list.ShouldHaveSingleItem().Id.ShouldBe(id);

        using var foreignAnnounce = await colleague.PostAsJsonAsync("/api/v1/files", Announcement(Guid.CreateVersion7(), jpeg, FileKind.Photo, "y.jpg", "image/jpeg"), TestContext.Current.CancellationToken);
        (await foreignAnnounce.ReadProblemAsync()).Code.ShouldBe("BAUAKTE_NOT_FOUND");

        (await office.DeleteAsync(new Uri($"/api/v1/files/{id}", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await office.GetAsync(new Uri($"/api/v1/files/{id}", UriKind.Relative), TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Announce_LargerThanAllowed_IsRejected()
    {
        using var response = await _worker.PostAsJsonAsync("/api/v1/files", new
        {
            id = Guid.CreateVersion7(), bauakteId = _bauakteId, kind = "photo", originalName = "riesig.jpg",
            contentType = "image/jpeg", sizeBytes = 60L * 1024 * 1024, sha256 = new string('a', 64),
        }, TestContext.Current.CancellationToken);

        (await response.ReadProblemAsync()).Code.ShouldBe("FILE_TOO_LARGE");
    }

    private async Task<byte[]> WaitForVariantAsync(Guid id, string variant)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            using var response = await _worker.GetAsync(new Uri($"/api/v1/files/{id}/content?variant={variant}", UriKind.Relative));
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsByteArrayAsync();
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("Variante wurde nicht erzeugt.");
    }

    private async Task<Guid> AnnounceAsync(byte[] content, FileKind kind, string name, string contentType = "image/jpeg")
    {
        var id = Guid.CreateVersion7();
        using var response = await _worker.PostAsJsonAsync("/api/v1/files", Announcement(id, content, kind, name, contentType));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return id;
    }

    private Task<HttpResponseMessage> AnnounceRawAsync(Guid id, byte[] content, FileKind kind) =>
        _worker.PostAsJsonAsync("/api/v1/files", Announcement(id, content, kind, "x.jpg", "image/jpeg"));

    private object Announcement(Guid id, byte[] content, FileKind kind, string name, string contentType) => new
    {
        id,
        bauakteId = _bauakteId,
        kind = kind.ToString().ToLowerInvariant(),
        originalName = name,
        contentType,
        sizeBytes = (long)content.Length,
        sha256 = Convert.ToHexStringLower(SHA256.HashData(content)),
        capturedAt = DateTimeOffset.UtcNow,
    };

    private Task<HttpResponseMessage> UploadAsync(Guid id, byte[] content, long? from = null, long? total = null)
    {
        var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        if (from is not null)
        {
            body.Headers.ContentRange = new ContentRangeHeaderValue(from.Value, from.Value + content.Length - 1, total!.Value);
        }

        return _worker.PutAsync(new Uri($"/api/v1/files/{id}/content", UriKind.Relative), body);
    }

    private static byte[] CreateJpeg(int width, int height)
    {
        using var image = (Image.Black(width, height, bands: 3) + new[] { 120.0, 80.0, (width % 200) + 20.0 }).Cast(Enums.BandFormat.Uchar);
        return image.JpegsaveBuffer(q: 85);
    }

    /// <summary>MP4-Kopf (ftyp isom) mit zufälligem Rest – genügt für Typprüfung, Teil-Upload und Range-Abruf.</summary>
    private static byte[] CreateFakeMp4(int size)
    {
        var data = RandomNumberGenerator.GetBytes(size);
        byte[] header = [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m'];
        header.CopyTo(data, 0);
        return data;
    }
}
