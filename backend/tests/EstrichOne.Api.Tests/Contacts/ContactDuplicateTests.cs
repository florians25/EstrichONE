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
    public async Task UnrelatedOrEmptyInput_FindsNothing()
    {
        (await FindAsync("name=Zimmermann&city=Passau")).ShouldBeEmpty();
        (await FindAsync("")).ShouldBeEmpty();
    }
}
