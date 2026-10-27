using System.Threading.Channels;
using EstrichOne.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NetVips;

namespace EstrichOne.Api.Modules.Documentation;

/// <summary>
/// Erzeugt Bildvarianten im Hintergrund: <c>thumb</c> (320 px), <c>display</c> (1280 px) und <c>large</c> (2560 px), WebP, ohne Metadaten
/// (EXIF/GPS bleiben nur im Original). Reagiert sofort auf neue Uploads und arbeitet nach einem Neustart
/// liegengebliebene Dateien ab.
/// </summary>
internal sealed partial class VariantWorker(
    Channel<Guid> queue, IServiceScopeFactory scopes, LocalFileStorage storage, ILogger<VariantWorker> logger) : BackgroundService
{
    public static readonly (string Name, int Size)[] Sizes = [("thumb", 320), ("display", 1280), ("large", 2560)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnqueuePendingAsync(stoppingToken);
        await foreach (var id in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(id, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Ein Fehler bei einer Datei darf den Dienst (und damit den Server) nicht beenden.
                LogVariantFailed(logger, id, exception.Message);
            }
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
                // Kleine Stufen etwas höher, große etwas niedriger komprimiert: scharf am Bildschirm, wenig Speicher.
                using var image = Image.ThumbnailBuffer(original, size, height: size, size: Enums.Size.Down);
                await storage.WriteVariantAsync(id, name, image.WebpsaveBuffer(q: size <= 400 ? 82 : size <= 1400 ? 80 : 78, effort: 5,
                    smartSubsample: true, keep: Enums.ForeignKeep.None), cancellationToken);
            }

            file.SetVariants(VariantState.Ready);
        }
        catch (VipsException exception)
        {
            LogVariantFailed(logger, id, exception.Message);
            file.SetVariants(VariantState.Failed);
        }

        // Gleichzeitige Änderungen (Beschriftung, Ablage) erhöhen die Version: neu lesen und erneut setzen.
        var state = file.Variants;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 10)
            {
                await db.Entry(file).ReloadAsync(cancellationToken);
                if (file.DeletedAt is not null)
                {
                    return;
                }

                file.SetVariants(state);
            }
        }
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
