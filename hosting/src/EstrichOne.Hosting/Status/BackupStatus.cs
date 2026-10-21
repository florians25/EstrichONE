using System.Globalization;
using System.Text.Json;

namespace EstrichOne.Hosting.Status;

public sealed record BackupEntry(string Status, string Message, DateTime At, double? DurationSeconds);

/// <summary>Sicherungszustand einer Instanz aus den Statusdateien des Backup-Dienstes (<c>data/backup-status/*.json</c>).</summary>
public sealed record BackupState(
    bool Configured,
    bool Healthy,
    string Summary,
    BackupEntry? LastRun,
    BackupEntry? LastSuccess,
    BackupEntry? LastCheck,
    BackupEntry? LastRestoreTest,
    bool RunRequested)
{
    /// <summary>Gekoppelte Backup-Ziele auf anderer Hardware (Suite: System → Backup-Ziele).</summary>
    public IReadOnlyList<BackupTargetState> Targets { get; init; } = [];

    /// <summary>Gekoppelter Archivserver (Suite: System → Archiv); null = nicht eingerichtet.</summary>
    public ArchiveState? Archive { get; init; }

    /// <summary>Wie die API der Suite: Sicherung gilt als aktuell, wenn die letzte erfolgreiche jünger als 36 Stunden ist.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(36);

    public static BackupState Read(string statusDirectory, DateTime now)
    {
        if (!Directory.Exists(statusDirectory))
        {
            return new BackupState(false, false, "Kein Sicherungsstatus vorhanden.", null, null, null, null, false);
        }

        var lastSuccess = ReadEntry(statusDirectory, "lastSuccess");
        var lastRun = ReadEntry(statusDirectory, "lastRun");
        var age = lastSuccess is null ? (TimeSpan?)null : now - lastSuccess.At;
        var healthy = age is not null && age <= MaxAge;
        var summary = lastSuccess is null
            ? lastRun?.Status == "failed" ? $"Letzte Sicherung fehlgeschlagen: {lastRun.Message}" : "Noch keine erfolgreiche Sicherung."
            : healthy ? "Die letzte Sicherung ist aktuell."
            : $"Die letzte erfolgreiche Sicherung ist {(int)age!.Value.TotalHours} Stunden alt.";
        return new BackupState(true, healthy, summary, lastRun, lastSuccess, ReadEntry(statusDirectory, "lastCheck"),
            ReadEntry(statusDirectory, "lastRestoreTest"), File.Exists(Path.Combine(statusDirectory, "requests", "backup-now")))
        {
            Targets = ReadTargets(statusDirectory),
            Archive = ReadArchive(statusDirectory),
        };
    }

    /// <summary>Je Ziel <c>targets/&lt;id&gt;.conf</c> (Name) und <c>target-&lt;id&gt;.json</c> (letzte Sicherung dorthin).</summary>
    private static List<BackupTargetState> ReadTargets(string directory)
    {
        var targets = Path.Combine(directory, "targets");
        if (!Directory.Exists(targets))
        {
            return [];
        }

        var result = new List<BackupTargetState>();
        foreach (var conf in Directory.EnumerateFiles(targets, "*.conf").Order(StringComparer.Ordinal))
        {
            var id = Path.GetFileNameWithoutExtension(conf);
            string name = id;
            try
            {
                var line = File.ReadLines(conf).FirstOrDefault(l => l.StartsWith("TARGET_NAME=", StringComparison.Ordinal));
                if (line is not null)
                {
                    name = line["TARGET_NAME=".Length..].Trim('\'');
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            result.Add(new BackupTargetState(id, name, ReadEntry(directory, "target-" + id)));
        }

        return result;
    }

    private static ArchiveState? ReadArchive(string directory)
    {
        var file = Path.Combine(directory, "archive.json");
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<ArchiveState>(File.ReadAllText(file), Json) : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static BackupEntry? ReadEntry(string directory, string name)
    {
        var file = Path.Combine(directory, name + ".json");
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            using var json = JsonDocument.Parse(File.ReadAllText(file));
            var root = json.RootElement;
            if (!root.TryGetProperty("at", out var atElement)
                || !DateTimeOffset.TryParse(atElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            {
                return null;
            }

            return new BackupEntry(
                root.TryGetProperty("status", out var status) ? status.GetString() ?? "unknown" : "unknown",
                root.TryGetProperty("message", out var message) ? message.GetString() ?? string.Empty : string.Empty,
                at.UtcDateTime,
                root.TryGetProperty("durationSeconds", out var duration) && duration.ValueKind == JsonValueKind.Number ? duration.GetDouble() : null);
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>Backup-Ziel einer Instanz mit Ergebnis der letzten Sicherung dorthin.</summary>
public sealed record BackupTargetState(string Id, string Name, BackupEntry? LastRun);

/// <summary>Archivserver einer Instanz, wie die Suite ihn meldet (<c>backup-status/archive.json</c>).</summary>
public sealed record ArchiveState(
    bool Connected, string? ServerName, string? Address, DateTimeOffset? LastContactAt, string? LastError, int Archived, int Pending,
    int Failed, DateTimeOffset At);
