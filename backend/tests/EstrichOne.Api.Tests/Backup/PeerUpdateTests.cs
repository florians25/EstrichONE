// Integrationstests laufen unter Linux (CI); kurze Aufrufe ohne Abbruch-Token.
#pragma warning disable xUnit1051, CA1416
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Backup;
using EstrichOne.Api.Modules.Notifications;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Archive;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ApiSemVer = EstrichOne.Api.Infrastructure.Peers.SemVer;
using PeerSemVer = EstrichOne.Archive.SemVer;

namespace EstrichOne.Api.Tests.Backup;

/// <summary>
/// Fernaktualisierung der Zusatzserver: signierter Auftrag über die gekoppelte Verbindung bzw. Abholung durch das ziehende
/// Ziel, Versionsprüfung, Rückmeldung des Updaters, Protokoll und Benachrichtigung der Administratoren.
/// </summary>
public sealed class PeerUpdateTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly string _status = Path.Combine(Path.GetTempPath(), "estrichone-peer-updates", Guid.NewGuid().ToString("N"));
    private readonly string _repository = Path.Combine(Path.GetTempPath(), "estrichone-peer-updates-repo", Guid.NewGuid().ToString("N"));
    private ApiFactory _factory = null!;
    private ArchiveServerHost _target = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_status);
        Directory.CreateDirectory(_repository);
        await File.WriteAllTextAsync(Path.Combine(_repository, "config"), "verschlüsselt");
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string>
        {
            ["EstrichOne:Backup:StatusPath"] = _status,
            ["EstrichOne:Backup:RepositoryPath"] = _repository,
            ["EstrichOne:Backup:PeerUpdateWorker"] = "false",
            ["EstrichOne:Health:Worker"] = "false",
            ["EstrichOne:Archive:Worker"] = "false",
        });
        _target = await ArchiveServerHost.StartAsync(ServerRole.BackupTarget);
        _admin = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _target.DisposeAsync();
        await _factory.DisposeAsync();
        Directory.Delete(_status, recursive: true);
        Directory.Delete(_repository, recursive: true);
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("0.1.0-preview.9", "0.1.0-preview.10", -1)]
    [InlineData("0.1.0-preview.22", "0.1.0", -1)]
    [InlineData("0.1.98-ci", "0.1.98-ci.2", -1)]
    [InlineData("0.1.98-ci.2", "0.1.99-ci", -1)]
    [InlineData("1.0.0-alpha", "1.0.0-1", 1)]
    [InlineData("2.0.0", "2.0.0", 0)]
    [InlineData("10.0.0", "9.9.9", 1)]
    public void SemVer_ComparesLikeTheSpecification_OnBothSides(string left, string right, int expected)
    {
        Math.Sign(ApiSemVer.Compare(left, right)).ShouldBe(expected);
        Math.Sign(PeerSemVer.Compare(left, right)).ShouldBe(expected);
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("1.0")]
    [InlineData("01.0.0")]
    [InlineData("1.0.0+abc")]
    [InlineData("1.0.0;rm")]
    [InlineData("1.0.0-")]
    [InlineData("")]
    public void SemVer_RejectsTagsAndInjections(string version)
    {
        ApiSemVer.IsValid(version).ShouldBeFalse();
        PeerSemVer.IsValid(version).ShouldBeFalse();
    }

    [Fact]
    public void RemoteUpdater_AcceptsOnlySignedFreshOrdersUpToTheServerVersion()
    {
        var dir = Path.Combine(_target.Options.UpdatePath);
        var options = new ArchiveOptions { DataPath = _target.Options.DataPath, UpdatePath = dir, Role = ServerRole.BackupTarget };
        var updater = new RemoteUpdater(options, TimeProvider.System);
        var token = ClientRegistry.Sha256("geheimes-token");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        UpdateOrder Order(string version, string server = "0.2.0", long? at = null, string? key = null, string role = "backup-target", string id = "0123456789abcdef0123456789abcdef")
        {
            var issued = at ?? now;
            return new UpdateOrder(id, version, server, issued, UpdateSignature.Compute(key ?? token, role, "client-1", id, version, server, issued));
        }

        updater.Accept(Order("0.2.0"), token, "client-1")!.Code.ShouldBe("UPDATER_MISSING");
        _target.UpdaterHeartbeat();
        updater.Accept(Order("0.2.0", key: ClientRegistry.Sha256("anderes")), token, "client-1")!.Code.ShouldBe("UPDATE_SIGNATURE_INVALID");
        updater.Accept(Order("0.2.0", role: "archive"), token, "client-1")!.Code.ShouldBe("UPDATE_SIGNATURE_INVALID");
        updater.Accept(Order("0.2.0"), token, "client-2")!.Code.ShouldBe("UPDATE_SIGNATURE_INVALID");
        updater.Accept(Order("0.3.0"), token, "client-1")!.Code.ShouldBe("UPDATE_VERSION_TOO_NEW");
        updater.Accept(Order("0.0.9"), token, "client-1")!.Code.ShouldBe("UPDATE_DOWNGRADE");
        updater.Accept(Order("latest"), token, "client-1")!.Code.ShouldBe("UPDATE_VERSION_INVALID");
        updater.Accept(Order("0.2.0", at: now - 3600), token, "client-1")!.Code.ShouldBe("UPDATE_EXPIRED");
        updater.Accept(Order("0.2.0", id: "../../etc"), token, "client-1")!.Code.ShouldBe("UPDATE_INVALID");

        updater.Accept(Order("0.2.0"), token, "client-1").ShouldBeNull();
        File.ReadAllText(Path.Combine(dir, "request")).ShouldContain("version=0.2.0");
        updater.Status().State.ShouldBe("pending");
        updater.Accept(Order("0.2.0", id: "fedcba9876543210fedcba9876543210"), token, "client-1")!.Code.ShouldBe("UPDATE_RUNNING");
    }

    [Fact]
    public async Task PushedTarget_SignedOrderOverPinnedConnection_ResultIsTrackedAuditedAndNotified()
    {
        using var paired = await _admin.PostAsJsonAsync("/api/v1/backup/targets", new { address = _target.Address, code = _target.NewCode() });
        paired.StatusCode.ShouldBe(HttpStatusCode.OK);
        var target = (await paired.Content.ReadFromJsonAsync<BackupTargetDto>(TestJson.Options))!;
        var key = $"target-{target.Id:N}";

        var list = await _admin.GetFromJsonAsync<PeerUpdatesResponse>("/api/v1/backup/peer-updates", TestJson.Options);
        var item = list!.Items.ShouldHaveSingleItem();
        item.Key.ShouldBe(key);
        item.InstalledVersion.ShouldBe(list.CurrentVersion);
        item.UpdaterAvailable.ShouldBeFalse();
        item.LocalCommand.ShouldContain($"estrichone-installer:{list.CurrentVersion} backup-ziel");

        // Ohne Updater auf dem Rechner: sofort verständlich abgelehnt.
        var missing = await RequestAsync(key, null);
        missing.Status.ShouldBe("failed");
        missing.Message!.ShouldContain("kein Updater");

        // Neuer als dieser Server bzw. kein gültiges Versionsformat: gar nicht erst beauftragt.
        (await ProblemCodeAsync(key, "99.0.0")).ShouldBe("PEER_UPDATE_VERSION_TOO_NEW");
        (await ProblemCodeAsync(key, "latest")).ShouldBe("PEER_UPDATE_VERSION_INVALID");
        (await ProblemCodeAsync(key, "1.0.0 --privileged")).ShouldBe("PEER_UPDATE_VERSION_INVALID");

        _target.UpdaterHeartbeat();
        var requested = await RequestAsync(key, null);
        requested.Status.ShouldBe("delivered");
        requested.TargetVersion.ShouldBe(list.CurrentVersion);
        var request = await File.ReadAllTextAsync(Path.Combine(_target.Options.UpdatePath, "request"));
        request.ShouldContain($"version={list.CurrentVersion}");
        var requestId = request.Split('\n')[0]["id=".Length..];
        (await ProblemCodeAsync(key, null)).ShouldBe("PEER_UPDATE_RUNNING");

        // Der Updater übernimmt …
        await File.WriteAllTextAsync(Path.Combine(_target.Options.UpdatePath, "started"), requestId);
        await PollAsync();
        (await ItemAsync(key)).Status.ShouldBe("running");

        // … die neue Version wird nicht gesund: Rücksprung auf die vorige.
        await File.WriteAllTextAsync(Path.Combine(_target.Options.UpdatePath, "result"),
            $"id={requestId}\nstatus=rolled_back\nversion=0.0.9\nprevious=0.0.9\nmessage=Neuer Container nicht gesund – vorige Version läuft wieder.\n");
        // Ergebnis kommt hier über die Seite (Live-Abfrage durch den Admin) – er wird trotzdem benachrichtigt.
        (await _admin.GetFromJsonAsync<PeerUpdatesResponse>("/api/v1/backup/peer-updates", TestJson.Options))!.Items.Single().Status.ShouldBe("rolled_back");
        var rolledBack = await ItemAsync(key);
        rolledBack.Status.ShouldBe("rolled_back");
        rolledBack.Message.ShouldBe("Neuer Container nicht gesund – vorige Version läuft wieder.");

        // Zweiter Versuch gelingt.
        var second = await RequestAsync(key, null);
        var secondId = (await File.ReadAllTextAsync(Path.Combine(_target.Options.UpdatePath, "request"))).Split('\n')[0]["id=".Length..];
        secondId.ShouldNotBe(requestId);
        second.Status.ShouldBe("delivered");
        await File.WriteAllTextAsync(Path.Combine(_target.Options.UpdatePath, "result"), $"id={secondId}\nstatus=succeeded\nversion={list.CurrentVersion}\n");
        await PollAsync();
        var done = await ItemAsync(key);
        done.Status.ShouldBe("succeeded");
        done.InstalledVersion.ShouldBe(list.CurrentVersion);

        // Automatisch mitaktualisieren lässt sich je Rechner schalten.
        using var auto = await _admin.PutAsJsonAsync($"/api/v1/backup/peer-updates/{key}/auto", new PeerAutoUpdateRequest(true));
        (await auto.Content.ReadFromJsonAsync<PeerUpdateDto>(TestJson.Options))!.AutoUpdate.ShouldBeTrue();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var titles = await db.Set<Notification>().Select(n => n.Title).ToListAsync();
        titles.ShouldContain("Aktualisierung zurückgerollt");
        titles.ShouldContain("Zusatzserver aktualisiert");
        var actions = await db.Set<EstrichOne.Api.Modules.Audit.AuditEvent>().Where(e => e.EntityId == key).Select(e => e.Action).ToListAsync();
        actions.ShouldContain("peer.update.requested");
        actions.ShouldContain("peer.update.rolled_back");
        actions.ShouldContain("peer.update.succeeded");
    }

    [Fact]
    public async Task PullingTarget_PicksUpSignedOrderOnCheckIn_AndReportsTheResult()
    {
        using var created = await _admin.PostAsJsonAsync("/api/v1/backup/mirrors", new CreateBackupMirrorRequest("NAS zuhause"));
        var mirror = (await created.Content.ReadFromJsonAsync<CreateBackupMirrorResponse>(TestJson.Options))!;
        using var anonymous = _factory.CreateClient();
        using var pair = await anonymous.PostAsJsonAsync("/api/v1/backup/mirror/pair", new MirrorPairRequest(mirror.Code, "Keller"));
        var token = (await pair.Content.ReadFromJsonAsync<MirrorPairResponse>(TestJson.Options))!;
        using var mirrorClient = _factory.CreateClient();
        mirrorClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        var key = $"mirror-{token.Id}";

        var idle = new PeerUpdateStateBody("0.0.9", true, "0.0.9", DateTimeOffset.UtcNow, null, null, "idle", null, null, null);
        (await CheckInAsync(mirrorClient, idle)).ShouldBeNull();
        using (var unauthorized = await anonymous.PostAsJsonAsync("/api/v1/backup/mirror/checkin", new { version = "0.0.9" }))
        {
            unauthorized.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var item = await ItemAsync(key);
        item.Kind.ShouldBe("backup-mirror");
        item.InstalledVersion.ShouldBe("0.0.9");
        item.UpdateAvailable.ShouldBeTrue();
        item.UpdaterAvailable.ShouldBeTrue();

        // Alle aktualisieren: der Spiegel wartet auf Abholung.
        using var all = await _admin.PostAsync(new Uri("/api/v1/backup/peer-updates/update-all", UriKind.Relative), null);
        (await all.Content.ReadFromJsonAsync<PeerUpdateAllResponse>(TestJson.Options))!.Requested.ShouldBe(1);
        (await ItemAsync(key)).Status.ShouldBe("requested");

        var order = (await CheckInAsync(mirrorClient, idle))!;
        var current = (await ItemAsync(key)).AvailableVersion;
        order.Version.ShouldBe(current);
        order.Signature.ShouldBe(UpdateSignature.Compute(ClientRegistry.Sha256(token.Token), "backup-target", token.Id, order.Id, current, current, order.IssuedAt));
        (await ItemAsync(key)).Status.ShouldBe("delivered");

        var running = idle with { RequestId = order.Id, RequestedVersion = current, State = "running" };
        (await CheckInAsync(mirrorClient, running)).ShouldBeNull();
        (await ItemAsync(key)).Status.ShouldBe("running");
        (await CheckInAsync(mirrorClient, running with { Installed = current, State = "succeeded" }, current)).ShouldBeNull();
        var done = await ItemAsync(key);
        done.Status.ShouldBe("succeeded");
        done.InstalledVersion.ShouldBe(current);
        done.UpdateAvailable.ShouldBeFalse();
    }

    private sealed record PeerUpdateStateBody(string Installed, bool UpdaterAvailable, string? UpdaterVersion, DateTimeOffset? UpdaterSeenAt, string? RequestId,
        string? RequestedVersion, string State, string? Message, string? PreviousVersion, DateTimeOffset? FinishedAt);

    private static async Task<UpdateOrder?> CheckInAsync(HttpClient client, PeerUpdateStateBody state, string? version = null)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/backup/mirror/checkin", new { version = version ?? state.Installed, update = state });
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("order").ValueKind == JsonValueKind.Null ? null : body.GetProperty("order").Deserialize<UpdateOrder>(TestJson.Options);
    }

    private async Task<PeerUpdateDto> RequestAsync(string key, string? version)
    {
        using var response = await _admin.PostAsJsonAsync($"/api/v1/backup/peer-updates/{key}/update", new RequestPeerUpdateRequest(version));
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PeerUpdateDto>(TestJson.Options))!;
    }

    private async Task<string?> ProblemCodeAsync(string key, string? version)
    {
        using var response = await _admin.PostAsJsonAsync($"/api/v1/backup/peer-updates/{key}/update", new RequestPeerUpdateRequest(version));
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();
    }

    private async Task<PeerUpdateDto> ItemAsync(string key) =>
        (await _admin.GetFromJsonAsync<PeerUpdatesResponse>("/api/v1/backup/peer-updates?live=false", TestJson.Options))!.Items.Single(i => i.Key == key);

    private async Task PollAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PeerUpdateService>().PollActiveAsync(CancellationToken.None);
    }
}
