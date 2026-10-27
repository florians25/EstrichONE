using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Helpdesk;
using EstrichOne.Api.Tests.Support;

namespace EstrichOne.Api.Tests.Helpdesk;

/// <summary>Neue Antworten (Zähler in Kopfleiste und App): je Benutzer, interne Notizen nur für die Bearbeitung.</summary>
public sealed class TicketUnreadTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _factory = await ApiFactory.CreateMigratedAsync(postgres);

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static async Task<TicketUnreadResponse> UnreadAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<TicketUnreadResponse>("/api/v1/tickets/unread", TestJson.Options, Ct))!;

    private void Later() => _factory.Clock.Advance(TimeSpan.FromMinutes(1));

    [Fact]
    public async Task NewRepliesAndChanges_AreUnreadUntilSeen_PerUser()
    {
        using var reporter = await _factory.CreateClientAsAsync("baustelle", StandardRoles.SiteWorkerId);
        using var agent = await _factory.CreateClientAsAsync("it", StandardRoles.AdministratorId);
        var id = Guid.CreateVersion7();
        (await reporter.PostAsJsonAsync("/api/v1/tickets", new { id, title = "Drucker druckt nicht", category = "officeIt", source = "web" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // Neue Meldung ist für die Bearbeitung neu, für den Melder nicht.
        (await UnreadAsync(agent)).Items.Select(i => i.Id).ShouldContain(id);
        (await UnreadAsync(reporter)).Count.ShouldBe(0);
        (await agent.GetAsync(new Uri($"/api/v1/tickets/{id}", UriKind.Relative), Ct)).EnsureSuccessStatusCode();
        (await UnreadAsync(agent)).Count.ShouldBe(0);

        Later();
        (await agent.PostAsJsonAsync($"/api/v1/tickets/{id}/comments", new { text = "Toner prüfen, bitte", isInternal = false }, Ct)).EnsureSuccessStatusCode();
        var unread = await UnreadAsync(reporter);
        unread.Count.ShouldBe(1);
        unread.Items.Single().Number.ShouldNotBeNullOrEmpty();
        (await reporter.GetFromJsonAsync<List<TicketSummary>>("/api/v1/tickets/mine", TestJson.Options, Ct))!.Single().HasUnread.ShouldBeTrue();
        (await UnreadAsync(agent)).Count.ShouldBe(0);

        // Ansehen gilt als gelesen; interne Notizen bleiben für den Melder unsichtbar – auch im Zähler.
        Later();
        (await reporter.GetAsync(new Uri($"/api/v1/tickets/{id}", UriKind.Relative), Ct)).EnsureSuccessStatusCode();
        Later();
        (await agent.PostAsJsonAsync($"/api/v1/tickets/{id}/comments", new { text = "Lieferant anrufen", isInternal = true }, Ct)).EnsureSuccessStatusCode();
        (await UnreadAsync(reporter)).Count.ShouldBe(0);

        // Antwort des Melders: neu für die Bearbeitung, bis sie es als gelesen markiert.
        Later();
        (await reporter.PostAsJsonAsync($"/api/v1/tickets/{id}/comments", new { text = "Toner ist neu.", isInternal = false }, Ct)).EnsureSuccessStatusCode();
        (await agent.GetFromJsonAsync<List<TicketSummary>>("/api/v1/tickets?filter=all", TestJson.Options, Ct))!.Single(t => t.Id == id).HasUnread.ShouldBeTrue();
        (await UnreadAsync(agent)).Count.ShouldBe(1);
        Later();
        (await agent.PostAsync(new Uri($"/api/v1/tickets/{id}/seen", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await UnreadAsync(agent)).Count.ShouldBe(0);

        // Fremde Tickets: wie nicht vorhanden.
        using var other = await _factory.CreateClientAsAsync("buero", StandardRoles.OfficeId);
        (await other.PostAsync(new Uri($"/api/v1/tickets/{id}/seen", UriKind.Relative), null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
