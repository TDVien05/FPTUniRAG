namespace FPTUniRAG.BusinessLayer.Rag.Embeddings;

public interface IChunkEmbeddingStore
{
    Task SaveEmbeddingsAsync(
        IReadOnlyList<(Guid ChunkId, float[] Embedding)> embeddings,
        string embeddingModel,
        CancellationToken cancellationToken = default);
}
