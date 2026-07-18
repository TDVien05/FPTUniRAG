using System.Collections.Concurrent;
using System.Threading.Channels;

namespace FPTUniRAG.BusinessLayer.Rag.Ingestion;

public sealed class DocumentProcessingQueue : IDocumentProcessingQueue
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>();
    private readonly ConcurrentQueue<Guid> _pending = new();
    private readonly object _syncRoot = new();
    private Guid? _currentDocumentId;

    public int QueueDepth => _pending.Count;

    public Guid? CurrentlyProcessingDocumentId => _currentDocumentId;

    public ValueTask QueueAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _pending.Enqueue(documentId);
            return _queue.Writer.WriteAsync(documentId, cancellationToken);
        }
    }

    public async ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken)
    {
        var documentId = await _queue.Reader.ReadAsync(cancellationToken);

        lock (_syncRoot)
        {
            if (_pending.TryPeek(out var head) && head == documentId)
            {
                _pending.TryDequeue(out _);
            }

            _currentDocumentId = documentId;
        }

        return documentId;
    }

    public int? GetQueuePosition(Guid documentId)
    {
        var position = 1;
        foreach (var pendingDocumentId in _pending)
        {
            if (pendingDocumentId == documentId)
            {
                return position;
            }

            position++;
        }

        return null;
    }

    public void MarkCompleted(Guid documentId)
    {
        lock (_syncRoot)
        {
            if (_currentDocumentId == documentId)
            {
                _currentDocumentId = null;
            }
        }
    }
}
