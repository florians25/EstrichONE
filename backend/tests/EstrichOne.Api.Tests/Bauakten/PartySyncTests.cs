using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;

namespace EstrichOne.Api.Tests.Bauakten;

/// <summary>Beteiligte (Bauherr, Bauleiter, Architekt …) erreichen die App über den Änderungsfeed – samt Entfernen.</summary>
public sealed class PartySyncTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task PartiesFromTheOffice_ReachTheFeed_WithRole_AndRemovalAsDeleted()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = Guid.CreateVersion7();
        (await office.PostAsJsonAsync("/api/v1/bauakten", new { id = bauakte, title = "EFH Kraus" }, Ct)).EnsureSuccessStatusCode();
        var manager = Guid.CreateVersion7();
        (await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte}/parties/{manager}",
            new { role = "siteManager", name = "Anna Lechner", company = "Lechner Bau", phone = "0171 555", email = "a.lechner@example.com" }, Ct))
            .EnsureSuccessStatusCode();

        var (changes, cursor) = await office.PullUntilAsync(null, all => all.Any(c => c.Id == manager));
        var change = changes.Last(c => c.Id == manager);
        change.EntityType.ShouldBe("bauakteParty");
        change.Deleted.ShouldBeFalse();
        change.Data.Text("role").ShouldBe("siteManager");
        change.Data.Text("name").ShouldBe("Anna Lechner");
        change.Data.Text("company").ShouldBe("Lechner Bau");
        change.Data.Text("phone").ShouldBe("0171 555");
        change.Data.Text("bauakteId").ShouldBe(bauakte.ToString());

        // Für die Baustelle (nicht zugewiesen) unsichtbar.
        using var site = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        (await site.PullAsync()).Changes.ShouldNotContain(c => c.Id == manager);

        (await office.DeleteAsync(new Uri($"/api/v1/bauakten/{bauakte}/parties/{manager}", UriKind.Relative), Ct)).StatusCode
            .ShouldBe(HttpStatusCode.OK);
        var (removed, _) = await office.PullUntilAsync(cursor, all => all.Any(c => c.Id == manager && c.Deleted));
        removed.Last(c => c.Id == manager).Deleted.ShouldBeTrue();
    }

    [Fact]
    public async Task PartiesFromTheApp_SaveAndDelete_WithPermissionAndVisibility()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = Guid.CreateVersion7();
        (await office.PostAsJsonAsync("/api/v1/bauakten", new { id = bauakte, title = "EFH Kraus" }, Ct)).EnsureSuccessStatusCode();

        var party = Guid.CreateVersion7();
        var saved = (await office.PushAsync(SyncClient.Op("bauakteParty.save", party, new
        {
            bauakteId = bauakte, role = "siteManager", name = "Anna Lechner", mobile = "0171 555", street = "Ländgasse 5", city = "Landshut", isPrimary = true,
        }))).Results.Single();
        saved.Status.ShouldBe(SyncOperationStatus.Applied);
        saved.Entity.Text("mobile").ShouldBe("0171 555");
        saved.Entity.Text("city").ShouldBe("Landshut");

        // Rolle ändern (bauakteId darf fehlen, der Eintrag kennt seine Bauakte).
        var changed = (await office.PushAsync(SyncClient.Op("bauakteParty.save", party, new { role = "architect", name = "Anna Lechner" }))).Results.Single();
        changed.Status.ShouldBe(SyncOperationStatus.Applied);
        var list = await office.GetFromJsonAsync<List<EstrichOne.Api.Modules.Bauakten.BauaktePartyResponse>>($"/api/v1/bauakten/{bauakte}/parties",
            TestJson.Options, Ct);
        list!.Single().Role.ShouldBe(EstrichOne.Api.Modules.Bauakten.BauaktePartyRole.Architect);
        list!.Single().IsPrimary.ShouldBeTrue();

        var invalid = (await office.PushAsync(SyncClient.Op("bauakteParty.save", Guid.CreateVersion7(), new { bauakteId = bauakte, role = "other" }))).Results.Single();
        invalid.Code.ShouldBe("VALIDATION_FAILED");

        // Baustelle ohne Zuweisung: Bauakte unsichtbar.
        using var site = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        (await site.PushAsync(SyncClient.Op("bauakteParty.delete", party, new { }))).Results.Single().Code.ShouldBe("NOT_FOUND");

        (await office.PushAsync(SyncClient.Op("bauakteParty.delete", party, new { }))).Results.Single().Status.ShouldBe(SyncOperationStatus.Applied);
        (await office.GetFromJsonAsync<List<EstrichOne.Api.Modules.Bauakten.BauaktePartyResponse>>($"/api/v1/bauakten/{bauakte}/parties",
            TestJson.Options, Ct))!.ShouldBeEmpty();
    }
}
