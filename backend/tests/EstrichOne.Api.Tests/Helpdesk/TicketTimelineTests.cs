using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Helpdesk;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Helpdesk;

/// <summary>Verlaufsansicht (C6): Zeitleiste mit Meldung, Antworten mit Anhängen, internen Notizen und Statuswechseln.</summary>
public sealed class TicketTimelineTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static async Task<Guid> UploadAsync(HttpClient client)
    {
        var id = Guid.CreateVersion7();
        using var announce = await client.PostAsJsonAsync("/api/v1/files", new
        {
            id, kind = "photo", originalName = "anleitung.png", contentType = "image/png", sizeBytes = (long)Png.Length,
            sha256 = Convert.ToHexStringLower(SHA256.HashData(Png)),
        }, Ct);
        announce.StatusCode.ShouldBe(HttpStatusCode.Created, await announce.Content.ReadAsStringAsync(Ct));
        using var body = new ByteArrayContent(Png);
        body.Headers.ContentType = new("application/octet-stream");
        using var upload = await client.PutAsync(new Uri($"/api/v1/files/{id}/content", UriKind.Relative), body, Ct);
        upload.StatusCode.ShouldBe(HttpStatusCode.OK);
        return id;
    }

    [Fact]
    public async Task Timeline_ShowsReplyWithAttachment_StatusChanges_InternalNotesOnlyForAgents()
    {
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        // Bearbeitung mit Upload-Recht (Administrator); IT-Administration allein lädt keine Dateien hoch.
        using var agent = await _factory.CreateClientAsAsync("it", StandardRoles.AdministratorId);
        var id = Guid.CreateVersion7();
        using var created = await reporter.PostAsJsonAsync("/api/v1/tickets", new
        {
            id, title = "Fotos laden nicht hoch", description = "Seit heute früh", category = "sync", source = "ios",
        }, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);

        using var note = await agent.PostAsJsonAsync($"/api/v1/tickets/{id}/comments", new { text = "Proxy-Limit prüfen", isInternal = true }, Ct);
        note.EnsureSuccessStatusCode();
        using var assign = await agent.PatchAsJsonAsync($"/api/v1/tickets/{id}", new { status = "waitingForReporter", priority = "high" }, Ct);
        assign.EnsureSuccessStatusCode();
        var screenshot = await UploadAsync(agent);
        using var reply = await agent.PostAsJsonAsync($"/api/v1/tickets/{id}/comments", new
        {
            text = "Bitte die App neu starten, siehe Bild.", isInternal = false, attachmentFileIds = new[] { screenshot },
        }, Ct);
        reply.EnsureSuccessStatusCode();

        var forAgent = (await agent.GetFromJsonAsync<TicketDetail>($"/api/v1/tickets/{id}", TestJson.Options, Ct))!;
        var timeline = forAgent.Timeline.ShouldNotBeNull();
        timeline[0].Kind.ShouldBe("created");
        timeline[0].Text.ShouldBe("Seit heute früh");
        timeline.ShouldContain(i => i.Kind == "note" && i.IsInternal);
        timeline.ShouldContain(i => i.Kind == "status" && i.Summary == "Status: Neu → Rückfrage");
        timeline.ShouldContain(i => i.Kind == "priority" && i.Summary == "Priorität: Normal → Hoch");
        timeline.Single(i => i.Kind == "comment").AttachmentFileIds.ShouldBe([screenshot]);

        var forReporter = (await reporter.GetFromJsonAsync<TicketDetail>($"/api/v1/tickets/{id}", TestJson.Options, Ct))!;
        forReporter.Timeline.ShouldNotBeNull().ShouldNotContain(i => i.IsInternal);
        (await reporter.GetAsync(new Uri($"/api/v1/files/{screenshot}/content", UriKind.Relative), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK,
            "Der Melder sieht Anhänge der Antworten auf sein Ticket.");

        // Antwort des Melders mit fremder Datei wird abgelehnt; eigene Antwort holt das Ticket zurück in die Bearbeitung.
        using var foreign = await reporter.PostAsJsonAsync($"/api/v1/tickets/{id}/comments", new { text = "x", isInternal = false, attachmentFileIds = new[] { screenshot } }, Ct);
        foreign.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        using var answer = await reporter.PostAsJsonAsync($"/api/v1/tickets/{id}/comments", new { text = "Geht wieder, danke!", isInternal = false }, Ct);
        var after = (await answer.Content.ReadFromJsonAsync<TicketDetail>(TestJson.Options, Ct))!;
        after.Timeline!.Last(i => i.Kind == "comment").IsReporter.ShouldBeTrue();
        after.Timeline!.ShouldContain(i => i.Summary == "Status: Rückfrage → In Bearbeitung");
    }
}
