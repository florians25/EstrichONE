using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EstrichOne.Api.Modules.Backup.Exports;
using Microsoft.Extensions.Options;

namespace EstrichOne.Api.Modules.Backup;

/// <summary>Anforderungen an den Backup-Dienst für ausgelagerte Originale (deploy/backup/estrichone-backup: offload-backup, offload-recover).</summary>
internal sealed partial class OffloadBackup(IOptions<BackupSettings> options) : IOffloadBackup
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string? Dir => string.IsNullOrWhiteSpace(options.Value.StatusPath) || !Directory.Exists(options.Value.StatusPath) ? null : options.Value.StatusPath;

    public bool Available => Dir is not null;

    public bool RequestCopy(string requestId, IReadOnlyCollection<string> relativePaths) => Write("offload-backup", requestId, relativePaths);

    public OffloadBackupResult? CopyResult(string requestId) => Read("offload-backup", requestId);

    public bool RequestRecovery(string requestId, IReadOnlyCollection<string> relativePaths) => Write("offload-recover", requestId, relativePaths);

    public OffloadBackupResult? RecoveryResult(string requestId) => Read("offload-recover", requestId);

    private bool Write(string kind, string requestId, IReadOnlyCollection<string> paths)
    {
        if (Dir is not { } dir || !IdPattern().IsMatch(requestId) || paths.Count == 0 || paths.Any(p => !PathPattern().IsMatch(p)))
        {
            return false;
        }

        var requests = Path.Combine(dir, "requests");
        Directory.CreateDirectory(requests);
        var target = Path.Combine(requests, kind + ".list");
        if (File.Exists(target))
        {
            return false;
        }

        var text = new StringBuilder().Append("id=").Append(requestId).Append('\n');
        foreach (var path in paths)
        {
            text.Append(path).Append('\n');
        }

        var temp = target + ".tmp";
        File.WriteAllText(temp, text.ToString());
        File.Move(temp, target);
        return true;
    }

    private OffloadBackupResult? Read(string kind, string requestId)
    {
        if (Dir is not { } dir)
        {
            return null;
        }

        var result = Path.Combine(dir, kind + ".json");
        try
        {
            if (File.Exists(result) && JsonSerializer.Deserialize<OffloadBackupResult>(File.ReadAllText(result), Json) is { } parsed
                && parsed.RequestId == requestId)
            {
                return parsed;
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            // Datei wird gerade geschrieben – beim nächsten Abruf erneut.
        }

        var request = Path.Combine(dir, "requests", kind + ".list");
        return File.Exists(request) && File.ReadLines(request).FirstOrDefault() == $"id={requestId}"
            ? new OffloadBackupResult(requestId, "requested", null, File.GetLastWriteTimeUtc(request), null)
            : null;
    }

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^originals/[0-9]{4}/[0-9]{2}/[0-9a-f]{32}$")]
    private static partial Regex PathPattern();
}
