using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EstrichOne.Archive;

/// <summary>Semantische Version (SemVer 2.0, ohne Build-Metadaten) – identisch in der Suite (Infrastructure/Peers/PeerUpdateProtocol.cs).</summary>
public static partial class SemVer
{
    public static bool IsValid(string? version) => version is { Length: > 0 and <= 64 } && Pattern().IsMatch(version);

    /// <summary>Vergleich nach SemVer 2.0: Vorabversionen liegen vor der Version ohne Zusatz, Zahlen numerisch, Zahlen vor Text.</summary>
    public static int Compare(string left, string right)
    {
        var a = Pattern().Match(left);
        var b = Pattern().Match(right);
        if (!a.Success || !b.Success)
        {
            throw new ArgumentException("Keine gültige Version.");
        }

        for (var i = 1; i <= 3; i++)
        {
            var c = long.Parse(a.Groups[i].Value, CultureInfo.InvariantCulture).CompareTo(long.Parse(b.Groups[i].Value, CultureInfo.InvariantCulture));
            if (c != 0)
            {
                return c;
            }
        }

        var pa = a.Groups[4].Success ? a.Groups[4].Value.Split('.') : [];
        var pb = b.Groups[4].Success ? b.Groups[4].Value.Split('.') : [];
        if (pa.Length == 0 || pb.Length == 0)
        {
            return pb.Length.CompareTo(pa.Length);
        }

        for (var i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            var na = long.TryParse(pa[i], NumberStyles.None, CultureInfo.InvariantCulture, out var x);
            var nb = long.TryParse(pb[i], NumberStyles.None, CultureInfo.InvariantCulture, out var y);
            var c = na && nb ? x.CompareTo(y) : na ? -1 : nb ? 1 : string.CompareOrdinal(pa[i], pb[i]);
            if (c != 0)
            {
                return Math.Sign(c);
            }
        }

        return pa.Length.CompareTo(pb.Length);
    }

    [GeneratedRegex(@"^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?$")]
    private static partial Regex Pattern();
}

/// <summary>
/// Signierter Auftrag des EstrichONE-Servers: „auf <see cref="Version"/> aktualisieren“. Nur die Version wird übertragen –
/// welches Image (ghcr.io/florians25/estrichone-&lt;rolle&gt;) daraus wird, legt der Updater auf diesem Rechner fest.
/// </summary>
/// <param name="IssuedAt">Unix-Sekunden; Aufträge gelten 10 Minuten.</param>
public sealed record UpdateOrder(string Id, string Version, string ServerVersion, long IssuedAt, string Signature);

/// <summary>Stand der Fernaktualisierung dieses Rechners.</summary>
/// <param name="State"><c>idle</c>, <c>pending</c> (Auftrag liegt bereit), <c>running</c>, <c>succeeded</c>, <c>rolled_back</c>, <c>failed</c>.</param>
public sealed record UpdateState(string Installed, bool UpdaterAvailable, string? UpdaterVersion, DateTimeOffset? UpdaterSeenAt, string? RequestId,
    string? RequestedVersion, string State, string? Message, string? PreviousVersion, DateTimeOffset? FinishedAt);

/// <summary>HMAC über alle Angaben des Auftrags; Schlüssel ist der SHA-256 des Kopplungs-Tokens (beide Seiten kennen ihn).</summary>
public static class UpdateSignature
{
    public static string Compute(string tokenSha256, string role, string clientId, string id, string version, string serverVersion, long issuedAt) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(tokenSha256.ToLowerInvariant()),
            Encoding.UTF8.GetBytes($"estrichone-update-v1|{role}|{clientId}|{id}|{version}|{serverVersion}|{issuedAt.ToString(CultureInfo.InvariantCulture)}")));
}

public sealed record UpdateRejection(int Status, string Code, string Message);

/// <summary>
/// Fernaktualisierung (docs/backup.md, „Zusatzserver aktualisieren“): Der Dienst selbst hat weder Shell noch Docker-Zugriff.
/// Er prüft den Auftrag und legt nur die Zielversion in das gemeinsame Verzeichnis <c>/update</c>. Der Updater daneben
/// (Container <c>estrichone-&lt;rolle&gt;-updater</c> aus dem Installer-Image) holt das Image, ersetzt den Container, prüft die
/// Gesundheit, rollt bei Bedarf zurück und schreibt das Ergebnis. Dateien: <c>updater</c> (Lebenszeichen), <c>request</c>,
/// <c>started</c>, <c>result</c> – jeweils <c>schlüssel=wert</c>-Zeilen.
/// </summary>
public sealed partial class RemoteUpdater(ArchiveOptions options, TimeProvider clock)
{
    public static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan OrderLifetime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(30);
    private readonly Lock _gate = new();

    private string Dir => options.UpdatePath;

    public UpdateState Status()
    {
        var heartbeat = Path.Combine(Dir, "updater");
        DateTimeOffset? seen = File.Exists(heartbeat) ? new DateTimeOffset(File.GetLastWriteTimeUtc(heartbeat), TimeSpan.Zero) : null;
        var updaterVersion = Read("updater").GetValueOrDefault("version");
        var available = seen is { } s && clock.GetUtcNow() - s < HeartbeatTimeout;
        var request = Read("request");
        var result = Read("result");
        var started = ReadText("started");
        var requestId = request.GetValueOrDefault("id");
        var installed = ArchiveServer.Version;

        if (requestId is null)
        {
            return new UpdateState(installed, available, updaterVersion, seen, null, null, "idle", null, null, null);
        }

        if (result.GetValueOrDefault("id") == requestId)
        {
            var status = result.GetValueOrDefault("status") switch
            {
                "succeeded" => "succeeded",
                "rolled_back" => "rolled_back",
                _ => "failed",
            };
            return new UpdateState(installed, available, updaterVersion, seen, requestId, request.GetValueOrDefault("version"), status,
                result.GetValueOrDefault("message"), result.GetValueOrDefault("previous"), Modified("result"));
        }

        return new UpdateState(installed, available, updaterVersion, seen, requestId, request.GetValueOrDefault("version"),
            started == requestId ? "running" : "pending", null, null, null);
    }

    /// <summary>Prüft den Auftrag und legt ihn für den Updater ab; null = angenommen.</summary>
    public UpdateRejection? Accept(UpdateOrder order, string tokenSha256, string clientId)
    {
        if (order.Id is not { Length: 32 } || !order.Id.All(char.IsAsciiHexDigitLower))
        {
            return new(400, "UPDATE_INVALID", "Ungültige Auftragskennung.");
        }

        if (!SemVer.IsValid(order.Version) || !SemVer.IsValid(order.ServerVersion))
        {
            return new(422, "UPDATE_VERSION_INVALID", "Keine gültige Version (Format 1.2.3 bzw. 1.2.3-preview.4).");
        }

        var expected = UpdateSignature.Compute(tokenSha256, options.RoleName, clientId, order.Id, order.Version, order.ServerVersion, order.IssuedAt);
        if (!PairingProof.Matches(expected, order.Signature))
        {
            return new(403, "UPDATE_SIGNATURE_INVALID", "Die Signatur des Auftrags stimmt nicht.");
        }

        var age = clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(order.IssuedAt);
        if (age > OrderLifetime || age < -OrderLifetime)
        {
            return new(422, "UPDATE_EXPIRED", "Der Auftrag ist abgelaufen (Uhrzeit beider Rechner prüfen).");
        }

        if (SemVer.Compare(order.Version, order.ServerVersion) > 0)
        {
            return new(422, "UPDATE_VERSION_TOO_NEW", $"Version {order.Version} ist neuer als der EstrichONE-Server ({order.ServerVersion}).");
        }

        if (SemVer.IsValid(ArchiveServer.Version) && SemVer.Compare(order.Version, ArchiveServer.Version) < 0)
        {
            return new(422, "UPDATE_DOWNGRADE", $"Version {order.Version} ist älter als die installierte ({ArchiveServer.Version}) – Zurückstufen nur am Rechner selbst.");
        }

        lock (_gate)
        {
            var status = Status();
            if (!status.UpdaterAvailable)
            {
                return new(409, "UPDATER_MISSING",
                    "Auf diesem Rechner läuft kein Updater. Einmal lokal mit dem aktuellen Installer aktualisieren, danach geht es per Fernupdate.");
            }

            if (status.RequestId == order.Id)
            {
                return null;
            }

            if (status.State is "pending" or "running" && Modified("request") is { } at && clock.GetUtcNow() - at < RunTimeout)
            {
                return new(409, "UPDATE_RUNNING", "Es läuft bereits eine Aktualisierung.");
            }

            try
            {
                var file = Path.Combine(Dir, "request");
                File.WriteAllText(file + ".tmp", $"id={order.Id}\nversion={order.Version}\nserver={order.ServerVersion}\n");
                File.Move(file + ".tmp", file, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new(500, "UPDATE_DIR_NOT_WRITABLE", "Das Verzeichnis für Aufträge ist nicht beschreibbar.");
            }
        }

        return null;
    }

    private Dictionary<string, string> Read(string name)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in (ReadText(name) ?? string.Empty).Split('\n'))
        {
            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0)
            {
                var value = line[(eq + 1)..].Trim();
                values[line[..eq].Trim()] = value.Length > 500 ? value[..500] : value;
            }
        }

        return values;
    }

    private string? ReadText(string name)
    {
        try
        {
            var path = Path.Combine(Dir, name);
            return File.Exists(path) && new FileInfo(path).Length < 16 * 1024 ? File.ReadAllText(path).Trim() : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private DateTimeOffset? Modified(string name)
    {
        var path = Path.Combine(Dir, name);
        return File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero) : null;
    }
}
