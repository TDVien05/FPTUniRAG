namespace FPTUniRAG.Services;

public interface IOpenRouterEmbeddingService
{
    Task<IReadOnlyList<float[]>> CreateEmbeddingsAsync(
        IReadOnlyList<string> chunks,
        CancellationToken cancellationToken = default);
}
