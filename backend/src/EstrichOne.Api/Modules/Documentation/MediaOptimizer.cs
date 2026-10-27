using System.Text;
using EstrichOne.Api.Modules.Organization.Exports;
using NetVips;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace EstrichOne.Api.Modules.Documentation;

/// <summary>Ergebnis einer Verdichtung: neuer Inhalt und was getan wurde (für Protokoll und Statistik).</summary>
public sealed record OptimizedContent(byte[] Content, string ContentType, string Note);

/// <summary>
/// Speicherschonend, aber scharf: Fotos (JPEG) werden mit mozjpeg-Einstellungen neu kodiert (Trellis-Quantisierung,
/// optimierte Huffman-Tabellen, progressive Scans), die EXIF-Ausrichtung angewendet und zu große Fotos auf die eingestellte
/// lange Kante verkleinert (Lanczos). EXIF (Aufnahmezeit, GPS), XMP und Farbprofil bleiben erhalten. PDFs werden verlustfrei
/// neu geschrieben (komprimierte Streams, nur erreichbare Objekte) – nie signierte oder verschlüsselte PDFs.
/// Ein Ergebnis zählt nur, wenn es spürbar kleiner ist; sonst bleibt das Original unverändert.
/// </summary>
public sealed partial class MediaOptimizer(ILogger<MediaOptimizer> logger)
{
    /// <summary>Mindestens 8 % kleiner, sonst lohnt die Neukodierung nicht (und das Original bleibt bitgenau).</summary>
    private const double MinimumSaving = 0.08;

    public OptimizedContent? Optimize(byte[] original, string detectedContentType, FileKind kind, MediaSettings settings)
    {
        try
        {
            return (kind, detectedContentType) switch
            {
                (FileKind.Photo, "image/jpeg") when !settings.KeepOriginalPhotos => OptimizeJpeg(original, settings),
                (FileKind.Document or FileKind.Photo, "application/pdf") when settings.OptimizePdfs => OptimizePdf(original),
                _ => null,
            };
        }
#pragma warning disable CA1031 // Fremde Bibliotheken (libvips, PDFsharp) werfen vielerlei; jedes Problem heißt: Original behalten.
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            // Verdichtung ist Zusatz: im Zweifel das Original unverändert speichern.
            LogSkipped(logger, detectedContentType, exception.Message);
            return null;
        }
    }

    internal static OptimizedContent? OptimizeJpeg(byte[] original, MediaSettings settings)
    {
        using var header = Image.NewFromBuffer(original, access: Enums.Access.Sequential);
        var orientation = header.Contains("orientation") ? (int)header.Get("orientation") : 1;
        var longEdge = Math.Max(header.Width, header.Height);
        var resize = longEdge > settings.PhotoMaxEdge;

        // Thumbnail dreht nach EXIF und verkleinert in einem Schritt (Lanczos3, mit Vorab-Verkleinerung beim Dekodieren).
        using var image = resize
            ? Image.ThumbnailBuffer(original, settings.PhotoMaxEdge, height: settings.PhotoMaxEdge, size: Enums.Size.Down)
            : orientation is > 1 and <= 8 ? Image.NewFromBuffer(original).Autorot() : Image.NewFromBuffer(original);
        var encoded = image.JpegsaveBuffer(q: settings.PhotoQuality, optimizeCoding: true, interlace: true, trellisQuant: true,
            overshootDeringing: true, optimizeScans: true, quantTable: 3, subsampleMode: Enums.ForeignSubsample.Auto,
            keep: Enums.ForeignKeep.Exif | Enums.ForeignKeep.Xmp | Enums.ForeignKeep.Icc);

        if (!resize && orientation <= 1 && encoded.Length > original.Length * (1 - MinimumSaving))
        {
            return null;
        }

        var note = resize ? $"Foto {header.Width}×{header.Height} → {image.Width}×{image.Height}, JPEG q{settings.PhotoQuality}"
            : $"Foto neu kodiert (JPEG q{settings.PhotoQuality}{(orientation > 1 ? ", gedreht" : string.Empty)})";
        return new OptimizedContent(encoded, "image/jpeg", note);
    }

    internal static OptimizedContent? OptimizePdf(byte[] original)
    {
        // Signaturen (inkrementelle Aktualisierungen) und Verschlüsselung niemals anfassen.
        var text = Encoding.Latin1.GetString(original);
        if (text.Contains("/ByteRange", StringComparison.Ordinal) || text.Contains("/Encrypt", StringComparison.Ordinal)
            || text.Contains("/XFA", StringComparison.Ordinal) || text.Contains("/Sig", StringComparison.Ordinal))
        {
            return null;
        }

        int pages;
        byte[] rewritten;
        using (var input = new MemoryStream(original, writable: false))
        using (var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify))
        {
            pages = document.PageCount;
            // Unkomprimierte Streams (Scanner, ältere Programme) verlustfrei mit Flate packen; Gefiltertes bleibt bitgenau.
            foreach (var item in document.Internals.GetAllObjects())
            {
                if (item is PdfDictionary { Stream: { } stream } dictionary && !dictionary.Elements.ContainsKey("/Filter") && stream.Value.Length > 64)
                {
                    var packed = PdfSharp.Pdf.Filters.Filtering.FlateDecode.Encode(stream.Value, PdfFlateEncodeMode.BestCompression);
                    if (packed.Length < stream.Value.Length)
                    {
                        stream.Value = packed;
                        dictionary.Elements.SetName("/Filter", "/FlateDecode");
                    }
                }
            }

            document.Options.CompressContentStreams = true;
            document.Options.NoCompression = false;
            document.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Never;
            using var output = new MemoryStream();
            document.Save(output, closeStream: false);
            rewritten = output.ToArray();
        }

        if (rewritten.Length > original.Length * (1 - MinimumSaving))
        {
            return null;
        }

        // Gegenprobe: das Ergebnis muss sich öffnen lassen und gleich viele Seiten haben.
        using (var check = new MemoryStream(rewritten, writable: false))
        using (var reopened = PdfReader.Open(check, PdfDocumentOpenMode.Import))
        {
            if (reopened.PageCount != pages)
            {
                return null;
            }
        }

        return new OptimizedContent(rewritten, "application/pdf", "PDF verlustfrei verdichtet");
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Verdichtung übersprungen ({ContentType}): {Reason}")]
    private static partial void LogSkipped(ILogger logger, string contentType, string reason);
}
