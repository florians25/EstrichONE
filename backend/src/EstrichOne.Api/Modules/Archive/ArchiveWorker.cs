namespace EstrichOne.Api.Modules.Archive;

/// <summary>Weckt den Archivdienst sofort (nach Kopplung, Regeländerung, manueller Archivierung).</summary>
public sealed class ArchiveSignal : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Dispose() => _signal.Dispose();

    public void Wake()
    {
        try
        {
            if (_signal.CurrentCount == 0)
            {
                _signal.Release();
            }
        }
        catch (SemaphoreFullException)
        {
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => _signal.WaitAsync(timeout, cancellationToken);
}

/// <summary>Reiht nach den Regeln ein und überträgt fällige Dokumente (jede Minute oder sofort auf Signal).</summary>
internal sealed partial class ArchiveWorker(IServiceScopeFactory scopes, ArchiveSignal signal, ILogger<ArchiveWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await signal.WaitAsync(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<ArchiveService>();
                await service.EnqueueByRulesAsync(stoppingToken);
                while (await service.ProcessDueAsync(stoppingToken) == 20)
                {
                }

                await service.WriteStatusFileAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(logger, exception.Message);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Archivdienst: {Error}")]
    private static partial void LogFailed(ILogger logger, string error);
}
