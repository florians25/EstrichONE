using System.Globalization;
using System.Text.Json;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Infrastructure;
using EstrichOne.Hosting.Instances;

namespace EstrichOne.Hosting.Operations;

/// <summary>
/// Geordneter Update-Ablauf einer Instanz (wie update.sh, deploy/README.md „Update-Ablauf“): Die Konsole schreibt den Stand
/// nach <c>data/backup-status/update-maintenance.json</c> der Instanz; die Suite zeigt daraus Countdown und Wartungsbildschirm,
/// beendet zu Beginn der Update-Phase alle Sitzungen und gibt erst nach dem Abschluss frei. Die Phase steht zusätzlich am
/// Vorgang (Spalte „Phase“ in der Konsole).
/// </summary>
public sealed class UpdateRun
{
    public const string Announce = "announce";
    public const string Update = "update";
    public const string Finish = "finish";
    public const string Rollback = "rollback";
    public const string Failed = "failed";
    public const string Released = "released";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _file;
    private readonly string _id;
    private readonly string _from;
    private readonly string _to;
    private readonly DateTimeOffset _started;
    private readonly OperationLog _log;
    private readonly HostingOptions _settings;
    private readonly TimeProvider _clock;
    private DateTimeOffset? _updateStarted;

    public UpdateRun(InstanceLayout layout, string slug, string from, string to, OperationLog log, HostingOptions settings, TimeProvider clock)
    {
        _file = FilePath(layout, slug);
        _from = from;
        _to = to;
        _log = log;
        _settings = settings;
        _clock = clock;
        _started = clock.GetUtcNow();
        _id = $"{_started:yyyyMMddTHHmmssZ}-{to}";
    }

    /// <summary>Läuft der Ablauf noch (Wartung aktiv)?</summary>
    public bool Active { get; private set; }

    public static string FilePath(InstanceLayout layout, string slug) =>
        Path.Combine(layout.DataDirectory(slug), "backup-status", "update-maintenance.json");

    /// <summary>Wartung einer Instanz sofort beenden (Neustart, Rücknahme, abgebrochener Ablauf).</summary>
    public static bool Clear(InstanceLayout layout, string slug)
    {
        var file = FilePath(layout, slug);
        if (!File.Exists(file))
        {
            return false;
        }

        File.Delete(file);
        return true;
    }

    /// <summary>1. Ankündigung: Countdown für alle Benutzer, neue Anmeldungen gesperrt, Apps gleichen ab.</summary>
    public async Task AnnounceAsync(CancellationToken cancellationToken)
    {
        _log.Write($"Phase 1/3 – Ankündigung: Benutzer werden informiert, Update auf {_to} beginnt in {Clock(_settings.UpdateAnnounceSeconds)}.");
        await WriteAsync(Announce, _settings.UpdateAnnounceSeconds, null, cancellationToken);
        await WaitAsync(_settings.UpdateAnnounceSeconds, cancellationToken);
    }

    /// <summary>2. Update: Die Suite beendet alle Sitzungen (prüft alle 2 s) und zeigt den Wartungsbildschirm.</summary>
    public async Task BeginUpdateAsync(CancellationToken cancellationToken)
    {
        _log.Write("Phase 2/3 – Update: Sitzungen werden beendet, Wartungsbildschirm aktiv.");
        _updateStarted = _clock.GetUtcNow();
        await WriteAsync(Update, _settings.UpdateMinimumSeconds, null, cancellationToken);
        await WaitAsync(Math.Min(3, _settings.UpdateMinimumSeconds), cancellationToken);
    }

    /// <summary>3. Abschluss: frühestens nach der Mindestdauer des Updates; danach Freigabe.</summary>
    public async Task FinishAsync(CancellationToken cancellationToken)
    {
        if (_updateStarted is { } started)
        {
            var rest = started.AddSeconds(_settings.UpdateMinimumSeconds) - _clock.GetUtcNow();
            if (rest > TimeSpan.Zero)
            {
                await Task.Delay(rest, _clock, cancellationToken);
            }
        }

        _log.Write($"Phase 3/3 – Abschluss: Version {_to} läuft, Freigabe in {Clock(_settings.UpdateFinishSeconds)}.");
        await WriteAsync(Finish, _settings.UpdateFinishSeconds, null, cancellationToken);
        await WaitAsync(_settings.UpdateFinishSeconds, cancellationToken);
        Release();
        _log.Write($"Freigegeben – Update-Ablauf insgesamt {(int)(_clock.GetUtcNow() - _started).TotalSeconds} s.");
    }

    /// <summary>Rücknahme läuft (Wartungsbildschirm bleibt).</summary>
    public Task RollbackAsync(CancellationToken cancellationToken)
    {
        _log.Write("Wartungsbildschirm: „Update wird zurückgenommen“.");
        return WriteAsync(Rollback, null, null, cancellationToken);
    }

    /// <summary>
    /// Fehlschlag: Läuft die Instanz (bisherige Version, ggf. nach Rücknahme), sehen die Benutzer kurz den Status und dann die
    /// Freigabe; sonst bleibt der Wartungsbildschirm (Freigabe durch „Neu starten“ bzw. erfolgreiches Zurücksetzen).
    /// </summary>
    public async Task FailAsync(string message, bool running, CancellationToken cancellationToken)
    {
        if (!Active)
        {
            return;
        }

        if (running)
        {
            _log.Write($"Benutzer sehen „Update fehlgeschlagen – {_from} läuft weiter“, Freigabe in {Clock(_settings.UpdateFinishSeconds)}.");
            await WriteAsync(Failed, _settings.UpdateFinishSeconds, message, cancellationToken);
            await WaitAsync(_settings.UpdateFinishSeconds, cancellationToken);
            Release();
            _log.Write("Freigegeben.");
        }
        else
        {
            await WriteAsync(Failed, null, message, cancellationToken);
            Active = false;
            _log.Write("Wartungsbildschirm bleibt aktiv, bis die Instanz wieder läuft („Neu starten“ oder „Aktualisierung zurücknehmen“).");
        }
    }

    private void Release()
    {
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }

        Active = false;
        _log.SetPhase(Released, null);
    }

    private async Task WriteAsync(string phase, int? seconds, string? message, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        DateTimeOffset? ends = seconds is { } s ? now.AddSeconds(s) : null;
        var state = new
        {
            id = _id, phase, fromVersion = _from, targetVersion = _to, startedAt = _started, phaseStartedAt = now, phaseEndsAt = ends,
            expiresAt = now.AddHours(6), message, source = "console",
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var temp = _file + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, Json), cancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        }

        File.Move(temp, _file, overwrite: true);
        Active = true;
        _log.SetPhase(phase, ends?.UtcDateTime);
        // Sofort sichtbar in der Konsole (sonst erst mit dem nächsten Protokoll-Takt).
        await _log.FlushAsync(cancellationToken);
    }

    private Task WaitAsync(int seconds, CancellationToken cancellationToken) =>
        seconds <= 0 ? Task.CompletedTask : Task.Delay(TimeSpan.FromSeconds(seconds), _clock, cancellationToken);

    private static string Clock(int seconds) => string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
}
