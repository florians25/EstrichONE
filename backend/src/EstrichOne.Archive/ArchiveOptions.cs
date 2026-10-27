namespace EstrichOne.Archive;

public enum ServerRole
{
    /// <summary>Revisionssicheres Archiv: unveränderbare Objekte, Löschsperre, Hash-Kette, Prüfprotokoll, Export.</summary>
    Archive,

    /// <summary>Backup-Ziel: restic-REST-Server (nur anhängen) hinter derselben Kopplung und demselben Zertifikat.</summary>
    BackupTarget,
}

/// <summary>Konfiguration über Umgebungsvariablen (<c>ESTRICHONE_…</c>), damit ein einziger <c>docker run</c> genügt.</summary>
public sealed class ArchiveOptions
{
    public required string DataPath { get; init; }
    public ServerRole Role { get; init; }
    public int Port { get; init; } = 8443;

    /// <summary>Anzeigename, z. B. „Archiv Büro Keller“.</summary>
    public string Name { get; init; } = "EstrichONE";

    /// <summary>Zusätzliche Namen/IP-Adressen für das Zertifikat (die beim Koppeln benutzte Adresse kommt automatisch dazu).</summary>
    public IReadOnlyList<string> Addresses { get; init; } = [];

    /// <summary>Mindestfrist in Jahren, die jede Archivierung einhalten muss (Schutz gegen zu kurze Fristen im Client).</summary>
    public int MinRetentionYears { get; init; }

    public long MaxObjectBytes { get; init; } = 4L * 1024 * 1024 * 1024;
    public TimeSpan PairingCodeLifetime { get; init; } = TimeSpan.FromHours(24);
    public TimeSpan VerificationInterval { get; init; } = TimeSpan.FromHours(24);
    public string RestServerPath { get; init; } = "/usr/local/bin/rest-server";
    public int RestServerPort { get; init; } = 8000;
    public bool AppendOnly { get; init; } = true;

    /// <summary>Backup-Ziel, das zieht: Adresse des EstrichONE-Servers (z. B. https://estrichone.firma.de), leer = aus.</summary>
    public string? PullFrom { get; init; }

    /// <summary>Einmal-Code aus dem Backup Center (nur für die erste Kopplung).</summary>
    public string? PullCode { get; init; }

    /// <summary>Optional: SHA-256-Fingerabdruck eines Zertifikats der Kette (eigenes Zertifikat statt öffentlicher CA).</summary>
    public string? PullFingerprint { get; init; }

    public TimeSpan PullInterval { get; init; } = TimeSpan.FromMinutes(60);

    public string RoleName => Role == ServerRole.Archive ? "archive" : "backup-target";
    public string RoleTitle => Role == ServerRole.Archive ? "Archivserver" : "Backup-Ziel";

    public static ArchiveOptions FromEnvironment(Func<string, string?>? get = null)
    {
        get ??= Environment.GetEnvironmentVariable;
        static int Int(string? value, int fallback) => int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        var role = (get("ESTRICHONE_ROLE") ?? "archive").Trim().ToLowerInvariant() switch
        {
            "archive" or "archiv" => ServerRole.Archive,
            "backup-target" or "backup" or "backupziel" => ServerRole.BackupTarget,
            var other => throw new InvalidOperationException($"Unbekannte Rolle ESTRICHONE_ROLE={other} (archive oder backup-target)."),
        };

        return new ArchiveOptions
        {
            DataPath = get("ESTRICHONE_DATA") ?? "/data",
            Role = role,
            Port = Int(get("ESTRICHONE_PORT"), 8443),
            Name = get("ESTRICHONE_NAME") is { Length: > 0 } name ? name : (role == ServerRole.Archive ? "EstrichONE-Archiv" : "EstrichONE-Backup-Ziel"),
            Addresses = (get("ESTRICHONE_ADDRESS") ?? string.Empty)
                .Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            MinRetentionYears = Math.Clamp(Int(get("ESTRICHONE_MIN_RETENTION_YEARS"), 0), 0, 30),
            AppendOnly = !string.Equals(get("ESTRICHONE_APPEND_ONLY"), "false", StringComparison.OrdinalIgnoreCase),
            RestServerPath = get("ESTRICHONE_REST_SERVER") ?? "/usr/local/bin/rest-server",
            PullFrom = get("ESTRICHONE_PULL_FROM") is { Length: > 0 } from ? from.Trim().TrimEnd('/') : null,
            PullCode = get("ESTRICHONE_PULL_CODE") is { Length: > 0 } code ? code.Trim() : null,
            PullFingerprint = get("ESTRICHONE_PULL_FINGERPRINT") is { Length: > 0 } fp ? fp : null,
            PullInterval = TimeSpan.FromMinutes(Math.Clamp(Int(get("ESTRICHONE_PULL_INTERVAL_MINUTES"), 60), 5, 1440)),
        };
    }

    public string PathOf(params string[] parts) => Path.Combine([DataPath, .. parts]);
}
