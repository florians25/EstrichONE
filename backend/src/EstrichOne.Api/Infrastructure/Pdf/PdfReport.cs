using System.Globalization;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace EstrichOne.Api.Infrastructure.Pdf;

/// <summary>Firmendaten für Kopf- und Fußzeile (aus dem Organisationsmodul übergeben).</summary>
public sealed record PdfCompany(string Name, string? Street, string? PostalCode, string? City, string? Phone, string? Email, string? Website)
{
    public string AddressLine => string.Join(" · ", new[] { Street, string.Join(' ', new[] { PostalCode, City }.Where(v => !string.IsNullOrWhiteSpace(v))) }
        .Where(v => !string.IsNullOrWhiteSpace(v)));

    public string ContactLine => string.Join(" · ", new[] { Phone is null ? null : $"Tel. {Phone}", Email, Website }.Where(v => !string.IsNullOrWhiteSpace(v)));
}

/// <summary>
/// Gemeinsames Layout aller PDF-Ausdrucke (MigraDoc, MIT): Schrift Liberation Sans (eingebettet, SIL OFL 1.1),
/// Kopfzeile mit Logo und Firma, Fußzeile mit Firmendaten und „Seite x von y“, deutsche Formate.
/// </summary>
public static class PdfReport
{
    public const string Font = "Liberation Sans";
    public static readonly Color Brand = Color.FromRgb(0x0B, 0x2D, 0x5B);
    public static readonly Color Muted = Color.FromRgb(0x5B, 0x67, 0x73);
    public static readonly Color Line = Color.FromRgb(0xD5, 0xDA, 0xDF);
    public static readonly Color Shade = Color.FromRgb(0xF2, 0xF4, 0xF6);
    public static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private static readonly Lock FontLock = new();

    static PdfReport() => EnsureFonts();

    public static void EnsureFonts()
    {
        lock (FontLock)
        {
            if (GlobalFontSettings.FontResolver is not EmbeddedFontResolver)
            {
                GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
            }
        }
    }

    public static byte[] Logo { get; } = EmbeddedFontResolver.Read("logo-horizontal.png");

    /// <summary>Neues Dokument mit Stilen und einem Abschnitt (A4 hoch oder quer) inkl. Kopf-/Fußzeile.</summary>
    public static (Document Document, Section Section) Create(string title, string subject, PdfCompany company, bool landscape = false)
    {
        var document = new Document();
        document.Info.Title = title;
        document.Info.Subject = subject;
        document.Info.Author = company.Name;

        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = Font;
        normal.Font.Size = 9.5;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(3);
        var h1 = document.Styles[StyleNames.Heading1]!;
        h1.Font.Size = 16;
        h1.Font.Bold = true;
        h1.Font.Color = Brand;
        h1.ParagraphFormat.SpaceBefore = Unit.FromPoint(6);
        h1.ParagraphFormat.SpaceAfter = Unit.FromPoint(8);
        h1.ParagraphFormat.KeepWithNext = true;
        h1.ParagraphFormat.OutlineLevel = OutlineLevel.Level1;
        var h2 = document.Styles[StyleNames.Heading2]!;
        h2.Font.Size = 12;
        h2.Font.Bold = true;
        h2.Font.Color = Brand;
        h2.ParagraphFormat.SpaceBefore = Unit.FromPoint(12);
        h2.ParagraphFormat.SpaceAfter = Unit.FromPoint(5);
        h2.ParagraphFormat.KeepWithNext = true;
        h2.ParagraphFormat.Borders.Bottom.Width = 0.75;
        h2.ParagraphFormat.Borders.Bottom.Color = Line;
        h2.ParagraphFormat.OutlineLevel = OutlineLevel.Level2;
        var muted = document.Styles.AddStyle("Muted", StyleNames.Normal);
        muted.Font.Color = Muted;
        muted.Font.Size = 8.5;
        var small = document.Styles.AddStyle("Small", StyleNames.Normal);
        small.Font.Size = 8;

        var section = document.AddSection();
        var setup = section.PageSetup;
        setup.PageFormat = PageFormat.A4;
        setup.Orientation = landscape ? Orientation.Landscape : Orientation.Portrait;
        setup.TopMargin = Unit.FromCentimeter(2.6);
        setup.BottomMargin = Unit.FromCentimeter(2.0);
        setup.LeftMargin = Unit.FromCentimeter(landscape ? 1.5 : 2.0);
        setup.RightMargin = Unit.FromCentimeter(landscape ? 1.5 : 1.8);
        setup.HeaderDistance = Unit.FromCentimeter(0.9);
        setup.FooterDistance = Unit.FromCentimeter(0.8);

        var header = section.Headers.Primary;
        var headerTable = header.AddTable();
        headerTable.Borders.Visible = false;
        var usable = (landscape ? 29.7 : 21.0) - setup.LeftMargin.Centimeter - setup.RightMargin.Centimeter;
        headerTable.AddColumn(Unit.FromCentimeter(usable * 0.45));
        headerTable.AddColumn(Unit.FromCentimeter(usable * 0.55));
        var row = headerTable.AddRow();
        var logo = row.Cells[0].AddImage(ImageSource(Logo));
        logo.Height = Unit.FromCentimeter(0.85);
        logo.LockAspectRatio = true;
        var right = row.Cells[1].AddParagraph();
        right.Format.Alignment = ParagraphAlignment.Right;
        right.AddFormattedText(company.Name, TextFormat.Bold).Font.Color = Brand;
        right.AddLineBreak();
        right.AddFormattedText(title, new Font { Size = 8.5, Color = Muted });
        row.Borders.Bottom.Width = 0.75;
        row.Borders.Bottom.Color = Line;
        row.BottomPadding = Unit.FromPoint(4);

        var footer = section.Footers.Primary;
        var footerTable = footer.AddTable();
        footerTable.Borders.Visible = false;
        footerTable.AddColumn(Unit.FromCentimeter(usable * 0.8));
        footerTable.AddColumn(Unit.FromCentimeter(usable * 0.2));
        var frow = footerTable.AddRow();
        frow.Borders.Top.Width = 0.5;
        frow.Borders.Top.Color = Line;
        frow.TopPadding = Unit.FromPoint(3);
        var info = frow.Cells[0].AddParagraph(string.Join(" · ", new[] { company.Name, company.AddressLine, company.ContactLine }.Where(v => !string.IsNullOrWhiteSpace(v))));
        info.Style = "Muted";
        var pages = frow.Cells[1].AddParagraph();
        pages.Style = "Muted";
        pages.Format.Alignment = ParagraphAlignment.Right;
        pages.AddText("Seite ");
        pages.AddPageField();
        pages.AddText(" von ");
        pages.AddNumPagesField();
        return (document, section);
    }

    /// <summary>Bild für MigraDoc ohne Datei (Base64-Quelle).</summary>
    public static string ImageSource(byte[] bytes) => "base64:" + Convert.ToBase64String(bytes);

    /// <summary>Tabelle mit Kopfzeile und dezenten Linien; Breiten in cm.</summary>
    public static Table AddTable(Section section, params (string Title, double Width, ParagraphAlignment Align)[] columns)
    {
        var table = section.AddTable();
        table.Borders.Width = 0;
        table.Rows.LeftIndent = 0;
        table.TopPadding = Unit.FromPoint(2.5);
        table.BottomPadding = Unit.FromPoint(2.5);
        table.LeftPadding = Unit.FromPoint(3);
        table.RightPadding = Unit.FromPoint(3);
        foreach (var (_, width, align) in columns)
        {
            table.AddColumn(Unit.FromCentimeter(width)).Format.Alignment = align;
        }

        var head = table.AddRow();
        head.HeadingFormat = true;
        head.Shading.Color = Shade;
        head.Format.Font.Bold = true;
        head.Format.Font.Size = 8.5;
        for (var i = 0; i < columns.Length; i++)
        {
            head.Cells[i].AddParagraph(columns[i].Title);
        }

        head.Borders.Bottom.Width = 0.75;
        head.Borders.Bottom.Color = Line;
        return table;
    }

    public static Row AddRow(Table table, params string?[] values)
    {
        var row = table.AddRow();
        row.Borders.Bottom.Width = 0.4;
        row.Borders.Bottom.Color = Line;
        row.VerticalAlignment = VerticalAlignment.Top;
        for (var i = 0; i < values.Length; i++)
        {
            row.Cells[i].AddParagraph(values[i] ?? "–");
        }

        return row;
    }

    /// <summary>Zweispaltige Angaben (Bezeichnung: Wert).</summary>
    public static void AddFacts(Section section, double width, params (string Label, string? Value)[] facts)
    {
        var table = section.AddTable();
        table.Borders.Width = 0;
        table.AddColumn(Unit.FromCentimeter(4.2));
        table.AddColumn(Unit.FromCentimeter(width - 4.2));
        foreach (var (label, value) in facts.Where(f => !string.IsNullOrWhiteSpace(f.Value)))
        {
            var row = table.AddRow();
            row.TopPadding = Unit.FromPoint(1.5);
            row.BottomPadding = Unit.FromPoint(1.5);
            row.Cells[0].AddParagraph(label).Style = "Muted";
            row.Cells[1].AddParagraph(value!);
        }
    }

    public static string Date(DateOnly? day) => day?.ToString("dd.MM.yyyy", German) ?? "–";

    public static string DateTime(DateTimeOffset? at) =>
        at is { } value ? TimeZoneInfo.ConvertTime(value, Berlin).ToString("dd.MM.yyyy, HH:mm", German) : "–";

    public static string Number(decimal? value, int decimals = 2) => value?.ToString("N" + decimals, German) ?? "–";

    private static readonly TimeZoneInfo Berlin = FindBerlin();

    private static TimeZoneInfo FindBerlin()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    public static byte[] Render(Document document)
    {
        EnsureFonts();
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }
}

/// <summary>Liefert die eingebettete Liberation Sans für alle Schriftanfragen (Server ohne Systemschriften).</summary>
internal sealed class EmbeddedFontResolver : IFontResolver
{
    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) =>
        new((bold, italic) switch
        {
            (true, true) => "LiberationSans-BoldItalic",
            (true, false) => "LiberationSans-Bold",
            (false, true) => "LiberationSans-Italic",
            _ => "LiberationSans-Regular",
        });

    public byte[] GetFont(string faceName) => Read(faceName + ".ttf");

    internal static byte[] Read(string name)
    {
        using var stream = typeof(EmbeddedFontResolver).Assembly.GetManifestResourceStream("EstrichOne.Pdf." + name)
            ?? throw new InvalidOperationException($"Eingebettete Ressource {name} fehlt.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
