using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Bauakten;
using EstrichOne.Api.Modules.Bauakten.Exports;
using EstrichOne.Api.Modules.Sync;
using EstrichOne.Api.Modules.Sync.Exports;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Api.Tests.Sync;

namespace EstrichOne.Api.Tests.Bauakten;

/// <summary>Bauakte wie in Estrich Web: Zeitraum, Baustellenkontakt, Hinweise, Storno, Team, Notizen anheften, Hinweiszeilen.</summary>
public sealed class BauakteDetailsTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static async Task<BauakteDetail> CreateAsync(HttpClient client, string title)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title }, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, BauakteDetail b, object changes)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = b.Title, ["status"] = b.Status.ToString(), ["street"] = b.Street, ["postalCode"] = b.PostalCode, ["city"] = b.City,
            ["latitude"] = b.Latitude, ["longitude"] = b.Longitude, ["description"] = b.Description, ["customerId"] = b.CustomerId,
            ["responsibleUserId"] = b.ResponsibleUserId, ["plannedStart"] = b.PlannedStart, ["plannedEnd"] = b.PlannedEnd,
            ["siteContactName"] = b.SiteContactName, ["siteContactPhone"] = b.SiteContactPhone, ["siteContactEmail"] = b.SiteContactEmail,
            ["siteNotes"] = b.SiteNotes,
        };
        foreach (var property in changes.GetType().GetProperties())
        {
            body[property.Name] = property.GetValue(changes);
        }

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/bauakten/{b.Id}") { Content = JsonContent.Create(body) };
        request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{b.Version}\""));
        return await client.SendAsync(request, Ct);
    }

    [Fact]
    public async Task PeriodSiteContactAndNotes_AreSavedAndAppearInHistoryAndHints()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = await CreateAsync(office, "EFH Kraus");
        _factory.Clock.Advance(TimeSpan.FromMinutes(1));

        using var response = await PutAsync(office, bauakte, new
        {
            plannedStart = "2026-11-02", plannedEnd = "2026-11-04", siteContactName = "Herr Bauer (Bauleiter)", siteContactPhone = "0171 2345678",
            siteContactEmail = "bauer@example.org", siteNotes = "Schlüssel beim Nachbarn, Hund im Garten",
        });
        response.EnsureSuccessStatusCode();
        var saved = (await response.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;

        saved.PlannedStart.ShouldBe(new DateOnly(2026, 11, 2));
        saved.SiteContactName.ShouldBe("Herr Bauer (Bauleiter)");
        saved.SiteNotes.ShouldBe("Schlüssel beim Nachbarn, Hund im Garten");
        var history = await office.GetFromJsonAsync<List<BauakteHistoryEntry>>($"/api/v1/bauakten/{bauakte.Id}/history", TestJson.Options, Ct);
        history![0].Text.ShouldBe("Max Huber hat den Zeitraum, den Baustellenkontakt und die Hinweise zur Baustelle geändert.");
        var hints = await office.GetFromJsonAsync<List<BauakteHint>>($"/api/v1/bauakten/{bauakte.Id}/hints", TestJson.Options, Ct);
        hints!.ShouldContain(h => h.Kind == "site_notes" && h.Text == "Schlüssel beim Nachbarn, Hund im Garten");
        hints!.ShouldContain(h => h.Kind == "initial_inspection_missing");
    }

    [Fact]
    public async Task InvalidPeriod_AndCancelWithoutReason_AreRejected_CancelWithReasonWorks()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = await CreateAsync(office, "Halle");

        using var period = await PutAsync(office, bauakte, new { plannedStart = "2026-11-05", plannedEnd = "2026-11-01" });
        using var noReason = await PutAsync(office, bauakte, new { status = "cancelled" });
        using var cancelled = await PutAsync(office, bauakte, new { status = "cancelled", cancellationReason = "Kunde hat abgesagt" });

        (await period.ReadProblemAsync()).Code.ShouldBe("INVALID_PERIOD");
        (await noReason.ReadProblemAsync()).Code.ShouldBe("CANCELLATION_REASON_REQUIRED");
        var result = (await cancelled.Content.ReadFromJsonAsync<BauakteDetail>(TestJson.Options, Ct))!;
        result.Status.ShouldBe(BauakteStatus.Cancelled);
        result.CancellationReason.ShouldBe("Kunde hat abgesagt");
        var hints = await office.GetFromJsonAsync<List<BauakteHint>>($"/api/v1/bauakten/{bauakte.Id}/hints", TestJson.Options, Ct);
        hints![0].ShouldBe(new BauakteHint("cancelled", BauakteHintSeverity.Danger, "Storniert: Kunde hat abgesagt"));
    }

    [Fact]
    public async Task Members_GrantAccess_AndAreListedWithResponsible()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var workerId = (await worker.GetFromJsonAsync<UserSummaryDto>("/api/v1/auth/me", TestJson.Options, Ct))!.Id;
        var bauakte = await CreateAsync(office, "MFH Lindenstraße");
        (await worker.GetAsync(new Uri($"/api/v1/bauakten/{bauakte.Id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var set = await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/members", new { userIds = new[] { workerId } }, Ct);
        set.EnsureSuccessStatusCode();
        var members = (await set.Content.ReadFromJsonAsync<List<BauakteMemberResponse>>(TestJson.Options, Ct))!;

        members.Select(m => (m.UserName(), m.Role)).ShouldBe([("Max Huber", BauakteMemberRole.Responsible), ("Max Huber", BauakteMemberRole.Member)]);
        (await worker.GetAsync(new Uri($"/api/v1/bauakten/{bauakte.Id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await office.PutAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/members", new { userIds = Array.Empty<Guid>() }, Ct)).EnsureSuccessStatusCode();
        (await worker.GetAsync(new Uri($"/api/v1/bauakten/{bauakte.Id}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var history = await office.GetFromJsonAsync<List<BauakteHistoryEntry>>($"/api/v1/bauakten/{bauakte.Id}/history", TestJson.Options, Ct);
        history!.Count(h => h.Text == "Max Huber hat die zugewiesenen Mitarbeiter geändert.").ShouldBe(2);
    }

    [Fact]
    public async Task Notes_CanBePinnedColoredAndUnpinned()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = await CreateAsync(office, "Garage");
        var noteId = Guid.CreateVersion7();
        (await office.PostAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/notes", new { id = noteId, text = "Zufahrt nur bis 7 Uhr" }, Ct)).EnsureSuccessStatusCode();

        using var pinned = await office.PatchAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/notes/{noteId}", new { isPinned = true, color = "red" }, Ct);
        using var badColor = await office.PatchAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/notes/{noteId}", new { color = "pink" }, Ct);
        using var unpinned = await office.PatchAsJsonAsync($"/api/v1/bauakten/{bauakte.Id}/notes/{noteId}", new { isPinned = false }, Ct);

        var note = (await pinned.Content.ReadFromJsonAsync<NoteDto>(TestJson.Options, Ct))!;
        note.IsPinned.ShouldBeTrue();
        note.Color.ShouldBe("red");
        badColor.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await unpinned.Content.ReadFromJsonAsync<NoteDto>(TestJson.Options, Ct))!.IsPinned.ShouldBeFalse();
    }

    [Fact]
    public async Task Sync_UpdatesSiteNotes_ButCannotCancel()
    {
        using var worker = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        var id = Guid.CreateVersion7();
        (await worker.PushAsync(SyncClient.Op("bauakte.create", id, new { title = "Bad" }))).Results.Single().Status.ShouldBe(SyncOperationStatus.Applied);

        var notes = await worker.PushAsync(SyncClient.Op("bauakte.update", id, new
        {
            changes = new { siteNotes = "Kein Strom vor Ort" }, @base = new { siteNotes = (string?)null },
        }));
        var cancel = await worker.PushAsync(SyncClient.Op("bauakte.update", id, new
        {
            changes = new { status = "cancelled" }, @base = new { status = "planned" },
        }));

        notes.Results.Single().Status.ShouldBe(SyncOperationStatus.Applied);
        cancel.Results.Single().Status.ShouldBe(SyncOperationStatus.Rejected);
        var detail = await worker.GetFromJsonAsync<BauakteDetail>($"/api/v1/bauakten/{id}", TestJson.Options, Ct);
        detail!.SiteNotes.ShouldBe("Kein Strom vor Ort");
        detail.Status.ShouldBe(BauakteStatus.Planned);
    }

    [Fact]
    public async Task Documents_HaveCategory_SetOnAnnounceAndChangeable()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var bauakte = await CreateAsync(office, "Halle Süd");
        var fileId = Guid.CreateVersion7();
        var announce = new
        {
            id = fileId, bauakteId = bauakte.Id, kind = "document", originalName = "Angebot.pdf", contentType = "application/pdf",
            sizeBytes = 10, sha256 = new string('a', 64), category = "offer",
        };

        using var created = await office.PostAsJsonAsync("/api/v1/files", announce, Ct);
        using var invalid = await office.PostAsJsonAsync("/api/v1/files", announce with { id = Guid.CreateVersion7(), category = "unknown" }, Ct);
        using var changed = await office.PatchAsJsonAsync($"/api/v1/files/{fileId}", new { caption = "Angebot Nr. 4711", category = "plan" }, Ct);

        (await created.Content.ReadFromJsonAsync<FileDto>(TestJson.Options, Ct))!.Category.ShouldBe("offer");
        invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var file = (await changed.Content.ReadFromJsonAsync<FileDto>(TestJson.Options, Ct))!;
        file.Category.ShouldBe("plan");
        file.Caption.ShouldBe("Angebot Nr. 4711");
    }

    [Fact]
    public async Task List_FiltersByResponsibleAndCustomer()
    {
        using var office = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        var mine = await CreateAsync(office, "Meine");
        var customer = Guid.CreateVersion7();
        (await office.PostAsJsonAsync("/api/v1/contacts", new { id = customer, kind = "person", name = "Familie Kraus" }, Ct)).EnsureSuccessStatusCode();
        using var other = await office.PostAsJsonAsync("/api/v1/bauakten", new { id = Guid.CreateVersion7(), title = "Kraus", customerId = customer }, Ct);
        other.EnsureSuccessStatusCode();

        var byCustomer = await office.GetFromJsonAsync<BauaktePage>($"/api/v1/bauakten?customerId={customer}", TestJson.Options, Ct);
        var byResponsible = await office.GetFromJsonAsync<BauaktePage>($"/api/v1/bauakten?responsibleUserId={mine.ResponsibleUserId}", TestJson.Options, Ct);
        var nobody = await office.GetFromJsonAsync<BauaktePage>($"/api/v1/bauakten?responsibleUserId={Guid.CreateVersion7()}", TestJson.Options, Ct);

        byCustomer!.Items.Single().Title.ShouldBe("Kraus");
        byResponsible!.Items.Count.ShouldBe(2);
        nobody!.Items.ShouldBeEmpty();
    }

    private sealed record FileDto(Guid Id, string? Caption, string? Category);

    private sealed record UserSummaryDto(Guid Id);
}

internal static class MemberExtensions
{
    public static string UserName(this BauakteMemberResponse member) => member.DisplayName;
}
