using System.Threading.Channels;
using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NetVips;

namespace EstrichOne.Api.Modules.Documentation;

/// <summary>
/// Erzeugt Bildvarianten im Hintergrund: <c>thumb</c> (400 px) und <c>display</c> (2048 px), WebP, ohne Metadaten
/// (EXIF/GPS bleiben nur im Original). Reagiert sofort auf neue Uploads und arbeitet nach einem Neustart
/// liegengebliebene Dateien ab.
/// </summary>
internal sealed partial class VariantWorker(
    Channel<Guid> queue, IServiceScopeFactory scopes, LocalFileStorage storage, ILogger<VariantWorker> logger) : BackgroundService
{
    public static readonly (string Name, int Size)[] Sizes = [("thumb", 400), ("display", 2048)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnqueuePendingAsync(stoppingToken);
        await foreach (var id in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await ProcessAsync(id, stoppingToken);
        }
    }

    public async Task ProcessAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
        var file = await db.Set<FileRecord>().SingleOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (file is not { Variants: VariantState.Pending, StorageKey: not null })
        {
            return;
        }

        try
        {
            var original = await File.ReadAllBytesAsync(storage.PathOf(file.StorageKey), cancellationToken);
            foreach (var (name, size) in Sizes)
            {
                using var image = Image.ThumbnailBuffer(original, size, height: size, size: Enums.Size.Down);
                await storage.WriteVariantAsync(id, name, image.WebpsaveBuffer(q: 80, keep: Enums.ForeignKeep.None), cancellationToken);
            }

            file.SetVariants(VariantState.Ready);
        }
        catch (VipsException exception)
        {
            LogVariantFailed(logger, id, exception.Message);
            file.SetVariants(VariantState.Failed);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task EnqueuePendingAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EstrichOneDbContext>();
            var pending = await db.Set<FileRecord>().Where(f => f.Variants == VariantState.Pending)
                .Select(f => f.Id).ToListAsync(cancellationToken);
            foreach (var id in pending)
            {
                await queue.Writer.WriteAsync(id, cancellationToken);
            }
        }
        catch (Npgsql.PostgresException exception) when (exception.SqlState == Npgsql.PostgresErrorCodes.UndefinedTable)
        {
            // Datenbank noch nicht migriert.
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Bildvarianten für Datei {FileId} nicht erzeugbar: {Reason}")]
    private static partial void LogVariantFailed(ILogger logger, Guid fileId, string reason);
}
