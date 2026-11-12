using System.Text.Json;
using EstrichOne.Api.Modules.Identity.Exports;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Notices;

/// <summary>
/// Geordneter Update-Ablauf (deploy/README.md, „Update-Ablauf“): Wer das Update einspielt (update.sh bzw. die
/// Hosting-Konsole), schreibt den Stand nach <c>backup-status/update-maintenance.json</c>. Die API liest die Datei bei jeder
/// Anfrage (sie überdauert den Austausch der Container), Caddy liefert sie zusätzlich als <c>/update-status.json</c> aus –
/// so bleibt die Wartungsseite auch sichtbar, solange die API nicht läuft.
/// </summary>
/// <param name="Phase"><see cref="UpdatePhases"/>: announce (Ankündigung, Arbeiten noch möglich), update (Container werden
/// getauscht), finish (Abschluss, neue Version läuft), rollback (Rücknahme), failed (fehlgeschlagen).</param>
/// <param name="PhaseEndsAt">Ende der Ankündigung bzw. des Abschlusses (Countdown); im Update die früheste Freigabe.</param>
/// <param name="ExpiresAt">Spätestens dann gilt der Stand als verwaist und wird ignoriert (abgebrochenes Skript).</param>
public sealed record UpdateMaintenanceState(
    string Id,
    string Phase,
    string? FromVersion,
    string? TargetVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset? PhaseStartedAt = null,
    DateTimeOffset? PhaseEndsAt = null,
    DateTimeOffset? ExpiresAt = null,
    string? Message = null,
    string? Source = null)
{
    /// <summary>Alles außer der Ankündigung sperrt die Arbeit (auch für Administratoren) und beendet die Sitzungen.</summary>
    public bool Blocking => Phase != UpdatePhases.Announce;
}

public static class UpdatePhases
{
    public const string Announce = "announce";
    public const string Update = "update";
    public const string Finish = "finish";
    public const string Rollback = "rollback";
    public const string Failed = "failed";

    public static readonly IReadOnlyList<string> All = [Announce, Update, Finish, Rollback, Failed];

    public static string Title(string phase) => phase switch
    {
        Announce => "Update angekündigt",
        Update => "Update wird eingespielt",
        Finish => "Update wird abgeschlossen",
        Rollback => "Update wird zurückgenommen",
        _ => "Update fehlgeschlagen",
    };
}

/// <summary>Liest den Update-Stand (Datei); gecacht, solange sich Änderungszeit und Größe nicht ändern.</summary>
public sealed class UpdateMaintenanceReader(IOptions<NoticeSettings> options, IConfiguration configuration, TimeProvider clock,
    ILogger<UpdateMaintenanceReader> logger)
{
    public const string FileName = "update-maintenance.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Lock _sync = new();
    private (DateTime Modified, long Length, UpdateMaintenanceState? State)? _cache;

    /// <summary>Pfad der Datei: <c>EstrichOne:Notices:UpdateStatePath</c>, sonst neben dem Backup-Status (<c>/status</c>).</summary>
    public string? Path
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(options.Value.UpdateStatePath))
            {
                return options.Value.UpdateStatePath;
            }

            var status = configuration["EstrichOne:Backup:StatusPath"];
            return string.IsNullOrWhiteSpace(status) ? null : System.IO.Path.Combine(status, FileName);
        }
    }

    /// <summary>Aktueller Stand oder <c>null</c> (kein Update, Datei unlesbar oder verwaist).</summary>
    public UpdateMaintenanceState? Current()
    {
        if (Path is not { } path)
        {
            return null;
        }

        UpdateMaintenanceState? state;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return null;
            }

            lock (_sync)
            {
                if (_cache is { } cached && cached.Modified == info.LastWriteTimeUtc && cached.Length == info.Length)
                {
                    state = cached.State;
                }
                else
                {
                    state = Parse(File.ReadAllText(path));
                    _cache = (info.LastWriteTimeUtc, info.Length, state);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Update-Stand {Path} nicht lesbar.", path);
            return null;
        }

        return state is not null && (state.ExpiresAt is null || state.ExpiresAt > clock.GetUtcNow()) ? state : null;
    }

    internal UpdateMaintenanceState? Parse(string json)
    {
        try
        {
            var state = JsonSerializer.Deserialize<UpdateMaintenanceState>(json, Json);
            if (state is null || string.IsNullOrWhiteSpace(state.Id) || !UpdatePhases.All.Contains(state.Phase))
            {
                logger.LogWarning("Update-Stand ohne gültige Phase – wird ignoriert.");
                return null;
            }

            return state;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Update-Stand ist kein gültiges JSON – wird ignoriert.");
            return null;
        }
    }
}

/// <summary>
/// Beendet zu Beginn der Update-Phase einmal alle Sitzungen (je Update-Kennung und Prozess; wiederholt ist es wirkungslos).
/// Ausgelöst vom ersten gesperrten Aufruf und zusätzlich alle zwei Sekunden im Hintergrund, falls gerade niemand anfragt.
/// </summary>
public sealed partial class UpdateSessionTerminator(UpdateMaintenanceReader reader, IServiceScopeFactory scopes,
    ILogger<UpdateSessionTerminator> logger) : BackgroundService
{
    public const string RevokeReason = "system_update";
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _done;

    public async Task EnsureAsync(UpdateMaintenanceState state, CancellationToken cancellationToken)
    {
        if (!state.Blocking || _done == state.Id)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_done == state.Id)
            {
                return;
            }

            await using var scope = scopes.CreateAsyncScope();
            var count = await scope.ServiceProvider.GetRequiredService<ISessionTermination>().RevokeAllAsync(RevokeReason, cancellationToken);
            _done = state.Id;
            LogEnded(logger, count, state.TargetVersion ?? "?");
        }
        finally
        {
            _gate.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (reader.Current() is { Blocking: true } state)
                {
                    await EnsureAsync(state, stoppingToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Datenbank gerade nicht erreichbar (Start, Update): beim nächsten Durchlauf erneut.
                logger.LogDebug(exception, "Sitzungen konnten (noch) nicht beendet werden.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), TimeProvider.System, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public override void Dispose()
    {
        _gate.Dispose();
        base.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Update auf {Version}: {Count} Sitzung(en) beendet, Anmeldung bis zur Freigabe gesperrt.")]
    private static partial void LogEnded(ILogger logger, int count, string version);
}
