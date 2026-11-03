using NetVips;

namespace EstrichOne.Api.Infrastructure.Media;

/// <summary>
/// Schutz vor Bildbomben und exotischen Formaten bei der Bildverarbeitung (libvips):
/// <list type="bullet">
/// <item>Nur die Lader für JPEG, PNG, WebP und HEIF/AVIF sind freigeschaltet – SVG, PDF, TIFF, Matlab, FITS, ImageMagick
/// usw. sind gesperrt (kein Angriff über ungewöhnliche Dateiformate oder deren Bibliotheken).</item>
/// <item>Höchstens <see cref="MaxPixels"/> Bildpunkte und <see cref="MaxEdge"/> Pixel Kantenlänge – ein kleines,
/// stark komprimiertes PNG mit 50 000 × 50 000 Pixeln würde sonst beim Verkleinern Gigabytes an Speicher belegen.</item>
/// </list>
/// </summary>
public static class ImageSafety
{
    /// <summary>100 Megapixel: deutlich über aktuellen Handyfotos (48–50 MP) und Panoramen.</summary>
    public const long MaxPixels = 100_000_000;
    public const int MaxEdge = 30_000;

    private static readonly Lazy<bool> Hardened = new(Harden);

    /// <summary>Einmal je Prozess: nicht benötigte Lader sperren. Liefert false, wenn libvips das nicht kann (Tests ohne libvips).</summary>
    public static bool EnsureHardened() => Hardened.Value;

    /// <summary>Kopfdaten lesen (ohne zu dekodieren) und Größe prüfen. False bei unlesbaren oder zu großen Bildern.</summary>
    public static bool IsWithinLimits(byte[] content)
    {
        EnsureHardened();
        try
        {
            using var header = Image.NewFromBuffer(content, access: Enums.Access.Sequential);
            return IsWithinLimits(header.Width, header.Height);
        }
        catch (VipsException)
        {
            return false;
        }
    }

    /// <summary>Wie <see cref="IsWithinLimits(byte[])"/>, für eine gespeicherte Datei.</summary>
    public static bool IsFileWithinLimits(string path)
    {
        EnsureHardened();
        try
        {
            using var header = Image.NewFromFile(path, access: Enums.Access.Sequential);
            return IsWithinLimits(header.Width, header.Height);
        }
        catch (VipsException)
        {
            return false;
        }
    }

    public static bool IsWithinLimits(int width, int height) =>
        width > 0 && height > 0 && width <= MaxEdge && height <= MaxEdge && (long)width * height <= MaxPixels;

    private static bool Harden()
    {
        try
        {
            Operation.Block("VipsForeignLoad", true);
            foreach (var allowed in new[] { "VipsForeignLoadJpeg", "VipsForeignLoadPng", "VipsForeignLoadWebp", "VipsForeignLoadHeif" })
            {
                Operation.Block(allowed, false);
            }

            return true;
        }
#pragma warning disable CA1031 // ohne native libvips (z. B. Werkzeuge) einfach nichts sperren
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException or VipsException)
#pragma warning restore CA1031
        {
            return false;
        }
    }
}
