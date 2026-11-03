using System.Globalization;
using System.Text.Json;

namespace EstrichOne.Hosting.Recovery;

/// <summary>Eine Sicherung (restic-Snapshot) einer Instanz.</summary>
public sealed record SnapshotInfo(string Id, string ShortId, DateTime Time, string Repository, string RepositoryKey);

/// <summary>
/// Liest die Sicherungsliste, die der Backup-Dienst der Suite nach jeder Sicherung schreibt
/// (<c>data/backup-status/snapshots.json</c>: je Repository die Ausgabe von <c>restic snapshots --json</c>).
/// </summary>
public static class SnapshotReader
{
    public const string FileName = "snapshots.json";

    public static IReadOnlyList<SnapshotInfo> Read(string statusDirectory)
    {
        var path = Path.Combine(statusDirectory, FileName);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var result = new List<SnapshotInfo>();
            if (!document.RootElement.TryGetProperty("repositories", out var repositories) || repositories.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            foreach (var repository in repositories.EnumerateArray())
            {
                var label = repository.TryGetProperty("label", out var l) ? l.GetString() ?? string.Empty : string.Empty;
                var key = repository.TryGetProperty("key", out var k) ? k.GetString() ?? string.Empty : string.Empty;
                if (!repository.TryGetProperty("snapshots", out var snapshots) || snapshots.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var snapshot in snapshots.EnumerateArray())
                {
                    var id = snapshot.TryGetProperty("id", out var i) ? i.GetString() : null;
                    var time = snapshot.TryGetProperty("time", out var t) ? t.GetString() : null;
                    if (string.IsNullOrEmpty(id) || !IsSnapshotId(id)
                        || !DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at))
                    {
                        continue;
                    }

                    var shortId = snapshot.TryGetProperty("short_id", out var s) && s.GetString() is { } sid ? sid : id[..Math.Min(8, id.Length)];
                    result.Add(new SnapshotInfo(id, shortId, at, label, key));
                }
            }

            return result.OrderByDescending(s => s.Time).ToList();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return [];
        }
    }

    /// <summary>Nur Hex-Kennungen (restic) – sie werden als Argument an Docker übergeben.</summary>
    public static bool IsSnapshotId(string? id) =>
        id is { Length: >= 8 and <= 64 } && id.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));
}
