namespace FPTUniRAG.BusinessLayer.Rag.Embeddings;

public interface IEmbeddingConfigurationService
{
    IReadOnlyList<EmbeddingModelOption> GetAvailableModels();

    Task<EmbeddingConfigurationSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task<EmbeddingConfigurationSnapshot> UpdateAsync(
        string model,
        int fixedChunkSize,
        Guid adminUserId,
        CancellationToken cancellationToken = default);
}

public sealed record EmbeddingModelOption(string Model, int Dimensions);

public sealed record EmbeddingConfigurationSnapshot(
    string Model,
    int Dimensions,
    int FixedChunkSize,
    DateTime? UpdatedAt,
    Guid? UpdatedBy,
    bool IsDatabaseBacked);
