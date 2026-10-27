using System.Net.Http.Json;
using System.Text.Json;
using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Peers;
using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Backup;

/// <summary>
/// Gekoppeltes Backup-Ziel auf anderer Hardware (Image <c>estrichone-backup-target</c>: restic-REST-Server, nur anhängen).
/// Der Backup-Dienst sichert zusätzlich dorthin; Zugangsdaten bekommt er als Datei im gemeinsamen Statusverzeichnis.
/// </summary>
public sealed class BackupTarget : IAuditedEntity
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public string ServerVersion { get; private set; } = string.Empty;
    public string CaCertificatePem { get; private set; } = string.Empty;
    public string CaFingerprint { get; private set; } = string.Empty;
    public string ClientId { get; private set; } = string.Empty;
    public string ProtectedToken { get; private set; } = string.Empty;
    public string RestPath { get; private set; } = string.Empty;
    public string RestUsername { get; private set; } = string.Empty;
    public string ProtectedRestPassword { get; private set; } = string.Empty;
    public DateTimeOffset PairedAt { get; private set; }
    public Guid PairedByUserId { get; private set; }
    public DateTimeOffset? LastContactAt { get; private set; }
    public string? LastError { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private BackupTarget()
    {
    }

    public static BackupTarget Create(Guid id, PeerPairing pairing, string protectedToken, string protectedRestPassword, Guid userId,
        DateTimeOffset at) => new()
    {
        Id = id, Name = pairing.Name, Address = pairing.Address.ToString().TrimEnd('/'), ServerVersion = pairing.Version,
        CaCertificatePem = pairing.CaCertificatePem, CaFingerprint = pairing.CaFingerprint, ClientId = pairing.ClientId,
        ProtectedToken = protectedToken, RestPath = pairing.RestPath ?? string.Empty, RestUsername = pairing.RestUsername ?? string.Empty,
        ProtectedRestPassword = protectedRestPassword, PairedByUserId = userId, PairedAt = at, LastContactAt = at,
    };

    public void Contacted(DateTimeOffset at, string? error, string? version)
    {
        LastError = error;
        if (error is null)
        {
            LastContactAt = at;
        }

        if (version is not null)
        {
            ServerVersion = version;
        }
    }
}

internal sealed class BackupTargetConfiguration : IEntityTypeConfiguration<BackupTarget>
{
    public void Configure(EntityTypeBuilder<BackupTarget> builder)
    {
        builder.ToTable("backup_targets");
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(200);
        builder.Property(t => t.Address).HasMaxLength(300);
        builder.Property(t => t.ServerVersion).HasMaxLength(50);
        builder.Property(t => t.CaFingerprint).HasMaxLength(64);
        builder.Property(t => t.ClientId).HasMaxLength(64);
        builder.Property(t => t.RestPath).HasMaxLength(200);
        builder.Property(t => t.RestUsername).HasMaxLength(100);
        builder.Property(t => t.LastError).HasMaxLength(1000);
    }
}

public sealed record BackupTargetRemoteStatus(long Bytes, int Snapshots, DateTimeOffset? LastSnapshotAt, bool AppendOnly, bool ServerRunning,
    long? DiskFreeBytes, long? DiskTotalBytes);

public sealed record BackupTargetDto(
    Guid Id, string Name, string Address, string ServerVersion, string FingerprintDisplay, DateTimeOffset PairedAt, DateTimeOffset? LastContactAt,
    string? LastError, BackupRunInfo? LastRun, BackupTargetRemoteStatus? Remote, bool BackupServiceConfigured);

public sealed record PairBackupTargetRequest(string Address, string Code);

internal sealed record TargetStatusResponse(string Version, long? DiskFreeBytes, long? DiskTotalBytes, TargetBackupInfo? Backup);

internal sealed record TargetBackupInfo(string Repository, long Bytes, int Snapshots, DateTimeOffset? LastSnapshotAt, bool AppendOnly, bool ServerRunning);

internal sealed class BackupTargetService(
    EstrichOneDbContext db, IDataProtectionProvider protection, TimeProvider clock, IOptions<BackupSettings> settings,
    IOptions<EstrichOneOptions> options, BackupStatusReader statusReader)
{
    private const string TokenPurpose = "EstrichOne.Backup.TargetToken";
    private const string PasswordPurpose = "EstrichOne.Backup.TargetPassword";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string? TargetsDir => string.IsNullOrWhiteSpace(settings.Value.StatusPath) ? null : Path.Combine(settings.Value.StatusPath, "targets");

    public async Task<BackupTarget> PairAsync(string address, string code, Guid userId, CancellationToken cancellationToken)
    {
        var uri = PeerClient.NormalizeAddress(address);
        var pairing = await PeerClient.PairAsync(uri, code, $"EstrichONE {options.Value.ServerName}", "backup-target", cancellationToken);
        if (pairing.RestPassword is null || pairing.RestUsername is null || pairing.RestPath is null)
        {
            throw new PeerException("PEER_PAIRING_FAILED", "Das Backup-Ziel hat keine Zugangsdaten geliefert.");
        }

        var target = BackupTarget.Create(Guid.CreateVersion7(clock.GetUtcNow()), pairing,
            protection.CreateProtector(TokenPurpose).Protect(pairing.Token),
            protection.CreateProtector(PasswordPurpose).Protect(pairing.RestPassword), userId, clock.GetUtcNow());
        db.Add(target);
        await db.SaveChangesAsync(cancellationToken);
        WriteConfiguration(target);
        return target;
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        var target = await db.Set<BackupTarget>().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (target is null)
        {
            return false;
        }

        try
        {
            using var http = Client(target, TimeSpan.FromSeconds(10));
            using var _ = await http.DeleteAsync(new Uri("/api/v1/pairing", UriKind.Relative), cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // Ziel nicht erreichbar: lokal trotzdem entfernen.
        }

        db.Remove(target);
        await db.SaveChangesAsync(cancellationToken);
        if (TargetsDir is { } dir)
        {
            File.Delete(Path.Combine(dir, $"{target.Id:N}.conf"));
            File.Delete(Path.Combine(dir, $"{target.Id:N}.ca.pem"));
        }

        return true;
    }

    public async Task<List<BackupTargetDto>> ListAsync(bool live, CancellationToken cancellationToken)
    {
        var targets = await db.Set<BackupTarget>().OrderBy(t => t.PairedAt).ToListAsync(cancellationToken);
        var result = new List<BackupTargetDto>();
        foreach (var target in targets)
        {
            WriteConfiguration(target);
            BackupTargetRemoteStatus? remote = null;
            if (live)
            {
                try
                {
                    using var http = Client(target, TimeSpan.FromSeconds(15));
                    var status = await http.GetFromJsonAsync<TargetStatusResponse>("/api/v1/status", Json, cancellationToken);
                    if (status?.Backup is { } backup)
                    {
                        remote = new BackupTargetRemoteStatus(backup.Bytes, backup.Snapshots, backup.LastSnapshotAt, backup.AppendOnly,
                            backup.ServerRunning, status.DiskFreeBytes, status.DiskTotalBytes);
                    }

                    target.Contacted(clock.GetUtcNow(), null, status?.Version);
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
                {
                    target.Contacted(clock.GetUtcNow(), exception is HttpRequestException { InnerException: System.Security.Authentication.AuthenticationException }
                        ? "Zertifikat passt nicht mehr zur Kopplung – bitte neu koppeln."
                        : $"Nicht erreichbar: {exception.Message}", null);
                }
            }

            result.Add(new BackupTargetDto(target.Id, target.Name, target.Address, target.ServerVersion,
                PeerClient.FormatFingerprint(target.CaFingerprint), target.PairedAt, target.LastContactAt, target.LastError,
                statusReader.ReadEntry($"target-{target.Id:N}"), remote, TargetsDir is not null));
        }

        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <summary>Schreibt Zugang und CA für den Backup-Dienst (liest beide bei jedem Lauf, siehe deploy/backup/estrichone-backup).</summary>
    public void WriteConfiguration(BackupTarget target)
    {
        if (TargetsDir is not { } dir || !Directory.Exists(settings.Value.StatusPath))
        {
            return;
        }

        Directory.CreateDirectory(dir);
        var password = protection.CreateProtector(PasswordPurpose).Unprotect(target.ProtectedRestPassword);
        var address = new Uri(target.Address);
        var repository = $"rest:https://{target.RestUsername}:{password}@{address.Authority}{target.RestPath}";
        var name = new string([.. target.Name.Where(c => c is not ('\'' or '\n' or '\r' or '\\'))]);
        WritePrivate(Path.Combine(dir, $"{target.Id:N}.ca.pem"), target.CaCertificatePem);
        WritePrivate(Path.Combine(dir, $"{target.Id:N}.conf"),
            $"# Von EstrichONE geschrieben (System → Backup-Ziele). Nicht von Hand ändern.\nTARGET_NAME='{name} ({address.Host})'\nTARGET_REPOSITORY='{repository}'\n");
    }

    /// <summary>
    /// Notfallzugang: alles, um nach einem Totalverlust des Servers von diesem Ziel wiederherzustellen
    /// (zusammen mit dem Backup-Passwort aus der .env). Getrennt vom Server aufbewahren.
    /// </summary>
    public async Task<string?> RecoveryInfoAsync(Guid id, CancellationToken cancellationToken)
    {
        var target = await db.Set<BackupTarget>().AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (target is null)
        {
            return null;
        }

        var password = protection.CreateProtector(PasswordPurpose).Unprotect(target.ProtectedRestPassword);
        var address = new Uri(target.Address);
        var repository = $"rest:https://{target.RestUsername}:{password}@{address.Authority}{target.RestPath}";
        return $"""
            EstrichONE – Notfallzugang zum Backup-Ziel „{target.Name}“ ({target.Address})
            Erstellt: {clock.GetUtcNow():dd.MM.yyyy HH:mm} UTC – VERTRAULICH, getrennt vom Server aufbewahren (z. B. Passwortmanager).

            Repository:     {repository}
            Fingerabdruck:  {PeerClient.FormatFingerprint(target.CaFingerprint)}

            Wiederherstellen auf einem neuen Server (zusätzlich nötig: RESTIC_PASSWORD aus der alten .env):
            1. EstrichONE wie gewohnt installieren und die alte .env zurückkopieren (bzw. RESTIC_PASSWORD eintragen).
            2. Den Zertifikatsblock unten als Datei ssh/backup-ziel-ca.pem im Installationsverzeichnis speichern.
            3. cd /opt/estrichone && sudo docker compose -p estrichone stop api
            4. sudo docker compose -p estrichone --profile restore run --rm \
                 -e RESTORE_FROM='{repository}' \
                 -e RESTIC_CACERT=/home/backup/.ssh/backup-ziel-ca.pem restore latest
            5. sudo docker compose -p estrichone up -d

            {target.CaCertificatePem.Trim()}

            """;
    }

    /// <summary>Zugänge aller gekoppelten Ziele für das Notfallblatt (Repository mit Zugangsdaten, Fingerabdruck).</summary>
    public async Task<IReadOnlyList<(string Name, string Address, string Repository, string Fingerprint)>> RecoveryEntriesAsync(CancellationToken cancellationToken)
    {
        var targets = await db.Set<BackupTarget>().AsNoTracking().OrderBy(t => t.PairedAt).ToListAsync(cancellationToken);
        return [.. targets.Select(target =>
        {
            var password = protection.CreateProtector(PasswordPurpose).Unprotect(target.ProtectedRestPassword);
            var address = new Uri(target.Address);
            return (target.Name, target.Address, $"rest:https://{target.RestUsername}:{password}@{address.Authority}{target.RestPath}",
                PeerClient.FormatFingerprint(target.CaFingerprint));
        })];
    }

    private HttpClient Client(BackupTarget target, TimeSpan timeout) =>
        PeerClient.CreateClient(new Uri(target.Address), target.CaCertificatePem,
            protection.CreateProtector(TokenPurpose).Unprotect(target.ProtectedToken), timeout);

    private static void WritePrivate(string path, string content)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temp, path, overwrite: true);
    }
}
