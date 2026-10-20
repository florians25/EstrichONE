using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Bauakten;

public sealed class BauakteApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Create_WithOnlyTitle_AssignsNumberAndIsIdempotent()
    {
        using var client = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var request = new { id = Guid.CreateVersion7(), title = "EFH Müller, Neubau" };

        using var first = await client.PostAsJsonAsync("/api/v1/bauakten", request, TestContext.Current.CancellationToken);
        using var repeat = await client.PostAsJsonAsync("/api/v1/bauakten", request, TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        repeat.StatusCode.ShouldBe(HttpStatusCode.OK);
        var created = await first.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, TestContext.Current.CancellationToken);
        var repeated = await repeat.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, TestContext.Current.CancellationToken);
        created!.Number.ShouldNotBeNull();
        created.Number.ShouldMatch(@"^\d{4}-\d{3,}$");
        repeated!.Number.ShouldBe(created.Number);
        created.Status.ShouldBe(BauakteStatus.Planned);
    }

    [Fact]
    public async Task Create_SameIdDifferentContent_IsConflict()
    {
        using var client = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var id = Guid.CreateVersion7();
        (await client.PostAsJsonAsync("/api/v1/bauakten", new { id, title = "Halle Nord" }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        using var response = await client.PostAsJsonAsync("/api/v1/bauakten", new { id, title = "Etwas anderes" }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadProblemAsync()).Code.ShouldBe("ID_CONFLICT");
    }

    [Fact]
    public async Task Create_WithUnknownCustomer_IsRejected()
    {
        using var client = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);

        using var response = await client.PostAsJsonAsync("/api/v1/bauakten",
            new { id = Guid.CreateVersion7(), title = "Schule", customerId = Guid.CreateVersion7() }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Code.ShouldBe("UNKNOWN_CUSTOMER");
    }

    [Fact]
    public async Task SiteWorker_SeesOnlyOwnBauakten_OfficeSeesAll()
    {
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var colleague = await _factory.CreateClientAsAsync("kollege", StandardRoles.SiteWorkerId);
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var mine = await CreateAsync(worker, "Meine Baustelle");
        var foreign = await CreateAsync(colleague, "Fremde Baustelle");

        var workerList = await worker.GetFromJsonAsync<BauaktePage>("/api/v1/bauakten", TestJson.Options, TestContext.Current.CancellationToken);
        var officeList = await office.GetFromJsonAsync<BauaktePage>("/api/v1/bauakten", TestJson.Options, TestContext.Current.CancellationToken);
        using var foreignDetail = await worker.GetAsync(new Uri($"/api/v1/bauakten/{foreign.Id}", UriKind.Relative), TestContext.Current.CancellationToken);

        workerList!.Items.Select(i => i.Id).ShouldBe([mine.Id]);
        officeList!.Items.Select(i => i.Id).ShouldBe([mine.Id, foreign.Id], ignoreOrder: true);
        foreignDetail.StatusCode.ShouldBe(HttpStatusCode.NotFound, "Fremde Bauakten dürfen nicht einmal ihre Existenz verraten.");
    }

    [Fact]
    public async Task UserWithoutRole_CannotCreateOrRead()
    {
        using var client = await _factory.CreateClientAsAsync("ohne.rolle");

        using var create = await client.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title = "x" }, TestContext.Current.CancellationToken);
        using var list = await client.GetAsync(new Uri("/api/v1/bauakten", UriKind.Relative), TestContext.Current.CancellationToken);

        create.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        list.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Update_RequiresMatchingVersion()
    {
        using var client = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var bauakte = await CreateAsync(client, "Reihenhaus");
        var change = new { title = "Reihenhaus Mitte", status = "inProgress", street = "Musterstr. 12", postalCode = "84028", city = "Landshut",
            latitude = (double?)null, longitude = (double?)null, description = (string?)null, customerId = (Guid?)null,
            responsibleUserId = bauakte.ResponsibleUserId };

        using var withoutVersion = await client.PutAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}", change, TestContext.Current.CancellationToken);
        withoutVersion.StatusCode.ShouldBe((HttpStatusCode)428);

        using var updated = await PutAsync(client, bauakte.Id, change, bauakte.Version);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var result = await updated.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, TestContext.Current.CancellationToken);
        result!.Version.ShouldBe(bauakte.Version + 1);
        result.Status.ShouldBe(BauakteStatus.InProgress);
        updated.Headers.ETag!.Tag.ShouldBe($"\"{result.Version}\"");

        using var stale = await PutAsync(client, bauakte.Id, change, bauakte.Version);
        stale.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await stale.ReadProblemAsync()).Code.ShouldBe("VERSION_CONFLICT");
    }

    [Fact]
    public async Task Delete_RequiresPermissionAndHidesBauakte()
    {
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = await CreateAsync(worker, "Garage");

        var path = new Uri($"/api/v1/bauakten/{bauakte.Id}/delete", UriKind.Relative);
        (await worker.PostAsJsonAsync(path, new { password = AuthClient.DefaultPassword }, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var wrong = await office.PostAsJsonAsync(path, new { password = "falsch-falsch" }, TestContext.Current.CancellationToken);
        wrong.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await wrong.ReadProblemAsync()).Code.ShouldBe("PASSWORD_CONFIRMATION_FAILED");
        (await office.GetAsync(new Uri($"/api/v1/bauakten/{bauakte.Id}", UriKind.Relative), TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await office.PostAsJsonAsync(path, new { password = AuthClient.DefaultPassword }, TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await office.GetAsync(new Uri($"/api/v1/bauakten/{bauakte.Id}", UriKind.Relative), TestContext.Current.CancellationToken))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_PagesWithCursorWithoutGapsAndSupportsSearch()
    {
        using var client = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var created = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            _factory.Clock.Advance(TimeSpan.FromSeconds(1));
            created.Add((await CreateAsync(client, $"Objekt {i}")).Id);
        }

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            var page = await client.GetFromJsonAsync<BauaktePage>(
                "/api/v1/bauakten?limit=2" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)), TestJson.Options, TestContext.Current.CancellationToken);
            seen.AddRange(page!.Items.Select(item => item.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        seen.ShouldBe(Enumerable.Reverse(created).ToList());
        var search = await client.GetFromJsonAsync<BauaktePage>("/api/v1/bauakten?search=objekt%203", TestJson.Options, TestContext.Current.CancellationToken);
        search!.Items.ShouldHaveSingleItem().Title.ShouldBe("Objekt 3");
    }

    private static async Task<BauakteDetail> CreateAsync(HttpClient client, string title)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options))!;
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient client, Guid id, object body, long version)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/bauakten/{id}") { Content = JsonContent.Create(body) };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{version}\""));
        return client.SendAsync(request);
    }
}
