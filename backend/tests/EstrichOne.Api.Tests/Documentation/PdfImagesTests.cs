using EstrichOne.Api.Infrastructure.Pdf;
using NetVips;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace EstrichOne.Api.Tests.Documentation;

/// <summary>Bildvorbereitung für PDFs: Ausrichtung, Formate, Farbräume, Logo und Einbettung. Testbilder werden hier erzeugt.</summary>
public sealed class PdfImagesTests
{
    // Aufrecht betrachtet: oben links rot, oben rechts grün, unten links blau, unten rechts weiß (120 × 80).
    private static readonly double[] Red = [220, 30, 30];
    private static readonly double[] Green = [30, 200, 60];
    private static readonly double[] Blue = [30, 60, 220];

    private static Image Upright()
    {
        using var tl = Solid(60, 40, Red);
        using var tr = Solid(60, 40, Green);
        using var bl = Solid(60, 40, Blue);
        using var br = Solid(60, 40, [255, 255, 255]);
        using var top = tl.Join(tr, Enums.Direction.Horizontal);
        using var bottom = bl.Join(br, Enums.Direction.Horizontal);
        return top.Join(bottom, Enums.Direction.Vertical);
    }

    private static Image Solid(int width, int height, double[] rgb)
    {
        using var black = Image.Black(width, height, bands: 3);
        using var filled = black + rgb;
        using var cast = filled.Cast(Enums.BandFormat.Uchar);
        return cast.Copy(interpretation: Enums.Interpretation.Srgb);
    }

    /// <summary>Gespeicherte Pixel so, dass die EXIF-Ausrichtung <paramref name="orientation"/> wieder das aufrechte Bild ergibt.</summary>
    private static Image Stored(Image upright, int orientation) => orientation switch
    {
        2 => upright.FlipHor(),
        3 => upright.Rot180(),
        4 => upright.FlipVer(),
        5 => Transpose(upright),
        6 => upright.Rot270(),
        7 => Transverse(upright),
        8 => upright.Rot90(),
        _ => upright.Copy(),
    };

    private static Image Transpose(Image image)
    {
        using var rotated = image.Rot90();
        return rotated.FlipHor();
    }

    private static Image Transverse(Image image)
    {
        using var rotated = image.Rot270();
        return rotated.FlipHor();
    }

    private static void ShouldBeNear(double[] actual, double[] expected, string where)
    {
        for (var band = 0; band < 3; band++)
        {
            Math.Abs(actual[band] - expected[band]).ShouldBeLessThan(40, $"{where}, Kanal {band}: {string.Join("/", actual)}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void PreparePhoto_AppliesExifOrientation(int orientation)
    {
        using var upright = Upright();
        using var stored = Stored(upright, orientation);
        using var tagged = stored.Mutate(m => m.Set(GValue.GIntType, "orientation", orientation));
        var jpeg = tagged.JpegsaveBuffer(q: 95, keep: Enums.ForeignKeep.Exif);
        using (var check = Image.NewFromBuffer(jpeg))
        {
            (check.Contains("orientation") ? (int)check.Get("orientation") : 1).ShouldBe(orientation);
        }

        var result = PdfImages.PreparePhoto(jpeg);

        result.Hint.ShouldBeNull();
        var image = result.Image.ShouldNotBeNull();
        image.Width.ShouldBe(120);
        image.Height.ShouldBe(80);
        using var output = Image.NewFromBuffer(image.Content);
        output.Width.ShouldBe(120);
        output.Height.ShouldBe(80);
        output.Contains("orientation").ShouldBeFalse(); // ohne Metadaten, sonst dreht ein Betrachter doppelt
        ShouldBeNear(output.Getpoint(15, 10), Red, $"Ausrichtung {orientation} oben links");
        ShouldBeNear(output.Getpoint(105, 10), Green, $"Ausrichtung {orientation} oben rechts");
        ShouldBeNear(output.Getpoint(15, 70), Blue, $"Ausrichtung {orientation} unten links");
    }

    [Fact]
    public void PreparePhoto_PortraitPhoneJpeg_IsUprightAndScaledDown()
    {
        // Typisches Handyfoto hochkant: Sensor quer (4000 × 3000), EXIF 6 = 90° im Uhrzeigersinn drehen.
        using var landscape = Solid(4000, 3000, Blue);
        using var tagged = landscape.Mutate(m => m.Set(GValue.GIntType, "orientation", 6));
        var result = PdfImages.PreparePhoto(tagged.JpegsaveBuffer(q: 80, keep: Enums.ForeignKeep.Exif));
        var image = result.Image.ShouldNotBeNull();
        image.Width.ShouldBe(1050);
        image.Height.ShouldBe(PdfImages.PhotoEdge);
        image.Content.Length.ShouldBeLessThan(400_000);
    }

    [Fact]
    public void PreparePhoto_ReadsWebpPngAvif_AndFlattensTransparency()
    {
        using var upright = Upright();
        PdfImages.PreparePhoto(upright.WebpsaveBuffer(q: 90)).Image.ShouldNotBeNull().Width.ShouldBe(120);
        PdfImages.PreparePhoto(upright.HeifsaveBuffer(compression: Enums.ForeignHeifCompression.Av1, q: 80)).Image.ShouldNotBeNull().Height.ShouldBe(80);

        using var alpha = Image.Black(40, 20, bands: 1);
        using var rgba = upright.Crop(0, 0, 40, 20).Bandjoin(alpha); // vollständig transparent
        var flat = PdfImages.PreparePhoto(rgba.PngsaveBuffer()).Image.ShouldNotBeNull();
        using var decoded = Image.NewFromBuffer(flat.Content);
        decoded.Bands.ShouldBe(3);
        ShouldBeNear(decoded.Getpoint(5, 5), [255, 255, 255], "transparent wird weiß");
    }

    [Fact]
    public void PreparePhoto_ConvertsSixteenBitAndGreyscaleToEightBitRgb()
    {
        using var upright = Upright();
        using var wide = upright.Colourspace(Enums.Interpretation.Rgb16);
        var fromWide = PdfImages.PreparePhoto(wide.PngsaveBuffer()).Image.ShouldNotBeNull();
        using (var decoded = Image.NewFromBuffer(fromWide.Content))
        {
            decoded.Format.ShouldBe(Enums.BandFormat.Uchar);
            decoded.Bands.ShouldBe(3);
            ShouldBeNear(decoded.Getpoint(15, 10), Red, "16 Bit");
        }

        using var grey = upright.Colourspace(Enums.Interpretation.Bw);
        var fromGrey = PdfImages.PreparePhoto(grey.JpegsaveBuffer()).Image.ShouldNotBeNull();
        using var greyDecoded = Image.NewFromBuffer(fromGrey.Content);
        greyDecoded.Bands.ShouldBe(3);
    }

    [Fact]
    public void PreparePhoto_Heic_WithoutDecoder_GivesHint_InsteadOfError()
    {
        // HEIC-Container (iPhone) ohne gültige HEVC-Daten: die mitgelieferte libvips hat keinen HEVC-Decoder,
        // echte iPhone-Fotos landen deshalb ebenso hier. Ergebnis ist ein Hinweis, kein Absturz und kein leeres Feld.
        byte[] heic = [0, 0, 0, 24, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'h', (byte)'e', (byte)'i', (byte)'c', 0, 0, 0, 0,
            (byte)'m', (byte)'i', (byte)'f', (byte)'1', (byte)'h', (byte)'e', (byte)'i', (byte)'c', 0, 0, 0, 8, (byte)'m', (byte)'d', (byte)'a', (byte)'t'];
        PdfImages.IsHeic(heic).ShouldBeTrue();
        var result = PdfImages.PreparePhoto(heic);
        result.Image.ShouldBeNull();
        result.Hint.ShouldBe(PdfImages.HeicHint);

        PdfImages.PreparePhoto("kein Bild"u8.ToArray()).Hint.ShouldBe(PdfImages.UnreadableHint);
    }

    [Fact]
    public void PreparePhoto_HeicSupport_IsReportedHonestly()
    {
        // Steht HEVC in libvips zur Verfügung (eigene Builds), muss HEIC als Bild durchgehen; sonst bleibt es beim Hinweis.
        using var upright = Upright();
        byte[] heic;
        try
        {
            heic = upright.HeifsaveBuffer(compression: Enums.ForeignHeifCompression.Hevc);
        }
        catch (VipsException)
        {
            Assert.Skip("libvips ohne HEVC-Encoder: HEIC-Dekodierung ist auf diesem System nicht prüfbar, der Hinweis ist durch den vorigen Test abgedeckt.");
            return;
        }

        PdfImages.PreparePhoto(heic).Image.ShouldNotBeNull().Width.ShouldBe(120);
    }

    [Fact]
    public void Fit_KeepsAspectRatio_WithinBox()
    {
        PdfImages.Fit(3000, 4000, 8.2, 8.6).ShouldBe((6.45, 8.6), new FitComparer());
        PdfImages.Fit(4000, 3000, 8.2, 8.6).ShouldBe((8.2, 6.15), new FitComparer());
        var (w, h) = PdfImages.Fit(1000, 9000, 8.2, 8.6); // Panorama hochkant läuft nicht über die Seite
        h.ShouldBe(8.6, 0.001);
        (w / h).ShouldBe(1000.0 / 9000, 0.0001);
    }

    private sealed class FitComparer : IEqualityComparer<(double, double)>
    {
        public bool Equals((double, double) x, (double, double) y) => Math.Abs(x.Item1 - y.Item1) < 0.01 && Math.Abs(x.Item2 - y.Item2) < 0.01;

        public int GetHashCode((double, double) obj) => 0;
    }

    [Fact]
    public void PrepareLogo_KeepsTransparency_AndConvertsGreyAlpha()
    {
        using var upright = Upright();
        using var alpha = Image.Black(120, 80, bands: 1) + 128;
        using var rgba = upright.Bandjoin(alpha.Cast(Enums.BandFormat.Uchar));
        var logo = PdfImages.PrepareLogo(rgba.PngsaveBuffer()).ShouldNotBeNull();
        logo.Width.ShouldBe(120);
        logo.Height.ShouldBe(80);
        using (var decoded = Image.NewFromBuffer(logo.Content))
        {
            decoded.Bands.ShouldBe(4);
            decoded.Format.ShouldBe(Enums.BandFormat.Uchar);
        }

        using var grey = upright.Colourspace(Enums.Interpretation.Bw);
        using var greyAlpha = grey.Bandjoin(alpha.Cast(Enums.BandFormat.Uchar));
        using var greyLogo = Image.NewFromBuffer(PdfImages.PrepareLogo(greyAlpha.PngsaveBuffer()).ShouldNotBeNull().Content);
        greyLogo.Bands.ShouldBe(4);
    }

    [Fact]
    public void Report_WithCompanyLogo_EmbedsIt_WithoutLogo_ShowsNoForeignLogo()
    {
        using var upright = Upright();
        using var alpha = Image.Black(600, 200, bands: 1) + 255;
        using var wide = upright.Resize(5, vscale: 2.5);
        using var rgba = wide.Bandjoin(alpha.Cast(Enums.BandFormat.Uchar));
        var logo = PdfImages.PrepareLogo(rgba.PngsaveBuffer()).ShouldNotBeNull();

        var withLogo = new PdfCompany("Musterbau GmbH", "Hauptstraße 1", "84028", "Landshut", null, null, null) { Logo = logo };
        var (document, section) = PdfReport.Create("Test", "Test", withLogo);
        section.AddParagraph("Inhalt");
        var images = PdfImageObjects(PdfReport.Render(document));
        images.ShouldContain((600, 200));

        var withoutLogo = withLogo with { Logo = null };
        var (plain, plainSection) = PdfReport.Create("Test", "Test", withoutLogo);
        plainSection.AddParagraph("Inhalt");
        PdfImageObjects(PdfReport.Render(plain)).ShouldBeEmpty(); // kein eingebettetes Produkt- oder Altlogo
    }

    [Fact]
    public void PreparedPhoto_IsEmbeddedByPdfSharp()
    {
        // Ursache der fehlenden Fotos bis 1.0.42: JPEG ohne JFIF-Kennung, PDFsharp setzte „Image has no valid type“ statt des Bildes.
        using var upright = Upright();
        var photo = PdfImages.PreparePhoto(upright.JpegsaveBuffer()).Image.ShouldNotBeNull();
        photo.Content.AsSpan(0, 4).ToArray().ShouldBe(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

        var (document, section) = PdfReport.Create("Test", "Test", new PdfCompany("Musterbau GmbH", null, null, null, null, null, null));
        PdfImages.Add(section.AddParagraph(), photo, 8, 8);
        PdfImageObjects(PdfReport.Render(document)).ShouldBe([(120, 80)]);
    }

    /// <summary>Alle Bildobjekte (XObject /Image) eines PDFs mit ihren Pixelmaßen, ohne Transparenzmasken.</summary>
    internal static List<(int Width, int Height)> PdfImageObjects(byte[] pdf)
    {
        using var document = PdfReader.Open(new MemoryStream(pdf), PdfDocumentOpenMode.Import);
        var masks = new HashSet<PdfObject>();
        var images = new List<(PdfDictionary Image, int Width, int Height)>();
        foreach (var item in document.Internals.GetAllObjects())
        {
            if (item is PdfDictionary dictionary && dictionary.Elements.GetName("/Subtype") == "/Image")
            {
                images.Add((dictionary, dictionary.Elements.GetInteger("/Width"), dictionary.Elements.GetInteger("/Height")));
                if (dictionary.Elements.GetReference("/SMask") is { } mask)
                {
                    masks.Add(mask.Value);
                }
            }
        }

        return images.Where(i => !masks.Contains(i.Image)).Select(i => (i.Width, i.Height)).ToList();
    }
}
