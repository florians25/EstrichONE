using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Documentation;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Sync;
using EstrichOne.Api.Tests.Support;
using NetVips;
using PdfSharp.Pdf.IO;

namespace EstrichOne.Api.Tests.Documentation;

public sealed class BauakteReportTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _worker = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres);
        _worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
    }

    public async ValueTask DisposeAsync()
    {
        _worker.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Report_IsPdf_WithCoverAndPhotos_AndOnlyForReaders()
    {
        using var created = await _worker.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title = "EFH Müller, Heizestrich", city = "Landshut" }, Ct);
        var bauakte = (await created.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;
        using var jpegImage = (Image.Black(1600, 1200, bands: 3) + new[] { 90.0, 120.0, 160.0 }).Cast(Enums.BandFormat.Uchar);
        var jpeg = jpegImage.JpegsaveBuffer(q: 85);
        var photoId = Guid.CreateVersion7();
        using (var announce = await _worker.PostAsJsonAsync("/api/v1/files", new
               {
                   id = photoId, bauakteId = bauakte.Id, kind = "photo", originalName = "IMG_1.jpg", contentType = "image/jpeg",
                   sizeBytes = (long)jpeg.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(jpeg)), capturedAt = DateTimeOffset.UtcNow,
                   caption = "Randdämmung Bad",
               }, Ct))
        {
            announce.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using var body = new ByteArrayContent(jpeg);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await _worker.PutAsync(new Uri($"/api/v1/files/{photoId}/content", UriKind.Relative), body, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        using var response = await _worker.GetAsync(new Uri($"/api/v1/bauakten/{bauakte.Id}/report.pdf", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        response.Content.Headers.ContentDisposition!.FileNameStar.ShouldNotBeNull().ShouldContain("EFH Müller");
        var pdf = await response.Content.ReadAsByteArrayAsync(Ct);
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).ShouldBe("%PDF-");
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        document.PageCount.ShouldBeGreaterThanOrEqualTo(3); // Deckblatt, Übersicht …, Fotodokumentation
        pdf.Length.ShouldBeGreaterThan(30_000); // Schrift, Logo und Foto eingebettet

        using var other = await _factory.CreateClientAsAsync("fremd", StandardRoles.SiteWorkerId);
        // Alle mit Bauakten-Zugriff sehen alle Bauakten – auch den Bericht; ohne Rolle nicht.
        (await other.GetAsync(new Uri($"/api/v1/bauakten/{bauakte.Id}/report.pdf", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        using var nobody = await _factory.CreateClientAsAsync("ohne.rolle");
        (await nobody.GetAsync(new Uri($"/api/v1/bauakten/{bauakte.Id}/report.pdf", UriKind.Relative), Ct)).StatusCode
            .ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Kundenmeldung 1.0.42: falsches Logo und Bauakten-Fotos falsch im PDF. Geprüft wird an den Bildobjekten des PDFs:
    /// Firmenlogo eingebettet (ohne Logo gar kein Logo), Hochformatfoto mit EXIF 6 aufrecht, Querformat quer, Video als Hinweis.
    /// Mit ESTRICHONE_SAMPLE_PDF_DIR werden die Berichte als Beispiel abgelegt.
    /// </summary>
    [Fact]
    public async Task Report_EmbedsCompanyLogo_AndUprightPhotos_WithoutForeignLogo()
    {
        using var admin = await _factory.CreateClientAsAsync("chefin", StandardRoles.AdministratorId);
        using var office = await _factory.CreateClientAsAsync("buero.bericht", StandardRoles.OfficeId);
        using var created = await office.PostAsJsonAsync("/api/v1/bauakten", new
        {
            id = Guid.CreateVersion7(), title = "MFH Am Anger, Zementestrich", street = "Am Anger 4", postalCode = "84028", city = "Landshut",
        }, Ct);
        var bauakte = (await created.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;
        var room = Guid.CreateVersion7();
        (await office.PushAsync(SyncClient.Op("room.create", room, new { bauakteId = bauakte.Id, name = "Wohnen", floor = "EG", areaSquareMeters = 32.5m })))
            .Results.Single().Status.ShouldBe(SyncOperationStatus.Applied);

        // Hochformat wie vom Handy: Sensor quer, EXIF-Ausrichtung 6. Oben ein roter Balken als Richtungsmerkmal.
        var portrait = await UploadAsync(office, bauakte.Id, Photo(1600, 1200, orientation: 6), "image/jpeg", "IMG_hoch.jpg", "Randdämmstreifen an der Außenwand");
        var landscape = await UploadAsync(office, bauakte.Id, Photo(1600, 1200, orientation: 1), "image/jpeg", "IMG_quer.jpg", "Estrich eingebracht");
        byte[] video = [0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 2, 0,
            (byte)'i', (byte)'s', (byte)'o', (byte)'m', (byte)'m', (byte)'p', (byte)'4', (byte)'1', 0, 0, 0, 8, (byte)'f', (byte)'r', (byte)'e', (byte)'e'];
        await UploadAsync(office, bauakte.Id, video, "video/mp4", "Rundgang.mp4", "Rundgang", kind: "video");
        using (var link = await office.PutAsJsonAsync($"/api/v1/files/{portrait}/details",
                   new { originalName = "IMG_hoch.jpg", caption = "Randdämmstreifen an der Außenwand", roomId = room }, Ct))
        {
            link.EnsureSuccessStatusCode();
        }

        // Ohne Firmenlogo: kein Logo im PDF (auch kein Produkt- oder Altlogo), nur die beiden Fotos.
        var plain = await ReportAsync(office, bauakte.Id);
        await SampleAsync("bauakte-ohne-logo.pdf", plain);
        var plainImages = PdfImagesTests.PdfImageObjects(plain);
        plainImages.Count.ShouldBe(2, string.Join(", ", plainImages));
        plainImages.ShouldContain(i => i.Height > i.Width); // Hochformat aufrecht
        plainImages.ShouldContain(i => i.Width > i.Height);

        // Mit Firmenlogo (transparentes PNG 3:1): im Kopf jeder Seite und auf dem Deckblatt, unverzerrt.
        using var logoImage = LogoPng();
        using (var content = new ByteArrayContent(logoImage.PngsaveBuffer()))
        {
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            (await admin.PutAsync(new Uri("/api/v1/company/logo", UriKind.Relative), content, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var branded = await ReportAsync(office, bauakte.Id);
        await SampleAsync("bauakte-mit-logo.pdf", branded);
        var images = PdfImagesTests.PdfImageObjects(branded);
        images.ShouldContain((900, 300));
        images.Count(i => i is not (900, 300)).ShouldBe(2);

        // Logo gewechselt: der nächste Bericht zeigt sofort das neue Logo (kein Zwischenspeicher).
        using var second = Image.Black(400, 400, bands: 3) + new[] { 20.0, 120.0, 60.0 };
        using (var content = new ByteArrayContent(second.Cast(Enums.BandFormat.Uchar).PngsaveBuffer()))
        {
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            (await admin.PutAsync(new Uri("/api/v1/company/logo", UriKind.Relative), content, Ct)).EnsureSuccessStatusCode();
        }

        var changed = PdfImagesTests.PdfImageObjects(await ReportAsync(office, bauakte.Id));
        changed.ShouldContain((400, 400));
        changed.ShouldNotContain((900, 300));
        using var logoResponse = await _factory.CreateClient().GetAsync(new Uri("/api/v1/company/logo", UriKind.Relative), Ct);
        logoResponse.Headers.CacheControl!.NoCache.ShouldBeTrue();

        // Abnahmeprotokoll mit denselben Fotos: ebenfalls Firmenlogo und aufrechte Fotos.
        using var signatureImage = (Image.Black(300, 100, bands: 3) + new[] { 40.0, 40.0, 40.0 }).Cast(Enums.BandFormat.Uchar);
        var signature = Convert.ToBase64String(signatureImage.PngsaveBuffer());
        (await office.PushAsync(SyncClient.Op("acceptance.create", Guid.CreateVersion7(), new
        {
            bauakteId = bauakte.Id, result = "accepted", clientName = "Familie Huber", contractorName = "M. Bauer", scope = "Zementestrich EG",
            photoFileIds = new[] { portrait, landscape },
            clientSignature = new { png = signature, signedAt = _factory.Clock.GetUtcNow() }, contractorSignature = new { png = signature },
        }))).Results.Single().Status.ShouldBe(SyncOperationStatus.Applied);
        var protocol = (await office.GetFromJsonAsync<List<AcceptanceDto>>($"/api/v1/bauakten/{bauakte.Id}/acceptances", TestJson.Options, Ct))!.Single();
        using var form = await office.GetAsync(new Uri($"/api/v1/files/{protocol.PdfFileId}/content", UriKind.Relative), Ct);
        var acceptancePdf = await form.Content.ReadAsByteArrayAsync(Ct);
        await SampleAsync("abnahme-mit-logo.pdf", acceptancePdf);
        var acceptanceImages = PdfImagesTests.PdfImageObjects(acceptancePdf).Where(i => i is not (300, 100)).ToList();
        acceptanceImages.ShouldContain((400, 400));
        acceptanceImages.Count(i => i is not (400, 400)).ShouldBe(2, string.Join(", ", acceptanceImages));
        acceptanceImages.ShouldContain(i => i.Height > i.Width);
    }

    private static async Task<byte[]> ReportAsync(HttpClient client, Guid bauakte)
    {
        using var response = await client.GetAsync(new Uri($"/api/v1/bauakten/{bauakte}/report.pdf", UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await response.Content.ReadAsByteArrayAsync(Ct);
    }

    private static async Task SampleAsync(string name, byte[] pdf)
    {
        if (Environment.GetEnvironmentVariable("ESTRICHONE_SAMPLE_PDF_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(Path.Combine(directory, name), pdf, Ct);
        }
    }

    private static async Task<Guid> UploadAsync(HttpClient client, Guid bauakte, byte[] content, string contentType, string name, string caption,
        string kind = "photo")
    {
        var id = Guid.CreateVersion7();
        using (var announce = await client.PostAsJsonAsync("/api/v1/files", new
               {
                   id, bauakteId = bauakte, kind, originalName = name, contentType, sizeBytes = (long)content.Length,
                   sha256 = Convert.ToHexStringLower(SHA256.HashData(content)), capturedAt = DateTimeOffset.UtcNow, caption,
               }, Ct))
        {
            announce.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        (await client.PutAsync(new Uri($"/api/v1/files/{id}/content", UriKind.Relative), body, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        return id;
    }

    /// <summary>Testfoto: Fläche mit rotem Balken oben (aufrecht betrachtet); bei Ausrichtung 6 so gespeichert, wie ein Handy es tut.</summary>
    private static byte[] Photo(int width, int height, int orientation)
    {
        var (w, h) = orientation == 6 ? (height, width) : (width, height);
        using var ground = (Image.Black(w, h, bands: 3) + new[] { 170.0, 160.0, 145.0 }).Cast(Enums.BandFormat.Uchar);
        using var bar = (Image.Black(w, h / 8, bands: 3) + new[] { 200.0, 30.0, 30.0 }).Cast(Enums.BandFormat.Uchar);
        using var marker = (Image.Black(w / 6, h / 6, bands: 3) + new[] { 30.0, 60.0, 200.0 }).Cast(Enums.BandFormat.Uchar);
        using var withBar = ground.Insert(bar, 0, 0);
        using var upright = withBar.Insert(marker, w / 12, h / 5).Copy(interpretation: Enums.Interpretation.Srgb);
        using var stored = orientation == 6 ? upright.Rot270() : upright.Copy();
        using var tagged = stored.Mutate(m => m.Set(GValue.GIntType, "orientation", orientation));
        return tagged.JpegsaveBuffer(q: 85, keep: Enums.ForeignKeep.Exif);
    }

    /// <summary>Firmenlogo 900 × 300 mit transparentem Hintergrund: dunkelblaues Feld links, Akzentbalken rechts.</summary>
    private static Image LogoPng()
    {
        using var transparent = Image.Black(900, 300, bands: 4).Cast(Enums.BandFormat.Uchar);
        using var mark = (Image.Black(260, 260, bands: 4) + new[] { 11.0, 45.0, 91.0, 255.0 }).Cast(Enums.BandFormat.Uchar);
        using var accent = (Image.Black(560, 70, bands: 4) + new[] { 31.0, 78.0, 121.0, 255.0 }).Cast(Enums.BandFormat.Uchar);
        using var second = (Image.Black(420, 50, bands: 4) + new[] { 90.0, 140.0, 190.0, 255.0 }).Cast(Enums.BandFormat.Uchar);
        using var a = transparent.Insert(mark, 20, 20);
        using var b = a.Insert(accent, 320, 80);
        using var c = b.Insert(second, 320, 180);
        return c.Copy(interpretation: Enums.Interpretation.Srgb);
    }
}
