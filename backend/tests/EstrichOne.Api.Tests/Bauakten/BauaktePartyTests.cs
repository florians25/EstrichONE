using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;

namespace EstrichOne.Api.Tests.Bauakten;

public sealed class BauaktePartyTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Parties_FromContactsOrFree_SortedByRole_WithHistory()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = Guid.CreateVersion7();
        (await office.PostAsJsonAsync("/api/v1/bauakten", new { id = bauakte, title = "EFH Kraus" }, Ct)).EnsureSuccessStatusCode();
        var architect = Guid.CreateVersion7();
        (await office.PostAsJsonAsync("/api/v1/contacts", new { id = architect, kind = "company", name = "Architekturbüro Lehner", phone = "0871 12345" }, Ct))
            .EnsureSuccessStatusCode();

        var supplier = Guid.CreateVersion7();
        (await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte}/parties/{supplier}",
            new { role = "supplier", name = "Baustoffe Maier", phone = "0871 999", note = "Anlieferung nur vormittags" }, Ct)).EnsureSuccessStatusCode();
        using var linked = await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte}/parties/{Guid.CreateVersion7()}",
            new { role = "architect", contactId = architect }, Ct);
        linked.StatusCode.ShouldBe(HttpStatusCode.OK);
        var list = await linked.Content.ReadFromJsonAsync<List<BauaktePartyResponse>>(TestJson.Options, Ct);

        list!.Select(p => p.Role).ShouldBe([BauaktePartyRole.Architect, BauaktePartyRole.Supplier]);
        list![0].Name.ShouldBe("Architekturbüro Lehner");
        list[0].Phone.ShouldBe("0871 12345");
        list[1].Note.ShouldBe("Anlieferung nur vormittags");

        using var empty = await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte}/parties/{Guid.CreateVersion7()}", new { role = "other", name = " " }, Ct);
        empty.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await office.DeleteAsync(new Uri($"/api/v1/bauakten/{bauakte}/parties/{supplier}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var history = await office.GetFromJsonAsync<List<BauakteHistoryEntry>>($"/api/v1/bauakten/{bauakte}/history", TestJson.Options, Ct);
        history!.Select(h => h.Text).ShouldContain(t => t.EndsWith("hat „Architekturbüro Lehner“ als Architekt hinzugefügt.", StringComparison.Ordinal));
        history!.Select(h => h.Text).ShouldContain(t => t.EndsWith("hat „Baustoffe Maier“ (Lieferant) entfernt.", StringComparison.Ordinal));

        // Fremde Bauakte: wie nicht vorhanden.
        using var site = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        (await site.GetAsync(new Uri($"/api/v1/bauakten/{bauakte}/parties", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
