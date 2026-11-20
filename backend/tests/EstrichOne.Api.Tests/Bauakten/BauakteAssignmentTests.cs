using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Notifications;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;

namespace EstrichOne.Api.Tests.Bauakten;

/// <summary>
/// „Jeder sieht alle Bauakten, ändern nur Zugeteilte“: Lesen für alle mit Bauakten-Zugriff, selbst als Beteiligter
/// eintragen/austragen (protokolliert, Leitung und Disposition benachrichtigt), Schreiben nur zugeteilt – sonst
/// 403 BAUAKTE_NOT_ASSIGNED (REST) bzw. abgelehnt mit BAUAKTE_NOT_ASSIGNED (Sync). „Alle Bauakten bearbeiten“ darf alles.
/// </summary>
public sealed class BauakteAssignmentTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ApiFactory _factory = null!;
    private HttpClient _office = null!;

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

    private async Task<(Guid UserId, HttpClient Client)> WorkerAsync(string name = "lukas")
    {
        var user = await _factory.CreateUserAsync(name, roleIds: StandardRoles.SiteWorkerId);
        var client = _factory.CreateClient();
        var tokens = await client.LoginOkAsync(name);
        return (user.Id, client.WithToken(tokens.AccessToken));
    }

    private async Task<BauakteDetail> OfficeBauakteAsync(string title)
    {
        using var response = await _office.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;
    }

    private static async Task<HttpResponseMessage> RenameAsync(HttpClient client, BauakteDetail bauakte, string title)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/bauakten/{bauakte.Id}")
        {
            Content = JsonContent.Create(new { title, status = "planned" }, options: TestJson.Options),
        };
        var current = (await client.GetFromJsonAsync<BauakteDetail>($"/api/v1/bauakten/{bauakte.Id}", TestJson.Options, Ct))!;
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{current.Version}\""));
        return await client.SendAsync(request, Ct);
    }

    [Fact]
    public async Task Worker_SeesAllBauakten_ButOnlyViewing()
    {
        var (_, site) = await WorkerAsync();
        using var siteClient = site;
        var akte = await OfficeBauakteAsync("Fremde Baustelle");

        var page = (await site.GetFromJsonAsync<BauaktePage>("/api/v1/bauakten", TestJson.Options, Ct))!;
        var row = page.Items.ShouldHaveSingleItem();
        row.Id.ShouldBe(akte.Id);
        row.IsAssigned.ShouldBeFalse();
        row.CanEdit.ShouldBeFalse();
        (await site.GetFromJsonAsync<BauaktePage>("/api/v1/bauakten?assigned=true", TestJson.Options, Ct))!.Items.ShouldBeEmpty();

        var detail = (await site.GetFromJsonAsync<BauakteDetail>($"/api/v1/bauakten/{akte.Id}", TestJson.Options, Ct))!;
        detail.Title.ShouldBe("Fremde Baustelle");
        detail.CanEdit.ShouldBe(false);
        detail.IsAssigned.ShouldBe(false);
        (await site.GetAsync($"/api/v1/bauakten/{akte.Id}/members", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await site.GetAsync($"/api/v1/bauakten/{akte.Id}/history", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Offline liegen nur zugeteilte Bauakten auf dem Gerät (Datenmenge).
        var pull = await site.PullAsync(includeScope: true);
        pull.VisibleBauakteIds!.ShouldNotContain(akte.Id);
        pull.Changes.ShouldNotContain(c => c.Id == akte.Id);
    }

    [Fact]
    public async Task Worker_CannotChangeAnUnassignedBauakte()
    {
        var (_, site) = await WorkerAsync();
        using var siteClient = site;
        var akte = await OfficeBauakteAsync("Halle Nord");

        using var rename = await RenameAsync(site, akte, "Gekapert");
        rename.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await rename.ReadProblemAsync()).Code.ShouldBe("BAUAKTE_NOT_ASSIGNED");

        using var note = await site.PostAsJsonAsync($"/api/v1/bauakten/{akte.Id}/notes", new { id = Guid.CreateVersion7(), text = "Hallo" }, Ct);
        note.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await note.ReadProblemAsync()).Code.ShouldBe("BAUAKTE_NOT_ASSIGNED");

        using var task = await site.PostAsJsonAsync("/api/v1/work-items",
            new { id = Guid.CreateVersion7(), kind = "task", title = "Fremde Aufgabe", bauakteId = akte.Id }, Ct);
        task.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var file = await site.PostAsJsonAsync("/api/v1/files", new
        {
            id = Guid.CreateVersion7(), bauakteId = akte.Id, kind = "photo", originalName = "a.jpg", contentType = "image/jpeg",
            sizeBytes = 10, sha256 = new string('a', 64),
        }, Ct);
        file.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await file.ReadProblemAsync()).Code.ShouldBe("BAUAKTE_NOT_ASSIGNED");

        var results = (await site.PushAsync(
            SyncClient.Op("note.create", Guid.CreateVersion7(), new { bauakteId = akte.Id, text = "Offline geschrieben" }),
            SyncClient.Op("room.create", Guid.CreateVersion7(), new { bauakteId = akte.Id, name = "Bad" }),
            SyncClient.Op("bauakte.setStatus", akte.Id, new { status = "inProgress", baseStatus = "planned" }))).Results;
        results.ShouldAllBe(r => r.Status == SyncOperationStatus.Rejected && r.Code == SyncOperationResult.NotAssignedCode);

        // Nichts geändert.
        (await _office.GetFromJsonAsync<BauakteDetail>($"/api/v1/bauakten/{akte.Id}", TestJson.Options, Ct))!.Title.ShouldBe("Halle Nord");
    }

    [Fact]
    public async Task SelfEnrolment_UnlocksEditing_IsAudited_AndNotifiesTheLead()
    {
        var (workerId, site) = await WorkerAsync();
        using var siteClient = site;
        var akte = await OfficeBauakteAsync("EFH Brunner");

        using var join = await site.PostAsync($"/api/v1/bauakten/{akte.Id}/members/me", null, Ct);
        join.StatusCode.ShouldBe(HttpStatusCode.OK);

        var detail = (await site.GetFromJsonAsync<BauakteDetail>($"/api/v1/bauakten/{akte.Id}", TestJson.Options, Ct))!;
        detail.IsAssigned.ShouldBe(true);
        detail.CanEdit.ShouldBe(true);
        using (var rename = await RenameAsync(site, akte, "EFH Brunner, Anbau"))
        {
            rename.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var note = (await site.PushAsync(SyncClient.Op("note.create", Guid.CreateVersion7(), new { bauakteId = akte.Id, text = "Bin dabei" }))).Results.Single();
        note.Status.ShouldBe(SyncOperationStatus.Applied);

        // Jetzt auch offline auf dem Gerät und bearbeitbar.
        var pull = await site.PullAsync(includeScope: true);
        pull.VisibleBauakteIds!.ShouldContain(akte.Id);
        pull.AssignedBauakteIds!.ShouldContain(akte.Id);

        var history = (await _office.GetFromJsonAsync<List<BauakteHistoryEntry>>($"/api/v1/bauakten/{akte.Id}/history", TestJson.Options, Ct))!;
        history.ShouldContain(h => h.Action == "bauakte.member_joined" && h.ActorUserId == workerId && h.Text.Contains("als Beteiligter eingetragen"));

        // Projektleitung (zuständig: Büro, das die Bauakte angelegt hat) erfährt es.
        var inbox = (await _office.GetFromJsonAsync<NotificationInbox>("/api/v1/notifications", TestJson.Options, Ct))!;
        inbox.Items.ShouldContain(n => n.ObjectId == akte.Id && n.Title.Contains("eingetragen"));
    }

    [Fact]
    public async Task Leaving_MakesTheBauakteViewOnlyAgain_TheResponsiblePersonCannotLeave()
    {
        var (_, site) = await WorkerAsync();
        using var siteClient = site;
        var akte = await OfficeBauakteAsync("Praxis Dr. Klein");
        (await site.PostAsync($"/api/v1/bauakten/{akte.Id}/members/me", null, Ct)).EnsureSuccessStatusCode();

        using var leave = await site.DeleteAsync($"/api/v1/bauakten/{akte.Id}/members/me", Ct);
        leave.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await site.GetFromJsonAsync<BauakteDetail>($"/api/v1/bauakten/{akte.Id}", TestJson.Options, Ct))!.CanEdit.ShouldBe(false);
        var history = (await _office.GetFromJsonAsync<List<BauakteHistoryEntry>>($"/api/v1/bauakten/{akte.Id}/history", TestJson.Options, Ct))!;
        history.ShouldContain(h => h.Action == "bauakte.member_left");

        using var responsible = await _office.DeleteAsync($"/api/v1/bauakten/{akte.Id}/members/me", Ct);
        responsible.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await responsible.ReadProblemAsync()).Code.ShouldBe("BAUAKTE_RESPONSIBLE");
    }

    [Fact]
    public async Task EditAll_MayChangeEveryBauakte_WithoutBeingAssigned()
    {
        var akte = await OfficeBauakteAsync("Wohnanlage Süd");
        using var colleague = await _factory.CreateClientAsAsync("buero2", StandardRoles.OfficeId);

        var detail = (await colleague.GetFromJsonAsync<BauakteDetail>($"/api/v1/bauakten/{akte.Id}", TestJson.Options, Ct))!;
        detail.IsAssigned.ShouldBe(false);
        detail.CanEdit.ShouldBe(true);
        using var rename = await RenameAsync(colleague, akte, "Wohnanlage Süd, BA 2");
        rename.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OthersAsMembers_NeedAssignmentOrTheAssignRight()
    {
        var (workerId, site) = await WorkerAsync();
        using var siteClient = site;
        var (otherId, other) = await WorkerAsync("mara");
        using var otherClient = other;
        var akte = await OfficeBauakteAsync("Lager Ost");

        // Nicht zugeteilt und ohne „Kollegen zuordnen“: andere nicht eintragen.
        using var denied = await site.PutAsJsonAsync($"/api/v1/bauakten/{akte.Id}/members", new { userIds = new[] { otherId } }, Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Zugeteilt: darf Kollegen hinzufügen.
        (await site.PostAsync($"/api/v1/bauakten/{akte.Id}/members/me", null, Ct)).EnsureSuccessStatusCode();
        using var allowed = await site.PutAsJsonAsync($"/api/v1/bauakten/{akte.Id}/members", new { userIds = new[] { workerId, otherId } }, Ct);
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await other.GetFromJsonAsync<BauakteDetail>($"/api/v1/bauakten/{akte.Id}", TestJson.Options, Ct))!.CanEdit.ShouldBe(true);
    }

    [Fact]
    public async Task OfficeRole_HasEditAll_SiteRoleDoesNot()
    {
        using var site = (await WorkerAsync()).Client;
        var mine = await site.GetFromJsonAsync<List<string>>("/api/v1/access/me/permissions", TestJson.Options, Ct);
        mine.ShouldNotBeNull();
        mine.ShouldContain("bauakten.read");
        mine.ShouldNotContain("bauakten.edit_all");
        var office = await _office.GetFromJsonAsync<List<string>>("/api/v1/access/me/permissions", TestJson.Options, Ct);
        office!.ShouldContain("bauakten.edit_all");
    }
}
