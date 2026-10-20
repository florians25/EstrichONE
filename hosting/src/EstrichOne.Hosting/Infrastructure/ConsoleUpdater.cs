using EstrichOne.Hosting.Docker;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Infrastructure;

public sealed record ConsoleUpdateState(bool Running, string? TargetVersion, string Message, DateTime? StartedAt, bool? Success);

/// <summary>
/// Selbst-Update der Konsole: lädt das Image der Zielversion und startet daraus losgelöst den Installer
/// (<c>… install</c>). Der Installer ersetzt den Konsolen-Container per <c>docker compose up -d</c>; Daten, .env und
/// alle Mandanten bleiben unverändert. Danach meldet sich die Konsole in der neuen Version zurück.
/// </summary>
public sealed class ConsoleUpdater(IDockerHost docker, IOptions<HostingOptions> options, TimeProvider clock, ILogger<ConsoleUpdater> logger)
{
    public const string UpdaterContainer = "estrichone-hosting-updater";

    public ConsoleUpdateState State { get; private set; } = new(false, null, "Kein Update angefordert.", null, null);

    public string ImageFor(string version) => $"{options.Value.Registry.TrimEnd('/')}/estrichone-hosting:{version}";

    public async Task<ConsoleUpdateState> StartAsync(string version, CancellationToken cancellationToken)
    {
        if (State.Running)
        {
            return State;
        }

        var image = ImageFor(version);
        State = new ConsoleUpdateState(true, version, $"Lade {image} …", clock.GetUtcNow().UtcDateTime, null);
        var pull = await docker.PullImageAsync(image, cancellationToken);
        if (!pull.Success)
        {
            State = State with { Running = false, Success = false, Message = $"Image nicht ladbar: {pull.Summary}" };
            return State;
        }

        var root = options.Value.Root;
        var run = await docker.RunDetachedAsync(image, UpdaterContainer,
            new Dictionary<string, string> { ["HOSTING_ROOT"] = root, ["HOSTING_IMAGE"] = image },
            ["/var/run/docker.sock:/var/run/docker.sock", $"{root}:{root}"], ["install"], cancellationToken);
        if (!run.Success)
        {
            State = State with { Running = false, Success = false, Message = $"Installer nicht gestartet: {run.Summary}" };
            return State;
        }

        logger.LogWarning("Selbst-Update auf {Image} gestartet – die Konsole wird gleich neu gestartet.", image);
        State = State with
        {
            Message = options.Value.IsFake
                ? $"Simulation: Installer für {version} wäre jetzt gestartet."
                : $"Installer für {version} läuft – die Konsole startet in Kürze neu. Seite danach neu laden.",
            Running = !options.Value.IsFake,
            Success = options.Value.IsFake ? true : null,
        };
        return State;
    }
}
