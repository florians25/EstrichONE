using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace EstrichOne.Archive;

/// <summary>
/// Backup-Ziel, das zieht (zuhause hinter NAT, ohne feste IP): verbindet sich ausgehend mit dem EstrichONE-Server,
/// lädt neue Dateien des verschlüsselten restic-Repositorys und legt sie unter <c>/data/mirror</c> ab. Nur anhängen:
/// es wird nie etwas gelöscht oder überschrieben; jede Datei wird gegen ihren Namen (SHA-256) geprüft. Gegenstück:
/// backend/src/EstrichOne.Api/Modules/Backup/BackupMirror.cs. Der Server kennt danach Stand, Plattenplatz und Erreichbarkeit.
/// </summary>
public sealed partial class MirrorPuller(ArchiveOptions options, ILogger<MirrorPuller> logger) : BackgroundService
{
    public const string ConfigFile = "pull.json";
    private static readonly string[] Types = ["keys", "data", "index", "snapshots"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string MirrorPath => options.PathOf("mirror");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = options.PullInterval;
            try
            {
                var config = await ConfigAsync(stoppingToken);
                if (config is null)
                {
                    wait = TimeSpan.FromMinutes(5);
                }
                else
                {
                    await SyncAsync(config, stoppingToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception.Message);
                wait = TimeSpan.FromMinutes(5);
            }

            try
            {
                await Task.Delay(wait, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<PullConfig?> ConfigAsync(CancellationToken cancellationToken)
    {
        var file = options.PathOf(ConfigFile);
        if (File.Exists(file))
        {
            var saved = JsonSerializer.Deserialize<PullConfig>(await File.ReadAllTextAsync(file, cancellationToken), Json);
            if (saved is not null && (options.PullFrom is null || saved.Server == options.PullFrom))
            {
                return saved;
            }
        }

        if (options.PullFrom is null || options.PullCode is null)
        {
            LogNoCode(logger);
            return null;
        }

        using var http = Client(options.PullFrom, null);
        using var response = await http.PostAsJsonAsync("/api/v1/backup/mirror/pair", new { code = options.PullCode, name = options.Name }, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            LogPairFailed(logger, (int)response.StatusCode);
            return null;
        }

        var paired = await response.Content.ReadFromJsonAsync<PairResult>(Json, cancellationToken)
            ?? throw new InvalidOperationException("Leere Antwort beim Koppeln.");
        var config = new PullConfig(options.PullFrom, paired.Id, paired.Token);
        await File.WriteAllTextAsync(file + ".tmp", JsonSerializer.Serialize(config, Json), cancellationToken);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file + ".tmp", UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(file + ".tmp", file, overwrite: true);
        LogPaired(logger, options.PullFrom);
        return config;
    }

    internal async Task<int> SyncAsync(PullConfig config, CancellationToken cancellationToken)
    {
        using var http = Client(config.Server, config.Token);
        var copied = 0;
        string? error = null;
        try
        {
            Directory.CreateDirectory(MirrorPath);
            var configPath = Path.Combine(MirrorPath, "config");
            if (!File.Exists(configPath))
            {
                var bytes = await http.GetByteArrayAsync(new Uri("/api/v1/backup/mirror/repo/config", UriKind.Relative), cancellationToken);
                await File.WriteAllBytesAsync(configPath, bytes, cancellationToken);
                copied++;
            }

            foreach (var type in Types)
            {
                var remote = await http.GetFromJsonAsync<List<RemoteFile>>(new Uri($"/api/v1/backup/mirror/repo/{type}/", UriKind.Relative), Json, cancellationToken) ?? [];
                foreach (var file in remote.Where(f => f.Name.Length == 64 && f.Name.All(char.IsAsciiHexDigitLower)))
                {
                    var local = type == "data" ? Path.Combine(MirrorPath, "data", file.Name[..2], file.Name) : Path.Combine(MirrorPath, type, file.Name);
                    if (File.Exists(local))
                    {
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                    var temp = local + ".tmp";
                    await using (var target = File.Create(temp))
                    {
                        await using var source = await http.GetStreamAsync(new Uri($"/api/v1/backup/mirror/repo/{type}/{file.Name}", UriKind.Relative), cancellationToken);
                        await source.CopyToAsync(target, cancellationToken);
                    }

                    // restic benennt jede Datei nach dem SHA-256 ihres Inhalts – Übertragungsfehler fallen so sofort auf.
                    await using (var check = File.OpenRead(temp))
                    {
                        if (Convert.ToHexStringLower(await SHA256.HashDataAsync(check, cancellationToken)) != file.Name)
                        {
                            File.Delete(temp);
                            throw new InvalidDataException($"Prüfsumme von {type}/{file.Name} stimmt nicht.");
                        }
                    }

                    File.Move(temp, local);
                    copied++;
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or IOException or TaskCanceledException)
        {
            error = exception.Message;
        }

        var snapshots = Directory.Exists(Path.Combine(MirrorPath, "snapshots")) ? Directory.GetFiles(Path.Combine(MirrorPath, "snapshots")) : [];
        var bytes2 = Directory.Exists(MirrorPath) ? Directory.EnumerateFiles(MirrorPath, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;
        DateTimeOffset? last = snapshots.Length == 0 ? null : snapshots.Max(f => new DateTimeOffset(File.GetLastWriteTimeUtc(f), TimeSpan.Zero));
        long? free = null, total = null;
        try
        {
            var drive = new DriveInfo(options.DataPath);
            free = drive.AvailableFreeSpace;
            total = drive.TotalSize;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
        }

        try
        {
            using var report = await http.PostAsJsonAsync("/api/v1/backup/mirror/report", new
            {
                snapshots = snapshots.Length, bytes = bytes2, lastSnapshotAt = last, diskFreeBytes = free, diskTotalBytes = total,
                version = ArchiveServer.Version, error, filesCopied = copied,
            }, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            error ??= exception.Message;
        }

        if (error is null)
        {
            LogSynced(logger, copied, snapshots.Length);
        }
        else
        {
            LogFailed(logger, error);
        }

        return copied;
    }

    private HttpClient Client(string server, string? token)
    {
        var handler = new HttpClientHandler();
        if (options.PullFingerprint is { } fp)
        {
            var expected = new string([.. fp.Where(char.IsAsciiHexDigit)]).ToLowerInvariant();
            handler.ServerCertificateCustomValidationCallback = (_, certificate, chain, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None
                || (certificate is not null && (Convert.ToHexStringLower(SHA256.HashData(certificate.RawData)) == expected
                    || (chain?.ChainElements.Any(e => Convert.ToHexStringLower(SHA256.HashData(e.Certificate.RawData)) == expected) ?? false)));
        }

        var http = new HttpClient(handler, disposeHandler: true) { BaseAddress = new Uri(server.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(30) };
        if (token is not null)
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return http;
    }

    internal sealed record PullConfig(string Server, string Id, string Token);

    private sealed record PairResult(string Id, string Token);

    private sealed record RemoteFile(string Name, long Size);

    [LoggerMessage(Level = LogLevel.Information, Message = "Spiegel gekoppelt mit {Server}.")]
    private static partial void LogPaired(ILogger logger, string server);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kopplung mit dem Server fehlgeschlagen (HTTP {Status}) – Code im Backup Center neu erzeugen und ESTRICHONE_PULL_CODE setzen.")]
    private static partial void LogPairFailed(ILogger logger, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Spiegel: kein Kopplungscode (ESTRICHONE_PULL_FROM und ESTRICHONE_PULL_CODE setzen).")]
    private static partial void LogNoCode(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Spiegel abgeglichen: {Copied} neue Datei(en), {Snapshots} Sicherung(en).")]
    private static partial void LogSynced(ILogger logger, int copied, int snapshots);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Spiegel: {Error}")]
    private static partial void LogFailed(ILogger logger, string error);
}
