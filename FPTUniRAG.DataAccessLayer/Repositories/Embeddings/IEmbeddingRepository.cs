namespace FPTUniRAG.DataAccessLayer.Repositories.Embeddings;

public interface IEmbeddingRepository
{
    Task<EmbeddingSettingRecord?> GetSettingAsync(CancellationToken cancellationToken = default);
    Task<EmbeddingSettingRecord> UpsertSettingAsync(string model, int dimensions, Guid updatedBy, DateTime updatedAt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<EmbeddingRunRecord>> GetRunsAsync(CancellationToken cancellationToken = default);
}

public sealed record EmbeddingSettingRecord(string Model, int Dimensions, DateTime? UpdatedAt, Guid? UpdatedBy);
public sealed record EmbeddingRunRecord(string Model, int Dimensions, Guid DocumentId, long? DocumentSizeBytes, int ChunkCount, int VectorCount, DateTime StartedAt, DateTime? CompletedAt, string Status);
