using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;

namespace EstrichOne.Api.Tests.Documentation;

/// <summary>Dokumentart beim Ankündigen und nachträgliche Beschriftung über die Synchronisation (App, offline).</summary>
public sealed class FileSyncTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _site = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres);
        _site = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
    }

    public async ValueTask DisposeAsync()
    {
        _site.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<Guid> BauakteAsync()
    {
        var id = Guid.CreateVersion7();
        (await _site.PushAsync(SyncClient.Op("bauakte.create", id, new { title = "Dokumente" }))).Results.Single().Status.ShouldBe(SyncOperationStatus.Applied);
        return id;
    }

    private static object Announce(Guid id, Guid bauakte, string kind, string? category = null, string? caption = null) =>
        SyncClient.Op("file.announce", id, new
        {
            bauakteId = bauakte, kind, originalName = kind == "document" ? "Aufmass.pdf" : "IMG_1.jpg",
            contentType = kind == "document" ? "application/pdf" : "image/jpeg", sizeBytes = 2048, sha256 = new string('b', 64),
            category, caption,
        });

    [Fact]
    public async Task ScannedDocument_KeepsItsCategoryAndTitle()
    {
        var bauakte = await BauakteAsync();
        var document = Guid.CreateVersion7();

        var result = (await _site.PushAsync(Announce(document, bauakte, "document", "delivery", "Lieferschein Estrich"))).Results.Single();

        result.Status.ShouldBe(SyncOperationStatus.Applied);
        result.Entity.Text("category").ShouldBe("delivery");
        result.Entity.Text("caption").ShouldBe("Lieferschein Estrich");
    }

    [Fact]
    public async Task UnknownCategory_IsRejected_AndPhotosGetNoCategory()
    {
        var bauakte = await BauakteAsync();

        var unknown = (await _site.PushAsync(Announce(Guid.CreateVersion7(), bauakte, "document", "geheim"))).Results.Single();
        var photo = (await _site.PushAsync(Announce(Guid.CreateVersion7(), bauakte, "photo", "delivery"))).Results.Single();

        unknown.Status.ShouldBe(SyncOperationStatus.Rejected);
        unknown.Code.ShouldBe("VALIDATION_FAILED");
        photo.Status.ShouldBe(SyncOperationStatus.Applied);
        photo.Entity.Text("category").ShouldBeNull();
    }

    [Fact]
    public async Task CaptionAndCategory_CanBeChangedLater_FieldByField()
    {
        var bauakte = await BauakteAsync();
        var photo = Guid.CreateVersion7();
        var document = Guid.CreateVersion7();
        await _site.PushAsync(Announce(photo, bauakte, "photo"), Announce(document, bauakte, "document", "other"));

        var results = (await _site.PushAsync(
            SyncClient.Op("file.update", photo, new { changes = new { caption = " Bad OG, Randdämmstreifen " }, @base = new { caption = (string?)null } }),
            SyncClient.Op("file.update", document, new { changes = new { category = "acceptance" }, @base = new { category = "other" } }),
            SyncClient.Op("file.update", photo, new { changes = new { caption = "Flur" }, @base = new { caption = "veraltet" } }))).Results;

        results[0].Status.ShouldBe(SyncOperationStatus.Applied);
        results[0].Entity.Text("caption").ShouldBe("Bad OG, Randdämmstreifen");
        results[1].Entity.Text("category").ShouldBe("acceptance");
        results[2].Status.ShouldBe(SyncOperationStatus.Conflict, "Parallel anders beschriftet: der Serverwert bleibt.");
        results[2].Entity.Text("caption").ShouldBe("Bad OG, Randdämmstreifen");
    }

    [Fact]
    public async Task ContentFieldsCannotBeChanged()
    {
        var bauakte = await BauakteAsync();
        var photo = Guid.CreateVersion7();
        await _site.PushAsync(Announce(photo, bauakte, "photo"));

        var result = (await _site.PushAsync(SyncClient.Op("file.update", photo, new
        {
            changes = new { sha256 = new string('c', 64) }, @base = new { sha256 = new string('b', 64) },
        }))).Results.Single();

        result.Status.ShouldBe(SyncOperationStatus.Rejected);
        result.Code.ShouldBe("VALIDATION_FAILED");
    }
}
