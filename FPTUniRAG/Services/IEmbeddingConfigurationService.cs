namespace FPTUniRAG.Services;

public interface IEmbeddingConfigurationService
{
    IReadOnlyList<EmbeddingModelOption> GetAvailableModels();

    Task<EmbeddingConfigurationSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task<EmbeddingConfigurationSnapshot> UpdateAsync(
        string model,
        Guid adminUserId,
        CancellationToken cancellationToken = default);
}

public sealed record EmbeddingModelOption(string Model, int Dimensions);

public sealed record EmbeddingConfigurationSnapshot(
    string Model,
    int Dimensions,
    DateTime? UpdatedAt,
    Guid? UpdatedBy,
    bool IsDatabaseBacked);
