namespace FPTUniRAG.BusinessLayer.Rag.Embeddings;

public interface IOpenRouterEmbeddingService
{
    Task<EmbeddingBatchResult> CreateEmbeddingsAsync(
        IReadOnlyList<string> chunks,
        CancellationToken cancellationToken = default);
}

public sealed record EmbeddingBatchResult(
    string Model,
    int Dimensions,
    IReadOnlyList<float[]> Vectors);
