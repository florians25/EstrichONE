using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Contacts;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;

namespace EstrichOne.Api.Tests.Contacts;

/// <summary>Kunden offline anlegen (contact.create) – z. B. bei der Schnellanlage einer Bauakte in der App.</summary>
public sealed class ContactSyncTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _site = null!;
    private HttpClient _office = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres);
        _site = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        _office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
    }

    public async ValueTask DisposeAsync()
    {
        _site.Dispose();
        _office.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task OfflineCreatedCustomer_AndBauakteInOnePush_AreLinkedAndVisibleToTheOffice()
    {
        var customer = Guid.CreateVersion7();
        var bauakte = Guid.CreateVersion7();

        var results = (await _site.PushAsync(
            SyncClient.Op("contact.create", customer, new
            {
                name = "  Familie Schneider ", phone = "0871 123456", email = "schneider@example.com",
                street = "Ahornweg 3", postalCode = "84028", city = "Landshut",
            }),
            SyncClient.Op("bauakte.create", bauakte, new { title = "EFH Schneider", customerId = customer }))).Results;

        results.Select(r => r.Status).ShouldAllBe(status => status == SyncOperationStatus.Applied);
        results[0].Entity.Text("name").ShouldBe("Familie Schneider");
        results[0].Entity.Text("kind").ShouldBe("person");
        results[0].Entity.Text("customerNumber").ShouldBeNull();
        results[1].Entity.Text("customerId").ShouldBe(customer.ToString());

        var contact = await _office.GetFromJsonAsync<ContactResponse>($"/api/v1/contacts/{customer}", TestJson.Options, TestContext.Current.CancellationToken);
        contact!.Phone.ShouldBe("0871 123456");
        contact.Email.ShouldBe("schneider@example.com");
        contact.City.ShouldBe("Landshut");

        var (changes, _) = await _site.PullUntilAsync(null, all => all.Any(c => c.Id == customer));
        changes.Single(c => c.Id == customer).Data.Text("street").ShouldBe("Ahornweg 3");
    }

    [Fact]
    public async Task RepeatedCreate_IsIdempotent_AndAnotherNameUnderTheSameIdIsRejected()
    {
        var customer = Guid.CreateVersion7();
        var op = SyncClient.Op("contact.create", customer, new { name = "Huber Bau GmbH", kind = "company" }, opId: Guid.CreateVersion7());

        (await _site.PushAsync(op)).Results.Single().Status.ShouldBe(SyncOperationStatus.Applied);
        (await _site.PushAsync(op)).Results.Single().Status.ShouldBe(SyncOperationStatus.Duplicate);
        // Antwort verloren, App sendet mit neuer opId: dieselbe Anlage wird bestätigt.
        var again = (await _site.PushAsync(SyncClient.Op("contact.create", customer, new { name = "Huber Bau GmbH" }))).Results.Single();
        again.Status.ShouldBe(SyncOperationStatus.Applied);
        again.Entity.Text("kind").ShouldBe("company");

        var other = (await _site.PushAsync(SyncClient.Op("contact.create", customer, new { name = "Jemand anderes" }))).Results.Single();
        other.Status.ShouldBe(SyncOperationStatus.Rejected);
        other.Code.ShouldBe("ID_CONFLICT");
    }

    [Fact]
    public async Task MissingNameOrInvalidEmail_IsRejected()
    {
        var results = (await _site.PushAsync(
            SyncClient.Op("contact.create", Guid.CreateVersion7(), new { name = "  " }),
            SyncClient.Op("contact.create", Guid.CreateVersion7(), new { name = "Maier", email = "keine-mail" }))).Results;

        results.ShouldAllBe(r => r.Status == SyncOperationStatus.Rejected && r.Code == "VALIDATION_FAILED");
    }

    [Fact]
    public async Task WithoutContactOrCreatePermission_IsDenied()
    {
        using var it = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);

        var result = (await it.PushAsync(SyncClient.Op("contact.create", Guid.CreateVersion7(), new { name = "Maier" }))).Results.Single();

        result.Status.ShouldBe(SyncOperationStatus.Rejected);
        result.Code.ShouldBe("PERMISSION_DENIED");
    }
}
