using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using EstrichOne.Api.Infrastructure.Hosting;
using EstrichOne.Api.Infrastructure.Peers;
using EstrichOne.Api.Infrastructure.Persistence;
using EstrichOne.Api.Modules.Access.Exports;
using EstrichOne.Api.Modules.Audit.Exports;
using EstrichOne.Api.Modules.Backup.Exports;
using EstrichOne.Api.Modules.Notifications.Exports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Backup;

/// <summary>
/// Fernaktualisierung eines Zusatzservers (docs/backup.md, „Zusatzserver aktualisieren“): Stand und letzter Auftrag je
/// gekoppeltem Backup-Ziel, Spiegel und Archivserver.
/// </summary>
public sealed class PeerUpdate : IAuditedEntity
{
    public const string None = "none";
    public const string Requested = "requested";
    public const string Delivered = "delivered";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string RolledBack = "rolled_back";
    public const string Failed = "failed";

    public string Key { get; private set; } = string.Empty;
    public string Kind { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? InstalledVersion { get; private set; }
    public bool UpdaterAvailable { get; private set; }
    public string? UpdaterVersion { get; private set; }
    public bool Reachable { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }

    /// <summary>Nach einem Update dieses Servers automatisch mitaktualisieren.</summary>
    public bool AutoUpdate { get; private set; }

    public string? RequestId { get; private set; }
    public string? TargetVersion { get; private set; }
    public string? PreviousVersion { get; private set; }
    public DateTimeOffset? RequestedAt { get; private set; }

    /// <summary>null = automatisch (Mitaktualisieren).</summary>
    public Guid? RequestedByUserId { get; private set; }

    public string Status { get; private set; } = None;
    public string? Message { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private PeerUpdate()
    {
    }

    public static PeerUpdate Create(string key, string kind, string name) => new() { Key = key, Kind = kind, Name = Trim(name, 200)! };

    public bool Active => Status is Requested or Delivered or Running;

    public void Rename(string name) => Name = Trim(name, 200)!;

    public void SetAutoUpdate(bool on) => AutoUpdate = on;

    public void Observe(string? installed, PeerUpdateState? state, DateTimeOffset at)
    {
        Reachable = true;
        LastCheckedAt = at;
        InstalledVersion = Trim(installed, 64) ?? InstalledVersion;
        UpdaterAvailable = state?.UpdaterAvailable ?? false;
        UpdaterVersion = Trim(state?.UpdaterVersion, 64);
    }

    public void Unreachable(DateTimeOffset at)
    {
        Reachable = false;
        LastCheckedAt = at;
    }

    public void Request(string requestId, string version, Guid? userId, DateTimeOffset at)
    {
        RequestId = requestId;
        TargetVersion = version;
        PreviousVersion = InstalledVersion;
        RequestedByUserId = userId;
        RequestedAt = at;
        Status = Requested;
        Message = null;
        FinishedAt = null;
    }

    public void Progress(string status) => Status = status;

    public void Finish(string status, string? message, DateTimeOffset at)
    {
        Status = status;
        Message = Trim(message, 1000);
        FinishedAt = at;
        if (status == Succeeded)
        {
            InstalledVersion = TargetVersion;
        }
    }

    private static string? Trim(string? value, int max) => value is null ? null : value.Length > max ? value[..max] : value;
}

internal sealed class PeerUpdateConfiguration : IEntityTypeConfiguration<PeerUpdate>
{
    public void Configure(EntityTypeBuilder<PeerUpdate> builder)
    {
        builder.ToTable("peer_updates");
        builder.HasKey(p => p.Key);
        builder.Property(p => p.Key).HasMaxLength(80);
        builder.Property(p => p.Kind).HasMaxLength(30);
        builder.Property(p => p.Name).HasMaxLength(200);
        builder.Property(p => p.InstalledVersion).HasMaxLength(64);
        builder.Property(p => p.UpdaterVersion).HasMaxLength(64);
        builder.Property(p => p.RequestId).HasMaxLength(32);
        builder.Property(p => p.TargetVersion).HasMaxLength(64);
        builder.Property(p => p.PreviousVersion).HasMaxLength(64);
        builder.Property(p => p.Status).HasMaxLength(20);
        builder.Property(p => p.Message).HasMaxLength(1000);
    }
}

/// <param name="Kind"><c>backup-target</c>, <c>backup-mirror</c> oder <c>archive</c>.</param>
/// <param name="Status"><c>none</c>, <c>requested</c> (wartet auf Abholung bzw. Zustellung), <c>delivered</c>, <c>running</c>,
/// <c>succeeded</c>, <c>rolled_back</c> (neue Version nicht gesund, vorige läuft wieder) oder <c>failed</c>.</param>
/// <param name="UpdateAvailable">Installierte Version ist älter als dieser Server.</param>
/// <param name="UpdaterAvailable">Der Updater auf dem Rechner meldet sich (sonst einmal lokal mit <paramref name="LocalCommand"/> aktualisieren).</param>
/// <param name="LocalCommand">Befehl für die einmalige Aktualisierung am Rechner selbst (bisherige -e-Optionen ergänzen).</param>
public sealed record PeerUpdateDto(string Key, string Kind, string Name, string? InstalledVersion, string AvailableVersion, bool UpdateAvailable,
    bool UpdaterAvailable, string? UpdaterVersion, bool Reachable, DateTimeOffset? LastCheckedAt, string Status, string? TargetVersion,
    string? PreviousVersion, string? Message, DateTimeOffset? RequestedAt, DateTimeOffset? FinishedAt, bool AutoUpdate, string LocalCommand);

public sealed record PeerUpdatesResponse(string CurrentVersion, IReadOnlyList<PeerUpdateDto> Items);

/// <param name="Version">Leer = Version dieses Servers. Nur gültige Versionen bis einschließlich der eigenen.</param>
public sealed record RequestPeerUpdateRequest(string? Version);

public sealed record PeerAutoUpdateRequest(bool AutoUpdate);

public sealed record PeerUpdateAllResponse(int Requested, IReadOnlyList<PeerUpdateDto> Items);

/// <summary>Meldung eines Ziels, das zieht (alle paar Minuten).</summary>
public sealed record MirrorCheckIn(string? Version, PeerUpdateState? Update, string? RejectedRequestId, string? RejectedMessage);

public sealed record MirrorCheckInAnswer(PeerUpdateOrder? Order);

internal sealed record PeerStatusProbe(string? Version, PeerUpdateState? Update);

public sealed record PeerUpdateProblem(int Status, string Code, string Message);

/// <summary>Backup-Ziele und Ziele, die ziehen.</summary>
internal sealed class BackupPeerSource(BackupTargetService targets, BackupMirrorStore mirrors) : IUpdatablePeerSource
{
    public async Task<IReadOnlyList<UpdatablePeer>> ListAsync(CancellationToken cancellationToken)
    {
        var result = (await targets.UpdatablePeersAsync(cancellationToken)).ToList();
        result.AddRange(mirrors.PairedForUpdates().Select(m => new UpdatablePeer($"mirror-{m.Id}", "backup-mirror", m.Name, "backup-target", m.Id,
            m.TokenHash, null, m.Version, "/system/backup-ziele")));
        return result;
    }
}

/// <summary>Aufträge erteilen, zustellen bzw. bereithalten, Rückmeldungen auswerten, Administratoren benachrichtigen.</summary>
internal sealed partial class PeerUpdateService(
    EstrichOneDbContext db, IEnumerable<IUpdatablePeerSource> sources, TimeProvider clock, INotifier notifier, IRoleAssignments roles,
    IAuditLog audit, IOptions<BackupSettings> settings, ILogger<PeerUpdateService> logger)
{
    /// <summary>Ohne Rückmeldung gilt ein Auftrag danach als fehlgeschlagen.</summary>
    public static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string CurrentVersion => ApplicationVersion.Current.Split('+')[0];

    public async Task<PeerUpdatesResponse> ListAsync(bool live, CancellationToken cancellationToken)
    {
        var pairs = await LoadAsync(cancellationToken);
        foreach (var (peer, row) in pairs)
        {
            if (live)
            {
                await RefreshAsync(peer, row, cancellationToken);
            }
            else
            {
                await CheckTimeoutAsync(peer, row, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new PeerUpdatesResponse(CurrentVersion, [.. pairs.Select(p => ToDto(p.Peer, p.Row))]);
    }

    public async Task<(PeerUpdateDto? Item, PeerUpdateProblem? Problem)> RequestAsync(string key, string? version, Guid? userId,
        CancellationToken cancellationToken)
    {
        var pairs = await LoadAsync(cancellationToken);
        if (pairs.FirstOrDefault(p => p.Row.Key == key) is not ({ } peer, { } row))
        {
            return (null, new PeerUpdateProblem(404, "PEER_NOT_FOUND", "Zusatzserver nicht gefunden."));
        }

        var current = CurrentVersion;
        var target = string.IsNullOrWhiteSpace(version) ? current : version.Trim();
        if (!SemVer.IsValid(target) || !SemVer.IsValid(current))
        {
            return (null, new PeerUpdateProblem(422, "PEER_UPDATE_VERSION_INVALID", "Keine gültige Version (Format 1.2.3 bzw. 1.2.3-preview.4)."));
        }

        if (SemVer.Compare(target, current) > 0)
        {
            return (null, new PeerUpdateProblem(422, "PEER_UPDATE_VERSION_TOO_NEW",
                $"Zusatzserver können höchstens auf die Version dieses Servers ({current}) aktualisiert werden."));
        }

        await CheckTimeoutAsync(peer, row, cancellationToken);
        if (row.Active)
        {
            return (null, new PeerUpdateProblem(409, "PEER_UPDATE_RUNNING", "Für diesen Rechner läuft bereits eine Aktualisierung."));
        }

        var now = clock.GetUtcNow();
        row.Request(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)), target, userId, now);
        audit.Record("peer.update.requested", userId, "peer", row.Key, new Dictionary<string, string?>
        {
            ["name"] = row.Name, ["kind"] = row.Kind, ["version"] = target, ["installed"] = row.InstalledVersion,
            ["automatic"] = userId is null ? "true" : "false",
        });
        LogRequested(logger, row.Name, target);

        if (peer.Connect is { } connect)
        {
            await DeliverAsync(peer, row, connect, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return (ToDto(peer, row), null);
    }

    public async Task<PeerUpdateAllResponse> RequestAllAsync(Guid? userId, IReadOnlyCollection<string>? kinds, CancellationToken cancellationToken)
    {
        var list = await ListAsync(live: true, cancellationToken);
        var count = 0;
        foreach (var item in list.Items.Where(i => i.UpdateAvailable && i.UpdaterAvailable && !IsActive(i.Status) && (kinds is null || kinds.Contains(i.Kind))))
        {
            if ((await RequestAsync(item.Key, null, userId, cancellationToken)).Problem is null)
            {
                count++;
            }
        }

        return new PeerUpdateAllResponse(count, (await ListAsync(live: false, cancellationToken)).Items);
    }

    public async Task<PeerUpdateDto?> SetAutoUpdateAsync(string key, bool on, Guid userId, CancellationToken cancellationToken)
    {
        var pairs = await LoadAsync(cancellationToken);
        if (pairs.FirstOrDefault(p => p.Row.Key == key) is not ({ } peer, { } row))
        {
            return null;
        }

        row.SetAutoUpdate(on);
        audit.Record("peer.update.auto_changed", userId, "peer", key, new Dictionary<string, string?> { ["autoUpdate"] = on ? "true" : "false" });
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(peer, row);
    }

    /// <summary>Ziel, das zieht, meldet sich: Stand übernehmen, Auftrag ausliefern.</summary>
    public async Task<PeerUpdateOrder?> CheckInAsync(string mirrorId, MirrorCheckIn report, CancellationToken cancellationToken)
    {
        var pairs = await LoadAsync(cancellationToken);
        if (pairs.FirstOrDefault(p => p.Row.Key == $"mirror-{mirrorId}") is not ({ } peer, { } row))
        {
            return null;
        }

        row.Observe(report.Version, report.Update, clock.GetUtcNow());
        if (row.Active && report.RejectedRequestId is { } rejected && rejected == row.RequestId)
        {
            await FinishAsync(peer, row, PeerUpdate.Failed, report.RejectedMessage ?? "Vom Rechner abgelehnt.", cancellationToken);
        }
        else
        {
            await ApplyAsync(peer, row, report.Update, cancellationToken);
        }

        await CheckTimeoutAsync(peer, row, cancellationToken);
        PeerUpdateOrder? order = null;
        if (row.Status is PeerUpdate.Requested or PeerUpdate.Delivered && report.Update?.RequestId != row.RequestId)
        {
            order = PeerUpdateOrders.Create(peer.TokenSha256, peer.Role, peer.ClientId, row.RequestId!, row.TargetVersion!, CurrentVersion, clock.GetUtcNow());
            row.Progress(PeerUpdate.Delivered);
        }

        await db.SaveChangesAsync(cancellationToken);
        return order;
    }

    /// <summary>Laufende Aufträge nachverfolgen (Hintergrunddienst, alle 30 Sekunden).</summary>
    public async Task<int> PollActiveAsync(CancellationToken cancellationToken)
    {
        if (!await db.Set<PeerUpdate>().AnyAsync(p => p.Status == PeerUpdate.Requested || p.Status == PeerUpdate.Delivered || p.Status == PeerUpdate.Running,
                cancellationToken))
        {
            return 0;
        }

        var active = (await LoadAsync(cancellationToken)).Where(p => p.Row.Active).ToList();
        foreach (var (peer, row) in active)
        {
            if (peer.Connect is not null)
            {
                await RefreshAsync(peer, row, cancellationToken);
            }
            else
            {
                await CheckTimeoutAsync(peer, row, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return active.Count;
    }

    /// <summary>
    /// Nach einem Update dieses Servers: Zusatzserver mit „automatisch mitaktualisieren“ beauftragen, sonst Administratoren
    /// einmal je Version auf die Aktualisierung hinweisen.
    /// </summary>
    public async Task OfferAsync(CancellationToken cancellationToken)
    {
        var list = await ListAsync(live: true, cancellationToken);
        var rows = await db.Set<PeerUpdate>().ToDictionaryAsync(r => r.Key, cancellationToken);
        var offer = new List<PeerUpdateDto>();
        foreach (var item in list.Items.Where(i => i.UpdateAvailable && !IsActive(i.Status)))
        {
            var row = rows[item.Key];
            var failedBefore = row.TargetVersion == list.CurrentVersion && row.Status is PeerUpdate.Failed or PeerUpdate.RolledBack;
            if (row.AutoUpdate && item.UpdaterAvailable && !failedBefore)
            {
                await RequestAsync(item.Key, null, null, cancellationToken);
            }
            else
            {
                offer.Add(item);
            }
        }

        if (offer.Count > 0)
        {
            var admins = await roles.GetUserIdsWithPermissionAsync(Permissions.BackupManage, cancellationToken);
            var names = string.Join(", ", offer.Select(o => o.Name).Take(5));
            await notifier.NotifyAsync(admins, new NotificationRequest(NotificationCategory.System,
                $"Zusatzserver auf {list.CurrentVersion} aktualisieren",
                $"{names}: installiert ist eine ältere Version. Im Backup Center unter Backup-Ziele bzw. Archiv mit einem Klick aktualisieren.",
                offer[0].Kind == "archive" ? "/system/archiv" : "/system/backup-ziele", "peerUpdate", null, NotificationUrgency.Normal,
                $"peer-update-available:{list.CurrentVersion}", NotificationEvents.PeerUpdateAvailable), cancellationToken);
        }
    }

    private async Task DeliverAsync(UpdatablePeer peer, PeerUpdate row, Func<TimeSpan, HttpClient> connect, CancellationToken cancellationToken)
    {
        var order = PeerUpdateOrders.Create(peer.TokenSha256, peer.Role, peer.ClientId, row.RequestId!, row.TargetVersion!, CurrentVersion, clock.GetUtcNow());
        try
        {
            using var http = connect(TimeSpan.FromSeconds(30));
            using var response = await http.PostAsJsonAsync(new Uri("/api/v1/update", UriKind.Relative), order, Json, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var state = await response.Content.ReadFromJsonAsync<PeerUpdateState>(Json, cancellationToken);
                row.Observe(state?.Installed, state, clock.GetUtcNow());
                row.Progress(state?.State == "running" ? PeerUpdate.Running : PeerUpdate.Delivered);
                return;
            }

            var message = response.StatusCode == System.Net.HttpStatusCode.NotFound
                ? "Dieser Rechner kennt die Fernaktualisierung noch nicht – einmal lokal mit dem aktuellen Installer aktualisieren (Befehl in der Weboberfläche)."
                : await ProblemTitleAsync(response, cancellationToken);
            await FinishAsync(peer, row, PeerUpdate.Failed, message, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            row.Unreachable(clock.GetUtcNow());
            await FinishAsync(peer, row, PeerUpdate.Failed, $"Nicht erreichbar: {exception.Message}", cancellationToken);
        }
    }

    private async Task RefreshAsync(UpdatablePeer peer, PeerUpdate row, CancellationToken cancellationToken)
    {
        if (peer.Connect is { } connect)
        {
            try
            {
                using var http = connect(TimeSpan.FromSeconds(15));
                var status = await http.GetFromJsonAsync<PeerStatusProbe>(new Uri("/api/v1/status", UriKind.Relative), Json, cancellationToken);
                row.Observe(status?.Version, status?.Update, clock.GetUtcNow());
                await ApplyAsync(peer, row, status?.Update, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                // Während des Austauschs ist der Rechner kurz nicht erreichbar – das ist kein Fehler des Auftrags.
                row.Unreachable(clock.GetUtcNow());
            }
        }

        await CheckTimeoutAsync(peer, row, cancellationToken);
    }

    private async Task ApplyAsync(UpdatablePeer peer, PeerUpdate row, PeerUpdateState? state, CancellationToken cancellationToken)
    {
        if (!row.Active || state is null || state.RequestId is null || state.RequestId != row.RequestId)
        {
            return;
        }

        switch (state.State)
        {
            case "pending":
                row.Progress(PeerUpdate.Delivered);
                break;
            case "running":
                row.Progress(PeerUpdate.Running);
                break;
            case PeerUpdate.Succeeded or PeerUpdate.RolledBack or PeerUpdate.Failed:
                await FinishAsync(peer, row, state.State, state.Message, cancellationToken);
                break;
        }
    }

    private async Task CheckTimeoutAsync(UpdatablePeer peer, PeerUpdate row, CancellationToken cancellationToken)
    {
        if (row.Active && row.RequestedAt is { } at && clock.GetUtcNow() - at > RunTimeout)
        {
            await FinishAsync(peer, row, PeerUpdate.Failed, peer.Connect is null && row.Status == PeerUpdate.Requested
                ? "Der Rechner hat den Auftrag nicht abgeholt (meldet er sich noch?)."
                : "Keine Rückmeldung innerhalb von 30 Minuten. Am Rechner prüfen: sudo docker logs estrichone-<rolle>-updater", cancellationToken);
        }
    }

    private async Task FinishAsync(UpdatablePeer peer, PeerUpdate row, string status, string? message, CancellationToken cancellationToken)
    {
        row.Finish(status, message, clock.GetUtcNow());
        audit.Record($"peer.update.{status}", null, "peer", row.Key, new Dictionary<string, string?>
        {
            ["name"] = row.Name, ["version"] = row.TargetVersion, ["previous"] = row.PreviousVersion, ["message"] = row.Message,
        });
        LogFinished(logger, row.Name, row.TargetVersion ?? "?", status);
        var admins = await roles.GetUserIdsWithPermissionAsync(Permissions.BackupManage, cancellationToken);
        var (title, body, urgency, code) = status switch
        {
            PeerUpdate.Succeeded => ("Zusatzserver aktualisiert", $"{row.Name}: jetzt Version {row.TargetVersion}.", NotificationUrgency.Low,
                NotificationEvents.PeerUpdateSucceeded),
            PeerUpdate.RolledBack => ("Aktualisierung zurückgerollt", $"{row.Name}: Version {row.TargetVersion} wurde nicht gesund, "
                + $"die vorige Version {row.PreviousVersion ?? ""} läuft wieder. {row.Message}".TrimEnd(), NotificationUrgency.High,
                NotificationEvents.PeerUpdateFailed),
            _ => ("Aktualisierung fehlgeschlagen", $"{row.Name}: {row.Message}", NotificationUrgency.High, NotificationEvents.PeerUpdateFailed),
        };
        await notifier.NotifyAsync(admins, new NotificationRequest(NotificationCategory.System, title, body, peer.Link, "peerUpdate", null, urgency,
            $"peer-update:{row.RequestId}", code), cancellationToken);
    }

    private async Task<List<(UpdatablePeer Peer, PeerUpdate Row)>> LoadAsync(CancellationToken cancellationToken)
    {
        var peers = new List<UpdatablePeer>();
        foreach (var source in sources)
        {
            peers.AddRange(await source.ListAsync(cancellationToken));
        }

        var rows = await db.Set<PeerUpdate>().ToDictionaryAsync(r => r.Key, cancellationToken);
        var result = new List<(UpdatablePeer, PeerUpdate)>();
        foreach (var peer in peers)
        {
            if (!rows.Remove(peer.Key, out var row))
            {
                row = PeerUpdate.Create(peer.Key, peer.Kind, peer.Name);
                db.Add(row);
            }

            if (row.Name != peer.Name)
            {
                row.Rename(peer.Name);
            }

            if (row.InstalledVersion is null && peer.ReportedVersion is { Length: > 0 } reported)
            {
                row.Observe(reported, null, row.LastCheckedAt ?? clock.GetUtcNow());
            }

            result.Add((peer, row));
        }

        // Entkoppelte Rechner: Stand verwerfen (der Verlauf bleibt im Protokoll).
        db.RemoveRange(rows.Values);
        return result;
    }

    private PeerUpdateDto ToDto(UpdatablePeer peer, PeerUpdate row)
    {
        var current = CurrentVersion;
        var available = row.InstalledVersion is { } installed && SemVer.IsValid(installed) && SemVer.IsValid(current)
            && SemVer.Compare(installed, current) < 0;
        var role = peer.Role == "archive" ? "archiv" : "backup-ziel";
        var command = $"sudo docker run --rm -v /var/run/docker.sock:/var/run/docker.sock {settings.Value.InstallerImage}:{current} {role}";
        return new PeerUpdateDto(row.Key, row.Kind, row.Name, row.InstalledVersion, current, available, row.UpdaterAvailable, row.UpdaterVersion,
            row.Reachable, row.LastCheckedAt, row.Status, row.TargetVersion, row.PreviousVersion, row.Message, row.RequestedAt, row.FinishedAt,
            row.AutoUpdate, command);
    }

    private static bool IsActive(string status) => status is PeerUpdate.Requested or PeerUpdate.Delivered or PeerUpdate.Running;

    private static async Task<string> ProblemTitleAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, cancellationToken);
            if (problem.ValueKind == JsonValueKind.Object && problem.TryGetProperty("title", out var title) && title.GetString() is { Length: > 0 } text)
            {
                return text;
            }
        }
        catch (JsonException)
        {
        }

        return $"Vom Rechner abgelehnt (HTTP {(int)response.StatusCode}).";
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Fernaktualisierung „{Name}“ auf {Version} beauftragt.")]
    private static partial void LogRequested(ILogger logger, string name, string version);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fernaktualisierung „{Name}“ auf {Version}: {Status}.")]
    private static partial void LogFinished(ILogger logger, string name, string version, string status);
}

/// <summary>Verfolgt laufende Aufträge (alle 30 Sekunden) und bietet nach einem Update dieses Servers die Aktualisierung an.</summary>
internal sealed partial class PeerUpdateWorker(IServiceScopeFactory scopes, ILogger<PeerUpdateWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextOffer = DateTimeOffset.UtcNow.AddMinutes(2);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<PeerUpdateService>();
                await service.PollActiveAsync(stoppingToken);
                if (DateTimeOffset.UtcNow >= nextOffer)
                {
                    nextOffer = DateTimeOffset.UtcNow.AddHours(1);
                    await service.OfferAsync(stoppingToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Fernaktualisierung: Durchlauf fehlgeschlagen.")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
