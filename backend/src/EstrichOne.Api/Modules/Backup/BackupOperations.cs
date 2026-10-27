using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Backup;

/// <summary>
/// Zeitplan, Aufbewahrung und Meldungen des Backup-Dienstes (Backup Center → Zeitplan). Jede Sicherung ist ein vollständiger
/// restic-Snapshot; der tägliche Lauf liest zusätzlich alle Dateien neu ein (Vollsicherung), Zwischenläufe nur Änderungen.
/// </summary>
/// <param name="DailyTime">Uhrzeit der täglichen Vollsicherung (HH:MM, Europe/Berlin).</param>
/// <param name="IncrementalEveryHours">Zusätzliche inkrementelle Sicherung alle n Stunden (0 = aus, 1–12).</param>
/// <param name="NotifyAdmins">Pushnachricht an alle mit Recht backup.manage nach jeder Sicherung und bei Störungen.</param>
/// <param name="NotifyOnSuccess">Auch erfolgreiche Sicherungen melden (sonst nur Fehler).</param>
/// <param name="WeeklyRestoreTest">Sonntags Integritätsprüfung und Wiederherstellungstest in eine temporäre Datenbank.</param>
public sealed record BackupPreferences(
    string DailyTime = "22:30",
    int IncrementalEveryHours = 0,
    int KeepDaily = 14,
    int KeepWeekly = 8,
    int KeepMonthly = 12,
    int KeepYearly = 3,
    bool NotifyAdmins = true,
    bool NotifyOnSuccess = true,
    bool WeeklyRestoreTest = true);

/// <summary>Ein Snapshot (vollständiger Stand von Datenbank, Dateien und Konfiguration).</summary>
/// <param name="Kind"><c>full</c> (tägliche Vollsicherung) oder <c>incremental</c>.</param>
public sealed record BackupSnapshot(string Id, string ShortId, DateTimeOffset Time, string Kind, long? Files, long? Bytes, long? AddedBytes);

/// <param name="Key"><c>primary</c>, <c>secondary</c> oder <c>target:&lt;id&gt;</c> – Quelle für die Wiederherstellung.</param>
public sealed record BackupRepositorySnapshots(string Key, string Label, IReadOnlyList<BackupSnapshot> Snapshots);

public sealed record BackupSnapshotsResponse(DateTimeOffset? UpdatedAt, IReadOnlyList<BackupRepositorySnapshots> Repositories);

/// <param name="Scope"><c>full</c> = ganzes System (Datenbank, Dateien, Einstellungen), <c>file</c> = eine Datei (Original).</param>
/// <param name="Confirmation">Bei <c>full</c> Pflicht: genau „WIEDERHERSTELLEN“.</param>
public sealed record RestoreRequest(string Snapshot, string Source = "primary", string Scope = "full", Guid? FileId = null, string? Confirmation = null);

/// <param name="Status"><c>requested</c>, <c>running</c>, <c>restored</c> (Daten zurück, API aktualisiert das Schema), <c>completed</c>, <c>failed</c>.</param>
public sealed record RestoreProgress(string Status, string Scope, string Snapshot, string Phase, int Percent, string Message, DateTimeOffset At,
    DateTimeOffset? StartedAt = null, string? RequestedBy = null)
{
    public bool Active => Scope == "full" && Status is "requested" or "running" or "restored";
}

/// <summary>Dateien im gemeinsamen Statusverzeichnis: settings.json/.env, snapshots.json, requests/restore.env, restore.json.</summary>
public sealed partial class BackupOperations(IOptions<BackupSettings> options, TimeProvider clock)
{
    public const string FullRestoreConfirmation = "WIEDERHERSTELLEN";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Unvollständige Anforderung, die der Backup-Dienst nicht abholt, gilt nach dieser Zeit als verfallen.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(15);

    private string? Dir => string.IsNullOrWhiteSpace(options.Value.StatusPath) || !Directory.Exists(options.Value.StatusPath) ? null : options.Value.StatusPath;

    public bool Configured => Dir is not null;

    public BackupPreferences Preferences()
    {
        if (Dir is not { } dir || !File.Exists(Path.Combine(dir, "settings.json")))
        {
            return new BackupPreferences();
        }

        try
        {
            return JsonSerializer.Deserialize<BackupPreferences>(File.ReadAllText(Path.Combine(dir, "settings.json")), Json) ?? new BackupPreferences();
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return new BackupPreferences();
        }
    }

    public static Dictionary<string, string[]> Validate(BackupPreferences p)
    {
        var errors = new Dictionary<string, string[]>();
        if (!TimeRegex().IsMatch(p.DailyTime ?? string.Empty)) errors["dailyTime"] = ["Uhrzeit im Format HH:MM, z. B. 22:30."];
        if (p.IncrementalEveryHours is < 0 or > 12) errors["incrementalEveryHours"] = ["0 (aus) bis 12 Stunden."];
        if (p.KeepDaily is < 7 or > 365) errors["keepDaily"] = ["7 bis 365 tägliche Sicherungen (mindestens eine Woche)."];
        if (p.KeepWeekly is < 0 or > 520) errors["keepWeekly"] = ["0 bis 520."];
        if (p.KeepMonthly is < 0 or > 240) errors["keepMonthly"] = ["0 bis 240."];
        if (p.KeepYearly is < 0 or > 30) errors["keepYearly"] = ["0 bis 30."];
        return errors;
    }

    /// <summary>Schreibt settings.json (für die API) und settings.env (liest der Backup-Dienst vor jedem Lauf).</summary>
    public bool SavePreferences(BackupPreferences p)
    {
        if (Dir is not { } dir)
        {
            return false;
        }

        WriteAtomic(Path.Combine(dir, "settings.json"), JsonSerializer.Serialize(p, Json));
        var env = new StringBuilder("# Von EstrichONE geschrieben (Backup Center → Zeitplan). Nicht von Hand ändern.\n")
            .Append(CultureInfo.InvariantCulture, $"BACKUP_TIME={p.DailyTime}\n")
            .Append(CultureInfo.InvariantCulture, $"BACKUP_INTERVAL_HOURS={p.IncrementalEveryHours}\n")
            .Append(CultureInfo.InvariantCulture, $"KEEP_DAILY={p.KeepDaily}\nKEEP_WEEKLY={p.KeepWeekly}\nKEEP_MONTHLY={p.KeepMonthly}\nKEEP_YEARLY={p.KeepYearly}\n")
            .Append(CultureInfo.InvariantCulture, $"RESTORE_TEST={(p.WeeklyRestoreTest ? 1 : 0)}\n");
        WriteAtomic(Path.Combine(dir, "settings.env"), env.ToString());
        return true;
    }

    public BackupSnapshotsResponse Snapshots()
    {
        var file = Dir is { } dir ? Path.Combine(dir, "snapshots.json") : null;
        if (file is null || !File.Exists(file))
        {
            return new BackupSnapshotsResponse(null, []);
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            var repositories = new List<BackupRepositorySnapshots>();
            foreach (var repo in root["repositories"]?.AsArray() ?? [])
            {
                var snapshots = new List<BackupSnapshot>();
                foreach (var s in repo?["snapshots"]?.AsArray() ?? [])
                {
                    if (s?["id"]?.GetValue<string>() is not { } id || s["time"]?.GetValue<string>() is not { } time)
                    {
                        continue;
                    }

                    var tags = s["tags"]?.AsArray().Select(t => t?.GetValue<string>()).ToList() ?? [];
                    var summary = s["summary"];
                    snapshots.Add(new BackupSnapshot(id, s["short_id"]?.GetValue<string>() ?? id[..8],
                        DateTimeOffset.Parse(time, CultureInfo.InvariantCulture), tags.Contains("full") ? "full" : tags.Contains("incremental") ? "incremental" : "full",
                        summary?["total_files_processed"]?.GetValue<long>(), summary?["total_bytes_processed"]?.GetValue<long>(), summary?["data_added"]?.GetValue<long>()));
                }

                repositories.Add(new BackupRepositorySnapshots(repo?["key"]?.GetValue<string>() ?? "primary", repo?["label"]?.GetValue<string>() ?? string.Empty,
                    [.. snapshots.OrderByDescending(s => s.Time)]));
            }

            var at = root["at"]?.GetValue<string>();
            return new BackupSnapshotsResponse(at is null ? null : DateTimeOffset.Parse(at, CultureInfo.InvariantCulture), repositories);
        }
        catch (Exception exception) when (exception is JsonException or IOException or FormatException or InvalidOperationException)
        {
            return new BackupSnapshotsResponse(null, []);
        }
    }

    /// <summary>Stand der laufenden bzw. letzten Wiederherstellung (schreibt der Backup-Dienst; <c>completed</c> setzt die API).</summary>
    public RestoreProgress? Restore()
    {
        if (Dir is not { } dir || !File.Exists(Path.Combine(dir, "restore.json")))
        {
            return null;
        }

        try
        {
            var p = JsonSerializer.Deserialize<RestoreProgress>(File.ReadAllText(Path.Combine(dir, "restore.json")), Json);
            if (p is { Status: "requested" } && clock.GetUtcNow() - p.At > RequestTimeout)
            {
                return p with { Status = "failed", Message = "Der Backup-Dienst hat die Wiederherstellung nicht übernommen (läuft der Dienst „backup“?)." };
            }

            return p;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>Gültige Anforderung ablegen; <c>null</c> bei Erfolg, sonst Fehlertext.</summary>
    public string? RequestRestore(RestoreRequest request, string requestedBy)
    {
        if (Dir is not { } dir)
        {
            return "Das Backup ist nicht eingerichtet.";
        }

        if (!SnapshotRegex().IsMatch(request.Snapshot ?? string.Empty)) return "Ungültige Sicherung.";
        if (!SourceRegex().IsMatch(request.Source ?? string.Empty)) return "Ungültige Quelle.";
        if (request.Scope is not ("full" or "file")) return "Umfang muss full oder file sein.";
        if (request.Scope == "file" && request.FileId is null) return "Für eine einzelne Datei die Datei-ID angeben.";
        if (request.Scope == "full" && request.Confirmation != FullRestoreConfirmation) return $"Zur Bestätigung „{FullRestoreConfirmation}“ eingeben.";
        if (Restore() is { Status: "requested" or "running" or "restored" }) return "Es läuft bereits eine Wiederherstellung.";
        if (request.Source != "primary" && Snapshots().Repositories.All(r => r.Key != request.Source)) return "Diese Quelle ist nicht bekannt.";

        var now = clock.GetUtcNow();
        Directory.CreateDirectory(Path.Combine(dir, "requests"));
        WriteAtomic(Path.Combine(dir, "restore.json"), JsonSerializer.Serialize(new RestoreProgress("requested", request.Scope, request.Snapshot!,
            "Wartet auf den Backup-Dienst", 0, "Wiederherstellung angefordert", now, now, requestedBy), Json));
        // Werte sind oben streng geprüft (nur Hex, Buchstaben, Ziffern, Doppelpunkt) – der Dienst liest die Datei per „source“.
        WriteAtomic(Path.Combine(dir, "requests", "restore.env"),
            $"RESTORE_SNAPSHOT={request.Snapshot}\nRESTORE_SOURCE={request.Source}\nRESTORE_SCOPE={request.Scope}\nRESTORE_FILE={request.FileId?.ToString("N") ?? string.Empty}\n");
        return null;
    }

    /// <summary>Nach dem Schema-Update der API: Wiederherstellung abschließen.</summary>
    public void CompleteRestore(RestoreProgress progress, string message)
    {
        if (Dir is { } dir)
        {
            WriteAtomic(Path.Combine(dir, "restore.json"), JsonSerializer.Serialize(progress with
            {
                Status = "completed", Phase = "Abgeschlossen", Percent = 100, Message = message, At = clock.GetUtcNow(),
            }, Json));
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        File.WriteAllText(path + ".tmp", content);
        File.Move(path + ".tmp", path, overwrite: true);
    }

    [GeneratedRegex("^([01][0-9]|2[0-3]):[0-5][0-9]$")]
    private static partial Regex TimeRegex();

    [GeneratedRegex("^([0-9a-f]{8,64}|latest)$")]
    private static partial Regex SnapshotRegex();

    [GeneratedRegex("^(primary|secondary|target:[0-9a-f]{32})$")]
    private static partial Regex SourceRegex();
}
