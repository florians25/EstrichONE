using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Organization;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Organization;

public sealed class CatalogTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly string[] Folders = ["Ausschreibung", "Nachträge"];
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Catalog_StoresTextBlocksFolderAndBauakteTemplates_WithValidation_AndOnlyAdminsEdit()
    {
        using var admin = await _factory.CreateClientAsAsync("chef", StandardRoles.AdministratorId);
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var text = Guid.CreateVersion7();
        (await admin.PutAsJsonAsync($"/api/v1/catalog/{text}", new { kind = "textBlock", name = "Untergrund", content = new { text = "Untergrund besenrein übergeben." } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PutAsJsonAsync($"/api/v1/catalog/{Guid.CreateVersion7()}", new { kind = "folderTemplate", name = "Angebote", content = new { category = "offer", folders = Folders } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.PutAsJsonAsync($"/api/v1/catalog/{Guid.CreateVersion7()}", new
        {
            kind = "bauakteTemplate", name = "EFH Heizestrich",
            content = new { description = "Heizestrich CAF 65 mm", rooms = new object[] { new { name = "Wohnen", floor = "EG", thicknessMillimeters = 65, underfloorHeating = true } } },
        }, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await admin.PutAsJsonAsync($"/api/v1/catalog/{Guid.CreateVersion7()}", new { kind = "textBlock", name = "Leer", content = new { text = " " } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await admin.PutAsJsonAsync($"/api/v1/catalog/{Guid.CreateVersion7()}", new { kind = "unbekannt", name = "X", content = new { } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await worker.PutAsJsonAsync($"/api/v1/catalog/{Guid.CreateVersion7()}", new { kind = "textBlock", name = "X", content = new { text = "x" } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var blocks = await worker.GetFromJsonAsync<List<CatalogEntryResponse>>("/api/v1/catalog?kind=textBlock", TestJson.Options, Ct);
        blocks!.ShouldHaveSingleItem().Content.GetProperty("text").GetString().ShouldBe("Untergrund besenrein übergeben.");
        (await worker.GetFromJsonAsync<List<CatalogEntryResponse>>("/api/v1/catalog", TestJson.Options, Ct))!.Count.ShouldBe(3);
        (await admin.DeleteAsync(new Uri($"/api/v1/catalog/{text}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }
}
