using System.Threading.Channels;

namespace FPTUniRAG.Services;

public sealed class DocumentProcessingQueue : IDocumentProcessingQueue
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();

    public ValueTask QueueAsync(Guid documentId, CancellationToken cancellationToken = default)
        => _queue.Writer.WriteAsync(documentId, cancellationToken);

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken)
        => _queue.Reader.ReadAsync(cancellationToken);
}
