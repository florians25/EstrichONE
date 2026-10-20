using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Materials;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Materials;

/// <summary>Materialstamm, Lieferanten und Bestellungen (AP-20) mit Lieferhinweisen in der Bauakte.</summary>
public sealed class MaterialOrderTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_factory.Clock.GetUtcNow(), Berlin).DateTime);

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static async Task<T> PutAsync<T>(HttpClient client, string path, object body)
    {
        using var response = await client.PutAsJsonAsync(path, body, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Ct))!;
    }

    private static async Task<(SupplierResponse Supplier, MaterialResponse Material)> MasterDataAsync(HttpClient office)
    {
        var supplierId = Guid.CreateVersion7();
        var supplier = await PutAsync<SupplierResponse>(office, $"/api/v1/suppliers/{supplierId}", new
        {
            id = supplierId, name = "Baustoff Huber KG", customerNumber = "K-4711", phone = "0871 99887", email = "bestellung@huber.example",
            orderNotes = "Bis 12 Uhr für Lieferung am Folgetag",
        });
        var materialId = Guid.CreateVersion7();
        var material = await PutAsync<MaterialResponse>(office, $"/api/v1/materials/{materialId}", new
        {
            id = materialId, name = "Zementestrich CT-C25-F4 Sackware", articleNumber = "ZE-25", category = "screed", unit = "Sack",
            supplierId, price = 6.45m,
        });
        return (supplier, material);
    }

    [Fact]
    public async Task Order_IsNumbered_TakesSiteAddress_AndGoesThroughStatus()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var (supplier, material) = await MasterDataAsync(office);
        using var created = await office.PostAsJsonAsync("/api/v1/bauakten",
            new { id = Guid.CreateVersion7(), title = "EFH Kraus", street = "Ahornweg 3", postalCode = "84028", city = "Landshut" }, Ct);
        var bauakte = (await created.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;

        var id = Guid.CreateVersion7();
        var order = await PutAsync<OrderResponse>(office, $"/api/v1/material-orders/{id}", new
        {
            id, supplierId = supplier.Id, bauakteId = bauakte.Id, deliveryDate = Today.AddDays(1), deliveryTime = "7:00–8:00",
            lines = new object[]
            {
                new { id = Guid.CreateVersion7(), materialId = material.Id, description = material.Name, quantity = 120, unit = "Sack", unitPrice = 6.45m },
                new { id = Guid.CreateVersion7(), description = "Randdämmstreifen 8 mm", quantity = 4, unit = "Rolle" },
            },
        });

        order.Number.ShouldBe($"B-{Today.Year}-0001");
        order.Status.ShouldBe(OrderStatus.Draft);
        order.DeliveryCity.ShouldBe("Landshut");
        order.DeliveryStreet.ShouldBe("Ahornweg 3");
        order.Lines.Count.ShouldBe(2);
        order.Lines[0].ArticleNumber.ShouldBe("ZE-25");
        order.Lines[0].Total.ShouldBe(774.00m);
        order.Total.ShouldBe(774.00m);
        order.CreatedByName.ShouldBe("Max Huber");

        var hints = await office.GetFromJsonAsync<List<BauakteHint>>($"/api/v1/bauakten/{bauakte.Id}/hints", TestJson.Options, Ct);
        hints!.ShouldContain(h => h.Kind == "order_draft");

        (await office.PostAsJsonAsync($"/api/v1/material-orders/{id}/status", new { status = "ordered" }, Ct)).EnsureSuccessStatusCode();
        hints = await office.GetFromJsonAsync<List<BauakteHint>>($"/api/v1/bauakten/{bauakte.Id}/hints", TestJson.Options, Ct);
        hints!.ShouldContain(h => h.Kind == "delivery_next");

        // Liefertermin vorgezogen: abgeschickte Bestellungen bleiben änderbar.
        order = await PutAsync<OrderResponse>(office, $"/api/v1/material-orders/{id}", new
        {
            id, supplierId = supplier.Id, bauakteId = bauakte.Id, deliveryDate = Today, deliveryTime = "7:00–8:00",
            lines = order.Lines.Select(l => new { l.Id, l.MaterialId, l.Description, l.Quantity, l.Unit, l.UnitPrice }),
        });
        order.Version.ShouldBe(3);
        hints = await office.GetFromJsonAsync<List<BauakteHint>>($"/api/v1/bauakten/{bauakte.Id}/hints", TestJson.Options, Ct);
        hints!.ShouldContain(h => h.Kind == "delivery_today" && h.Text.Contains("Baustoff Huber KG", StringComparison.Ordinal));
        var todays = await office.GetFromJsonAsync<List<OrderResponse>>($"/api/v1/material-orders?open=true&from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}",
            TestJson.Options, Ct);
        todays!.ShouldHaveSingleItem().Id.ShouldBe(id);

        using var delivered = await office.PostAsJsonAsync($"/api/v1/material-orders/{id}/status", new { status = "delivered" }, Ct);
        var result = (await delivered.Content.ReadFromJsonAsync<OrderResponse>(TestJson.Options, Ct))!;
        result.DeliveredAt.ShouldNotBeNull();
        result.OrderedAt.ShouldNotBeNull();
        hints = await office.GetFromJsonAsync<List<BauakteHint>>($"/api/v1/bauakten/{bauakte.Id}/hints", TestJson.Options, Ct);
        hints!.ShouldNotContain(h => h.Kind.StartsWith("delivery", StringComparison.Ordinal));

        using var edit = await office.PutAsJsonAsync($"/api/v1/material-orders/{id}", new { id, supplierId = supplier.Id, lines = Array.Empty<object>() }, Ct);
        using var delete = await office.DeleteAsync(new Uri($"/api/v1/material-orders/{id}", UriKind.Relative), Ct);
        edit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task EmptyOrder_CannotBeSent_AndInvalidLinesAreRejected()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var (supplier, _) = await MasterDataAsync(office);
        var id = Guid.CreateVersion7();
        await PutAsync<OrderResponse>(office, $"/api/v1/material-orders/{id}", new { id, supplierId = supplier.Id });

        using var send = await office.PostAsJsonAsync($"/api/v1/material-orders/{id}/status", new { status = "ordered" }, Ct);
        using var badLine = await office.PutAsJsonAsync($"/api/v1/material-orders/{id}", new
        {
            id, supplierId = supplier.Id, lines = new[] { new { id = Guid.CreateVersion7(), description = " ", quantity = 1, unit = "t" } },
        }, Ct);
        using var jump = await office.PostAsJsonAsync($"/api/v1/material-orders/{id}/status", new { status = "delivered" }, Ct);
        using var delete = await office.DeleteAsync(new Uri($"/api/v1/material-orders/{id}", UriKind.Relative), Ct);

        send.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        badLine.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        jump.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        delete.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task SupplierWithOpenOrders_CannotBeDeleted_AndWorkerHasNoAccess()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var (supplier, material) = await MasterDataAsync(office);
        var id = Guid.CreateVersion7();
        await PutAsync<OrderResponse>(office, $"/api/v1/material-orders/{id}", new
        {
            id, supplierId = supplier.Id, lines = new[] { new { id = Guid.CreateVersion7(), description = "Folie PE 0,2 mm", quantity = 2, unit = "Rolle" } },
        });
        (await office.PostAsJsonAsync($"/api/v1/material-orders/{id}/status", new { status = "ordered" }, Ct)).EnsureSuccessStatusCode();

        using var deleteSupplier = await office.DeleteAsync(new Uri($"/api/v1/suppliers/{supplier.Id}", UriKind.Relative), Ct);
        using var workerList = await worker.GetAsync(new Uri("/api/v1/material-orders", UriKind.Relative), Ct);
        (await office.DeleteAsync(new Uri($"/api/v1/materials/{material.Id}", UriKind.Relative), Ct)).EnsureSuccessStatusCode();

        deleteSupplier.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        workerList.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await office.GetFromJsonAsync<List<MaterialResponse>>("/api/v1/materials", TestJson.Options, Ct))!.ShouldBeEmpty();
        (await office.GetFromJsonAsync<List<MaterialResponse>>("/api/v1/materials?search=ze-25", TestJson.Options, Ct))!.ShouldBeEmpty();
        (await office.GetFromJsonAsync<List<OrderResponse>>("/api/v1/material-orders?search=folie", TestJson.Options, Ct))!.ShouldHaveSingleItem();
    }
}
