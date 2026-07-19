namespace FPTUniRAG.BusinessLayer.Rag.Embeddings;

public interface IEmbeddingBenchmarkService
{
    Task<IReadOnlyList<EmbeddingBenchmarkRow>> GetSummaryAsync(CancellationToken cancellationToken = default);
}

public sealed record EmbeddingBenchmarkRow(
    string Model,
    int DocumentCount,
    int ChunkCount,
    int Dimensions,
    double? EmbeddingTimeMs,
    double? ChunksPerSecond,
    long StorageBytes,
    decimal SuccessRate,
    DateTime? LatestEmbeddedAt,
    bool HasData);
