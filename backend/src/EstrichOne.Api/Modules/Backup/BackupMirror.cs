using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Backup;

/// <summary>
/// „Ziel zieht“: Ein Backup-Ziel ohne erreichbare Adresse (zuhause hinter NAT, ohne feste IP) verbindet sich ausgehend mit
/// diesem Server und spiegelt das verschlüsselte restic-Repository – nur lesend, nur anhängend. Der Server muss dafür
/// öffentlich erreichbar sein (z. B. im Rechenzentrum). Gekoppelt wird mit einem Einmal-Code aus dem Backup Center.
/// Ablage ohne Datenbank in <c>&lt;Status&gt;/mirrors/&lt;id&gt;.json</c> (Token nur als SHA-256).
/// </summary>
public sealed class BackupMirrorStore(IOptions<BackupSettings> options, TimeProvider clock)
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] RepositoryTypes = ["keys", "snapshots", "index", "data"];
    private static readonly Lock Gate = new();

    /// <summary>Spiegel meldet sich mindestens so oft; sonst gilt er als nicht erreichbar.</summary>
    public static readonly TimeSpan ReportTimeout = TimeSpan.FromHours(3);

    private string? Dir => string.IsNullOrWhiteSpace(options.Value.StatusPath) || !Directory.Exists(options.Value.StatusPath)
        ? null : Path.Combine(options.Value.StatusPath, "mirrors");

    /// <summary>Lokales Repository, das gespiegelt werden darf (nur Pfad-Repositorys; sftp/rest sind dafür ungeeignet).</summary>
    public string? RepositoryPath => options.Value.RepositoryPath is { Length: > 0 } path && File.Exists(Path.Combine(path, "config")) ? path : null;

    public bool Available => Dir is not null && !string.IsNullOrWhiteSpace(options.Value.RepositoryPath);

    public IReadOnlyList<BackupMirrorDto> List()
    {
        if (Dir is not { } dir || !Directory.Exists(dir))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(dir, "*.json").Select(Read).OfType<MirrorRecord>().OrderBy(m => m.CreatedAt).Select(ToDto)];
    }

    /// <summary>Neuen Spiegel anlegen; der Code gilt 24 Stunden und einmal.</summary>
    public (BackupMirrorDto Mirror, string Code)? Create(string name)
    {
        if (Dir is not { } dir || !Available)
        {
            return null;
        }

        Directory.CreateDirectory(dir);
        var code = string.Join('-', Enumerable.Range(0, 4).Select(_ => new string([.. Enumerable.Range(0, 4).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)])])));
        var now = clock.GetUtcNow();
        var record = new MirrorRecord(Guid.CreateVersion7(now).ToString("N"), string.IsNullOrWhiteSpace(name) ? "Backup-Ziel (zieht)" : name.Trim()[..Math.Min(name.Trim().Length, 100)],
            now, Hash(Normalize(code)), now.AddHours(24), null, null, null, null);
        Write(record);
        return (ToDto(record), code);
    }

    public bool Remove(string id)
    {
        if (Dir is not { } dir || !IsId(id) || !File.Exists(Path.Combine(dir, id + ".json")))
        {
            return false;
        }

        File.Delete(Path.Combine(dir, id + ".json"));
        return true;
    }

    /// <summary>Einmal-Code gegen ein dauerhaftes Token tauschen.</summary>
    public (string Id, string Token)? Pair(string code, string? targetName)
    {
        if (Dir is not { } dir || !Directory.Exists(dir))
        {
            return null;
        }

        var hash = Hash(Normalize(code));
        lock (Gate)
        {
            foreach (var record in Directory.EnumerateFiles(dir, "*.json").Select(Read).OfType<MirrorRecord>())
            {
                if (record.CodeHash is { } h && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(h), Encoding.ASCII.GetBytes(hash))
                    && record.CodeExpiresAt > clock.GetUtcNow())
                {
                    var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
                    Write(record with { CodeHash = null, CodeExpiresAt = null, TokenHash = Hash(token), TargetName = targetName?.Trim() is { Length: > 0 } n ? n[..Math.Min(n.Length, 100)] : null });
                    return (record.Id, token);
                }
            }
        }

        return null;
    }

    /// <summary>Spiegel zum Bearer-Token (oder null).</summary>
    public string? Authenticate(string? authorization)
    {
        const string prefix = "Bearer ";
        if (Dir is not { } dir || !Directory.Exists(dir) || authorization is null || !authorization.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var hash = Encoding.ASCII.GetBytes(Hash(authorization[prefix.Length..].Trim()));
        return Directory.EnumerateFiles(dir, "*.json").Select(Read).OfType<MirrorRecord>()
            .FirstOrDefault(r => r.TokenHash is { } t && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(t), hash))?.Id;
    }

    public void Report(string id, BackupMirrorReport report)
    {
        lock (Gate)
        {
            if (Dir is { } dir && Read(Path.Combine(dir, id + ".json")) is { } record)
            {
                Write(record with { LastSeenAt = clock.GetUtcNow(), LastReport = report with { Error = report.Error is { Length: > 500 } e ? e[..500] : report.Error } });
            }
        }
    }

    public void Seen(string id)
    {
        lock (Gate)
        {
            if (Dir is { } dir && Read(Path.Combine(dir, id + ".json")) is { } record
                && (record.LastSeenAt is null || clock.GetUtcNow() - record.LastSeenAt > TimeSpan.FromMinutes(1)))
            {
                Write(record with { LastSeenAt = clock.GetUtcNow() });
            }
        }
    }

    /// <summary>Dateiliste eines Typs wie die restic-REST-API v2 (<c>[{name,size}]</c>); data/ ist zweistufig.</summary>
    public IReadOnlyList<MirrorFile>? ListFiles(string type)
    {
        if (RepositoryPath is not { } repo || !RepositoryTypes.Contains(type))
        {
            return null;
        }

        var dir = Path.Combine(repo, type);
        if (!Directory.Exists(dir))
        {
            return [];
        }

        return [.. Directory.EnumerateFiles(dir, "*", type == "data" ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .Where(f => IsObjectName(Path.GetFileName(f)))
            .Select(f => new MirrorFile(Path.GetFileName(f), new FileInfo(f).Length))];
    }

    /// <summary>Pfad einer Repository-Datei (nur bekannte Typen, nur Hex-Namen) oder null.</summary>
    public string? FilePath(string type, string name)
    {
        if (RepositoryPath is not { } repo || !RepositoryTypes.Contains(type) || !IsObjectName(name))
        {
            return null;
        }

        var path = type == "data" ? Path.Combine(repo, "data", name[..2], name) : Path.Combine(repo, type, name);
        return File.Exists(path) ? path : null;
    }

    public string? ConfigPath => RepositoryPath is { } repo ? Path.Combine(repo, "config") : null;

    private BackupMirrorDto ToDto(MirrorRecord r)
    {
        var reachable = r.LastSeenAt is { } seen && clock.GetUtcNow() - seen <= ReportTimeout;
        return new BackupMirrorDto(r.Id, r.Name, r.TargetName, r.CreatedAt, r.TokenHash is not null, r.CodeExpiresAt, r.LastSeenAt, reachable, r.LastReport);
    }

    private static bool IsObjectName(string name) => name.Length == 64 && name.All(char.IsAsciiHexDigitLower);

    private static bool IsId(string id) => id.Length == 32 && id.All(char.IsAsciiHexDigitLower);

    private static string Normalize(string code) => new([.. (code ?? string.Empty).ToUpperInvariant().Where(c => Alphabet.Contains(c, StringComparison.Ordinal))]);

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static MirrorRecord? Read(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<MirrorRecord>(File.ReadAllText(file), Json) : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return null;
        }
    }

    private void Write(MirrorRecord record)
    {
        var path = Path.Combine(Dir!, record.Id + ".json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(record, Json));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path + ".tmp", UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(path + ".tmp", path, overwrite: true);
    }

    private sealed record MirrorRecord(string Id, string Name, DateTimeOffset CreatedAt, string? CodeHash, DateTimeOffset? CodeExpiresAt,
        string? TokenHash, string? TargetName, DateTimeOffset? LastSeenAt, BackupMirrorReport? LastReport);
}

public sealed record MirrorFile(string Name, long Size);

/// <summary>Meldung des Spiegels nach jedem Abgleich.</summary>
public sealed record BackupMirrorReport(int Snapshots, long Bytes, DateTimeOffset? LastSnapshotAt, long? DiskFreeBytes, long? DiskTotalBytes,
    string? Version, string? Error, int? FilesCopied = null);

public sealed record BackupMirrorDto(string Id, string Name, string? TargetName, DateTimeOffset CreatedAt, bool Paired, DateTimeOffset? CodeExpiresAt,
    DateTimeOffset? LastSeenAt, bool Reachable, BackupMirrorReport? LastReport);

public sealed record CreateBackupMirrorRequest(string? Name);

/// <param name="Command">Ein Befehl für den Rechner zuhause (Backup-Ziel, das zieht).</param>
public sealed record CreateBackupMirrorResponse(BackupMirrorDto Mirror, string Code, string ServerUrl, string Command);

public sealed record MirrorPairRequest(string Code, string? Name);

public sealed record MirrorPairResponse(string Id, string Token);
