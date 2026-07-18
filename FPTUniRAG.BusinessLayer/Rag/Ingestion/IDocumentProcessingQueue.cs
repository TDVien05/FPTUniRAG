namespace FPTUniRAG.BusinessLayer.Rag.Ingestion;

public interface IDocumentProcessingQueue
{
    /// <summary>Number of documents currently waiting to be picked up by the single background worker.</summary>
    int QueueDepth { get; }

    /// <summary>The document the background worker is actively processing right now, if any.</summary>
    Guid? CurrentlyProcessingDocumentId { get; }

    ValueTask QueueAsync(Guid documentId, CancellationToken cancellationToken = default);

    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken);

    /// <summary>1-based position of <paramref name="documentId"/> among documents still waiting, or null if it is not currently waiting.</summary>
    int? GetQueuePosition(Guid documentId);

    /// <summary>Clears the currently-processing marker once a document's processing has finished (success or failure).</summary>
    void MarkCompleted(Guid documentId);
}
