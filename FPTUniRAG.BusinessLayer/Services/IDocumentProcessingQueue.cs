namespace FPTUniRAG.BusinessLayer.Services;

public interface IDocumentProcessingQueue
{
    ValueTask QueueAsync(Guid documentId, CancellationToken cancellationToken = default);

    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);
}
