using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;

namespace EstrichOne.Api.Infrastructure.Pdf;

/// <summary>
/// Geschäftsbrief nach DIN 5008, Form B: Firmenkopf (Logo), Anschriftfeld 45 mm von oben und 20 mm von links
/// (85 × 45 mm, mit Rücksendeangabe), Informationsblock mit Bezugszeichen rechts, Falz- und Lochmarken,
/// Brieftext ab 98,46 mm; Fußzeile mit Firmendaten (siehe <see cref="PdfReport"/>).
/// </summary>
public static class PdfLetter
{
    public static (Document Document, Section Section) Create(string title, PdfCompany company, IReadOnlyList<string> recipient,
        IReadOnlyList<(string Label, string? Value)> info, string subject)
    {
        PdfReport.EnsureFonts();
        var (document, section) = PdfReport.Create(title, title, company);
        section.PageSetup.LeftMargin = Unit.FromMillimeter(25);
        section.PageSetup.RightMargin = Unit.FromMillimeter(20);

        foreach (var y in new[] { 105.0, 148.5, 210.0 })
        {
            var mark = Frame(section, 4, y, y == 148.5 ? 7 : 5, 0.5);
            mark.LineFormat.Width = 0.4;
            mark.LineFormat.Color = PdfReport.Muted;
        }

        var address = Frame(section, 20, 45, 85, 45);
        var sender = address.AddParagraph(string.Join(" · ", new[] { company.Name, company.AddressLine }.Where(v => !string.IsNullOrWhiteSpace(v))));
        sender.Format.Font.Size = 7;
        sender.Format.Font.Color = PdfReport.Muted;
        sender.Format.SpaceAfter = Unit.FromMillimeter(4);
        foreach (var line in recipient.Where(l => !string.IsNullOrWhiteSpace(l)).Take(6))
        {
            address.AddParagraph(line);
        }

        var block = Frame(section, 125, 50, 75, 40);
        foreach (var (label, value) in info.Where(i => !string.IsNullOrWhiteSpace(i.Value)))
        {
            var line = block.AddParagraph();
            line.Format.Font.Size = 8.5;
            line.AddFormattedText($"{label}: ", new Font { Color = PdfReport.Muted });
            line.AddText(value!);
        }

        var heading = section.AddParagraph(subject);
        heading.Format.SpaceBefore = Unit.FromMillimeter(72);
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 11;
        heading.Format.SpaceAfter = Unit.FromMillimeter(8);
        return (document, section);
    }

    public static string Today(TimeProvider clock)
    {
        var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        return TimeZoneInfo.ConvertTime(clock.GetUtcNow(), berlin).ToString("dd.MM.yyyy", PdfReport.German);
    }

    private static TextFrame Frame(Section section, double left, double top, double width, double height)
    {
        var frame = section.AddTextFrame();
        frame.RelativeHorizontal = RelativeHorizontal.Page;
        frame.RelativeVertical = RelativeVertical.Page;
        frame.Left = Unit.FromMillimeter(left);
        frame.Top = Unit.FromMillimeter(top);
        frame.Width = Unit.FromMillimeter(width);
        frame.Height = Unit.FromMillimeter(height);
        frame.WrapFormat.Style = WrapStyle.None;
        return frame;
    }
}
