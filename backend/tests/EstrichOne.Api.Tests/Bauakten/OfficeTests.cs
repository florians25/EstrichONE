using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Contacts;
using EstrichOne.Api.Modules.Work;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Bauakten;

/// <summary>Büroarbeit an der Bauakte (AP-18): Räume, Bereiche, Notizen, Aufgaben, Kunden, Anfragen.</summary>
public sealed class OfficeTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static async Task<BauakteDetail> CreateAsync(HttpClient client, object body)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/bauakten", body, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;
    }

    [Fact]
    public async Task Rooms_WithSectionsAndBuildup_SumAreas()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = await CreateAsync(office, new { id = Guid.CreateVersion7(), title = "MFH Ahornweg" });
        var types = await office.GetFromJsonAsync<List<ScreedTypeResponse>>("/api/v1/screed-types", TestJson.Options, Ct);
        var cement = types!.First(t => t.Binder == ScreedBinder.Cement);
        var house = Guid.CreateVersion7();

        (await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/sections/{house}", new { id = house, name = "Haus A" }, Ct)).EnsureSuccessStatusCode();
        async Task<RoomOverview> SaveRoomAsync(Guid room, string name, decimal area, bool heated, Guid? section)
        {
            using var response = await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/rooms/{room}", new
            {
                id = room, sectionId = section, name, floor = "EG", areaSquareMeters = area, screedTypeId = cement.Id, thicknessMillimeters = 65,
                insulationMillimeters = 30, underfloorHeating = heated, status = "planned",
            }, Ct);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<RoomOverview>(TestJson.Options, Ct))!;
        }

        await SaveRoomAsync(Guid.CreateVersion7(), "Wohnen", 32.5m, true, house);
        var bath = Guid.CreateVersion7();
        var overview = await SaveRoomAsync(bath, "Bad", 8.25m, false, null);

        overview.Sections.ShouldHaveSingleItem().Name.ShouldBe("Haus A");
        overview.Rooms.Count.ShouldBe(2);
        overview.TotalAreaSquareMeters.ShouldBe(40.75m);
        overview.HeatedAreaSquareMeters.ShouldBe(32.5m);
        overview.Rooms.ShouldAllBe(r => r.ScreedTypeName != null && r.ScreedTypeName.StartsWith(cement.Name));

        using var invalid = await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/rooms/{bath}", new
        {
            id = bath, name = "Bad", thicknessMillimeters = 5, underfloorHeating = false, status = "planned",
        }, Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        using var deleted = await office.DeleteAsync(new Uri($"/api/v1/bauakten/{bauakte.Id}/rooms/{bath}", UriKind.Relative), Ct);
        (await deleted.Content.ReadFromJsonAsync<RoomOverview>(TestJson.Options, Ct))!.TotalAreaSquareMeters.ShouldBe(32.5m);
        (await office.DeleteAsync(new Uri($"/api/v1/bauakten/{bauakte.Id}/sections/{house}", UriKind.Relative), Ct)).EnsureSuccessStatusCode();
        var after = await office.GetFromJsonAsync<RoomOverview>($"/api/v1/bauakten/{bauakte.Id}/rooms", TestJson.Options, Ct);
        after!.Rooms.ShouldHaveSingleItem().SectionId.ShouldBeNull("Räume bleiben ohne Bereich erhalten.");
    }

    [Fact]
    public async Task SiteWorker_CannotSeeRoomsOfForeignBauakte()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var bauakte = await CreateAsync(office, new { id = Guid.CreateVersion7(), title = "Halle" });

        using var response = await worker.GetAsync(new Uri($"/api/v1/bauakten/{bauakte.Id}/rooms", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task NotesAndWorkItems_FromTheBrowser()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var worker = await _factory.CreateUserAsync("baustelle", roleIds: StandardRoles.SiteWorkerId);
        var bauakte = await CreateAsync(office, new { id = Guid.CreateVersion7(), title = "EFH Berger" });
        var note = Guid.CreateVersion7();

        using var created = await office.PostAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/notes", new { id = note, text = "Kunde wünscht Termin vormittags" }, Ct);
        using var repeated = await office.PostAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/notes", new { id = note, text = "Kunde wünscht Termin vormittags" }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        repeated.StatusCode.ShouldBe(HttpStatusCode.OK);

        var item = Guid.CreateVersion7();
        using var task = await office.PostAsJsonAsync("/api/v1/work-items", new
        {
            id = item, bauakteId = bauakte.Id, kind = "defect", title = "Randfuge offen", dueDate = "2026-11-02", assigneeUserId = worker.Id,
        }, Ct);
        task.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await office.PatchAsJsonAsync($"/api/v1/work-items/{item}", new { status = "done" }, Ct)).EnsureSuccessStatusCode();

        var items = await office.GetFromJsonAsync<List<WorkItemDto>>($"/api/v1/bauakten/{bauakte.Id}/work-items", TestJson.Options, Ct);
        items.ShouldHaveSingleItem().Status.ShouldBe(WorkItemStatus.Done);
        items[0].AssigneeUserId.ShouldBe(worker.Id);
        var history = await office.GetFromJsonAsync<List<BauakteHistoryEntry>>($"/api/v1/bauakten/{bauakte.Id}/history", TestJson.Options, Ct);
        history!.Select(h => h.Action).ShouldContain("bauakte.workitem_done");
        var notes = await office.GetFromJsonAsync<List<NoteDto>>($"/api/v1/bauakten/{bauakte.Id}/notes", TestJson.Options, Ct);
        notes.ShouldHaveSingleItem().Text.ShouldBe("Kunde wünscht Termin vormittags");
    }

    [Fact]
    public async Task Inquiry_BecomesOrder_AndCustomerCanBeEdited()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var customer = Guid.CreateVersion7();
        using var createdCustomer = await office.PostAsJsonAsync("/api/v1/contacts", new { id = customer, kind = "person", name = "Familie Berger" }, Ct);
        createdCustomer.StatusCode.ShouldBe(HttpStatusCode.Created, await createdCustomer.Content.ReadAsStringAsync(Ct));
        var contact = (await createdCustomer.Content.ReadFromJsonAsync<ContactResponse>(TestJson.Options, Ct))!;

        using var update = await office.PutAsJsonAsync($"/api/v1/contacts/{customer}", new
        {
            version = contact.Version, kind = "person", name = "Familie Berger", customerNumber = "K-1001", city = "Landshut", phone = "0871 123",
        }, Ct);
        using var stale = await office.PutAsJsonAsync($"/api/v1/contacts/{customer}", new { version = contact.Version, kind = "person", name = "X" }, Ct);
        update.StatusCode.ShouldBe(HttpStatusCode.OK, await update.Content.ReadAsStringAsync(Ct));
        (await update.Content.ReadFromJsonAsync<ContactResponse>(TestJson.Options, Ct))!.City.ShouldBe("Landshut");
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        var inquiry = await CreateAsync(office, new { id = Guid.CreateVersion7(), title = "Anfrage Berger, Heizestrich 120 m²", customerId = customer, status = "inquiry" });
        inquiry.Status.ShouldBe(BauakteStatus.Inquiry);
        var inquiries = await office.GetFromJsonAsync<BauaktePage>("/api/v1/bauakten?status=inquiry", TestJson.Options, Ct);
        inquiries!.Items.ShouldHaveSingleItem().Id.ShouldBe(inquiry.Id);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/bauakten/{inquiry.Id}")
        {
            Content = JsonContent.Create(new
            {
                title = inquiry.Title, status = "planned", street = (string?)null, postalCode = (string?)null, city = "Landshut", latitude = (double?)null,
                longitude = (double?)null, description = (string?)null, customerId = customer, responsibleUserId = inquiry.ResponsibleUserId,
            }),
        };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{inquiry.Version}\""));
        using var order = await office.SendAsync(request, Ct);
        (await order.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!.Status.ShouldBe(BauakteStatus.Planned);
    }
}
