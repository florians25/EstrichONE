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

    [Fact]
    public async Task Parties_PrimaryContact_RoleChange_AllNumbersAndAddress()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = Guid.CreateVersion7();
        (await office.PostAsJsonAsync("/api/v1/bauakten", new { id = bauakte, title = "MFH Seligenthal" }, Ct)).EnsureSuccessStatusCode();
        var architect = Guid.CreateVersion7();
        (await office.PostAsJsonAsync("/api/v1/contacts", new { id = architect, kind = "company", name = "Architekturbüro Lehner", phone = "0871 12345",
            email = "info@lehner.example", street = "Altstadt 1", postalCode = "84028", city = "Landshut" }, Ct)).EnsureSuccessStatusCode();

        var linked = Guid.CreateVersion7();
        (await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte}/parties/{linked}",
            new { role = "architect", contactId = architect, mobile = "0171 2222", email = "p.lehner@lehner.example", isPrimary = true }, Ct)).EnsureSuccessStatusCode();
        var free = Guid.CreateVersion7();
        using var saved = await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte}/parties/{free}",
            new { role = "siteManager", name = "Anna Bauer", phone = "0871 1", street = "Ländgasse 5", postalCode = "84028", city = "Landshut" }, Ct);
        var list = (await saved.Content.ReadFromJsonAsync<List<BauaktePartyResponse>>(TestJson.Options, Ct))!;

        // Hauptkontakt zuerst, mit allen Nummern (eigene zuerst) und Anschrift aus dem Kundenstamm.
        list[0].Id.ShouldBe(linked);
        list[0].IsPrimary.ShouldBeTrue();
        list[0].Phones!.Select(n => n.Number).ShouldBe(["0171 2222", "0871 12345"]);
        list[0].Emails!.Select(m => m.Address).ShouldBe(["p.lehner@lehner.example", "info@lehner.example"]);
        list[0].Address.ShouldBe("Altstadt 1, 84028 Landshut");
        list[0].ContactKind.ShouldBe(EstrichOne.Api.Modules.Contacts.Exports.ContactKind.Company);
        list[1].Address.ShouldBe("Ländgasse 5, 84028 Landshut");
        list[1].IsPrimary.ShouldBeFalse();

        // Neuer Hauptkontakt: der bisherige verliert die Markierung.
        using var primary = await office.PostAsync(new Uri($"/api/v1/bauakten/{bauakte}/parties/{free}/primary", UriKind.Relative), null, Ct);
        var afterPrimary = (await primary.Content.ReadFromJsonAsync<List<BauaktePartyResponse>>(TestJson.Options, Ct))!;
        afterPrimary.Single(p => p.IsPrimary).Id.ShouldBe(free);
        afterPrimary[0].Id.ShouldBe(free);

        // Rolle ändern (mit Version) und veraltete Version abweisen.
        var current = afterPrimary.Single(p => p.Id == free);
        (await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte}/parties/{free}",
            new { role = "siteContact", name = "Anna Bauer", phone = "0871 1", version = current.Version }, Ct)).EnsureSuccessStatusCode();
        using var stale = await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte}/parties/{free}",
            new { role = "other", name = "Anna Bauer", version = current.Version }, Ct);
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        var parties = await office.GetFromJsonAsync<List<BauaktePartyResponse>>($"/api/v1/bauakten/{bauakte}/parties", TestJson.Options, Ct);
        var anna = parties!.Single(p => p.Id == free);
        anna.Role.ShouldBe(BauaktePartyRole.SiteContact);
        anna.IsPrimary.ShouldBeTrue();
        anna.Street.ShouldBeNull();

        (await office.DeleteAsync(new Uri($"/api/v1/bauakten/{bauakte}/parties/{free}/primary", UriKind.Relative), Ct)).EnsureSuccessStatusCode();
        (await office.GetFromJsonAsync<List<BauaktePartyResponse>>($"/api/v1/bauakten/{bauakte}/parties", TestJson.Options, Ct))!
            .ShouldAllBe(p => !p.IsPrimary);

        using var badMail = await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte}/parties/{Guid.CreateVersion7()}",
            new { role = "other", name = "X", email = "kein-mail" }, Ct);
        badMail.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var history = await office.GetFromJsonAsync<List<BauakteHistoryEntry>>($"/api/v1/bauakten/{bauakte}/history", TestJson.Options, Ct);
        history!.Select(h => h.Text).ShouldContain(t => t.EndsWith("hat „Anna Bauer“ (Bauleiter) als Hauptkontakt markiert.", StringComparison.Ordinal));
        history!.Select(h => h.Text).ShouldContain(t => t.EndsWith("hat „Anna Bauer“ jetzt als Ansprechpartner vor Ort eingetragen.", StringComparison.Ordinal));
    }
}
