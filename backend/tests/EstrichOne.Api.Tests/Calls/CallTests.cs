using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Calls;
using EstrichOne.Api.Modules.Contacts;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Calls;

/// <summary>Anrufe und Anfragen wie in Estrich Web: Gesprächsnotiz, Wiedervorlage, Anfrage-Status, Umwandlung in Bauakte.</summary>
public sealed class CallTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static async Task<CallResponse> SaveAsync(HttpClient client, object body, Guid id)
    {
        using var response = await client.PutAsJsonAsync($"/api/v1/calls/{id}", body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<CallResponse>(TestJson.Options, Ct))!;
    }

    [Fact]
    public async Task Call_WithFollowUp_AppearsAsDueUntilDone()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var id = Guid.CreateVersion7();

        var call = await SaveAsync(office, new
        {
            id, kind = "call", direction = "inbound", callerName = "Herr Kraus", callerPhone = "0871 123456", subject = "Rückfrage Termin",
            notes = "Möchte Termin vorziehen", followUp = true,
        }, id);
        var due = await office.GetFromJsonAsync<List<CallResponse>>("/api/v1/calls?kind=call&due=true", TestJson.Options, Ct);
        (await office.PostAsync(new Uri($"/api/v1/calls/{id}/follow-up-done", UriKind.Relative), null, Ct)).EnsureSuccessStatusCode();
        var afterwards = await office.GetFromJsonAsync<List<CallResponse>>("/api/v1/calls?due=true", TestJson.Options, Ct);

        call.HandledByName.ShouldBe("Max Huber");
        call.FollowUp.ShouldBeTrue();
        due!.Single().Id.ShouldBe(id);
        afterwards!.ShouldBeEmpty();
        (await office.GetFromJsonAsync<List<CallResponse>>("/api/v1/calls?search=kraus", TestJson.Options, Ct))!.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Call_WithoutAnyCaller_IsRejected_AndWorkerCannotWrite()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var id = Guid.CreateVersion7();

        using var empty = await office.PutAsJsonAsync($"/api/v1/calls/{id}", new { id, kind = "call", notes = "?" }, Ct);
        using var forbidden = await worker.PutAsJsonAsync($"/api/v1/calls/{id}", new { id, kind = "call", callerName = "X" }, Ct);

        empty.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Inquiry_GoesThroughStatus_AndConvertsToBauakteWithNewCustomer()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var id = Guid.CreateVersion7();
        await SaveAsync(office, new
        {
            id, kind = "inquiry", source = "email", callerName = "Petra Lang", company = "Lang Bau GmbH", callerEmail = "lang@example.org",
            subject = "Heizestrich Neubau Halle", notes = "Halle mit Fußbodenheizung", areaSquareMeters = 380.5m, screedType = "CAF",
            desiredStart = "2026-12-01", siteCity = "Landshut", sitePostalCode = "84028",
        }, id);

        (await office.PostAsJsonAsync($"/api/v1/calls/{id}/status", new { status = "inProgress" }, Ct)).EnsureSuccessStatusCode();
        using var convert = await office.PostAsJsonAsync($"/api/v1/calls/{id}/convert", new { }, Ct);
        convert.StatusCode.ShouldBe(HttpStatusCode.OK, await convert.Content.ReadAsStringAsync(Ct));
        var result = (await convert.Content.ReadFromJsonAsync<ConvertInquiryResponse>(TestJson.Options, Ct))!;
        using var again = await office.PostAsJsonAsync($"/api/v1/calls/{id}/convert", new { }, Ct);
        using var reopen = await office.PostAsJsonAsync($"/api/v1/calls/{id}/status", new { status = "open" }, Ct);

        result.Inquiry.InquiryStatus.ShouldBe(InquiryStatus.Converted);
        result.Inquiry.CustomerName.ShouldBe("Lang Bau GmbH");
        result.BauakteNumber.ShouldNotBeNull();
        var bauakte = await office.GetFromJsonAsync<BauakteDetail>($"/api/v1/bauakten/{result.BauakteId}", TestJson.Options, Ct);
        bauakte!.Title.ShouldBe("Heizestrich Neubau Halle");
        bauakte.Status.ShouldBe(BauakteStatus.Planned);
        bauakte.City.ShouldBe("Landshut");
        bauakte.Description!.ShouldContain("Fläche laut Anfrage: 380,5 m²");
        var customer = await office.GetFromJsonAsync<ContactResponse>($"/api/v1/contacts/{bauakte.CustomerId}", TestJson.Options, Ct);
        customer!.Email.ShouldBe("lang@example.org");
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        reopen.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await office.GetFromJsonAsync<List<CallResponse>>("/api/v1/calls?kind=inquiry&open=true", TestJson.Options, Ct))!.ShouldBeEmpty();
    }
}
