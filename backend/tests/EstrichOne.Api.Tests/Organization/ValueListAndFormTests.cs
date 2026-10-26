using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Organization;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;

namespace EstrichOne.Api.Tests.Organization;

public sealed class ValueListAndFormTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task NewScreedType_IsAcceptedForBauakte_AndCustomFieldsLandInTheSchema()
    {
        using var admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        var lists = await admin.GetFromJsonAsync<List<ValueListItemResponse>>("/api/v1/value-lists", TestJson.Options, Ct);
        lists!.ShouldContain(i => i.List == "screedType" && i.Key == "ct");

        var id = Guid.CreateVersion7();
        using var rejected = await admin.PostAsJsonAsync("/api/v1/bauakten", new { id, title = "Halle", project = new { screedType = "mgf" } }, Ct);
        rejected.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        using var added = await admin.PutAsJsonAsync($"/api/v1/value-lists/{Guid.CreateVersion7()}",
            new { list = "screedType", key = "mgf", label = "Magnesiaestrich (MA)", sortOrder = 60 }, Ct);
        added.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var created = await admin.PostAsJsonAsync("/api/v1/bauakten",
            new { id, title = "Halle", project = new { screedType = "mgf", custom = new { zufahrt = 3.5 } } }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await created.Content.ReadAsStringAsync(Ct)).ShouldContain("\"zufahrt\":3.5");

        var schema = await admin.GetFromJsonAsync<FormSchemaResponse>("/api/v1/forms/bauakte", TestJson.Options, Ct);
        schema!.Lists!["screedType"].ShouldContain(o => o.Key == "mgf");
        var groups = schema.Groups.Append(new FormSchemaGroup("zugang", "Zugang",
            [new FormSchemaField("zufahrt", "Zufahrtsbreite", "number", Unit: "m", Required: true)])).ToList();
        using var saved = await admin.PutAsJsonAsync("/api/v1/forms/bauakte", new { groups }, Ct);
        saved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await saved.Content.ReadFromJsonAsync<FormSchemaResponse>(TestJson.Options, Ct);
        body!.Groups[^1].Fields[0].Path.ShouldBe("project.custom.zufahrt");
        body.Groups[0].Fields[0].Builtin.ShouldBeTrue();
    }

    [Fact]
    public async Task InitialInspection_FillsBasisdaten_FromSchemaTargets()
    {
        using var admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        var schema = await admin.GetFromJsonAsync<FormSchemaResponse>("/api/v1/forms/erstbegehung", TestJson.Options, Ct);
        schema!.Groups.SelectMany(g => g.Fields).ShouldContain(f => f.Key == "measuredArea" && f.Target == "project.totalArea");

        var id = Guid.CreateVersion7();
        (await admin.PostAsJsonAsync("/api/v1/bauakten", new { id, title = "EFH Messung" }, Ct)).EnsureSuccessStatusCode();
        var result = await admin.PushAsync(SyncClient.Op("inspection.create", Guid.CreateVersion7(), new
        {
            bauakteId = id, kind = "initialInspection", checks = new { substrate = "ok" },
            values = new { measuredArea = 87.5, screedType = "caf" }, checkRemarks = new { substrate = "eben" }, schemaVersion = schema.Version,
        }));
        result.Results[0].Status.ShouldBe(SyncOperationStatus.Applied);
        var body = await admin.GetStringAsync($"/api/v1/bauakten/{id}", Ct);
        body.ShouldContain("\"totalArea\":87.5");
        body.ShouldContain("\"screedType\":\"caf\"");
    }
}
