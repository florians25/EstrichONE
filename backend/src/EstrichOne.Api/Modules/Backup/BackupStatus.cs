using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Backup;

/// <summary>Abschnitt <c>EstrichOne:Backup</c>.</summary>
public sealed class BackupSettings
{
    public const string SectionName = "EstrichOne:Backup";

    /// <summary>Gemeinsames Verzeichnis mit dem Backup-Dienst (Status, Anforderungen). Leer = Backup nicht eingerichtet.</summary>
    public string? StatusPath { get; set; }

    /// <summary>Ab diesem Alter der letzten erfolgreichen Sicherung meldet <c>/health/ready</c> „Degraded“.</summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromHours(36);
}

public sealed record BackupRunInfo(string Status, string Message, DateTimeOffset At, JsonNode? Details);

public sealed record BackupStatusResponse(
    bool Configured, bool Healthy, string Summary, BackupRunInfo? LastRun, BackupRunInfo? LastSuccess,
    BackupRunInfo? LastCheck, BackupRunInfo? LastRestoreTest, bool RunRequested);

/// <summary>
/// Liest den Status, den der Backup-Container schreibt (deploy/backup/estrichone-backup). Kein Docker-Socket,
/// keine Fernsteuerung des Containers: „Jetzt sichern“ legt nur eine Anforderungsdatei ab.
/// </summary>
public sealed class BackupStatusReader(IOptions<BackupSettings> options, TimeProvider clock)
{
    private BackupSettings Settings => options.Value;

    public BackupStatusResponse Read()
    {
        if (string.IsNullOrWhiteSpace(Settings.StatusPath) || !Directory.Exists(Settings.StatusPath))
        {
            return new BackupStatusResponse(false, false, "Das Backup ist nicht eingerichtet.", null, null, null, null, false);
        }

        var lastSuccess = ReadEntry("lastSuccess");
        var age = lastSuccess is null ? (TimeSpan?)null : clock.GetUtcNow() - lastSuccess.At;
        var healthy = age is not null && age <= Settings.MaxAge;
        var summary = lastSuccess is null
            ? "Es gibt noch keine erfolgreiche Sicherung."
            : healthy ? "Die letzte Sicherung ist aktuell."
            : $"Die letzte erfolgreiche Sicherung ist {(int)age!.Value.TotalHours} Stunden alt.";

        return new BackupStatusResponse(true, healthy, summary, ReadEntry("lastRun"), lastSuccess, ReadEntry("lastCheck"),
            ReadEntry("lastRestoreTest"), File.Exists(RequestPath));
    }

    public bool RequestRun()
    {
        if (string.IsNullOrWhiteSpace(Settings.StatusPath) || !Directory.Exists(Settings.StatusPath))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(RequestPath)!);
        File.WriteAllText(RequestPath, clock.GetUtcNow().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }

    /// <summary>Der Backup-Dienst schreibt je Eintrag eine Datei (z. B. <c>lastSuccess.json</c>).</summary>
    private BackupRunInfo? ReadEntry(string name)
    {
        var file = Path.Combine(Settings.StatusPath!, name + ".json");
        try
        {
            return File.Exists(file) ? Parse(JsonNode.Parse(File.ReadAllText(file))) : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            return null;
        }
    }

    private string RequestPath => Path.Combine(Settings.StatusPath ?? string.Empty, "requests", "backup-now");

    private static BackupRunInfo? Parse(JsonNode? node)
    {
        if (node is not JsonObject run || run["at"]?.GetValue<string>() is not { } at
            || !DateTimeOffset.TryParse(at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var when))
        {
            return null;
        }

        var details = run.DeepClone().AsObject();
        details.Remove("status");
        details.Remove("message");
        details.Remove("at");
        return new BackupRunInfo(run["status"]?.GetValue<string>() ?? "unknown", run["message"]?.GetValue<string>() ?? string.Empty, when,
            details.Count == 0 ? null : details);
    }
}

/// <summary>Meldet „Degraded“, wenn das Backup eingerichtet, aber veraltet ist – auswertbar durch externes Monitoring.</summary>
internal sealed class BackupHealthCheck(BackupStatusReader reader) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = reader.Read();
        return Task.FromResult(!status.Configured || status.Healthy
            ? HealthCheckResult.Healthy(status.Summary)
            : HealthCheckResult.Degraded(status.Summary));
    }
}
