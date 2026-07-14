namespace FPTUniRAG.BusinessLayer.Services;

public interface IChunkEmbeddingStore
{
    Task SaveEmbeddingsAsync(
        IReadOnlyList<(Guid ChunkId, float[] Embedding)> embeddings,
        string embeddingModel,
        CancellationToken cancellationToken = default);
}
