using EstrichOne.Api.Infrastructure.Pdf;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;

namespace EstrichOne.Api.Modules.Documentation;

/// <summary>Ein Eintrag der Fotodokumentation: Bild oder Hinweis, dazu die Bildunterschrift.</summary>
public sealed record ReportPhoto(PdfImageResult Image, string Title, IReadOnlyList<string?> Lines);

/// <summary>
/// Fotos aus der Bauakte für PDF-Ausdrucke (Bauaktenbericht, Erstbegehung, Abnahme). Quelle in dieser Reihenfolge:
/// Vorschau-Variante <c>display</c> bzw. <c>large</c> (schon aufgerichtet), sonst das Original, auch wenn es auf den Storage
/// ausgelagert ist. Archivierte (ausgelagerte) Akten, HEIC ohne Decoder und Videos erscheinen als Hinweisfeld.
/// </summary>
public sealed partial class ReportPhotos
{
    private readonly LocalFileStorage storage;
    private readonly OffloadService offload;
    private readonly ILogger<ReportPhotos> logger;

    internal ReportPhotos(LocalFileStorage storage, OffloadService offload, ILogger<ReportPhotos> logger)
    {
        this.storage = storage;
        this.offload = offload;
        this.logger = logger;
    }

    public const string VideoHint = "Video. Die Wiedergabe ist nur in der Bauakte möglich (Web oder App).";

    public const string ArchivedHint = "Foto liegt im Aktenarchiv. Bitte die Akte unter Aktenarchiv abrufen und den Bericht erneut erzeugen.";

    public async Task<PdfImageResult> LoadAsync(FileRecord file, CancellationToken cancellationToken)
    {
        if (file.Kind == FileKind.Video)
        {
            return PdfImageResult.Missing(VideoHint);
        }

        try
        {
            foreach (var variant in new[] { "display", "large" })
            {
                var path = storage.VariantPath(file.Id, variant);
                if (File.Exists(path))
                {
                    var prepared = PdfImages.PreparePhoto(await File.ReadAllBytesAsync(path, cancellationToken));
                    if (prepared.Image is not null)
                    {
                        return prepared;
                    }
                }
            }

            if (file.StorageKey is not { } key)
            {
                return PdfImageResult.Missing(PdfImages.UnreadableHint);
            }

            string? original;
            try
            {
                original = await offload.OriginalPathAsync(key, cancellationToken);
            }
            catch (OffloadUnavailableException exception)
            {
                LogUnavailable(logger, file.Id, exception.Message);
                return PdfImageResult.Missing("Foto liegt auf dem Storage, der gerade nicht erreichbar ist. Bitte später erneut drucken.");
            }

            if (original is null || !File.Exists(original))
            {
                // Akte archiviert und ausgelagert: Originale und Vorschauen liegen nur noch auf dem Archivserver.
                return PdfImageResult.Missing(ArchivedHint);
            }

            var result = PdfImages.PreparePhoto(await File.ReadAllBytesAsync(original, cancellationToken));
            if (result.Image is null)
            {
                LogUnreadable(logger, file.Id, file.DetectedContentType ?? file.ContentType);
            }

            return result;
        }
        catch (IOException exception)
        {
            LogUnavailable(logger, file.Id, exception.Message);
            return PdfImageResult.Missing(PdfImages.UnreadableHint);
        }
    }

    /// <summary>
    /// Raster mit 1 bis 3 Spalten. Jede Zelle hält Bild und Bildunterschrift zusammen; eine Tabellenzeile wird von MigraDoc
    /// nicht über einen Seitenumbruch geteilt, und die Bildhöhe ist so begrenzt, dass zwei Zeilen auf eine Seite passen.
    /// </summary>
    public static void AddGrid(Section section, double width, int columns, IReadOnlyList<ReportPhoto> photos)
    {
        columns = Math.Clamp(columns, 1, 3);
        const double gap = 0.4;
        var column = (width - gap * (columns - 1)) / columns;
        var maxHeight = columns switch { 1 => 12.0, 2 => 8.6, _ => 5.8 };
        var grid = section.AddTable();
        grid.Borders.Visible = false;
        grid.LeftPadding = 0;
        grid.RightPadding = 0;
        for (var i = 0; i < columns; i++)
        {
            if (i > 0)
            {
                grid.AddColumn(Unit.FromCentimeter(gap));
            }

            grid.AddColumn(Unit.FromCentimeter(column));
        }

        Row? row = null;
        for (var index = 0; index < photos.Count; index++)
        {
            if (index % columns == 0)
            {
                row = grid.AddRow();
                row.TopPadding = Unit.FromPoint(4);
                row.BottomPadding = Unit.FromPoint(10);
                row.VerticalAlignment = VerticalAlignment.Top;
            }

            var cell = row!.Cells[index % columns * 2];
            var photo = photos[index];
            if (photo.Image.Image is { } image)
            {
                var holder = cell.AddParagraph();
                holder.Format.Alignment = ParagraphAlignment.Center;
                holder.Format.KeepWithNext = true;
                PdfImages.Add(holder, image, column, maxHeight);
            }
            else
            {
                AddPlaceholder(cell, column, Math.Min(maxHeight, column * 0.75), photo.Image.Hint ?? PdfImages.UnreadableHint);
            }

            var caption = cell.AddParagraph();
            caption.Format.SpaceBefore = Unit.FromPoint(3);
            caption.Format.Font.Size = 8;
            caption.AddFormattedText(photo.Title, TextFormat.Bold);
            foreach (var line in photo.Lines.OfType<string>().Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                caption.AddLineBreak();
                caption.AddText(line);
            }
        }
    }

    /// <summary>Graues Hinweisfeld in Bildgröße (MigraDoc kann keine Tabellen in Zellen, daher ein Textrahmen).</summary>
    private static void AddPlaceholder(Cell cell, double width, double height, string hint)
    {
        var frame = cell.AddTextFrame();
        frame.Width = Unit.FromCentimeter(width);
        frame.Height = Unit.FromCentimeter(height);
        frame.FillFormat.Color = PdfReport.Shade;
        frame.LineFormat.Color = PdfReport.Line;
        frame.LineFormat.Width = 0.5;
        frame.MarginLeft = Unit.FromCentimeter(0.5);
        frame.MarginRight = Unit.FromCentimeter(0.5);
        frame.MarginTop = Unit.FromCentimeter(Math.Max(0.3, height / 2 - 0.7));
        frame.WrapFormat.Style = MigraDoc.DocumentObjectModel.Shapes.WrapStyle.TopBottom;
        var text = frame.AddParagraph(hint);
        text.Format.Alignment = ParagraphAlignment.Center;
        text.Format.Font.Size = 8;
        text.Format.Font.Color = PdfReport.Muted;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Foto {FileId} für das PDF nicht verfügbar: {Reason}")]
    private static partial void LogUnavailable(ILogger logger, Guid fileId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Foto {FileId} ({ContentType}) für das PDF nicht lesbar.")]
    private static partial void LogUnreadable(ILogger logger, Guid fileId, string contentType);
}
