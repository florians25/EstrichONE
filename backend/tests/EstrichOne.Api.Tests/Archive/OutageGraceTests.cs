// Integrationstests laufen unter Linux (CI); kurze Aufrufe ohne Abbruch-Token.
#pragma warning disable xUnit1051, CA1416
using System.Net.Http.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Archive;
using EstrichOne.Api.Modules.Notifications;
using EstrichOne.Api.Modules.Organization;
using EstrichOne.Api.Modules.Organization.Exports;
using EstrichOne.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace EstrichOne.Api.Tests.Archive;

/// <summary>
/// Ausfall von Archivserver/Backup-Ziel erst melden, wenn er ununterbrochen länger als die Frist (Standard 3 Minuten) dauert:
/// keine Banner, Warnungen oder Benachrichtigungen für kurze Aussetzer; „wieder erreichbar“ nur nach gemeldetem Ausfall.
/// </summary>
public sealed class OutageGraceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiFactory _factory = null!;
    private ArchiveServerHost _archive = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
        {
            ["EstrichOne:Archive:Worker"] = "false",
            ["EstrichOne:Health:Worker"] = "false",
        });
        _archive = await ArchiveServerHost.StartAsync();
        _admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        using var paired = await _admin.PostAsJsonAsync("/api/v1/archive/pair", new { address = _archive.Address, code = _archive.NewCode() });
        paired.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _archive.DisposeAsync();
        await _factory.DisposeAsync();
    }

    private SystemHealthMonitor Monitor => _factory.Services.GetRequiredService<SystemHealthMonitor>();

    [Fact]
    public async Task Archivserver_ErstNachDreiMinutenOhneVerbindung_Gemeldet_WiederErreichbarNurNachMeldung()
    {
        (await Monitor.RefreshAsync(notify: true, CancellationToken.None)).Archive.Severity.ShouldBe("ok");

        // Kurzer Aussetzer (2 Minuten): live nicht erreichbar, aber kein Banner, keine Warnung, keine Benachrichtigung.
        await _archive.StopAsync();
        var first = await Monitor.RefreshAsync(notify: true, CancellationToken.None);
        first.Archive.Reachable.ShouldBeFalse();
        first.Archive.Severity.ShouldBe("ok");
        first.Banners.ShouldBeEmpty();
        var overview = (await _admin.GetFromJsonAsync<MonitoringOverview>("/api/v1/system/monitoring", TestJson.Options))!;
        var item = overview.Systems.Single(s => s.Key == "archive").Items.Single(i => i.Label == "Erreichbar");
        item.Level.ShouldBe(MonitorLevel.Ok);
        item.Value.ShouldStartWith("kurzzeitig nicht erreichbar");
        overview.Warnings.ShouldNotContain(w => w.Contains("Erreichbar", StringComparison.Ordinal));

        _factory.Clock.Advance(TimeSpan.FromMinutes(2));
        (await Monitor.RefreshAsync(notify: true, CancellationToken.None)).Banners.ShouldBeEmpty();
        await _archive.StartAgainAsync();
        (await Monitor.RefreshAsync(notify: true, CancellationToken.None)).Archive.Reachable.ShouldBeTrue();

        // Erneuter Ausfall: Frist beginnt neu (kein Aufaddieren der Aussetzer).
        await _archive.StopAsync();
        (await Monitor.RefreshAsync(notify: true, CancellationToken.None)).Banners.ShouldBeEmpty();
        _factory.Clock.Advance(TimeSpan.FromMinutes(2));
        (await Monitor.RefreshAsync(notify: true, CancellationToken.None)).Banners.ShouldBeEmpty();
        (await TitlesAsync()).ShouldNotContain(t => t.StartsWith("Archivserver", StringComparison.Ordinal));

        // Nach 3 Minuten ununterbrochen: gelbes Banner, Warnstatus und genau eine Benachrichtigung.
        _factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var down = await Monitor.RefreshAsync(notify: true, CancellationToken.None);
        down.Archive.Severity.ShouldBe("warning");
        down.Banners.Single().Key.ShouldBe("archive");
        _factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await Monitor.RefreshAsync(notify: true, CancellationToken.None);
        overview = (await _admin.GetFromJsonAsync<MonitoringOverview>("/api/v1/system/monitoring", TestJson.Options))!;
        overview.Systems.Single(s => s.Key == "archive").Items.Single(i => i.Label == "Erreichbar").Level.ShouldBe(MonitorLevel.Warning);
        (await TitlesAsync()).Count(t => t == "Archivserver nicht erreichbar").ShouldBe(1);

        await _archive.StartAgainAsync();
        (await Monitor.RefreshAsync(notify: true, CancellationToken.None)).Banners.ShouldBeEmpty();
        await Monitor.RefreshAsync(notify: true, CancellationToken.None);
        (await TitlesAsync()).Count(t => t == "Archivserver wieder erreichbar").ShouldBe(1);
    }

    [Fact]
    public async Task Frist_IstSystemeinstellung_NullMeldetSofort()
    {
        var settings = (await _admin.GetFromJsonAsync<SystemSettingsResponse>("/api/v1/settings", TestJson.Options))!;
        settings.Settings.Monitoring.OutageGraceMinutes.ShouldBe(MonitoringSettings.DefaultOutageGraceMinutes);
        var immediate = settings.Settings with { Monitoring = new MonitoringSettings { OutageGraceMinutes = 0 } };
        (await _admin.PutAsJsonAsync("/api/v1/settings", new { settings = immediate, version = settings.Version })).EnsureSuccessStatusCode();

        await _archive.StopAsync();
        (await Monitor.RefreshAsync(notify: true, CancellationToken.None)).Archive.Severity.ShouldBe("warning");
        (await TitlesAsync()).ShouldContain("Archivserver nicht erreichbar");
    }

    [Fact]
    public void Tracker_MerktErstenFehlschlag_ErfolgSetztZurueck_EinstellungBegrenzt()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var tracker = new OutageTracker(clock);
        var grace = TimeSpan.FromMinutes(3);
        var since = tracker.Observe("archive", reachable: false);
        since.ShouldBe(clock.GetUtcNow());
        tracker.Exceeded(since, grace).ShouldBeFalse();
        tracker.Pending.ShouldBeTrue(); // innerhalb der Frist: Worker prüft alle 30 Sekunden
        clock.Advance(TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(59));
        tracker.Observe("archive", reachable: false).ShouldBe(since);
        tracker.Exceeded(tracker.Since("archive"), grace).ShouldBeFalse();
        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.Exceeded(tracker.Since("archive"), grace).ShouldBeTrue();
        tracker.Pending.ShouldBeFalse(); // gemeldet: kein beschleunigtes Prüfen mehr nötig
        tracker.Observe("archive", reachable: true).ShouldBeNull();
        tracker.Since("archive").ShouldBeNull();

        tracker.Observe("backup:A|x", reachable: false);
        tracker.Observe("backup:B|y", reachable: false);
        tracker.Retain("backup:", ["backup:B|y"]);
        tracker.Since("backup:A|x").ShouldBeNull();
        tracker.Since("backup:B|y").ShouldNotBeNull();

        new MonitoringSettings { OutageGraceMinutes = -5 }.Normalize().OutageGraceMinutes.ShouldBe(0);
        new MonitoringSettings { OutageGraceMinutes = 500 }.Normalize().OutageGraceMinutes.ShouldBe(60);
        SystemSettingsStore.Parse("{}").Monitoring.OutageGraceMinutes.ShouldBe(3);
    }

    private async Task<List<string>> TitlesAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>().Set<Notification>().AsNoTracking()
            .Select(n => n.Title).ToListAsync();
    }
}
