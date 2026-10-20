using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Notices;
using EstrichOne.Hosting.Status;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Hosting.Tests;

public sealed class NoticeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void BuildDocument_BeruecksichtigtZielUndZeitraum()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        Notice N(NoticeKind kind, string title, bool all, Guid? target, DateTime? start, DateTime? end) => new()
        {
            Kind = kind, Title = title, CreatedBy = "t", AllTenants = all, TenantIds = target?.ToString() ?? string.Empty, StartsAt = start, EndsAt = end,
        };
        var notices = new[]
        {
            N(NoticeKind.Announcement, "Für alle", true, null, null, null),
            N(NoticeKind.Announcement, "Nur B", false, b, null, null),
            N(NoticeKind.Announcement, "Vorbei", true, null, null, now.AddHours(-1)),
            N(NoticeKind.Maintenance, "Update", false, a, now.AddHours(1), now.AddHours(2)),
        };
        var json = JsonSerializer.SerializeToElement(NoticeDistributor.BuildDocument(a, notices, now), Web);
        json.GetProperty("announcements").EnumerateArray().Select(x => x.GetProperty("title").GetString()).ShouldBe(["Für alle"]);
        json.GetProperty("maintenance").GetProperty("message").GetString().ShouldBe("Update");
        json.GetProperty("maintenance").GetProperty("adminsMayWork").GetBoolean().ShouldBeTrue();
        var forB = JsonSerializer.SerializeToElement(NoticeDistributor.BuildDocument(b, notices, now), Web);
        forB.GetProperty("maintenance").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Hinweise_WerdenAnZielinstanzenVerteilt()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var plans = await client.GetFromJsonAsync<JsonElement>("/api/plans", Ct);
        var plan = plans[0].GetProperty("id").GetString();
        var ids = new List<string>();
        foreach (var slug in new[] { "nord", "sued" })
        {
            var created = await (await client.PostAsJsonAsync("/api/tenants", new { companyName = slug, contactEmail = "a@b.de", slug, planId = plan }, Ct))
                .Content.ReadFromJsonAsync<JsonElement>(Ct);
            ids.Add(created.GetProperty("id").GetString()!);
            for (var i = 0; i < 100 && (await client.GetFromJsonAsync<JsonElement>($"/api/tenants/{ids[^1]}", Ct)).GetProperty("summary").GetProperty("state").GetString() != "Running"; i++)
            {
                await Task.Delay(50, Ct);
            }
        }

        var invalid = await client.PostAsJsonAsync("/api/notices", new { kind = "Maintenance", title = "", allTenants = false }, Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        (await client.PostAsJsonAsync("/api/notices", new { kind = "Announcement", title = "Neue Funktionen", text = "Bautagebuch", severity = "info" }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        var start = DateTime.UtcNow.AddHours(1);
        (await client.PostAsJsonAsync("/api/notices", new { kind = "Maintenance", title = "Datenbank-Update", startsAt = start, endsAt = start.AddHours(1), allTenants = false, tenantIds = new[] { ids[0] } }, Ct))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var probe = console.Services.GetRequiredService<FakeInstanceProbe>();
        probe.PushedNotices["nord"].ShouldContain("Datenbank-Update");
        probe.PushedNotices["nord"].ShouldContain("Neue Funktionen");
        probe.PushedNotices["sued"].ShouldContain("Neue Funktionen");
        probe.PushedNotices["sued"].ShouldContain("\"maintenance\":null");

        var list = await client.GetFromJsonAsync<JsonElement>("/api/notices", Ct);
        list.GetArrayLength().ShouldBe(2);
        var maintenance = list.EnumerateArray().Single(n => n.GetProperty("kind").GetString() == "Maintenance");
        maintenance.GetProperty("status").GetString().ShouldBe("geplant");
        maintenance.GetProperty("targets").GetArrayLength().ShouldBe(1);
        maintenance.GetProperty("targets")[0].GetProperty("synced").GetBoolean().ShouldBeTrue();

        (await client.PostAsync($"/api/notices/{maintenance.GetProperty("id").GetString()}/end", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        probe.PushedNotices["nord"].ShouldContain("\"maintenance\":null");
    }

    [Fact]
    public async Task Konsole_SelbstUpdate_StartetInstaller()
    {
        await using var console = await ConsoleApp.StartAsync();
        var client = await console.AdminClientAsync();
        var info = await client.GetFromJsonAsync<JsonElement>("/api/system/console-update", Ct);
        info.GetProperty("versions").GetArrayLength().ShouldBeGreaterThan(0);
        (await client.PostAsJsonAsync("/api/system/console-update", new { defaultVersion = "kaputt" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync("/api/system/console-update", new { defaultVersion = "0.2.0" }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        console.Docker.Calls.ShouldContain(c => c.StartsWith("run estrichone-hosting-updater ghcr.io/florians25/estrichone-hosting:0.2.0 install", StringComparison.Ordinal));
    }
}
