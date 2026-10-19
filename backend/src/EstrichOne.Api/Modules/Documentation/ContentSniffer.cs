namespace EstrichOne.Api.Modules.Documentation;

/// <summary>
/// Bestimmt den Dateityp aus dem Inhalt (Magic Bytes), nicht aus Endung oder Client-Angabe.
/// Nur diese Typen werden angenommen (docs/reengineering/17: Upload-Prüfung).
/// </summary>
public static class ContentSniffer
{
    public static string? Detect(ReadOnlySpan<byte> head) => head switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => "image/webp",
        [(byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-', ..] => "application/pdf",
        [(byte)'P', (byte)'K', 0x03, 0x04, ..] => "application/zip", // docx, xlsx, odt …
        [_, _, _, _, (byte)'f', (byte)'t', (byte)'y', (byte)'p', .. var brand] => FromIsoBrand(brand),
        _ => null,
    };

    public static bool IsAllowed(FileKind kind, string detected) => kind switch
    {
        FileKind.Photo => detected is "image/jpeg" or "image/png" or "image/webp" or "image/heic",
        FileKind.Video => detected is "video/mp4" or "video/quicktime",
        FileKind.Document => detected is "application/pdf" or "application/zip" or "image/jpeg" or "image/png",
        _ => false,
    };

    /// <summary>Bilder, für die Vorschauvarianten erzeugt werden.</summary>
    public static bool HasVariants(string detected) => detected is "image/jpeg" or "image/png" or "image/webp" or "image/heic";

    private static string? FromIsoBrand(ReadOnlySpan<byte> brand)
    {
        if (brand.Length < 4)
        {
            return null;
        }

        var name = System.Text.Encoding.ASCII.GetString(brand[..4]);
        return name switch
        {
            "heic" or "heix" or "mif1" or "msf1" or "heim" or "heis" => "image/heic",
            "qt  " => "video/quicktime",
            "isom" or "iso2" or "mp41" or "mp42" or "avc1" or "M4V " or "dash" => "video/mp4",
            _ => null,
        };
    }
}
