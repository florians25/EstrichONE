using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Contacts;
using EstrichOne.Api.Modules.Organization;
using EstrichOne.Api.Modules.Organization.Exports;
using EstrichOne.Api.Modules.Work;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Contacts;

/// <summary>Kundenanlage und Kundenakte im Web: automatische Kundennummer, Ansprechpartner, Kennzahlen, Bauakten an derselben Adresse.</summary>
public sealed class CustomerRecordTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private HttpClient _office = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres);
        _office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
    }

    public async ValueTask DisposeAsync()
    {
        _office.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<ContactResponse> CreateContactAsync(object body)
    {
        using var response = await _office.PostAsJsonAsync("/api/v1/contacts", body, TestJson.Options, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<ContactResponse>(TestJson.Options, Ct))!;
    }

    private static async Task<BauakteDetail> CreateBauakteAsync(HttpClient client, object body)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/bauakten", body, TestJson.Options, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;
    }

    [Fact]
    public async Task CustomerNumbers_AreAssignedFromOwnSequence_ManualNumbersStayUnique()
    {
        var numbering = await _office.GetFromJsonAsync<CustomerNumberingResponse>("/api/v1/contacts/numbering", TestJson.Options, Ct);
        numbering!.Pattern.ShouldBe("60{NNNN}");
        numbering.Capacity.ShouldBe(9999);
        numbering.Preview.ShouldBe("600001");

        (await CreateContactAsync(new { id = Guid.CreateVersion7(), kind = "company", name = "Bau Huber GmbH" })).CustomerNumber.ShouldBe("600001");
        // Von Hand vergebene Nummer, die der Zähler später erreichen würde, wird übersprungen.
        (await CreateContactAsync(new { id = Guid.CreateVersion7(), kind = "person", name = "Eigene Nummer", customerNumber = "600002" })).CustomerNumber.ShouldBe("600002");
        (await CreateContactAsync(new { id = Guid.CreateVersion7(), kind = "person", name = "Eva Lang" })).CustomerNumber.ShouldBe("600003");
        using (var taken = await _office.PostAsJsonAsync("/api/v1/contacts", new { id = Guid.CreateVersion7(), kind = "person", name = "Doppelt", customerNumber = "600003" }, TestJson.Options, Ct))
        {
            taken.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }

        // Ändern ohne Nummer (ältere Apps, Formulare ohne Feld) behält die vergebene Nummer.
        var eva = (await _office.GetFromJsonAsync<List<ContactResponse>>("/api/v1/contacts?search=Eva", TestJson.Options, Ct))!.Single();
        using (var updated = await _office.PutAsJsonAsync($"/api/v1/contacts/{eva.Id}", new { version = eva.Version, kind = "person", name = "Eva Lang", city = "Landshut" }, TestJson.Options, Ct))
        {
            (await updated.Content.ReadFromJsonAsync<ContactResponse>(TestJson.Options, Ct))!.CustomerNumber.ShouldBe("600003");
        }

        // Muster und Startwert in den Einstellungen: nur mit Einstellungsrecht.
        (await _office.PutAsJsonAsync("/api/v1/contacts/numbering", new { nextNumber = 500 }, TestJson.Options, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var admin = await _factory.CreateClientAsAsync("chef", StandardRoles.AdministratorId);
        var settings = (await admin.GetFromJsonAsync<SystemSettingsResponse>("/api/v1/settings", TestJson.Options, Ct))!;
        (await admin.PutAsJsonAsync("/api/v1/settings", new SaveSystemSettingsRequest(
            settings.Settings with { Numbering = settings.Settings.Numbering with { CustomerPattern = "KD{JJ}-{NNNN}" } }, settings.Version), TestJson.Options, Ct))
            .EnsureSuccessStatusCode();
        using (var saved = await admin.PutAsJsonAsync("/api/v1/contacts/numbering", new { nextNumber = 500 }, TestJson.Options, Ct))
        {
            var after = (await saved.Content.ReadFromJsonAsync<CustomerNumberingResponse>(TestJson.Options, Ct))!;
            after.NextNumber.ShouldBe(500);
            after.Preview.ShouldEndWith("-0500");
        }

        var year = DateTime.UtcNow.Year % 100;
        (await CreateContactAsync(new { id = Guid.CreateVersion7(), kind = "person", name = "Nach Umstellung" })).CustomerNumber.ShouldBe($"KD{year:00}-0500");
    }

    [Fact]
    public async Task ParallelCreation_GetsDistinctNumbers()
    {
        var tasks = Enumerable.Range(1, 20).Select(async i =>
        {
            using var response = await _office.PostAsJsonAsync("/api/v1/contacts", new { id = Guid.CreateVersion7(), kind = "person", name = $"Parallel {i}" }, TestJson.Options, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            return (await response.Content.ReadFromJsonAsync<ContactResponse>(TestJson.Options, Ct))!.CustomerNumber;
        });
        var numbers = await Task.WhenAll(tasks);
        numbers.ShouldAllBe(n => n != null && n.Length == 6 && n.StartsWith("60", StringComparison.Ordinal));
        numbers.Distinct().Count().ShouldBe(20);
    }

    [Fact]
    public async Task ContactPersons_CanBeAddedChangedAndRemoved()
    {
        var company = await CreateContactAsync(new { id = Guid.CreateVersion7(), kind = "company", name = "Wohnbau Süd GmbH" });
        var person = Guid.CreateVersion7();
        using (var saved = await _office.PutAsJsonAsync($"/api/v1/contacts/{company.Id}/persons/{person}",
                   new { name = "Thomas Berger", function = "Bauleitung", phone = "0171 2223334", email = "berger@wohnbau-sued.de" }, TestJson.Options, Ct))
        {
            saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await _office.PutAsJsonAsync($"/api/v1/contacts/{company.Id}/persons/{person}", new { name = "Thomas Berger", function = "Projektleitung" }, TestJson.Options, Ct))
            .EnsureSuccessStatusCode();
        var list = await _office.GetFromJsonAsync<List<ContactPersonResponse>>($"/api/v1/contacts/{company.Id}/persons", TestJson.Options, Ct);
        list.ShouldHaveSingleItem().Function.ShouldBe("Projektleitung");
        list![0].Phone.ShouldBeNull();

        // Ansprechpartner eines anderen Kunden lässt sich nicht über diesen ändern; Name ist Pflicht.
        var other = await CreateContactAsync(new { id = Guid.CreateVersion7(), kind = "company", name = "Andere GmbH" });
        (await _office.PutAsJsonAsync($"/api/v1/contacts/{other.Id}/persons/{person}", new { name = "X" }, TestJson.Options, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _office.PutAsJsonAsync($"/api/v1/contacts/{company.Id}/persons/{Guid.CreateVersion7()}", new { name = " " }, TestJson.Options, Ct))
            .StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
        (await _office.PutAsJsonAsync($"/api/v1/contacts/{Guid.CreateVersion7()}/persons/{Guid.CreateVersion7()}", new { name = "X" }, TestJson.Options, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await _office.DeleteAsync(new Uri($"/api/v1/contacts/{other.Id}/persons/{person}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _office.DeleteAsync(new Uri($"/api/v1/contacts/{company.Id}/persons/{person}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await _office.GetFromJsonAsync<List<ContactPersonResponse>>($"/api/v1/contacts/{company.Id}/persons", TestJson.Options, Ct))!.ShouldBeEmpty();

        // Baustellenkräfte dürfen Kunden lesen, aber keine Ansprechpartner pflegen.
        using var worker = await _factory.CreateClientAsAsync("baustelle.person", StandardRoles.SiteWorkerId);
        (await worker.PutAsJsonAsync($"/api/v1/contacts/{company.Id}/persons/{Guid.CreateVersion7()}", new { name = "X" }, TestJson.Options, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await worker.PutAsJsonAsync("/api/v1/contacts/numbering", new { nextNumber = 1 }, TestJson.Options, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task KeyFigures_CountVisibleBauaktenAreaAndOpenWork()
    {
        var customer = await CreateContactAsync(new { id = Guid.CreateVersion7(), kind = "person", name = "Familie Kraus" });
        var planned = await CreateBauakteAsync(_office, new
        {
            id = Guid.CreateVersion7(), title = "EFH Kraus", customerId = customer.Id, street = "Hauptstraße 3", postalCode = "84028", city = "Landshut",
            project = new { totalArea = 120.5m },
        });
        await CreateBauakteAsync(_office, new { id = Guid.CreateVersion7(), title = "Garage Kraus", customerId = customer.Id, status = "inquiry", project = new { totalArea = 30m } });
        await CreateBauakteAsync(_office, new { id = Guid.CreateVersion7(), title = "Fremd", postalCode = "84028" });
        (await _office.PostAsJsonAsync("/api/v1/work-items", new { id = Guid.CreateVersion7(), bauakteId = planned.Id, kind = "task", title = "Angebot nachfassen" }, TestJson.Options, Ct))
            .EnsureSuccessStatusCode();
        (await _office.PostAsJsonAsync("/api/v1/work-items", new { id = Guid.CreateVersion7(), bauakteId = planned.Id, kind = "defect", title = "Riss im Flur" }, TestJson.Options, Ct))
            .EnsureSuccessStatusCode();

        var figures = await _office.GetFromJsonAsync<CustomerKeyFigures>($"/api/v1/contacts/{customer.Id}/key-figures", TestJson.Options, Ct);
        figures!.BauaktenTotal.ShouldBe(2);
        figures.BauaktenActive.ShouldBe(1);
        figures.Inquiries.ShouldBe(1);
        figures.TotalArea.ShouldBe(150.5m);
        figures.OpenTasks.ShouldBe(1);
        figures.OpenDefects.ShouldBe(1);
        figures.LastActivity.ShouldNotBeNull();

        // Unbekannter Kunde: leere Kennzahlen.
        (await _office.GetFromJsonAsync<CustomerKeyFigures>($"/api/v1/contacts/{Guid.CreateVersion7()}/key-figures", TestJson.Options, Ct))!.BauaktenTotal.ShouldBe(0);

        // Baustellenkraft sieht nur eigene Bauakten: die Bauakten des Büros zählen für sie nicht.
        using var worker = await _factory.CreateClientAsAsync("baustelle.kennzahlen", StandardRoles.SiteWorkerId);
        (await worker.GetFromJsonAsync<CustomerKeyFigures>($"/api/v1/contacts/{customer.Id}/key-figures", TestJson.Options, Ct))!.BauaktenTotal.ShouldBe(0);

        // Bauakten an derselben Adresse (PLZ-Filter der Liste).
        var page = await _office.GetFromJsonAsync<BauaktePage>("/api/v1/bauakten?postalCode=84028&status=all", TestJson.Options, Ct);
        page!.Items.Select(b => b.Title).ShouldBe(["EFH Kraus", "Fremd"], ignoreOrder: true);
    }

    [Fact]
    public async Task ExhaustedSequence_IsReportedClearly()
    {
        using var admin = await _factory.CreateClientAsAsync("chef.erschoepft", StandardRoles.AdministratorId);
        (await admin.PutAsJsonAsync("/api/v1/contacts/numbering", new { nextNumber = 9999 }, TestJson.Options, Ct)).EnsureSuccessStatusCode();
        (await CreateContactAsync(new { id = Guid.CreateVersion7(), kind = "person", name = "Letzter" })).CustomerNumber.ShouldBe("609999");

        using var response = await _office.PostAsJsonAsync("/api/v1/contacts", new { id = Guid.CreateVersion7(), kind = "person", name = "Zu viel" }, TestJson.Options, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain("CUSTOMER_NUMBERS_EXHAUSTED");
        var numbering = (await _office.GetFromJsonAsync<CustomerNumberingResponse>("/api/v1/contacts/numbering", TestJson.Options, Ct))!;
        numbering.Remaining.ShouldBe(0);
    }
}
