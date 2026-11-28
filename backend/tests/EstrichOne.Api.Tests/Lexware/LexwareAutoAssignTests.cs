using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Lexware;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Lexware;

/// <summary>
/// Automatische Belegzuordnung: nur sichere Belege (eigener Beleg, Bauaktennummer im Beleg bei verknüpftem Kunden,
/// Belegkette, optional einzige offene Bauakte), nie bei Mehrdeutigkeit, nie an vorhandenen Zuordnungen vorbei, und
/// aufgehobene Zuordnungen werden nicht wiederholt.
/// </summary>
public sealed class LexwareAutoAssignTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly FakeLexware _lexware = new();
    private ApiFactory _factory = null!;
    private HttpClient _admin = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
        {
            ["EstrichOne:Lexware:MinRequestInterval"] = "00:00:00", ["EstrichOne:Lexware:RetryDelay"] = "00:00:00.020",
            ["EstrichOne:Lexware:BackgroundChangeSync"] = "false", ["EstrichOne:Lexware:StatusCacheDuration"] = "00:00:00",
        }, services => services.AddHttpClient(LexwareClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _lexware));
        _admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _factory.DisposeAsync();
        _lexware.Dispose();
    }

    private static async Task<T> SendAsync<T>(HttpClient client, HttpMethod method, string path, object? body = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var request = new HttpRequestMessage(method, path) { Content = body is null ? null : JsonContent.Create(body, options: TestJson.Options) };
        using var response = await client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<T>(TestJson.Options, Ct))!;
    }

    private async Task<AutoAssignRun> RunAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<LexwareAutoAssignService>().RunAsync(null, Ct);
    }

    private async Task<(Guid Customer, string Contact)> CustomerAsync(string name)
    {
        var contact = _lexware.AddContact(name, email: $"{Guid.NewGuid():N}@kunde.example");
        var id = Guid.CreateVersion7();
        await SendAsync<JsonObject>(_admin, HttpMethod.Post, "/api/v1/contacts", new { lexwareCreateAnyway = true, id, kind = "company", name }, HttpStatusCode.Created);
        await SendAsync<CustomerLexwareResponse>(_admin, HttpMethod.Post, $"/api/v1/lexware/customers/{id}/link", new { lexwareContactId = contact });
        return (id, contact);
    }

    private async Task<(Guid Id, string Number)> BauakteAsync(Guid customer, string status = "planned")
    {
        var id = Guid.CreateVersion7();
        var created = await SendAsync<JsonObject>(_admin, HttpMethod.Post, "/api/v1/bauakten", new { id, title = "Projekt", customerId = customer, status },
            HttpStatusCode.Created);
        return (id, created["number"]!.GetValue<string>());
    }

    private async Task<List<LexwareVoucherLink>> LinksAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<LexwareVoucherLink>().AsNoTracking().ToListAsync(Ct);
    }

    [Fact]
    public void Rules_OnlyUnambiguous()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var today = new DateOnly(2026, 11, 28);
        List<AutoAssignBauakte> two = [new(a, "2026-041", true, new DateOnly(2026, 9, 1)), new(b, "2026-042", false, new DateOnly(2026, 1, 1))];
        AutoAssignVoucher V(string text, params string[] preceding) => new("x", "invoice", new DateOnly(2026, 11, 2), "RE-1", text, preceding);
        var linked = new Dictionary<string, Guid> { ["q1"] = a, ["q2"] = b };

        LexwareAutoAssignRules.Decide(V("Bauvorhaben 2026-041 Halle"), two, linked, null, false, today).ShouldBe(new AutoAssignDecision(a, "number"));
        LexwareAutoAssignRules.Decide(V("Bauakten 2026-041 und 2026-042"), two, linked, null, false, today).ShouldBeNull(); // mehrdeutig
        LexwareAutoAssignRules.Decide(V("Nr. 2026-0410"), two, linked, null, false, today).ShouldBeNull(); // längere Nummer
        LexwareAutoAssignRules.Decide(V("ohne Nummer", "q1"), two, linked, null, false, today).ShouldBe(new AutoAssignDecision(a, "chain"));
        LexwareAutoAssignRules.Decide(V("2026-042", "q1"), two, linked, null, false, today).ShouldBeNull(); // Widerspruch
        LexwareAutoAssignRules.Decide(V("ohne Nummer", "q1", "q2"), two, linked, null, false, today).ShouldBeNull();
        LexwareAutoAssignRules.Decide(V("ohne Nummer"), two, linked, b, false, today).ShouldBe(new AutoAssignDecision(b, "own"));

        // Einzige offene Bauakte: nur mit Schalter und Datum im Zeitraum.
        LexwareAutoAssignRules.Decide(V("ohne Nummer"), two, linked, null, false, today).ShouldBeNull();
        LexwareAutoAssignRules.Decide(V("ohne Nummer"), two, linked, null, true, today).ShouldBe(new AutoAssignDecision(a, "singleOpen"));
        LexwareAutoAssignRules.Decide(V("ohne Nummer") with { Date = new DateOnly(2026, 8, 1) }, two, linked, null, true, today).ShouldBeNull();
        List<AutoAssignBauakte> twoOpen = [two[0], two[1] with { Open = true }];
        LexwareAutoAssignRules.Decide(V("ohne Nummer"), twoOpen, linked, null, true, today).ShouldBeNull();
        LexwareAutoAssignRules.ReasonText("number").ShouldBe("automatisch zugeordnet: Bauaktennummer im Beleg");
    }

    [Fact]
    public async Task Run_AssignsSafeVouchers_SkipsAmbiguousAndForeign_RemembersUnassigned()
    {
        await SendAsync<JsonObject>(_admin, HttpMethod.Put, "/api/v1/settings/modules", new { modules = new { lexware = true } });
        await SendAsync<LexwareSettingsResponse>(_admin, HttpMethod.Put, "/api/v1/settings/integrations/lexware", new { apiKey = FakeLexware.Key });
        var (customer, contact) = await CustomerAsync("Huber Bau GmbH");
        var first = await BauakteAsync(customer);
        var second = await BauakteAsync(customer);
        var (_, foreignContact) = await CustomerAsync("Moser Hoch- und Tiefbau");
        var unlinkedContact = _lexware.AddContact("Ohne Verknüpfung GmbH");

        var named = _lexware.AddVoucher("invoice", "open", "RE-1", "2026-11-02", "2026-11-16", 100m, 119m, contact, "Huber", title: $"Rechnung Bauakte {first.Number}");
        var both = _lexware.AddVoucher("invoice", "open", "RE-2", "2026-11-03", null, 100m, 119m, contact, "Huber",
            introduction: $"Bauakten {first.Number} und {second.Number}");
        var foreign = _lexware.AddVoucher("invoice", "open", "RE-3", "2026-11-04", null, 100m, 119m, foreignContact, "Moser", title: $"Bauakte {first.Number}");
        var unlinked = _lexware.AddVoucher("invoice", "open", "RE-4", "2026-11-05", null, 100m, 119m, unlinkedContact, "Ohne", title: $"Bauakte {first.Number}");
        var nothing = _lexware.AddVoucher("quotation", "draft", null, "2026-11-06", null, 100m, 119m, contact, "Huber");
        var chained = _lexware.AddVoucher("creditnote", "open", "GS-1", "2026-11-07", null, 10m, 11.9m, contact, "Huber");
        _lexware.Vouchers[chained]["precedingSalesVoucherId"] = named;

        var run = await RunAsync();
        run.Error.ShouldBeNull();
        var links = await LinksAsync();
        links.Select(l => l.LexwareId).ShouldBe([named], ignoreOrder: true);
        links.Single().Origin.ShouldBe("auto");
        links.Single().AutoReason.ShouldBe("number");
        links.Single().BauakteId.ShouldBe(first.Id);

        // Zweiter Lauf: Die Gutschrift hängt jetzt an einem zugeordneten Beleg (Belegkette).
        await RunAsync();
        links = await LinksAsync();
        links.Single(l => l.LexwareId == chained).AutoReason.ShouldBe("chain");
        links.ShouldNotContain(l => l.LexwareId == both || l.LexwareId == foreign || l.LexwareId == unlinked || l.LexwareId == nothing);

        // Anzeige in der Bauakte mit Grund; Übersicht in den Einstellungen.
        var bauakte = await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{first.Id}/lexware");
        bauakte.Vouchers.Single(v => v.LexwareId == named).AutoAssigned.ShouldBe("automatisch zugeordnet: Bauaktennummer im Beleg");
        var overview = await SendAsync<List<LexwareAutoAssignedResponse>>(_admin, HttpMethod.Get, "/api/v1/settings/integrations/lexware/auto-assigned");
        overview.Count.ShouldBe(2);

        // Aufheben: wird gemerkt, kein erneutes automatisches Zuordnen.
        var link = bauakte.Vouchers.Single(v => v.LexwareId == named);
        using (var response = await _admin.DeleteAsync(new Uri($"/api/v1/bauakten/{first.Id}/lexware/vouchers/{link.Id}", UriKind.Relative), Ct))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await RunAsync();
        (await LinksAsync()).ShouldNotContain(l => l.LexwareId == named);

        // Von Hand zugeordnete Belege bleiben unberührt; Schalter aus: nichts geschieht.
        await SendAsync<LexwareSettingsResponse>(_admin, HttpMethod.Put, "/api/v1/settings/integrations/lexware", new { autoAssign = false });
        var later = _lexware.AddVoucher("invoice", "open", "RE-9", "2026-11-20", null, 100m, 119m, contact, "Huber", title: $"Bauakte {second.Number}");
        (await RunAsync()).Assigned.ShouldBe(0);
        (await LinksAsync()).ShouldNotContain(l => l.LexwareId == later);
        _lexware.Calls.ShouldAllBe(c => c.Method == "GET" || c.Path.StartsWith("/v1/contacts", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SingleOpenRule_OffByDefault_OnWhenSwitched_AndRefreshRunsIt()
    {
        await SendAsync<JsonObject>(_admin, HttpMethod.Put, "/api/v1/settings/modules", new { modules = new { lexware = true } });
        var settings = await SendAsync<LexwareSettingsResponse>(_admin, HttpMethod.Put, "/api/v1/settings/integrations/lexware", new { apiKey = FakeLexware.Key });
        settings.AutoAssign.ShouldBeTrue();
        settings.AutoAssignSingleOpen.ShouldBeFalse();
        var (customer, contact) = await CustomerAsync("Lang Wohnbau");
        var only = await BauakteAsync(customer);
        var done = await BauakteAsync(customer);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Database
                .ExecuteSqlInterpolatedAsync($"UPDATE bauakten SET status = 'Completed' WHERE id = {done.Id}", Ct);
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var voucher = _lexware.AddVoucher("invoice", "open", "RE-5", today, null, 100m, 119m, contact, "Lang");

        (await RunAsync()).Assigned.ShouldBe(0);
        await SendAsync<LexwareSettingsResponse>(_admin, HttpMethod.Put, "/api/v1/settings/integrations/lexware", new { autoAssignSingleOpen = true });

        // „Aktualisieren“ in der Bauakte ordnet zu.
        var bauakte = await SendAsync<BauakteLexwareResponse>(_admin, HttpMethod.Get, $"/api/v1/bauakten/{only.Id}/lexware?refresh=true");
        bauakte.Vouchers.ShouldHaveSingleItem().LexwareId.ShouldBe(voucher);
        bauakte.Vouchers[0].AutoAssigned.ShouldBe("automatisch zugeordnet: einzige offene Bauakte des Kunden");
    }
}
