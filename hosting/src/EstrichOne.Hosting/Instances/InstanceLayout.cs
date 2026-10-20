using EstrichOne.Hosting.Docker;
using EstrichOne.Hosting.Domain;
using EstrichOne.Hosting.Infrastructure;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Instances;

/// <summary>Namen und Pfade einer Instanz. Jede Instanz ist ein eigenes Compose-Projekt <c>estrichone-&lt;kürzel&gt;</c>.</summary>
public sealed class InstanceLayout(IOptions<HostingOptions> options)
{
    /// <summary>Compose-Dateien in der Reihenfolge der Zusammenführung (wie beim Docker-Installer + Hosting-Ergänzung).</summary>
    public static readonly IReadOnlyList<string> ComposeFiles = ["compose.yml", "compose.override.yml", "compose.proxy.yml", "compose.hosting.yml"];

    /// <summary>Port, auf dem Caddy einer Instanz in der Betriebsart „proxy“ HTTP anbietet (nur im Instanznetz).</summary>
    public const int InstanceHttpPort = 8181;

    public const string TenantLabel = "de.estrichone.hosting.tenant";

    public static string ProjectName(string slug) => $"estrichone-{Checked(slug)}";

    /// <summary>Netz zwischen dem gemeinsamen Proxy (und der Konsole) und dem Caddy dieser einen Instanz.</summary>
    public static string EdgeNetwork(string slug) => $"estrichone-{Checked(slug)}-edge";

    public static string WebContainer(string slug) => $"estrichone-{Checked(slug)}-caddy-1";

    public static string PostgresContainer(string slug) => $"estrichone-{Checked(slug)}-postgres-1";

    public string Directory(string slug) => Path.Combine(options.Value.InstancesDirectory, Checked(slug));

    public string DataDirectory(string slug) => Path.Combine(Directory(slug), "data");

    public string BackupDirectory(string slug) =>
        string.IsNullOrWhiteSpace(options.Value.BackupRoot)
            ? Path.Combine(Directory(slug), "backup-repository")
            : Path.Combine(options.Value.BackupRoot, Checked(slug));

    public ComposeProject Project(string slug) => new(ProjectName(slug), Directory(slug), ComposeFiles);

    /// <summary>Jeder Pfad- oder Namensbestandteil wird hier ein letztes Mal geprüft.</summary>
    private static string Checked(string slug) =>
        Validation.IsValidSlug(slug) ? slug : throw new ArgumentException($"Ungültiges Kürzel: {slug}", nameof(slug));
}
