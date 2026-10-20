namespace EstrichOne.Hosting.Docker;

/// <summary>Ein Compose-Projekt einer Instanz: Projektname, Verzeichnis und Compose-Dateien (relativ zum Verzeichnis).</summary>
public sealed record ComposeProject(string Name, string Directory, IReadOnlyList<string> Files);

public sealed record CommandResult(int ExitCode, string Output, string Error)
{
    public bool Success => ExitCode == 0;

    /// <summary>Kurze Fehlermeldung für Anzeige und Protokoll (letzte Zeilen der Fehlerausgabe).</summary>
    public string Summary
    {
        get
        {
            var text = string.IsNullOrWhiteSpace(Error) ? Output : Error;
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return lines.Length == 0 ? $"Exit-Code {ExitCode}" : string.Join(" | ", lines.TakeLast(3));
        }
    }
}

/// <summary>Zustand eines Containers eines Compose-Projekts.</summary>
public sealed record ContainerInfo(
    string Id,
    string Name,
    string Service,
    string State,
    string? Health,
    string Status,
    string Image,
    int? ExitCode);

/// <summary>Momentaufnahme aus <c>docker stats</c>.</summary>
public sealed record ContainerUsage(string Id, string Name, double CpuPercent, long MemoryBytes, long MemoryLimitBytes);

public sealed record DockerSystemInfo(
    bool Reachable,
    string? ServerVersion,
    string? ComposeVersion,
    int Cpus,
    long MemoryBytes,
    int ContainersRunning,
    int Containers,
    string? OperatingSystem,
    string? Error);

/// <summary>
/// Steuerung von Docker. Produktiv: <see cref="DockerCliHost"/> (Docker-CLI und Compose-Plugin über den Socket);
/// Tests und Vorführung: <see cref="FakeDockerHost"/>.
/// </summary>
public interface IDockerHost
{
    /// <summary><c>docker compose</c> für ein Projekt; <paramref name="output"/> erhält jede Ausgabezeile (stdout und stderr).</summary>
    Task<CommandResult> ComposeAsync(ComposeProject project, IReadOnlyList<string> arguments, TimeSpan timeout,
        Action<string>? output, CancellationToken cancellationToken);

    /// <summary>Alle Container (auch beendete) eines Compose-Projekts.</summary>
    Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(string projectName, CancellationToken cancellationToken);

    /// <summary>CPU- und Speicherverbrauch der angegebenen (laufenden) Container.</summary>
    Task<IReadOnlyList<ContainerUsage>> GetUsageAsync(IReadOnlyList<string> containerIds, CancellationToken cancellationToken);

    /// <summary>Die letzten <paramref name="tail"/> Zeilen eines Dienstes (mit Zeitstempel).</summary>
    Task<string> GetLogsAsync(ComposeProject project, string service, int tail, CancellationToken cancellationToken);

    /// <summary>Legt ein Netz an, falls es fehlt.</summary>
    Task EnsureNetworkAsync(string name, bool isInternal, IReadOnlyDictionary<string, string> labels, CancellationToken cancellationToken);

    /// <summary>Entfernt ein Netz; ein fehlendes Netz ist kein Fehler.</summary>
    Task RemoveNetworkAsync(string name, CancellationToken cancellationToken);

    /// <summary>Verbindet einen Container mit einem Netz; eine bestehende Verbindung ist kein Fehler.</summary>
    Task<bool> ConnectNetworkAsync(string network, string container, CancellationToken cancellationToken);

    /// <summary>Trennt einen Container von einem Netz; eine fehlende Verbindung ist kein Fehler.</summary>
    Task DisconnectNetworkAsync(string network, string container, CancellationToken cancellationToken);

    /// <summary>Führt einen Befehl in einem laufenden Container aus (ohne Shell, Argumente als Liste).</summary>
    Task<CommandResult> ExecAsync(string container, IReadOnlyList<string> command, TimeSpan timeout, CancellationToken cancellationToken);

    Task<DockerSystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken);
}
