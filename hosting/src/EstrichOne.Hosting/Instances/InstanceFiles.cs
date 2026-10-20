using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using EstrichOne.Hosting.Data;
using EstrichOne.Hosting.Infrastructure;
using Microsoft.Extensions.Options;

namespace EstrichOne.Hosting.Instances;

/// <summary>Erzeugt und pflegt die Dateien einer Instanz: Compose-Vorlagen, Hosting-Ergänzung, .env, Datenverzeichnisse.</summary>
public sealed partial class InstanceFiles(IOptions<HostingOptions> options, InstanceLayout layout, ILogger<InstanceFiles> logger)
{
    /// <summary>Benutzer „app“ im .NET-Container der Suite (API und Backup laufen ohne root).</summary>
    public const int ApiUid = 1654;

    private static readonly string[] Templates = ["compose.yml", "compose.override.yml", "compose.proxy.yml"];

    private HostingOptions Settings => options.Value;

    /// <summary>Legt Verzeichnis, Vorlagen, Hosting-Ergänzung und (nur beim ersten Mal) die .env mit neuen Passwörtern an.</summary>
    public async Task<bool> PrepareAsync(Tenant tenant, Plan plan, CancellationToken cancellationToken)
    {
        var directory = layout.Directory(tenant.Slug);
        Directory.CreateDirectory(directory);
        RestrictDirectory(directory);
        await CopyTemplatesAsync(tenant.Slug, cancellationToken);
        await WriteOverlayAsync(tenant, plan, cancellationToken);

        var envFile = Path.Combine(directory, ".env");
        var created = false;
        if (!File.Exists(envFile))
        {
            var content = BuildEnv(tenant, Secret(40), Secret(48), Settings);
            await WriteRestrictedAsync(envFile, content, cancellationToken);
            created = true;
        }
        else
        {
            await UpdateEnvAsync(tenant.Slug, EnvValuesFor(tenant, Settings), cancellationToken);
        }

        CreateDataDirectories(tenant.Slug);
        return created;
    }

    public async Task CopyTemplatesAsync(string slug, CancellationToken cancellationToken)
    {
        var directory = layout.Directory(slug);
        foreach (var name in Templates)
        {
            var source = Path.Combine(Settings.TemplateDirectory, name);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException($"Compose-Vorlage fehlt im Image: {source}");
            }

            var text = await File.ReadAllTextAsync(source, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, name), text, cancellationToken);
        }
    }

    public Task WriteOverlayAsync(Tenant tenant, Plan plan, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(Path.Combine(layout.Directory(tenant.Slug), "compose.hosting.yml"), BuildOverlay(tenant.Slug, plan), cancellationToken);

    /// <summary>
    /// Ergänzung für den Hosting-Betrieb: Caddy ohne veröffentlichte Ports, nur im eigenen Edge-Netz für den gemeinsamen
    /// Proxy erreichbar; Ressourcengrenzen des Tarifs für die API; Kennzeichnung aller Container mit dem Mandanten.
    /// </summary>
    public static string BuildOverlay(string slug, Plan plan)
    {
        var edge = InstanceLayout.EdgeNetwork(slug);
        var builder = new StringBuilder();
        builder.AppendLine("# Von der EstrichONE Hosting-Konsole erzeugt – Änderungen werden beim nächsten Vorgang überschrieben.");
        builder.AppendLine(CultureInfo.InvariantCulture, $"# Mandant: {slug} · Tarif: {Sanitize(plan.Name)}");
        builder.AppendLine();
        builder.AppendLine("x-tenant-labels: &tenant-labels");
        builder.AppendLine(CultureInfo.InvariantCulture, $"  {InstanceLayout.TenantLabel}: \"{slug}\"");
        builder.AppendLine();
        builder.AppendLine("services:");
        builder.AppendLine("  postgres:");
        builder.AppendLine("    labels: *tenant-labels");
        builder.AppendLine("  migrate:");
        builder.AppendLine("    labels: *tenant-labels");
        builder.AppendLine("  backup:");
        builder.AppendLine("    labels: *tenant-labels");
        builder.AppendLine("  api:");
        builder.AppendLine("    labels: *tenant-labels");
        if (plan.MemoryMb > 0)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"    mem_limit: {plan.MemoryMb}m");
        }

        if (plan.CpuLimit > 0)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"    cpus: {plan.CpuLimit.ToString("0.##", CultureInfo.InvariantCulture)}");
        }

        builder.AppendLine("  caddy:");
        builder.AppendLine("    labels: *tenant-labels");
        builder.AppendLine("    # Keine veröffentlichten Ports: Zugriff nur über den gemeinsamen Reverse Proxy.");
        builder.AppendLine("    ports: !reset []");
        builder.AppendLine("    networks: !override");
        builder.AppendLine("      frontend: {}");
        builder.AppendLine("      edge: {}");
        builder.AppendLine();
        builder.AppendLine("networks:");
        builder.AppendLine("  edge:");
        builder.AppendLine("    # Von der Konsole angelegt (intern, ohne Internetzugang); verbunden sind nur Proxy, Konsole und dieser Caddy.");
        builder.AppendLine(CultureInfo.InvariantCulture, $"    name: {edge}");
        builder.AppendLine("    external: true");
        return builder.ToString();
    }

    /// <summary>Werte, die sich aus den Stammdaten ergeben und bei Änderungen nachgeführt werden.</summary>
    public static IReadOnlyDictionary<string, string> EnvValuesFor(Tenant tenant, HostingOptions settings) => new Dictionary<string, string>
    {
        ["ESTRICHONE_VERSION"] = tenant.Version,
        ["ESTRICHONE_REGISTRY"] = settings.Registry,
        ["ESTRICHONE_DOMAIN"] = tenant.Domain,
        ["ESTRICHONE_PUBLIC_URL"] = PublicUrl(tenant.Domain, settings),
        ["ESTRICHONE_SERVER_NAME"] = $"\"{tenant.CompanyName}\"",
    };

    public static string PublicUrl(string domain, HostingOptions settings) =>
        (string.Equals(settings.Tls, "off", StringComparison.OrdinalIgnoreCase) ? "http://" : "https://") + domain;

    public static string BuildEnv(Tenant tenant, string postgresPassword, string resticPassword, HostingOptions settings)
    {
        var values = EnvValuesFor(tenant, settings);
        var backupDir = string.IsNullOrWhiteSpace(settings.BackupRoot) ? "./backup-repository" : Path.Combine(settings.BackupRoot, tenant.Slug);
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"# EstrichONE-Instanz „{tenant.Slug}“ – erzeugt von der Hosting-Konsole am {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC.");
        builder.AppendLine("# Enthält Passwörter: nur für root lesbar. Verwaltung über die Konsole; manuell: docker compose ps|logs");
        builder.AppendLine(CultureInfo.InvariantCulture, $"COMPOSE_PROJECT_NAME={InstanceLayout.ProjectName(tenant.Slug)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"COMPOSE_FILE={string.Join(':', InstanceLayout.ComposeFiles)}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"ESTRICHONE_VERSION={values["ESTRICHONE_VERSION"]}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"ESTRICHONE_REGISTRY={values["ESTRICHONE_REGISTRY"]}");
        builder.AppendLine("ESTRICHONE_MODE=proxy");
        builder.AppendLine(CultureInfo.InvariantCulture, $"ESTRICHONE_DOMAIN={values["ESTRICHONE_DOMAIN"]}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"ESTRICHONE_PUBLIC_URL={values["ESTRICHONE_PUBLIC_URL"]}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"ESTRICHONE_SERVER_NAME={values["ESTRICHONE_SERVER_NAME"]}");
        builder.AppendLine("ESTRICHONE_DATA_DIR=./data");
        builder.AppendLine(CultureInfo.InvariantCulture, $"ESTRICHONE_HTTP_PORT={InstanceLayout.InstanceHttpPort}");
        builder.AppendLine("ESTRICHONE_HTTPS_PORT=8143");
        builder.AppendLine("ESTRICHONE_TRUSTED_PROXIES=private_ranges");
        builder.AppendLine("POSTGRES_DB=estrichone");
        builder.AppendLine("POSTGRES_USER=estrichone");
        builder.AppendLine(CultureInfo.InvariantCulture, $"POSTGRES_PASSWORD={postgresPassword}");
        builder.AppendLine("# Schlüssel der Sicherungen – ohne ihn ist keine Wiederherstellung möglich.");
        builder.AppendLine(CultureInfo.InvariantCulture, $"RESTIC_PASSWORD={resticPassword}");
        builder.AppendLine(CultureInfo.InvariantCulture, $"ESTRICHONE_BACKUP_DIR={backupDir}");
        builder.AppendLine("BACKUP_REPOSITORY=/repository");
        builder.AppendLine("BACKUP_REPOSITORY_2=");
        builder.AppendLine("ESTRICHONE_SSH_DIR=./ssh");
        builder.AppendLine(CultureInfo.InvariantCulture, $"BACKUP_TIME={BackupTime(tenant.Slug)}");
        return builder.ToString();
    }

    /// <summary>Verteilt die nächtlichen Sicherungen gleichmäßig zwischen 21:00 und 04:59 (stabil je Kürzel).</summary>
    public static string BackupTime(string slug)
    {
        var minutes = (int)(BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(slug)), 0) % (8 * 60));
        var total = (21 * 60 + minutes) % (24 * 60);
        return string.Create(CultureInfo.InvariantCulture, $"{total / 60:00}:{total % 60:00}");
    }

    public static string Secret(int length) =>
        RandomNumberGenerator.GetString("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789", length);

    public async Task<IReadOnlyDictionary<string, string>> ReadEnvAsync(string slug, CancellationToken cancellationToken)
    {
        var file = Path.Combine(layout.Directory(slug), ".env");
        return File.Exists(file) ? ParseEnv(await File.ReadAllTextAsync(file, cancellationToken)) : new Dictionary<string, string>();
    }

    public static Dictionary<string, string> ParseEnv(string content)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var index = line.IndexOf('=', StringComparison.Ordinal);
            if (index > 0)
            {
                var value = line[(index + 1)..];
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                {
                    value = value[1..^1];
                }

                values[line[..index]] = value;
            }
        }

        return values;
    }

    /// <summary>Ersetzt bzw. ergänzt Zeilen der .env; alle anderen Zeilen (Passwörter, Kommentare) bleiben unverändert.</summary>
    public async Task UpdateEnvAsync(string slug, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken)
    {
        var file = Path.Combine(layout.Directory(slug), ".env");
        var lines = (await File.ReadAllTextAsync(file, cancellationToken)).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        foreach (var (key, value) in values)
        {
            var index = lines.FindIndex(l => l.StartsWith(key + "=", StringComparison.Ordinal));
            if (index >= 0)
            {
                lines[index] = $"{key}={value}";
            }
            else
            {
                lines.Add($"{key}={value}");
            }
        }

        await WriteRestrictedAsync(file, string.Join('\n', lines) + "\n", cancellationToken);
    }

    /// <summary>Datenverzeichnisse wie <c>install.sh</c>; Verzeichnisse von API und Backup gehören UID 1654.</summary>
    public void CreateDataDirectories(string slug)
    {
        var data = layout.DataDirectory(slug);
        var directory = layout.Directory(slug);
        var appDirectories = new[]
        {
            Path.Combine(data, "files"), Path.Combine(data, "backup-status"), Path.Combine(data, "backup-staging"),
            Path.Combine(data, "restic-cache"), layout.BackupDirectory(slug),
        };
        foreach (var path in new[] { Path.Combine(data, "postgres"), Path.Combine(data, "caddy", "data"), Path.Combine(data, "caddy", "config"), Path.Combine(directory, "ssh") }.Concat(appDirectories))
        {
            Directory.CreateDirectory(path);
        }

        foreach (var path in appDirectories)
        {
            ChownRecursive(path, ApiUid);
        }
    }

    /// <summary>Löscht das Instanzverzeichnis vollständig (nach <c>docker compose down</c>).</summary>
    public void DeleteDirectory(string slug)
    {
        var directory = layout.Directory(slug);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        var backup = layout.BackupDirectory(slug);
        if (Directory.Exists(backup))
        {
            Directory.Delete(backup, recursive: true);
        }
    }

    /// <summary>
    /// Endsicherung aufbewahren: Sicherungs-Repository und .env (enthält den Schlüssel) nach
    /// <c>archive/&lt;kürzel&gt;-&lt;zeitpunkt&gt;</c> verschieben. Gibt den Archivpfad zurück.
    /// </summary>
    public string Archive(string slug, DateTime now)
    {
        var target = Path.Combine(Settings.ArchiveDirectory, $"{slug}-{now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(target);
        RestrictDirectory(Settings.ArchiveDirectory);
        RestrictDirectory(target);
        var backup = layout.BackupDirectory(slug);
        if (Directory.Exists(backup))
        {
            MoveDirectory(backup, Path.Combine(target, "backup-repository"));
        }

        var env = Path.Combine(layout.Directory(slug), ".env");
        if (File.Exists(env))
        {
            File.Copy(env, Path.Combine(target, "instanz.env"), overwrite: true);
        }

        foreach (var name in Templates.Append("compose.hosting.yml"))
        {
            var file = Path.Combine(layout.Directory(slug), name);
            if (File.Exists(file))
            {
                File.Copy(file, Path.Combine(target, name), overwrite: true);
            }
        }

        File.WriteAllText(Path.Combine(target, "LIESMICH.txt"),
            $"Endsicherung der EstrichONE-Instanz „{slug}“ vom {now:yyyy-MM-dd HH:mm} UTC.\n" +
            "backup-repository: restic-Repository, Schlüssel = RESTIC_PASSWORD in instanz.env.\n" +
            "Wiederherstellung: Instanz neu anlegen, Repository und .env-Werte übernehmen, dann\n" +
            "  docker compose stop api && docker compose --profile restore run --rm restore latest\n");
        return target;
    }

    private static void MoveDirectory(string source, string target)
    {
        try
        {
            Directory.Move(source, target);
        }
        catch (IOException)
        {
            // Anderes Dateisystem (z. B. BackupRoot auf zweiter Platte): kopieren und Quelle löschen.
            CopyDirectory(source, target);
            Directory.Delete(source, recursive: true);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }

    private static async Task WriteRestrictedAsync(string file, string content, CancellationToken cancellationToken)
    {
        var temp = file + ".tmp";
        await File.WriteAllTextAsync(temp, content, new UTF8Encoding(false), cancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temp, file, overwrite: true);
    }

    private static void RestrictDirectory(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    private void ChownRecursive(string path, int uid)
    {
        if (OperatingSystem.IsWindows() || NativeMethods.geteuid() != 0)
        {
            // Ohne root (Tests, Entwicklung) ist kein Besitzerwechsel möglich – die Simulation braucht ihn nicht.
            if (!Settings.IsFake)
            {
                logger.LogWarning("Konsole läuft nicht als root: {Path} kann nicht UID {Uid} übergeben werden.", path, uid);
            }

            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories).Prepend(path))
        {
            if (NativeMethods.lchown(entry, uid, uid) != 0)
            {
                throw new IOException($"Besitzer von {entry} konnte nicht geändert werden (errno {Marshal.GetLastPInvokeError()}).");
            }
        }
    }

    private static string Sanitize(string text) => new(text.Where(c => !char.IsControl(c)).ToArray());

    private static partial class NativeMethods
    {
        [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial int lchown(string path, int owner, int group);

        [LibraryImport("libc")]
        internal static partial uint geteuid();
    }
}
