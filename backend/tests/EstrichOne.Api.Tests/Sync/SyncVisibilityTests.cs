using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Sync;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Sync;

/// <summary>
/// „Daten nicht verfügbar“ auf Handy und Tablet: Eingeplante sehen ihre Bauakte, der Snapshot liefert den bisherigen
/// Inhalt einer neu sichtbaren Bauakte nach, „Mir zuweisen“ macht sie dauerhaft sichtbar.
/// </summary>
public sealed class SyncVisibilityTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
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

    private async Task<(EstrichOne.Api.Modules.Identity.Exports.User User, HttpClient Client)> WorkerAsync()
    {
        var user = await _factory.CreateUserAsync("lukas", roleIds: StandardRoles.SiteWorkerId);
        var client = _factory.CreateClient();
        var tokens = await client.LoginOkAsync("lukas");
        return (user, client.WithToken(tokens.AccessToken));
    }

    private async Task<(Guid Bauakte, Guid Note)> OfficeBauakteWithNoteAsync(string title)
    {
        var bauakte = Guid.CreateVersion7();
        var note = Guid.CreateVersion7();
        var results = (await _office.PushAsync(
            SyncClient.Op("bauakte.create", bauakte, new { title }),
            SyncClient.Op("note.create", note, new { bauakteId = bauakte, text = "Schlüssel beim Nachbarn" }))).Results;
        results.ShouldAllBe(r => r.Status == SyncOperationStatus.Applied);
        return (bauakte, note);
    }

    private async Task PlanAsync(Guid bauakte, Guid userId) =>
        (await _office.PutAsJsonAsync($"/api/v1/planning/assignments/{Guid.CreateVersion7()}", new
        {
            bauakteId = bauakte, day = Today.AddDays(1), activity = "Estrich einbringen", userIds = new[] { userId },
        }, Ct)).EnsureSuccessStatusCode();

    private static async Task<SnapshotResponse> SnapshotAsync(HttpClient client, params Guid[] ids)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/sync/snapshot", new { bauakteIds = ids }, TestJson.Options, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SnapshotResponse>(TestJson.Options, Ct))!;
    }

    [Fact]
    public async Task PlannedWorker_SeesTheBauakte_AndTheSnapshotDeliversItsExistingContent()
    {
        var (worker, site) = await WorkerAsync();
        using var siteClient = site;
        var (bauakte, note) = await OfficeBauakteWithNoteAsync("Halle Gewerbering");

        var before = await SnapshotAsync(site, bauakte);
        before.Changes.ShouldBeEmpty();
        before.Missing.ShouldBe([bauakte]);

        await PlanAsync(bauakte, worker.Id);

        var pull = await site.PullAsync(includeScope: true);
        pull.VisibleBauakteIds!.ShouldContain(bauakte);
        var snapshot = await SnapshotAsync(site, bauakte);
        snapshot.Missing.ShouldBeEmpty();
        snapshot.Changes[0].EntityType.ShouldBe("bauakte");
        snapshot.Changes.Single(c => c.Id == bauakte).Data.Text("title").ShouldBe("Halle Gewerbering");
        snapshot.Changes.Single(c => c.Id == note).Data.Text("text").ShouldBe("Schlüssel beim Nachbarn");

        // Eingeplant heißt auch: darf dort dokumentieren.
        var diary = (await site.PushAsync(SyncClient.Op("diary.create", Guid.CreateVersion7(),
            new { bauakteId = bauakte, day = Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), text = "Material angeliefert" }))).Results.Single();
        diary.Status.ShouldBe(SyncOperationStatus.Applied);
    }

    [Fact]
    public async Task AssignMe_WorksForPlannedBauakten_ButNotForForeignOnes()
    {
        var (worker, site) = await WorkerAsync();
        using var siteClient = site;
        var (planned, _) = await OfficeBauakteWithNoteAsync("EFH Schneider");
        var (foreign, _) = await OfficeBauakteWithNoteAsync("Fremde Baustelle");
        await PlanAsync(planned, worker.Id);

        using var denied = await site.PostAsync($"/api/v1/bauakten/{foreign}/members/me", null, Ct);
        denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var assigned = await site.PostAsync($"/api/v1/bauakten/{planned}/members/me", null, Ct);
        assigned.StatusCode.ShouldBe(HttpStatusCode.OK);
        var members = await _office.GetFromJsonAsync<List<MemberRow>>($"/api/v1/bauakten/{planned}/members", TestJson.Options, Ct);
        members!.ShouldContain(m => m.UserId == worker.Id);

        // Zweimal ist unschädlich.
        using var again = await site.PostAsync($"/api/v1/bauakten/{planned}/members/me", null, Ct);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Snapshot_IsLimited()
    {
        using var response = await _office.PostAsJsonAsync("/api/v1/sync/snapshot",
            new { bauakteIds = Enumerable.Range(0, SyncService.MaxBauaktenPerSnapshot + 1).Select(_ => Guid.CreateVersion7()).ToArray() }, TestJson.Options, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    private sealed record MemberRow(Guid UserId, string DisplayName);
}
