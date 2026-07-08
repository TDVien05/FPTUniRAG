namespace FPTUniRAG.Services;

public interface IChunkEmbeddingStore
{
    Task SaveEmbeddingsAsync(
        IReadOnlyList<(Guid ChunkId, float[] Embedding)> embeddings,
        CancellationToken cancellationToken = default);
}
