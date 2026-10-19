namespace EstrichOne.Api.Modules.Documentation;

/// <summary>Abschnitt <c>EstrichOne:Files</c>.</summary>
public sealed class FileStorageSettings
{
    public const string SectionName = "EstrichOne:Files";

    /// <summary>Ablageort (im Docker-Stack ein Host-Verzeichnis, das ins Backup gehört).</summary>
    public string RootPath { get; set; } = "/data/files";

    public long MaxPhotoBytes { get; set; } = 50L * 1024 * 1024;
    public long MaxDocumentBytes { get; set; } = 100L * 1024 * 1024;
    public long MaxVideoBytes { get; set; } = 500L * 1024 * 1024;

    public long MaxBytesFor(FileKind kind) => kind switch
    {
        FileKind.Photo => MaxPhotoBytes,
        FileKind.Video => MaxVideoBytes,
        _ => MaxDocumentBytes,
    };
}

/// <summary>
/// Dateiablage auf dem lokalen Dateisystem:
/// <c>originals/JJJJ/MM/&lt;id&gt;</c> (unverändert, ohne Endung), <c>variants/&lt;id&gt;_&lt;variante&gt;.webp</c>,
/// <c>partial/&lt;id&gt;</c> für laufende Uploads. Dateinamen der Nutzer erscheinen nie im Dateisystem.
/// </summary>
public sealed class LocalFileStorage(Microsoft.Extensions.Options.IOptions<FileStorageSettings> options)
{
    private string Root => options.Value.RootPath;

    public static string OriginalKey(Guid id, DateTimeOffset createdAt) =>
        $"originals/{createdAt:yyyy}/{createdAt:MM}/{id:N}";

    public string PathOf(string storageKey) => Path.Combine(Root, storageKey);

    public string PartialPath(Guid id) => Path.Combine(Root, "partial", id.ToString("N"));

    public string VariantPath(Guid id, string variant) => Path.Combine(Root, "variants", $"{id:N}_{variant}.webp");

    public long PartialLength(Guid id)
    {
        var info = new FileInfo(PartialPath(id));
        return info.Exists ? info.Length : 0;
    }

    /// <summary>Hängt ein Teilstück an den laufenden Upload an.</summary>
    public async Task<long> AppendAsync(Guid id, Stream content, long maxTotal, CancellationToken cancellationToken)
    {
        var path = PartialPath(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var target = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (target.Length + read > maxTotal)
            {
                throw new FileTooLargeException();
            }

            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await target.FlushAsync(cancellationToken);
        return target.Length;
    }

    public void DiscardPartial(Guid id) => File.Delete(PartialPath(id));

    /// <summary>Verschiebt den vollständigen Upload atomar an seinen endgültigen Ort.</summary>
    public void Commit(Guid id, string storageKey)
    {
        var target = PathOf(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Move(PartialPath(id), target, overwrite: true);
    }

    public async Task WriteVariantAsync(Guid id, string variant, byte[] content, CancellationToken cancellationToken)
    {
        var path = VariantPath(id, variant);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, content, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }
}

public sealed class FileTooLargeException() : Exception("Die Datei ist größer als angekündigt bzw. erlaubt.");
