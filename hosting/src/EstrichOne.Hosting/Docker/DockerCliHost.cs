using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EstrichOne.Hosting.Infrastructure;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Docker;

/// <summary>
/// Docker-Steuerung über die offizielle Docker-CLI mit Compose-Plugin (im Konsolen-Image enthalten).
/// Begründung: Die Instanzen sind Compose-Projekte mit Abhängigkeiten, Healthchecks, einmaligen Diensten (migrate),
/// Profilen und YAML-Merge-Tags (<c>!override</c>, <c>!reset</c>). Diese Semantik implementiert nur Compose selbst –
/// eine Nachbildung über die Engine-API (Docker.DotNet) wäre aufwendig und fehleranfällig. Die CLI wird ohne Shell
/// mit Argumentlisten aufgerufen; alle variablen Teile (Kürzel, Domains, Versionen) sind vorher streng geprüft.
/// </summary>
public sealed partial class DockerCliHost(IOptions<HostingOptions> options, ILogger<DockerCliHost> logger) : IDockerHost
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(30);

    private string Cli => options.Value.DockerCli;

    private Task<CommandResult> DockerAsync(IEnumerable<string> arguments, TimeSpan timeout, CancellationToken cancellationToken,
        Action<string>? output = null, string? workingDirectory = null) =>
        ProcessRunner.RunAsync(Cli, arguments, timeout, output, cancellationToken, workingDirectory);

    public static IReadOnlyList<string> ComposeArguments(ComposeProject project, IReadOnlyList<string> arguments)
    {
        var list = new List<string> { "compose", "--project-directory", project.Directory, "-p", project.Name };
        foreach (var file in project.Files)
        {
            list.Add("-f");
            list.Add(Path.Combine(project.Directory, file));
        }

        list.Add("--env-file");
        list.Add(Path.Combine(project.Directory, ".env"));
        list.AddRange(arguments);
        return list;
    }

    public Task<CommandResult> ComposeAsync(ComposeProject project, IReadOnlyList<string> arguments, TimeSpan timeout,
        Action<string>? output, CancellationToken cancellationToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("docker compose -p {Project} {Arguments}", project.Name, string.Join(' ', arguments));
        }

        return DockerAsync(ComposeArguments(project, arguments), timeout, cancellationToken, output, project.Directory);
    }

    public async Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(string projectName, CancellationToken cancellationToken)
    {
        var result = await DockerAsync(
            ["ps", "-a", "--no-trunc", "--filter", $"label=com.docker.compose.project={projectName}", "--format",
             "{{.ID}}\t{{.Names}}\t{{.Label \"com.docker.compose.service\"}}\t{{.State}}\t{{.Status}}\t{{.Image}}"],
            ShortTimeout, cancellationToken);
        if (!result.Success)
        {
            throw new DockerException($"Container von {projectName} konnten nicht gelesen werden: {result.Summary}");
        }

        return ParseContainers(result.Output);
    }

    public static IReadOnlyList<ContainerInfo> ParseContainers(string output)
    {
        var containers = new List<ContainerInfo>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 6)
            {
                continue;
            }

            var status = parts[4];
            string? health = status.Contains("(healthy)", StringComparison.Ordinal) ? "healthy"
                : status.Contains("(unhealthy)", StringComparison.Ordinal) ? "unhealthy"
                : status.Contains("health: starting", StringComparison.Ordinal) ? "starting"
                : null;
            int? exitCode = null;
            var exited = ExitedPattern().Match(status);
            if (exited.Success)
            {
                exitCode = int.Parse(exited.Groups[1].Value, CultureInfo.InvariantCulture);
            }

            containers.Add(new ContainerInfo(parts[0], parts[1], parts[2], parts[3], health, status, parts[5], exitCode));
        }

        return containers.OrderBy(c => c.Service, StringComparer.Ordinal).ToList();
    }

    public async Task<IReadOnlyList<ContainerUsage>> GetUsageAsync(IReadOnlyList<string> containerIds, CancellationToken cancellationToken)
    {
        if (containerIds.Count == 0)
        {
            return [];
        }

        var arguments = new List<string> { "stats", "--no-stream", "--no-trunc", "--format", "{{.ID}}\t{{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}" };
        arguments.AddRange(containerIds);
        var result = await DockerAsync(arguments, ShortTimeout, cancellationToken);
        if (!result.Success)
        {
            logger.LogWarning("docker stats fehlgeschlagen: {Error}", result.Summary);
            return [];
        }

        return ParseUsage(result.Output);
    }

    public static IReadOnlyList<ContainerUsage> ParseUsage(string output)
    {
        var list = new List<ContainerUsage>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split('\t');
            if (parts.Length < 4)
            {
                continue;
            }

            var cpu = double.TryParse(parts[2].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var c) ? c : 0;
            var memory = parts[3].Split('/', StringSplitOptions.TrimEntries);
            list.Add(new ContainerUsage(parts[0], parts[1], cpu, ParseSize(memory[0]), memory.Length > 1 ? ParseSize(memory[1]) : 0));
        }

        return list;
    }

    /// <summary>„12.5MiB“, „1.944GiB“, „800kB“ → Bytes.</summary>
    public static long ParseSize(string text)
    {
        var match = SizePattern().Match(text.Trim());
        if (!match.Success)
        {
            return 0;
        }

        var value = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var factor = match.Groups[2].Value.ToUpperInvariant() switch
        {
            "KIB" => 1024d,
            "MIB" => 1024d * 1024,
            "GIB" => 1024d * 1024 * 1024,
            "TIB" => 1024d * 1024 * 1024 * 1024,
            "KB" => 1000d,
            "MB" => 1000d * 1000,
            "GB" => 1000d * 1000 * 1000,
            "TB" => 1000d * 1000 * 1000 * 1000,
            _ => 1d,
        };
        return (long)(value * factor);
    }

    public async Task<string> GetLogsAsync(ComposeProject project, string service, int tail, CancellationToken cancellationToken)
    {
        var result = await DockerAsync(
            ComposeArguments(project, ["logs", "--no-color", "--timestamps", "--tail", tail.ToString(CultureInfo.InvariantCulture), service]),
            ShortTimeout, cancellationToken, workingDirectory: project.Directory);
        if (!result.Success)
        {
            throw new DockerException($"Protokoll von {service} nicht lesbar: {result.Summary}");
        }

        // Compose schreibt Protokolle auf stdout, Hinweise auf stderr.
        return result.Output;
    }

    public async Task EnsureNetworkAsync(string name, bool isInternal, IReadOnlyDictionary<string, string> labels, CancellationToken cancellationToken)
    {
        var inspect = await DockerAsync(["network", "inspect", "--format", "{{.Name}}", name], ShortTimeout, cancellationToken);
        if (inspect.Success)
        {
            return;
        }

        var arguments = new List<string> { "network", "create" };
        if (isInternal)
        {
            arguments.Add("--internal");
        }

        foreach (var (key, value) in labels)
        {
            arguments.Add("--label");
            arguments.Add($"{key}={value}");
        }

        arguments.Add(name);
        var result = await DockerAsync(arguments, ShortTimeout, cancellationToken);
        if (!result.Success && !result.Error.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            throw new DockerException($"Netz {name} konnte nicht angelegt werden: {result.Summary}");
        }
    }

    public async Task RemoveNetworkAsync(string name, CancellationToken cancellationToken)
    {
        var result = await DockerAsync(["network", "rm", name], ShortTimeout, cancellationToken);
        if (!result.Success && !result.Error.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            throw new DockerException($"Netz {name} konnte nicht entfernt werden: {result.Summary}");
        }
    }

    public async Task<bool> ConnectNetworkAsync(string network, string container, CancellationToken cancellationToken)
    {
        var result = await DockerAsync(["network", "connect", network, container], ShortTimeout, cancellationToken);
        if (result.Success)
        {
            return true;
        }

        if (result.Error.Contains("already exists", StringComparison.OrdinalIgnoreCase)
            || result.Error.Contains("already attached", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        throw new DockerException($"{container} konnte nicht mit {network} verbunden werden: {result.Summary}");
    }

    public async Task DisconnectNetworkAsync(string network, string container, CancellationToken cancellationToken)
    {
        var result = await DockerAsync(["network", "disconnect", "--force", network, container], ShortTimeout, cancellationToken);
        if (!result.Success
            && !result.Error.Contains("not connected", StringComparison.OrdinalIgnoreCase)
            && !result.Error.Contains("not found", StringComparison.OrdinalIgnoreCase)
            && !result.Error.Contains("No such container", StringComparison.OrdinalIgnoreCase))
        {
            throw new DockerException($"{container} konnte nicht von {network} getrennt werden: {result.Summary}");
        }
    }

    public Task<CommandResult> ExecAsync(string container, IReadOnlyList<string> command, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "exec", container };
        arguments.AddRange(command);
        return DockerAsync(arguments, timeout, cancellationToken);
    }

    public async Task<DockerSystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken)
    {
        var info = await DockerAsync(["info", "--format", "{{json .}}"], ShortTimeout, cancellationToken);
        if (!info.Success)
        {
            return new DockerSystemInfo(false, null, null, 0, 0, 0, 0, null, info.Summary);
        }

        var compose = await DockerAsync(["compose", "version", "--short"], ShortTimeout, cancellationToken);
        try
        {
            using var json = JsonDocument.Parse(info.Output);
            var root = json.RootElement;
            string? Text(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            long Number(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;
            return new DockerSystemInfo(true, Text("ServerVersion"), compose.Success ? compose.Output.Trim() : null,
                (int)Number("NCPU"), Number("MemTotal"), (int)Number("ContainersRunning"), (int)Number("Containers"),
                Text("OperatingSystem"), null);
        }
        catch (JsonException exception)
        {
            return new DockerSystemInfo(false, null, null, 0, 0, 0, 0, null, exception.Message);
        }
    }

    public Task<CommandResult> PullImageAsync(string image, CancellationToken cancellationToken) =>
        DockerAsync(["pull", "--quiet", image], TimeSpan.FromMinutes(15), cancellationToken);

    public Task<CommandResult> RunDetachedAsync(string image, string name, IReadOnlyDictionary<string, string> environment,
        IReadOnlyList<string> volumes, IReadOnlyList<string> command, CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "run", "-d", "--rm", "--name", name };
        foreach (var (key, value) in environment)
        {
            arguments.Add("-e");
            arguments.Add($"{key}={value}");
        }

        foreach (var volume in volumes)
        {
            arguments.Add("-v");
            arguments.Add(volume);
        }

        arguments.Add(image);
        arguments.AddRange(command);
        return DockerAsync(arguments, ShortTimeout, cancellationToken);
    }

    [GeneratedRegex(@"Exited \((-?\d+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex ExitedPattern();

    [GeneratedRegex(@"^([0-9]+(?:\.[0-9]+)?)\s*([KMGT]?i?B)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SizePattern();
}

public sealed class DockerException(string message) : Exception(message);
