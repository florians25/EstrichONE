using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Contacts;
using EstrichOne.Api.Modules.Contacts.Exports;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Contacts;

public sealed class ContactDuplicateTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _office = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres);
        _office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        foreach (var contact in new[]
        {
            new SaveContactRequest(Guid.CreateVersion7(), ContactKind.Person, "Familie Kraus", null, "Hauptstraße 3", "84028", "Landshut", "0871 123456", "kraus@example.de"),
            new SaveContactRequest(Guid.CreateVersion7(), ContactKind.Company, "Bau Huber GmbH", "K-1", "Industriestr. 1", "84034", "Landshut", "+49 871 99887766"),
            new SaveContactRequest(Guid.CreateVersion7(), ContactKind.Person, "Maria Schneider", null, null, "80331", "München"),
        })
        {
            (await _office.PostAsJsonAsync("/api/v1/contacts", contact, TestJson.Options, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _office.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<List<ContactDuplicateCandidate>> FindAsync(string query) =>
        (await _office.GetFromJsonAsync<List<ContactDuplicateCandidate>>($"/api/v1/contacts/duplicates?{query}", TestJson.Options, TestContext.Current.CancellationToken))!;

    [Fact]
    public async Task SimilarName_IsFound_IgnoringSalutationAndUmlauts()
    {
        var hits = await FindAsync("name=Kraus&city=Landshut");
        hits.Count.ShouldBe(1);
        hits[0].Contact.Name.ShouldBe("Familie Kraus");
        hits[0].Reasons.ShouldContain("gleicher Name");

        (await FindAsync("name=Krauss")).Single().Reasons.ShouldContain("ähnlicher Name");
        (await FindAsync("name=Maria%20Schnieder")).Single().Contact.Name.ShouldBe("Maria Schneider");
    }

    [Fact]
    public async Task SamePhoneEmailOrAddress_IsFound_EvenWithOtherName()
    {
        (await FindAsync("name=Huber%20Bau&phone=0871%2099887766")).First().Reasons.ShouldBe(["gleicher Name", "gleiche Telefonnummer"], ignoreOrder: true);
        (await FindAsync("name=Neu&email=KRAUS@example.de")).Single().Reasons.ShouldContain("gleiche E-Mail-Adresse");
        (await FindAsync("name=Mieter&street=Hauptstr.%203&postalCode=84028")).Single().Reasons.ShouldContain("gleiche Anschrift");
    }

    [Fact]
    public async Task ExtendedCustomerData_SeveralNumbers_LegacyFieldsFollowFirst_DuplicatesOverAllNumbers()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.CreateVersion7();
        using var created = await _office.PostAsJsonAsync("/api/v1/contacts", new
        {
            id, kind = "person", name = "Peter Lang", salutation = "Herr", title = "Dr.", preferredChannel = "mobile", birthday = "1970-05-04",
            phones = new object[] { new { kind = "mobile", number = "0171 5550001", label = "privat" }, new { kind = "office", number = "0871 4440002" } },
            emails = new[] { new { address = "p.lang@example.de" }, new { address = "buero@lang.de" } },
            billingAddress = new { name = "Lang Verwaltung", street = "Postfach 1", postalCode = "84001", city = "Landshut" },
            vatId = "DE123456789", website = "https://lang.de",
        }, TestJson.Options, ct);
        created.EnsureSuccessStatusCode();
        var c = (await created.Content.ReadFromJsonAsync<ContactResponse>(TestJson.Options, ct))!;
        c.Phone.ShouldBe("0171 5550001");
        c.Email.ShouldBe("p.lang@example.de");
        c.Phones.Count.ShouldBe(2);
        c.Phones[0].Kind.ShouldBe(ContactPhoneKind.Mobile);
        c.BillingAddress!.City.ShouldBe("Landshut");
        c.Birthday.ShouldBe(new DateOnly(1970, 5, 4));
        c.PreferredChannel.ShouldBe(ContactChannel.Mobile);

        // Dublettenprüfung findet auch die zweite Nummer und die zweite E-Mail-Adresse.
        (await FindAsync("name=Neu&phone=0871%204440002")).Single().Contact.Id.ShouldBe(id);
        (await FindAsync("name=Neu&email=buero@lang.de")).Single().Contact.Id.ShouldBe(id);

        // Ältere App ändert nur phone: erste Nummer wird ersetzt, weitere Daten bleiben.
        using var updated = await _office.PutAsJsonAsync($"/api/v1/contacts/{id}", new { version = c.Version, kind = "person", name = "Peter Lang", phone = "0171 5559999", email = c.Email },
            TestJson.Options, ct);
        updated.EnsureSuccessStatusCode();
        var u = (await updated.Content.ReadFromJsonAsync<ContactResponse>(TestJson.Options, ct))!;
        u.Phones.Select(p => p.Number).ShouldBe(["0171 5559999", "0871 4440002"]);
        u.Phones[0].Label.ShouldBe("privat");
        u.Emails.Count.ShouldBe(2);
        u.VatId.ShouldBe("DE123456789");
    }

    [Fact]
    public async Task UnrelatedOrEmptyInput_FindsNothing()
    {
        (await FindAsync("name=Zimmermann&city=Passau")).ShouldBeEmpty();
        (await FindAsync("")).ShouldBeEmpty();
    }
}
