using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Feedback;
using EstrichOne.Api.Modules.Notifications;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Feedback;

/// <summary>Fehler melden und Funktionen wünschen: Einreichen, Sichtbarkeit, Verwaltung durch die IT, Benachrichtigungen.</summary>
public sealed class FeedbackTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static object Bug(Guid id, IReadOnlyList<Guid>? attachments = null) => new
    {
        id, kind = "bug", title = "Speichern im Bautagebuch hängt", area = "Bauakten", stepsToReproduce = "1. Bauakte öffnen\n2. Bautagebuch\n3. Speichern",
        expectedResult = "Eintrag wird gespeichert", actualResult = "Ladeanzeige dreht endlos", frequency = "always", impact = "high",
        context = new { page = "/bauakten/123?abschnitt=tagebuch", browser = "Chrome 140", screen = "1920×1080", viewport = "1536×730", appVersion = "1.2.3" },
        attachmentFileIds = attachments ?? [],
    };

    private static object Feature(Guid id) => new
    {
        id, kind = "feature", title = "Bauakten als Excel exportieren", area = "Bauakten", problem = "Für die Nachkalkulation tippe ich alles ab.",
        desiredSolution = "Export-Knopf über der Liste", impact = "medium",
    };

    private static async Task<FeedbackDetail> CreateAsync(HttpClient client, object body)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/feedback", body, Ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await response.Content.ReadAsStringAsync(Ct));
        }

        return (await response.Content.ReadFromJsonAsync<FeedbackDetail>(TestJson.Options, Ct))!;
    }

    [Fact]
    public async Task Submit_AnySignedInUser_NumberedIdempotentAndWithContext()
    {
        using var reporter = await _factory.CreateClientAsAsync("ohne.rolle");
        var id = Guid.CreateVersion7();

        using var first = await reporter.PostAsJsonAsync("/api/v1/feedback", Bug(id), Ct);
        using var repeat = await reporter.PostAsJsonAsync("/api/v1/feedback", Bug(id), Ct);
        var feature = await CreateAsync(reporter, Feature(Guid.CreateVersion7()));

        first.StatusCode.ShouldBe(HttpStatusCode.Created);
        repeat.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bug = (await first.Content.ReadFromJsonAsync<FeedbackDetail>(TestJson.Options, Ct))!;
        bug.Summary.Number.ShouldMatch(@"^F-\d{4}-0001$");
        feature.Summary.Number.ShouldMatch(@"^F-\d{4}-0002$");
        bug.Summary.Status.ShouldBe(FeedbackStatus.New);
        bug.Summary.Priority.ShouldBe(FeedbackPriority.Normal);
        bug.Summary.Impact.ShouldBe(FeedbackImpact.High);
        bug.Frequency.ShouldBe(FeedbackFrequency.Always);
        bug.ActualResult.ShouldBe("Ladeanzeige dreht endlos");
        bug.Context!.Browser.ShouldBe("Chrome 140");
        bug.Context.Page.ShouldBe("/bauakten/123?abschnitt=tagebuch");
        feature.Problem.ShouldNotBeNull();
        feature.ActualResult.ShouldBeNull();
        bug.Timeline.ShouldHaveSingleItem().Kind.ShouldBe("created");

        var mine = await reporter.GetFromJsonAsync<List<FeedbackSummary>>("/api/v1/feedback/mine", TestJson.Options, Ct);
        mine!.Select(f => f.Id).ShouldBe([feature.Summary.Id, id]);
    }

    [Fact]
    public async Task Submit_RequiresFieldsPerKind()
    {
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);

        using var bugWithoutResult = await reporter.PostAsJsonAsync("/api/v1/feedback",
            new { id = Guid.CreateVersion7(), kind = "bug", title = "x", area = "Planung", frequency = "once", impact = "low" }, Ct);
        using var featureWithoutProblem = await reporter.PostAsJsonAsync("/api/v1/feedback",
            new { id = Guid.CreateVersion7(), kind = "feature", title = "x", area = "Planung", desiredSolution = "y", impact = "low" }, Ct);
        using var withoutArea = await reporter.PostAsJsonAsync("/api/v1/feedback",
            new { id = Guid.CreateVersion7(), kind = "feature", title = "x", area = " ", problem = "p", desiredSolution = "y", impact = "low" }, Ct);

        bugWithoutResult.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        featureWithoutProblem.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        ((int)withoutArea.StatusCode).ShouldBeOneOf(400, 422);
    }

    [Fact]
    public async Task Visibility_OthersSeeNothing_ManagementOnlyWithPermission()
    {
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var stranger = await _factory.CreateClientAsAsync("kollege", StandardRoles.OfficeId);
        using var it = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        var item = await CreateAsync(reporter, Bug(Guid.CreateVersion7()));
        var id = item.Summary.Id;

        using var strangerView = await stranger.GetAsync(new Uri($"/api/v1/feedback/{id}", UriKind.Relative), Ct);
        using var strangerList = await stranger.GetAsync(new Uri("/api/v1/feedback", UriKind.Relative), Ct);
        using var strangerPatch = await stranger.PatchAsJsonAsync($"/api/v1/feedback/{id}", new { status = "rejected" }, Ct);
        using var strangerComment = await stranger.PostAsJsonAsync($"/api/v1/feedback/{id}/comments", new { text = "x", isInternal = false }, Ct);
        using var reporterPatch = await reporter.PatchAsJsonAsync($"/api/v1/feedback/{id}", new { status = "done" }, Ct);
        using var reporterNote = await reporter.PostAsJsonAsync($"/api/v1/feedback/{id}/comments", new { text = "x", isInternal = true }, Ct);
        var strangerMine = await stranger.GetFromJsonAsync<List<FeedbackSummary>>("/api/v1/feedback/mine", TestJson.Options, Ct);

        strangerView.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        strangerList.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        strangerPatch.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        strangerComment.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        reporterPatch.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        reporterNote.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        strangerMine.ShouldBeEmpty();

        var list = (await it.GetFromJsonAsync<FeedbackListResponse>("/api/v1/feedback?status=all", TestJson.Options, Ct))!;
        list.Items.ShouldHaveSingleItem().Id.ShouldBe(id);
        list.Counts.New.ShouldBe(1);
        list.Counts.OpenBugs.ShouldBe(1);
        list.Areas.ShouldBe(["Bauakten"]);
        (await it.GetFromJsonAsync<FeedbackListResponse>("/api/v1/feedback?kind=feature", TestJson.Options, Ct))!.Items.ShouldBeEmpty();
        (await it.GetFromJsonAsync<FeedbackListResponse>("/api/v1/feedback?q=bautagebuch&area=bauakten&status=new", TestJson.Options, Ct))!
            .Items.ShouldHaveSingleItem();
        (await it.GetFromJsonAsync<FeedbackListResponse>("/api/v1/feedback?q=gibtsnicht", TestJson.Options, Ct))!.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task StatusFlow_RepliesAndNotes_NotifyTheReporter()
    {
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var it = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        var first = await CreateAsync(reporter, Feature(Guid.CreateVersion7()));
        var item = await CreateAsync(reporter, Feature(Guid.CreateVersion7()));
        var id = item.Summary.Id;

        // Die IT wurde über beide neuen Meldungen benachrichtigt (Ereignis nur für die IT).
        var itInbox = (await it.GetFromJsonAsync<NotificationInbox>("/api/v1/notifications", TestJson.Options, Ct))!;
        itInbox.Items.Count(n => n.ObjectType == "feedback").ShouldBe(2);
        itInbox.Items[0].Link.ShouldBe($"/it-center/meldungen/{id}");

        (await it.PatchAsJsonAsync($"/api/v1/feedback/{id}", new { status = "inReview", priority = "high", assigneeUserId = await UserIdAsync(it) }, Ct))
            .EnsureSuccessStatusCode();
        (await it.PostAsJsonAsync($"/api/v1/feedback/{id}/comments", new { text = "Prüfen wir mit der Buchhaltung.", isInternal = true }, Ct))
            .EnsureSuccessStatusCode();
        (await it.PostAsJsonAsync($"/api/v1/feedback/{id}/comments", new { text = "Kommt mit dem nächsten Update.", isInternal = false }, Ct))
            .EnsureSuccessStatusCode();
        (await it.PatchAsJsonAsync($"/api/v1/feedback/{id}", new { status = "planned" }, Ct)).EnsureSuccessStatusCode();

        var reporterView = (await reporter.GetFromJsonAsync<FeedbackDetail>($"/api/v1/feedback/{id}", TestJson.Options, Ct))!;
        reporterView.Summary.Status.ShouldBe(FeedbackStatus.Planned);
        reporterView.Summary.Priority.ShouldBe(FeedbackPriority.High);
        reporterView.Timeline.Where(t => t.Kind is "comment" or "note").Select(t => t.Text).ShouldBe(["Kommt mit dem nächsten Update."]);
        reporterView.Timeline.Select(t => t.Kind).ShouldContain("status");
        var itView = (await it.GetFromJsonAsync<FeedbackDetail>($"/api/v1/feedback/{id}", TestJson.Options, Ct))!;
        itView.Timeline.Count(t => t.Kind == "note").ShouldBe(1);

        // Einreicher: zwei Statuswechsel und eine Antwort, nichts zur internen Notiz.
        var inbox = (await reporter.GetFromJsonAsync<NotificationInbox>("/api/v1/notifications", TestJson.Options, Ct))!;
        var mine = inbox.Items.Where(n => n.ObjectId == id).ToList();
        mine.Count.ShouldBe(3);
        mine.ShouldAllBe(n => n.Link == $"/meine-meldungen/{id}");
        mine.ShouldContain(n => n.Title.StartsWith("Antwort zu Meldung", StringComparison.Ordinal));
        mine.ShouldContain(n => n.Title.StartsWith("Status geändert", StringComparison.Ordinal) && n.Body!.EndsWith("· Geplant", StringComparison.Ordinal));

        // Rückmeldung des Einreichers geht an die zuständige Person.
        (await reporter.PostAsJsonAsync($"/api/v1/feedback/{id}/comments", new { text = "Danke!", isInternal = false }, Ct)).EnsureSuccessStatusCode();
        var after = (await it.GetFromJsonAsync<NotificationInbox>("/api/v1/notifications", TestJson.Options, Ct))!;
        after.Items.ShouldContain(n => n.ObjectId == id && n.Title.Contains("Rückmeldung", StringComparison.Ordinal));

        // Duplikat mit Verweis; ohne gültige Nummer abgelehnt.
        using var withoutReference = await it.PatchAsJsonAsync($"/api/v1/feedback/{id}", new { status = "duplicate" }, Ct);
        withoutReference.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var unknown = await it.PatchAsJsonAsync($"/api/v1/feedback/{id}", new { status = "duplicate", duplicateOfNumber = "F-1999-0001" }, Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var duplicate = await it.PatchAsJsonAsync($"/api/v1/feedback/{id}", new { status = "duplicate", duplicateOfNumber = first.Summary.Number.ToLowerInvariant() }, Ct);
        duplicate.EnsureSuccessStatusCode();
        var marked = (await duplicate.Content.ReadFromJsonAsync<FeedbackDetail>(TestJson.Options, Ct))!;
        marked.Summary.DuplicateOfId.ShouldBe(first.Summary.Id);
        marked.Summary.DuplicateOfNumber.ShouldBe(first.Summary.Number);
        marked.Summary.ClosedAt.ShouldNotBeNull();

        var counts = (await it.GetFromJsonAsync<FeedbackListResponse>("/api/v1/feedback?status=closed", TestJson.Options, Ct))!;
        counts.Items.ShouldHaveSingleItem().Id.ShouldBe(id);
        counts.Counts.Duplicate.ShouldBe(1);
        counts.Counts.Open.ShouldBe(1);

        // Audit: Einreichen und Änderungen sind protokolliert.
        var audit = await it.GetStringAsync(new Uri($"/api/v1/audit?entityType=feedback&entityId={id}", UriKind.Relative), Ct);
        audit.ShouldContain("feedback.submitted");
        audit.ShouldContain("feedback.updated");
    }

    [Fact]
    public async Task Attachments_VisibleToItButNotToOthers()
    {
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var it = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
        using var stranger = await _factory.CreateClientAsAsync("kollege", StandardRoles.SiteWorkerId);
        var file = await UploadAsync(reporter);

        var item = await CreateAsync(reporter, Bug(Guid.CreateVersion7(), [file]));
        using var foreign = await stranger.PostAsJsonAsync("/api/v1/feedback", Bug(Guid.CreateVersion7(), [file]), Ct);

        item.Attachments.ShouldHaveSingleItem().Name.ShouldBe("bildschirm.png");
        foreign.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await it.GetAsync(new Uri($"/api/v1/files/{file}/content", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await stranger.GetAsync(new Uri($"/api/v1/files/{file}", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private static async Task<Guid> UploadAsync(HttpClient client)
    {
        var id = Guid.CreateVersion7();
        (await client.PostAsJsonAsync("/api/v1/files", new
        {
            id, bauakteId = (Guid?)null, kind = "photo", originalName = "bildschirm.png", contentType = "image/png", sizeBytes = (long)Png.Length,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(Png)),
        }, Ct)).EnsureSuccessStatusCode();
        using var body = new ByteArrayContent(Png);
        body.Headers.ContentType = new("application/octet-stream");
        (await client.PutAsync(new Uri($"/api/v1/files/{id}/content", UriKind.Relative), body, Ct)).EnsureSuccessStatusCode();
        return id;
    }

    private static async Task<Guid> UserIdAsync(HttpClient client)
    {
        var me = await client.GetFromJsonAsync<System.Text.Json.Nodes.JsonObject>("/api/v1/auth/me", TestJson.Options, Ct);
        return (me!["user"]?["id"] ?? me["id"])!.GetValue<Guid>();
    }
}
