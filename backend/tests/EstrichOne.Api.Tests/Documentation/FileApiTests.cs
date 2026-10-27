using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Documentation;
using EstrichOne.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
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
        var stored = (await _worker.GetFromJsonAsync<FileResponse>($"/api/v1/files/{id}", TestJson.Options, TestContext.Current.CancellationToken))!;
        Convert.ToHexStringLower(SHA256.HashData(original)).ShouldBe(stored.Sha256, "Prüfsumme beschreibt den gespeicherten (ggf. verdichteten) Inhalt.");
        stored.SizeBytes.ShouldBe(original.Length);
        using (var check = Image.NewFromBuffer(original))
        {
            (check.Width, check.Height).ShouldBe((3000, 2000));
        }

        var thumb = await WaitForVariantAsync(id, "thumb");
        using var image = Image.NewFromBuffer(thumb);
        Math.Max(image.Width, image.Height).ShouldBe(320);
        using var large = Image.NewFromBuffer(await WaitForVariantAsync(id, "large"));
        Math.Max(large.Width, large.Height).ShouldBe(2560);
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
    public async Task Document_CanBeRenamedFiledLinkedAndVersioned()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var pdf = "%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n"u8.ToArray();
        var id = await AnnounceAsync(pdf, FileKind.Document, "scan001.pdf", contentType: "application/pdf");
        (await UploadAsync(id, pdf)).EnsureSuccessStatusCode();

        var newRoom = Guid.CreateVersion7();
        using var room = await office.PutAsJsonAsync($"/api/v1/bauakten/{_bauakteId}/rooms/{newRoom}", new
        {
            id = newRoom, name = "Bad", underfloorHeating = false, status = "planned",
        }, TestContext.Current.CancellationToken);
        room.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roomId = (await room.Content.ReadFromJsonAsync<RoomOverview>(TestJson.Options, TestContext.Current.CancellationToken))!.Rooms.Single().Id;

        using var updated = await office.PutAsJsonAsync($"/api/v1/files/{id}/details", new
        {
            originalName = "Grundriss EG.pdf", caption = "Stand Bauantrag", category = "plan", folder = " Pläne / EG ", roomId,
        }, TestContext.Current.CancellationToken);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var file = (await updated.Content.ReadFromJsonAsync<FileResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
        file.OriginalName.ShouldBe("Grundriss EG.pdf");
        file.Folder.ShouldBe("Pläne/EG");
        file.Category.ShouldBe("plan");
        file.RoomId.ShouldBe(roomId);

        using var foreignRoom = await office.PutAsJsonAsync($"/api/v1/files/{id}/details", new
        {
            originalName = "Grundriss EG.pdf", roomId = Guid.CreateVersion7(),
        }, TestContext.Current.CancellationToken);
        (await foreignRoom.ReadProblemAsync()).Code.ShouldBe("VALIDATION_FAILED");

        var second = Guid.CreateVersion7();
        using var version = await _worker.PostAsJsonAsync("/api/v1/files", new
        {
            id = second, bauakteId = _bauakteId, kind = "document", originalName = "Grundriss EG v2.pdf", contentType = "application/pdf",
            sizeBytes = (long)pdf.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(pdf)), previousVersionId = id,
        }, TestContext.Current.CancellationToken);
        version.StatusCode.ShouldBe(HttpStatusCode.Created);
        var next = (await version.Content.ReadFromJsonAsync<FileResponse>(TestJson.Options, TestContext.Current.CancellationToken))!;
        next.PreviousVersionId.ShouldBe(id);
        next.Folder.ShouldBe("Pläne/EG");
        next.Category.ShouldBe("plan");

        using var photoAsVersion = await _worker.PostAsJsonAsync("/api/v1/files", new
        {
            id = Guid.CreateVersion7(), bauakteId = _bauakteId, kind = "photo", originalName = "x.jpg", contentType = "image/jpeg",
            sizeBytes = 10L, sha256 = new string('a', 64), previousVersionId = id,
        }, TestContext.Current.CancellationToken);
        (await photoAsVersion.ReadProblemAsync()).Code.ShouldBe("VALIDATION_FAILED");
    }

    [Fact]
    public async Task SelectedFiles_CanBeDownloadedAsZip()
    {
        var first = CreateJpeg(60, 60);
        var second = CreateJpeg(80, 60);
        var a = await AnnounceAsync(first, FileKind.Photo, "foto.jpg");
        var b = await AnnounceAsync(second, FileKind.Photo, "foto.jpg");
        (await UploadAsync(a, first)).EnsureSuccessStatusCode();
        (await UploadAsync(b, second)).EnsureSuccessStatusCode();

        using var response = await _worker.GetAsync(new Uri($"/api/v1/bauakten/{_bauakteId}/files/archive?ids={a},{b}", UriKind.Relative),
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/zip");
        await using var stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var zip = new System.IO.Compression.ZipArchive(stream);
        zip.Entries.Select(e => e.FullName).Order().ShouldBe(["Fotos/foto (2).jpg", "Fotos/foto.jpg"]);

        using var other = await _factory.CreateClientAsAsync("fremd", StandardRoles.SiteWorkerId);
        (await other.GetAsync(new Uri($"/api/v1/bauakten/{_bauakteId}/files/archive?ids={a}", UriKind.Relative),
            TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InlineContent_MayBeFramedBySameOrigin_ApiOtherwiseNot()
    {
        var jpeg = CreateJpeg(50, 50);
        var id = await AnnounceAsync(jpeg, FileKind.Photo, "rahmen.jpg");
        (await UploadAsync(id, jpeg)).EnsureSuccessStatusCode();

        using var content = await _worker.GetAsync(new Uri($"/api/v1/files/{id}/content", UriKind.Relative), TestContext.Current.CancellationToken);
        content.Headers.GetValues("X-Frame-Options").ShouldHaveSingleItem().ShouldBe("SAMEORIGIN");
        using var meta = await _worker.GetAsync(new Uri($"/api/v1/files/{id}", UriKind.Relative), TestContext.Current.CancellationToken);
        meta.Headers.GetValues("X-Frame-Options").ShouldHaveSingleItem().ShouldBe("DENY");
    }

    [Fact]
    public async Task ConcurrentDetailChanges_AreRetried_NotServerErrors()
    {
        var jpeg = CreateJpeg(1200, 900);
        var id = await AnnounceAsync(jpeg, FileKind.Photo, "parallel.jpg");
        (await UploadAsync(id, jpeg)).EnsureSuccessStatusCode();

        // Gleichzeitig: Vorschaubilder entstehen im Hintergrund, mehrere Beschriftungen kommen parallel.
        var responses = await Task.WhenAll(Enumerable.Range(1, 8).Select(i => _worker.PutAsJsonAsync($"/api/v1/files/{id}/details",
            new { originalName = "parallel.jpg", caption = $"Fassung {i}" }, TestContext.Current.CancellationToken)));

        responses.Select(r => r.StatusCode).ShouldAllBe(status => status == HttpStatusCode.OK || status == HttpStatusCode.Conflict);
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).ShouldBeGreaterThan(0);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Theory]
    [InlineData("photo", 1)]
    [InlineData("Photo", 1)]
    [InlineData("document", 0)]
    [InlineData("Document", 0)]
    public async Task FileList_KindFilter_IgnoresCase(string kind, int expected)
    {
        var jpeg = CreateJpeg(40, 40);
        var id = await AnnounceAsync(jpeg, FileKind.Photo, "filter.jpg");
        (await UploadAsync(id, jpeg)).EnsureSuccessStatusCode();

        var list = await _worker.GetFromJsonAsync<List<FileResponse>>($"/api/v1/bauakten/{_bauakteId}/files?kind={kind}", TestJson.Options,
            TestContext.Current.CancellationToken);
        list!.Count.ShouldBe(expected);

        using var invalid = await _worker.GetAsync(new Uri($"/api/v1/bauakten/{_bauakteId}/files?kind=foo", UriKind.Relative),
            TestContext.Current.CancellationToken);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
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

    /// <summary>Kamera-Foto: groß, hohe Qualität, EXIF-Ausrichtung 6 (hochkant), Aufnahmezeit und GPS.</summary>
    private static byte[] CreateCameraJpeg(int width, int height)
    {
        using var noise = Image.Gaussnoise(width, height, sigma: 30, mean: 128);
        using var rgb = noise.Bandjoin((noise * 0.8) + 20, (noise * 0.6) + 40).Cast(Enums.BandFormat.Uchar);
        using var tagged = rgb.Mutate(m =>
        {
            m.Set(GValue.GIntType, "orientation", 6);
            m.Set(GValue.GStrType, "exif-ifd2-DateTimeOriginal", "2026:10:20 08:15:00");
            m.Set(GValue.GStrType, "exif-ifd3-GPSLatitudeRef", "N");
            m.Set(GValue.GStrType, "exif-ifd3-GPSLatitude", "48/1 32/1 1200/100");
        });
        return tagged.JpegsaveBuffer(q: 97, keep: Enums.ForeignKeep.Exif);
    }

    [Fact]
    public async Task CameraPhoto_IsStoredSmaller_Rotated_LimitedTo4096_WithExifKept_AndAnnounceStaysIdempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        var jpeg = CreateCameraJpeg(5200, 3000);
        var id = await AnnounceAsync(jpeg, FileKind.Photo, "IMG_4711.jpg");

        using var upload = await UploadAsync(id, jpeg);
        upload.StatusCode.ShouldBe(HttpStatusCode.OK, await upload.Content.ReadAsStringAsync(ct));
        var file = (await upload.Content.ReadFromJsonAsync<FileResponse>(TestJson.Options, ct))!;
        var stored = await _worker.GetByteArrayAsync(new Uri($"/api/v1/files/{id}/content", UriKind.Relative), ct);

        file.SizeBytes.ShouldBe(stored.Length);
        stored.Length.ShouldBeLessThan(jpeg.Length / 2);
        using var image = Image.NewFromBuffer(stored);
        (image.Width, image.Height).ShouldBe((2363, 4096), "EXIF-Ausrichtung angewendet, lange Kante begrenzt");
        image.Contains("orientation").ShouldBeTrue();
        ((int)image.Get("orientation")).ShouldBe(1);
        ((string)image.Get("exif-ifd2-DateTimeOriginal")).ShouldStartWith("2026:10:20 08:15:00");
        ((string)image.Get("exif-ifd3-GPSLatitudeRef")).ShouldStartWith("N");

        // Wiederholte Ankündigung mit dem Original (App nach Verbindungsabbruch) gilt weiter als dieselbe Datei.
        using var repeat = await AnnounceRawAsync(id, jpeg, FileKind.Photo);
        repeat.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SamePhotoTwice_SharesStorage()
    {
        var jpeg = CreateCameraJpeg(1200, 900);
        var first = await AnnounceAsync(jpeg, FileKind.Photo, "a.jpg");
        (await UploadAsync(first, jpeg)).EnsureSuccessStatusCode();
        var second = await AnnounceAsync(jpeg, FileKind.Photo, "a-kopie.jpg");
        (await UploadAsync(second, jpeg)).EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOne.Api.Infrastructure.Persistence.EstrichOneDbContext>();
        var keys = db.Set<FileRecord>().Where(f => f.Id == first || f.Id == second).Select(f => f.StorageKey).ToList();
        keys.Distinct().ShouldHaveSingleItem();
        (await _worker.GetByteArrayAsync(new Uri($"/api/v1/files/{second}/content", UriKind.Relative), TestContext.Current.CancellationToken)).Length
            .ShouldBeGreaterThan(0);
    }

    private static byte[] CreateUncompressedPdf(bool signed = false)
    {
        using var document = new PdfSharp.Pdf.PdfDocument();
        document.Options.CompressContentStreams = false;
        document.Options.NoCompression = true;
        for (var p = 0; p < 3; p++)
        {
            var page = document.AddPage();
            using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
            for (var line = 0; line < 120; line++)
            {
                gfx.DrawLine(PdfSharp.Drawing.XPens.Black, 20, 20 + (line * 6), 560, 20 + (line * 6) + (p * 2));
                gfx.DrawRectangle(PdfSharp.Drawing.XPens.Gray, 30 + line, 30 + line, 100, 50);
            }
        }

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        var bytes = output.ToArray();
        return signed ? [.. bytes, .. System.Text.Encoding.ASCII.GetBytes("%/ByteRange [0 0 0 0]\n")] : bytes;
    }

    [Fact]
    public async Task Pdf_IsCompressedLosslessly_ButSignedPdfStaysUntouched()
    {
        var ct = TestContext.Current.CancellationToken;
        var pdf = CreateUncompressedPdf();
        var signed = CreateUncompressedPdf(signed: true);
        var id = await AnnounceAsync(pdf, FileKind.Document, "Aufmaß.pdf", "application/pdf");
        var signedId = await AnnounceAsync(signed, FileKind.Document, "Vertrag-signiert.pdf", "application/pdf");
        (await UploadAsync(id, pdf)).EnsureSuccessStatusCode();
        (await UploadAsync(signedId, signed)).EnsureSuccessStatusCode();

        var stored = await _worker.GetByteArrayAsync(new Uri($"/api/v1/files/{id}/content", UriKind.Relative), ct);
        stored.Length.ShouldBeLessThan(pdf.Length / 2);
        using (var reopened = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(stored), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
        {
            reopened.PageCount.ShouldBe(3);
        }

        (await _worker.GetByteArrayAsync(new Uri($"/api/v1/files/{signedId}/content", UriKind.Relative), ct)).ShouldBe(signed);
    }

    [Fact]
    public async Task StorageStats_ShowSavings_AndOptimizationJobRuns_KeepOriginalSettingIsHonoured()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        var settings = (await admin.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/v1/settings", ct))!;
        settings["settings"]!["media"]!["keepOriginalPhotos"] = true;
        (await admin.PutAsJsonAsync("/api/v1/settings", new { settings = settings["settings"], version = (long)settings["version"]! }, ct)).EnsureSuccessStatusCode();

        var kept = CreateCameraJpeg(1000, 800);
        var keptId = await AnnounceAsync(kept, FileKind.Photo, "unveraendert.jpg");
        (await UploadAsync(keptId, kept)).EnsureSuccessStatusCode();
        (await _worker.GetByteArrayAsync(new Uri($"/api/v1/files/{keptId}/content", UriKind.Relative), ct)).ShouldBe(kept);

        settings = (await admin.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/v1/settings", ct))!;
        settings["settings"]!["media"]!["keepOriginalPhotos"] = false;
        (await admin.PutAsJsonAsync("/api/v1/settings", new { settings = settings["settings"], version = (long)settings["version"]! }, ct)).EnsureSuccessStatusCode();

        using var start = await admin.PostAsync(new Uri("/api/v1/files/storage/optimize", UriKind.Relative), null, ct);
        start.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        StorageStats stats;
        var attempts = 0;
        do
        {
            await Task.Delay(200, ct);
            stats = (await admin.GetFromJsonAsync<StorageStats>("/api/v1/files/storage", TestJson.Options, ct))!;
        }
        while (stats.Job.Running && ++attempts < 100);

        stats.Job.Optimized.ShouldBe(1);
        stats.SavedBytes.ShouldBeGreaterThan(0);
        stats.Candidates.ShouldBe(0);
        stats.Kinds.Single(k => k.Kind == FileKind.Photo).Optimized.ShouldBe(1);
        (await _worker.GetByteArrayAsync(new Uri($"/api/v1/files/{keptId}/content", UriKind.Relative), ct)).Length.ShouldBeLessThan(kept.Length);
        using var forbidden = await _worker.GetAsync(new Uri("/api/v1/files/storage", UriKind.Relative), ct);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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
