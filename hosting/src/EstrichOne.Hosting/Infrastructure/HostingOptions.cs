namespace EstrichOne.Hosting.Infrastructure;

/// <summary>Konfiguration der Hosting-Konsole (Abschnitt „Hosting“, Umgebungsvariablen <c>Hosting__…</c>).</summary>
public sealed class HostingOptions
{
    public const string SectionName = "Hosting";

    /// <summary>
    /// Wurzelverzeichnis auf dem Host (innen und außen derselbe Pfad, damit relative Pfade der Compose-Dateien
    /// vom Docker-Daemon richtig aufgelöst werden). Darunter: <c>instances/</c>, <c>proxy/</c>, <c>archive/</c>, <c>console/</c>.
    /// </summary>
    public string Root { get; set; } = "/srv/estrichone-hosting";

    /// <summary>Basis-Domain für Mandanten-Subdomains, z. B. <c>estrichone-hosting.de</c> → <c>firma.estrichone-hosting.de</c>.</summary>
    public string BaseDomain { get; set; } = "localhost";

    /// <summary>Adresse der Konsole selbst (wird im gemeinsamen Proxy eingetragen).</summary>
    public string ConsoleDomain { get; set; } = "konsole.localhost";

    /// <summary>Version der Suite für neue Mandanten, solange in den Einstellungen nichts anderes gewählt ist.</summary>
    public string DefaultVersion { get; set; } = "0.1.0-preview.4";

    /// <summary>Registry der Suite-Images (wie <c>ESTRICHONE_REGISTRY</c> beim Installer).</summary>
    public string Registry { get; set; } = "ghcr.io/florians25";

    /// <summary><c>docker</c> = echte Docker-Steuerung über den Socket; <c>fake</c> = Simulation (nur Tests/Vorführung).</summary>
    public string Docker { get; set; } = "docker";

    /// <summary>Pfad der Docker-CLI im Container.</summary>
    public string DockerCli { get; set; } = "docker";

    /// <summary>
    /// <c>caddy</c> = mitgelieferter Caddy als gemeinsamer Reverse Proxy (Konsole schreibt dessen Konfiguration);
    /// <c>extern</c> = vorhandener Proxy (z. B. Nginx Proxy Manager), Konsole verbindet ihn nur mit den Instanznetzen.
    /// </summary>
    public string Proxy { get; set; } = "caddy";

    /// <summary>Containername des gemeinsamen Reverse Proxys.</summary>
    public string ProxyContainer { get; set; } = "estrichone-hosting-proxy";

    /// <summary>Containername der Konsole (für den Zugriff auf die Instanznetze).</summary>
    public string ConsoleContainer { get; set; } = "estrichone-hosting-console";

    /// <summary>Zertifikate im mitgelieferten Caddy: <c>acme</c> (Let's Encrypt), <c>internal</c> (eigene CA, Tests), <c>off</c> (nur HTTP).</summary>
    public string Tls { get; set; } = "acme";

    /// <summary>E-Mail-Adresse für Let's Encrypt (optional, empfohlen).</summary>
    public string? AcmeEmail { get; set; }

    /// <summary>Sitzungscookie ohne <c>Secure</c> – nur für lokale Tests über http://localhost.</summary>
    public bool InsecureCookies { get; set; }

    /// <summary>Intervall der Statuserhebung in Sekunden.</summary>
    public int StatusIntervalSeconds { get; set; } = 30;

    /// <summary>Intervall der Speicherplatz- und Benutzerzählung in Minuten.</summary>
    public int UsageIntervalMinutes { get; set; } = 10;

    /// <summary>Gleichzeitig ausgeführte Vorgänge (verschiedene Mandanten).</summary>
    public int MaxParallelOperations { get; set; } = 2;

    /// <summary>Wartezeit für <c>docker compose up --wait</c> in Sekunden.</summary>
    public int ComposeWaitSeconds { get; set; } = 300;

    /// <summary>
    /// Optional: eigenes Wurzelverzeichnis für die Sicherungs-Repositorys (z. B. zweite Festplatte); je Mandant ein
    /// Unterordner. Muss wie <see cref="Root"/> mit gleichem Pfad in den Konsolen-Container eingebunden sein.
    /// </summary>
    public string? BackupRoot { get; set; }

    /// <summary>Verzeichnis mit den Compose-Vorlagen der Suite im Image.</summary>
    public string TemplateDirectory { get; set; } = Path.Combine(AppContext.BaseDirectory, "templates");

    public bool IsFake => string.Equals(Docker, "fake", StringComparison.OrdinalIgnoreCase);

    public bool ManagesProxy => string.Equals(Proxy, "caddy", StringComparison.OrdinalIgnoreCase);

    public string InstancesDirectory => Path.Combine(Root, "instances");

    public string ArchiveDirectory => Path.Combine(Root, "archive");

    public string ProxyDirectory => Path.Combine(Root, "proxy");

    public string ConsoleDataDirectory => Path.Combine(Root, "console");
}
