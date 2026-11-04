// Integrationstests laufen unter Linux (CI); kurze Aufrufe ohne Abbruch-Token.
#pragma warning disable xUnit1051, CA1416
using System.Net;
using System.Net.Http.Json;
using EstrichOne.Api.Modules.Access;
using EstrichOne.Api.Modules.Archive;
using EstrichOne.Api.Modules.Backup;
using EstrichOne.Api.Tests.Support;
using EstrichOne.Archive;
using Microsoft.Extensions.DependencyInjection;

namespace EstrichOne.Api.Tests.Backup;

/// <summary>Backup-Ziel koppeln: Zugang für den Backup-Dienst wird geschrieben, Status kommt vom Ziel.</summary>
public sealed class BackupTargetTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly string _status = Path.Combine(Path.GetTempPath(), "estrichone-backup-targets", Guid.NewGuid().ToString("N"));
    private ApiFactory _factory = null!;
    private ArchiveServerHost _target = null!;
    private HttpClient _admin = null!;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_status);
        _factory = await ApiFactory.CreateMigratedAsync(postgres, new Dictionary<string, string> { ["EstrichOne:Backup:StatusPath"] = _status });
        _target = await ArchiveServerHost.StartAsync(ServerRole.BackupTarget);
        _admin = await _factory.CreateClientAsAsync("it", StandardRoles.ItAdministratorId);
    }

    public async ValueTask DisposeAsync()
    {
        _admin.Dispose();
        await _target.DisposeAsync();
        await _factory.DisposeAsync();
        Directory.Delete(_status, recursive: true);
    }

    [Fact]
    public async Task Kombiserver_EinCode_KoppeltBackupZielUndArchiv()
    {
        await using var archiveHost = await ArchiveServerHost.StartAsync();
        using var admin = await _factory.CreateClientAsAsync("admin", StandardRoles.AdministratorId);
        var registry = _target.Services.GetRequiredService<ClientRegistry>();
        Should.Throw<ArgumentException>(() => registry.SetPairingCode("ABCD-EFGH"));
        Should.Throw<ArgumentException>(() => registry.SetPairingCode("ABCD-EFGH-JKLM-NPQ0"));
        // Ein vom Installer erzeugter Code für beide Dienste (getrennte Container, getrennte Daten).
        const string code = "KMBX-2345-6789-ABCD";
        registry.SetPairingCode(code.ToLowerInvariant()).Code.ShouldBe(code);
        archiveHost.Services.GetRequiredService<ClientRegistry>().SetPairingCode(code);

        using var backup = await _admin.PostAsJsonAsync("/api/v1/backup/targets", new { address = _target.Address, code });
        backup.StatusCode.ShouldBe(HttpStatusCode.OK, await backup.Content.ReadAsStringAsync());
        using var archive = await admin.PostAsJsonAsync("/api/v1/archive/pair", new { address = archiveHost.Address, code });
        archive.StatusCode.ShouldBe(HttpStatusCode.OK, await archive.Content.ReadAsStringAsync());
        (await admin.GetFromJsonAsync<ArchiveOverview>("/api/v1/archive/status", TestJson.Options))!.Connected.ShouldBeTrue();
        registry.Clients.Count.ShouldBe(1);
        archiveHost.Services.GetRequiredService<ClientRegistry>().Clients.Count.ShouldBe(1);
        // Beide Codes sind verbraucht.
        registry.OpenPairing().ShouldBeNull();
        archiveHost.Services.GetRequiredService<ClientRegistry>().OpenPairing().ShouldBeNull();
    }

    [Fact]
    public async Task PairTarget_WritesRepositoryForBackupService_AndShowsStatus()
    {
        using var paired = await _admin.PostAsJsonAsync("/api/v1/backup/targets", new { address = _target.Address, code = _target.NewCode() });
        paired.StatusCode.ShouldBe(HttpStatusCode.OK);
        var target = (await paired.Content.ReadFromJsonAsync<BackupTargetDto>(TestJson.Options))!;
        target.Name.ShouldBe("Backup-Ziel Test");
        target.Remote.ShouldNotBeNull();
        target.Remote.AppendOnly.ShouldBeTrue();
        target.BackupServiceConfigured.ShouldBeTrue();

        var conf = await File.ReadAllTextAsync(Path.Combine(_status, "targets", $"{target.Id:N}.conf"));
        conf.ShouldContain($"TARGET_REPOSITORY='rest:https://eo-");
        conf.ShouldContain($"@{_target.Address}/restic/eo-");
        File.Exists(Path.Combine(_status, "targets", $"{target.Id:N}.ca.pem")).ShouldBeTrue();

        var recovery = await _admin.GetStringAsync(new Uri($"/api/v1/backup/targets/{target.Id}/recovery", UriKind.Relative));
        recovery.ShouldContain("Repository:     rest:https://eo-");
        recovery.ShouldContain("estrichone-installer:<version> restore");
        recovery.ShouldContain("BEGIN CERTIFICATE");

        // Archivserver-Code passt nicht für ein Backup-Ziel und umgekehrt.
        await using var archive = await ArchiveServerHost.StartAsync();
        using var wrongRole = await _admin.PostAsJsonAsync("/api/v1/backup/targets", new { address = archive.Address, code = archive.NewCode() });
        (await wrongRole.Content.ReadAsStringAsync()).ShouldContain("PEER_WRONG_ROLE");

        using var removed = await _admin.DeleteAsync(new Uri($"/api/v1/backup/targets/{target.Id}", UriKind.Relative));
        removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        File.Exists(Path.Combine(_status, "targets", $"{target.Id:N}.conf")).ShouldBeFalse();
        _target.Services.GetService(typeof(ClientRegistry)).ShouldBeOfType<ClientRegistry>().Clients.ShouldBeEmpty();
    }
}
