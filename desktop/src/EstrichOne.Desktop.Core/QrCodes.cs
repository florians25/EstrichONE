using QRCoder;

namespace EstrichOne.Desktop;

/// <summary>QR-Codes lokal erzeugen (z. B. Einrichtungslink für ein neues Gerät) – ohne Onlinedienst.</summary>
public static class QrCodes
{
    /// <summary>PNG (schwarz auf weiß) mit Ruhezone; <paramref name="pixelsPerModule"/> bestimmt die Größe.</summary>
    public static byte[] Png(string text, int pixelsPerModule = 10)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }
}
