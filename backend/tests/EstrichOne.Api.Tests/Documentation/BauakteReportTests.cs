using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
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
        (await other.GetAsync(new Uri($"/api/v1/bauakten/{bauakte.Id}/report.pdf", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
