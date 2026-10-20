using System.Threading.Channels;

namespace EstrichOne.Hosting.Operations;

/// <summary>Warteschlange der auszuführenden Vorgänge (IDs; der Vorgang selbst steht in der Datenbank).</summary>
public sealed class OperationDispatcher
{
    private readonly Channel<Guid> channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid operationId) => channel.Writer.TryWrite(operationId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) => channel.Reader.ReadAllAsync(cancellationToken);
}
