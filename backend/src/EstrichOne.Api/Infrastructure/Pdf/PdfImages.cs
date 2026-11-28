using MigraDoc.DocumentObjectModel;
using NetVips;

namespace EstrichOne.Api.Infrastructure.Pdf;

/// <summary>Für das PDF vorbereitetes Bild: Inhalt (JPEG oder PNG) und Pixelmaße nach dem Aufrichten.</summary>
public sealed record PdfImage(byte[] Content, int Width, int Height);

/// <summary>Ergebnis der Bildvorbereitung: Bild oder ein verständlicher Hinweis, warum keins vorliegt.</summary>
public sealed record PdfImageResult(PdfImage? Image, string? Hint)
{
    public static PdfImageResult Missing(string hint) => new(null, hint);
}

/// <summary>
/// Bildvorbereitung für alle PDF-Ausdrucke (libvips):
/// <list type="bullet">
/// <item>Fotos werden nach EXIF aufgerichtet (Ausrichtung 1 bis 8), nach sRGB mit 8 Bit gewandelt (CMYK, 16 Bit, Graustufen mit Alpha),
/// Transparenz auf Weiß gelegt und als JPEG ohne Metadaten mit höchstens <see cref="PhotoEdge"/> Pixeln Kantenlänge geliefert.</item>
/// <item>Logos bleiben PNG mit Transparenz, werden aber auf 8 Bit sRGB(A) gebracht, damit PDFsharp sie sicher einbettet.</item>
/// <item>HEIC (iPhone) kann die mitgelieferte libvips nicht dekodieren (HEVC-Patente); dafür gibt es einen Hinweis statt eines Fehlers.</item>
/// </list>
/// Gesetzt wird stets in eine Box (Breite und Höhe in cm) mit erhaltenem Seitenverhältnis, damit nichts verzerrt wird oder über den Rand läuft.
/// </summary>
public static class PdfImages
{
    /// <summary>Lange Kante eines Fotos im PDF: rund 350 dpi bei 8 cm Breite, scharf im Druck und trotzdem klein.</summary>
    public const int PhotoEdge = 1400;

    public const string HeicHint = "Foto im Format HEIC (iPhone). Der Server kann daraus keine Vorschau erzeugen. Bitte das Foto in der Bauakte öffnen oder als JPEG hochladen.";

    public const string UnreadableHint = "Foto nicht lesbar. Bitte das Original in der Bauakte prüfen.";

    /// <summary>Foto (JPEG, PNG, WebP, AVIF, HEIC sofern dekodierbar) aufrichten, verkleinern und als JPEG liefern.</summary>
    public static PdfImageResult PreparePhoto(byte[] content, int maxEdge = PhotoEdge)
    {
        if (IsHeic(content) && !CanDecode(content))
        {
            return PdfImageResult.Missing(HeicHint);
        }

        if (!Media.ImageSafety.IsWithinLimits(content))
        {
            return PdfImageResult.Missing(IsHeic(content) ? HeicHint : UnreadableHint);
        }

        try
        {
            // Thumbnail richtet nach EXIF auf (auch 5 und 7, gespiegelt) und verkleinert beim Dekodieren.
            using var thumb = Image.ThumbnailBuffer(content, maxEdge, height: maxEdge, size: Enums.Size.Down);
            using var srgb = ToSrgb(thumb);
            using var flat = srgb.HasAlpha() ? srgb.Flatten(background: [255, 255, 255]) : srgb.Copy();
            using var rgb = flat.Bands == 3 ? flat.Copy() : flat.Bands == 1 ? flat.Bandjoin([flat, flat]) : flat.ExtractBand(0, n: 3);
            var jpeg = WithJfifHeader(rgb.JpegsaveBuffer(q: 82, optimizeCoding: true, keep: Enums.ForeignKeep.None));
            return new PdfImageResult(new PdfImage(jpeg, rgb.Width, rgb.Height), null);
        }
        catch (VipsException)
        {
            return PdfImageResult.Missing(IsHeic(content) ? HeicHint : UnreadableHint);
        }
    }

    /// <summary>Logo als PNG mit 8 Bit sRGB(A); Transparenz bleibt erhalten. <c>null</c>, wenn nicht lesbar.</summary>
    public static PdfImage? PrepareLogo(byte[] content)
    {
        if (!Media.ImageSafety.IsWithinLimits(content))
        {
            return null;
        }

        try
        {
            using var thumb = Image.ThumbnailBuffer(content, 1600, height: 800, size: Enums.Size.Down);
            using var srgb = ToSrgb(thumb);
            using var bands = srgb.Bands switch
            {
                1 => srgb.Bandjoin([srgb, srgb]),
                2 => srgb.ExtractBand(0).Bandjoin([srgb.ExtractBand(0), srgb.ExtractBand(0), srgb.ExtractBand(1)]),
                _ => srgb.Copy(),
            };
            var png = bands.PngsaveBuffer(compression: 6, keep: Enums.ForeignKeep.None);
            return new PdfImage(png, bands.Width, bands.Height);
        }
        catch (VipsException)
        {
            return null;
        }
    }

    /// <summary>Setzt Breite und Höhe so, dass das Bild in die Box passt (Seitenverhältnis bleibt, nie vergrößert über die Box).</summary>
    public static (double Width, double Height) Fit(int pixelWidth, int pixelHeight, double maxWidthCm, double maxHeightCm)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return (maxWidthCm, maxHeightCm);
        }

        var scale = Math.Min(maxWidthCm / pixelWidth, maxHeightCm / pixelHeight);
        return (pixelWidth * scale, pixelHeight * scale);
    }

    /// <summary>Bild in einen Absatz setzen, eingepasst in die Box.</summary>
    public static MigraDoc.DocumentObjectModel.Shapes.Image Add(Paragraph paragraph, PdfImage image, double maxWidthCm, double maxHeightCm)
    {
        var (width, height) = Fit(image.Width, image.Height, maxWidthCm, maxHeightCm);
        var picture = paragraph.AddImage(PdfReport.ImageSource(image.Content));
        picture.LockAspectRatio = false;
        picture.Width = Unit.FromCentimeter(width);
        picture.Height = Unit.FromCentimeter(height);
        return picture;
    }

    /// <summary>
    /// JPEG mit JFIF-Kennung (APP0). libvips schreibt ohne Metadaten keinen APP0-Block; PDFsharp erkennt solche JPEGs nicht
    /// („Image has no valid type“) und setzt statt des Fotos nur einen Fehlertext. Ursache der fehlenden Fotos bis 1.0.42.
    /// </summary>
    public static byte[] WithJfifHeader(byte[] jpeg)
    {
        if (jpeg is not [0xFF, 0xD8, ..] || jpeg is [0xFF, 0xD8, 0xFF, 0xE0, ..])
        {
            return jpeg;
        }

        byte[] app0 = [0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00];
        var result = new byte[jpeg.Length + app0.Length];
        result[0] = 0xFF;
        result[1] = 0xD8;
        app0.CopyTo(result, 2);
        jpeg.AsSpan(2).CopyTo(result.AsSpan(2 + app0.Length));
        return result;
    }

    /// <summary>ISO-BMFF mit HEIC/HEIF-Marke (nicht AVIF).</summary>
    public static bool IsHeic(ReadOnlySpan<byte> content) =>
        content.Length >= 12 && content[4] == 'f' && content[5] == 't' && content[6] == 'y' && content[7] == 'p'
        && System.Text.Encoding.ASCII.GetString(content.Slice(8, 4)) is "heic" or "heix" or "hevc" or "hevx" or "heim" or "heis" or "mif1" or "msf1";

    private static bool CanDecode(byte[] content)
    {
        try
        {
            using var header = Image.NewFromBuffer(content, access: Enums.Access.Sequential);
            using var pixel = header.Crop(0, 0, Math.Min(header.Width, 8), Math.Min(header.Height, 8));
            pixel.Avg();
            return true;
        }
        catch (VipsException)
        {
            return false;
        }
    }

    private static Image ToSrgb(Image image)
    {
        // CMYK-JPEGs, 16-Bit-PNGs und Graustufen sicher nach 8 Bit sRGB (bzw. 8 Bit Grau) wandeln.
        if (image.Interpretation is Enums.Interpretation.Srgb or Enums.Interpretation.Bw && image.Format == Enums.BandFormat.Uchar)
        {
            return image.Copy();
        }

        var target = image.Interpretation is Enums.Interpretation.Bw or Enums.Interpretation.Grey16 ? Enums.Interpretation.Bw : Enums.Interpretation.Srgb;
        using var converted = image.Colourspace(target);
        return converted.Format == Enums.BandFormat.Uchar ? converted.Copy() : converted.Cast(Enums.BandFormat.Uchar);
    }
}
