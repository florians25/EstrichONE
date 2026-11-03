using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;

namespace EstrichOne.Hosting.Recovery;

/// <summary>Ein Bestandteil eines Exports: Datei oder Verzeichnis und sein Name im Archiv.</summary>
public sealed record ExportPart(string Source, string EntryName);

/// <summary>
/// Einmal-Downloads für Exporte (tar.gz mit Sicherungs-Repository und Schlüsseldatei). Ausgegeben nur nach erneuter
/// Passwortabfrage; der Link gilt 10 Minuten und einmal.
/// </summary>
public sealed class Downloads(TimeProvider clock)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<string, (string FileName, IReadOnlyList<ExportPart> Parts, DateTimeOffset Expires, string Actor)> tickets = new(StringComparer.Ordinal);

    public string Issue(string fileName, IReadOnlyList<ExportPart> parts, string actor)
    {
        foreach (var expired in tickets.Where(t => t.Value.Expires < clock.GetUtcNow()).Select(t => t.Key).ToList())
        {
            tickets.TryRemove(expired, out _);
        }

        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        tickets[token] = (fileName, parts, clock.GetUtcNow() + Lifetime, actor);
        return token;
    }

    public (string FileName, IReadOnlyList<ExportPart> Parts, string Actor)? Redeem(string token)
    {
        if (!tickets.TryRemove(token, out var ticket) || ticket.Expires < clock.GetUtcNow())
        {
            return null;
        }

        return (ticket.FileName, ticket.Parts, ticket.Actor);
    }

    /// <summary>Schreibt alle Teile als tar.gz in den Ausgabestrom (Verzeichnisse rekursiv, ohne Symlinks zu folgen).</summary>
    public static async Task WriteTarGzAsync(Stream output, IReadOnlyList<ExportPart> parts, CancellationToken cancellationToken)
    {
        await using var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true);
        await using var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true);
        foreach (var part in parts)
        {
            if (File.Exists(part.Source))
            {
                await tar.WriteEntryAsync(part.Source, part.EntryName, cancellationToken);
            }
            else if (Directory.Exists(part.Source))
            {
                foreach (var file in Directory.EnumerateFiles(part.Source, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint }))
                {
                    var relative = Path.GetRelativePath(part.Source, file).Replace('\\', '/');
                    await tar.WriteEntryAsync(file, $"{part.EntryName}/{relative}", cancellationToken);
                }
            }
        }
    }

    public static long SizeOf(string path)
    {
        if (File.Exists(path))
        {
            return new FileInfo(path).Length;
        }

        return Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true })
                .Sum(f => new FileInfo(f).Length)
            : 0;
    }
}
