using EstrichOne.Api.Modules.Documentation.Exports;
using NetVips;

namespace EstrichOne.Api.Modules.Documentation;

internal sealed partial class MediaPreparation(MediaOptimizer optimizer, Organization.Exports.ISystemSettings systemSettings, ILogger<MediaPreparation> logger)
    : IMediaPreparation
{
    public async Task<PreparedMedia?> PrepareAsync(byte[] content, CancellationToken cancellationToken)
    {
        var detected = ContentSniffer.Detect(content.AsSpan(0, Math.Min(content.Length, 64)));
        if (detected is not ("image/jpeg" or "image/png" or "image/webp" or "image/heic" or "application/pdf"))
        {
            return null;
        }

        var media = (await systemSettings.GetAsync(cancellationToken)).Media;
        if (detected == "application/pdf")
        {
            var pdf = optimizer.Optimize(content, detected, FileKind.Document, media);
            return new PreparedMedia(pdf?.Content ?? content, "application/pdf", false, null, null, null);
        }

        try
        {
            if (detected == "image/heic")
            {
                // Browser zeigen HEIC nicht an: einmal nach JPEG wandeln (gedreht nach EXIF).
                using var heic = Image.NewFromBuffer(content).Autorot();
                content = heic.JpegsaveBuffer(q: media.PhotoQuality, optimizeCoding: true, interlace: true, keep: Enums.ForeignKeep.Exif | Enums.ForeignKeep.Icc);
                detected = "image/jpeg";
            }

            if (optimizer.Optimize(content, detected, FileKind.Photo, media) is { } optimized)
            {
                content = optimized.Content;
                detected = optimized.ContentType;
            }

            using var image = Image.NewFromBuffer(content);
            var orientation = image.Contains("orientation") ? (int)image.Get("orientation") : 1;
            var swap = orientation is >= 5 and <= 8;
            using var thumb = Image.ThumbnailBuffer(content, 480, height: 480, size: Enums.Size.Down);
            var preview = thumb.WebpsaveBuffer(q: 78, effort: 4, keep: Enums.ForeignKeep.None);
            return new PreparedMedia(content, detected, true, preview, swap ? image.Height : image.Width, swap ? image.Width : image.Height);
        }
#pragma warning disable CA1031 // libvips wirft vielerlei: Bild nicht lesbar heißt hier „nicht erlaubt“.
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            LogUnreadable(logger, detected, exception.Message);
            return detected == "image/heic" ? null : new PreparedMedia(content, detected, true, null, null, null);
        }
    }

    public byte[]? Square(byte[] content, int size)
    {
        try
        {
            using var image = Image.ThumbnailBuffer(content, size, height: size, crop: Enums.Interesting.Centre);
            return image.WebpsaveBuffer(q: 82, effort: 4, keep: Enums.ForeignKeep.None);
        }
#pragma warning disable CA1031
        catch (Exception exception) when (exception is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            LogUnreadable(logger, "image", exception.Message);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Bild nicht verarbeitbar ({ContentType}): {Reason}")]
    private static partial void LogUnreadable(ILogger logger, string contentType, string reason);
}
