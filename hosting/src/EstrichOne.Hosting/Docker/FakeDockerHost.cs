using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EstrichOne.Hosting.Docker;

/// <summary>
/// Simulierter Docker-Host für Tests und Vorführungen (<c>Hosting__Docker=fake</c>): bildet die Compose-Befehle der
/// Konsole auf einen Zustand im Speicher ab. Die Oberfläche zeigt in diesem Modus deutlich „Simulation“ an.
/// Eine Version mit „fehler“ im Namen lässt das Laden der Images scheitern (für Fehlerpfade in Tests).
/// </summary>
public sealed class FakeDockerHost : IDockerHost
{
    private static readonly string[] Services = ["postgres", "migrate", "api", "backup", "caddy"];

    private readonly ConcurrentDictionary<string, List<FakeContainer>> projects = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> networks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> connections = new(StringComparer.Ordinal);

    /// <summary>Künstliche Dauer je Schritt (Tests: 0).</summary>
    public TimeSpan StepDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Alle ausgeführten Compose-Aufrufe (für Tests).</summary>
    public ConcurrentQueue<string> Calls { get; } = new();

    public IReadOnlyCollection<string> Networks => networks.Keys.ToList();

    public bool IsConnected(string network, string container) => connections.ContainsKey($"{network}|{container}");

    private sealed class FakeContainer
    {
        public required string Service { get; init; }
        public required string Name { get; init; }
        public required string Image { get; set; }
        public string State { get; set; } = "running";
        public int? ExitCode { get; set; }
        public DateTime Since { get; set; } = DateTime.UtcNow;
    }

    public async Task<CommandResult> ComposeAsync(ComposeProject project, IReadOnlyList<string> arguments, TimeSpan timeout,
        Action<string>? output, CancellationToken cancellationToken)
    {
        Calls.Enqueue($"{project.Name} {string.Join(' ', arguments)}");
        var verb = arguments.Count > 0 ? arguments[0] : string.Empty;
        var env = ReadEnv(project.Directory);
        var version = env.GetValueOrDefault("ESTRICHONE_VERSION", "0.0.0");
        var registry = env.GetValueOrDefault("ESTRICHONE_REGISTRY", "ghcr.io/florians25");
        void Say(string line) => output?.Invoke(line);

        switch (verb)
        {
            case "pull":
                foreach (var service in new[] { "api", "web", "backup" })
                {
                    await Task.Delay(StepDelay, cancellationToken);
                    if (version.Contains("fehler", StringComparison.Ordinal))
                    {
                        Say($" {service} Error manifest unknown");
                        return new CommandResult(18, string.Empty, $"Error response from daemon: manifest for {registry}/estrichone-{service}:{version} not found: manifest unknown");
                    }

                    Say($" {service} Pulled");
                }

                return new CommandResult(0, string.Empty, string.Empty);

            case "up":
                var containers = projects.GetOrAdd(project.Name, _ => []);
                lock (containers)
                {
                    foreach (var service in Services)
                    {
                        var image = service switch
                        {
                            "postgres" => "postgres:17-alpine",
                            "caddy" => $"{registry}/estrichone-web:{version}",
                            "migrate" or "api" => $"{registry}/estrichone-api:{version}",
                            _ => $"{registry}/estrichone-{service}:{version}",
                        };
                        var container = containers.FirstOrDefault(c => c.Service == service);
                        if (container is null)
                        {
                            container = new FakeContainer { Service = service, Name = $"{project.Name}-{service}-1", Image = image };
                            containers.Add(container);
                            Say($" Container {container.Name} Created");
                        }

                        container.Image = image;
                        container.State = service == "migrate" ? "exited" : "running";
                        container.ExitCode = service == "migrate" ? 0 : null;
                        container.Since = DateTime.UtcNow;
                        Say($" Container {container.Name} {(service == "migrate" ? "Exited" : "Healthy")}");
                    }
                }

                await Task.Delay(StepDelay, cancellationToken);
                return new CommandResult(0, string.Empty, string.Empty);

            case "stop":
            case "start":
            case "restart":
                if (!projects.TryGetValue(project.Name, out var existing))
                {
                    return new CommandResult(1, string.Empty, $"no container found for project \"{project.Name}\"");
                }

                lock (existing)
                {
                    foreach (var container in existing.Where(c => c.Service != "migrate"))
                    {
                        container.State = verb == "stop" ? "exited" : "running";
                        container.ExitCode = verb == "stop" ? 0 : null;
                        container.Since = DateTime.UtcNow;
                        Say($" Container {container.Name} {(verb == "stop" ? "Stopped" : "Started")}");
                    }
                }

                await Task.Delay(StepDelay, cancellationToken);
                return new CommandResult(0, string.Empty, string.Empty);

            case "down":
                projects.TryRemove(project.Name, out _);
                Say($" Network {project.Name}_backend Removed");
                await Task.Delay(StepDelay, cancellationToken);
                return new CommandResult(0, string.Empty, string.Empty);

            case "exec":
                if (arguments.Contains("estrichone-backup"))
                {
                    await Task.Delay(StepDelay, cancellationToken);
                    var status = Path.Combine(project.Directory, "data", "backup-status");
                    Directory.CreateDirectory(status);
                    var at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                    var json = $"{{\"status\":\"succeeded\",\"message\":\"Sicherung erfolgreich\",\"at\":\"{at}\",\"durationSeconds\":3,\"targets\":1}}";
                    await File.WriteAllTextAsync(Path.Combine(status, "lastRun.json"), json, cancellationToken);
                    await File.WriteAllTextAsync(Path.Combine(status, "lastSuccess.json"), json, cancellationToken);
                    var repository = Path.Combine(project.Directory, "backup-repository");
                    Directory.CreateDirectory(repository);
                    await File.WriteAllTextAsync(Path.Combine(repository, "config"), "simulierte Sicherung", cancellationToken);
                    Say($"{at} Fertig: Snapshot 1a2b3c4d");
                }

                return new CommandResult(0, string.Empty, string.Empty);

            default:
                return new CommandResult(0, string.Empty, string.Empty);
        }
    }

    public Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(string projectName, CancellationToken cancellationToken)
    {
        if (!projects.TryGetValue(projectName, out var containers))
        {
            return Task.FromResult<IReadOnlyList<ContainerInfo>>([]);
        }

        lock (containers)
        {
            IReadOnlyList<ContainerInfo> list = containers
                .Select(c => new ContainerInfo(Id(c.Name), c.Name, c.Service, c.State,
                    c.State == "running" && c.Service is "postgres" or "api" or "caddy" ? "healthy" : null,
                    c.State == "running" ? $"Up {Since(c.Since)}" + (c.Service is "postgres" or "api" or "caddy" ? " (healthy)" : string.Empty)
                                         : $"Exited ({c.ExitCode ?? 0}) {Since(c.Since)} ago",
                    c.Image, c.State == "running" ? null : c.ExitCode))
                .OrderBy(c => c.Service, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(list);
        }
    }

    public Task<IReadOnlyList<ContainerUsage>> GetUsageAsync(IReadOnlyList<string> containerIds, CancellationToken cancellationToken)
    {
        var all = projects.Values.SelectMany(list => { lock (list) { return list.ToList(); } }).ToList();
        IReadOnlyList<ContainerUsage> usage = containerIds
            .Select(id => all.FirstOrDefault(c => Id(c.Name) == id))
            .Where(c => c is not null && c.State == "running")
            .Select(c =>
            {
                var seed = Hash(c!.Name);
                var memory = c.Service switch { "api" => 180L, "postgres" => 60L, "caddy" => 25L, _ => 12L } * 1024 * 1024 + seed % (20 * 1024 * 1024);
                return new ContainerUsage(Id(c.Name), c.Name, Math.Round(seed % 400 / 100d, 2), memory, 2L * 1024 * 1024 * 1024);
            })
            .ToList();
        return Task.FromResult(usage);
    }

    public Task<string> GetLogsAsync(ComposeProject project, string service, int tail, CancellationToken cancellationToken)
    {
        if (!projects.ContainsKey(project.Name))
        {
            return Task.FromResult(string.Empty);
        }

        var now = DateTime.UtcNow;
        var lines = new List<string>();
        for (var i = 0; i < 40; i++)
        {
            var at = now.AddSeconds(-(40 - i) * 15).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture);
            lines.Add(service switch
            {
                "api" => $"{service}-1  | {at} {{\"@t\":\"{at}\",\"@l\":\"{(i % 13 == 5 ? "Warning" : "Information")}\",\"@mt\":\"HTTP {{RequestMethod}} {{RequestPath}} antwortete {{StatusCode}}\",\"RequestMethod\":\"GET\",\"RequestPath\":\"/health/ready\",\"StatusCode\":200}}",
                "backup" => $"{service}-1  | {at} {at} Warte auf nächste Sicherung (22:30)",
                "postgres" => $"{service}-1  | {at} LOG:  checkpoint complete: wrote {i} buffers",
                _ => $"{service}-1  | {at} {{\"level\":\"info\",\"msg\":\"handled request\",\"status\":200}}",
            });
        }

        if (service == "api")
        {
            lines.Insert(3, $"api-1  | {now.AddMinutes(-9).ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture)} {{\"@l\":\"Warning\",\"@mt\":\"EstrichONE ist noch nicht eingerichtet. Einrichtungscode: {SetupCode(project.Name)} (gültig 24 Stunden, einmalig).\"}}");
        }

        return Task.FromResult(string.Join('\n', lines.TakeLast(tail)) + "\n");
    }

    /// <summary>Simulierter Einrichtungscode einer Instanz (stabil je Projekt).</summary>
    public static string SetupCode(string projectName)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(projectName));
        var chars = bytes.Take(12).Select(b => alphabet[b % alphabet.Length]).ToArray();
        return $"{new string(chars, 0, 4)}-{new string(chars, 4, 4)}-{new string(chars, 8, 4)}";
    }

    public Task EnsureNetworkAsync(string name, bool isInternal, IReadOnlyDictionary<string, string> labels, CancellationToken cancellationToken)
    {
        networks[name] = isInternal;
        return Task.CompletedTask;
    }

    public Task RemoveNetworkAsync(string name, CancellationToken cancellationToken)
    {
        networks.TryRemove(name, out _);
        foreach (var key in connections.Keys.Where(k => k.StartsWith(name + "|", StringComparison.Ordinal)))
        {
            connections.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ConnectNetworkAsync(string network, string container, CancellationToken cancellationToken)
    {
        if (!networks.ContainsKey(network))
        {
            throw new DockerException($"network {network} not found");
        }

        return Task.FromResult(connections.TryAdd($"{network}|{container}", true));
    }

    public Task DisconnectNetworkAsync(string network, string container, CancellationToken cancellationToken)
    {
        connections.TryRemove($"{network}|{container}", out _);
        return Task.CompletedTask;
    }

    public Task<CommandResult> ExecAsync(string container, IReadOnlyList<string> command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (command.Contains("psql"))
        {
            return Task.FromResult(new CommandResult(0, (Hash(container) % 4 + 1).ToString(CultureInfo.InvariantCulture) + "\n", string.Empty));
        }

        return Task.FromResult(new CommandResult(0, string.Empty, string.Empty));
    }

    public Task<DockerSystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken)
    {
        var all = projects.Values.SelectMany(list => { lock (list) { return list.ToList(); } }).ToList();
        return Task.FromResult(new DockerSystemInfo(true, "28.0.0 (Simulation)", "2.36.0 (Simulation)", 8, 32L * 1024 * 1024 * 1024,
            all.Count(c => c.State == "running"), all.Count, "Simulation", null));
    }

    public Task<CommandResult> PullImageAsync(string image, CancellationToken cancellationToken)
    {
        Calls.Enqueue($"pull {image}");
        return Task.FromResult(image.Contains("fehler", StringComparison.Ordinal)
            ? new CommandResult(1, string.Empty, $"manifest for {image} not found")
            : new CommandResult(0, string.Empty, string.Empty));
    }

    public Task<CommandResult> RunDetachedAsync(string image, string name, IReadOnlyDictionary<string, string> environment,
        IReadOnlyList<string> volumes, IReadOnlyList<string> command, CancellationToken cancellationToken)
    {
        Calls.Enqueue($"run {name} {image} {string.Join(' ', command)}");
        return Task.FromResult(new CommandResult(0, "simuliert", string.Empty));
    }

    private static Dictionary<string, string> ReadEnv(string directory)
    {
        var file = Path.Combine(directory, ".env");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(file))
        {
            return result;
        }

        foreach (var line in File.ReadAllLines(file))
        {
            var index = line.IndexOf('=', StringComparison.Ordinal);
            if (index > 0 && !line.StartsWith('#'))
            {
                result[line[..index]] = line[(index + 1)..].Trim('"');
            }
        }

        return result;
    }

    private static string Id(string name) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..12];

    private static long Hash(string text) => BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0);

    private static string Since(DateTime since)
    {
        var span = DateTime.UtcNow - since;
        return span.TotalMinutes < 1 ? "Less than a second" : span.TotalHours < 1 ? $"{(int)span.TotalMinutes} minutes" : $"{(int)span.TotalHours} hours";
    }
}
