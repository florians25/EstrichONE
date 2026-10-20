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
            ReadEntry(statusDirectory, "lastRestoreTest"), File.Exists(Path.Combine(statusDirectory, "requests", "backup-now")));
    }

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
