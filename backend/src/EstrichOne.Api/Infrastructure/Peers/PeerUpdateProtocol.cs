using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace EstrichOne.Api.Infrastructure.Peers;

/// <summary>Semantische Version (SemVer 2.0, ohne Build-Metadaten) – identisch im Archivserver/Backup-Ziel (EstrichOne.Archive/RemoteUpdate.cs).</summary>
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

/// <summary>Auftrag an einen Zusatzserver (Gegenstück: UpdateOrder in backend/src/EstrichOne.Archive/RemoteUpdate.cs).</summary>
public sealed record PeerUpdateOrder(string Id, string Version, string ServerVersion, long IssuedAt, string Signature);

/// <summary>Stand der Fernaktualisierung, wie ihn der Zusatzserver meldet.</summary>
public sealed record PeerUpdateState(string Installed, bool UpdaterAvailable, string? UpdaterVersion, DateTimeOffset? UpdaterSeenAt, string? RequestId,
    string? RequestedVersion, string State, string? Message, string? PreviousVersion, DateTimeOffset? FinishedAt);

/// <summary>HMAC über alle Angaben des Auftrags; Schlüssel ist der SHA-256 des Kopplungs-Tokens (beide Seiten kennen ihn).</summary>
public static class UpdateSignature
{
    public static string Compute(string tokenSha256, string role, string clientId, string id, string version, string serverVersion, long issuedAt) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(tokenSha256.ToLowerInvariant()),
            Encoding.UTF8.GetBytes($"estrichone-update-v1|{role}|{clientId}|{id}|{version}|{serverVersion}|{issuedAt.ToString(CultureInfo.InvariantCulture)}")));
}

/// <summary>Auftrag bauen und signieren (Schlüssel: SHA-256 des Kopplungs-Tokens).</summary>
public static class PeerUpdateOrders
{
    public static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static PeerUpdateOrder Create(string tokenSha256, string role, string clientId, string requestId, string version, string serverVersion,
        DateTimeOffset now)
    {
        var issuedAt = now.ToUnixTimeSeconds();
        return new PeerUpdateOrder(requestId, version, serverVersion, issuedAt,
            UpdateSignature.Compute(tokenSha256, role, clientId, requestId, version, serverVersion, issuedAt));
    }
}
