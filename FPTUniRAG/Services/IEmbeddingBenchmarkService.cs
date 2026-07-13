namespace FPTUniRAG.Services;

public interface IEmbeddingBenchmarkService
{
    Task<IReadOnlyList<EmbeddingBenchmarkRow>> GetSummaryAsync(CancellationToken cancellationToken = default);
}

public sealed record EmbeddingBenchmarkRow(
    string Model,
    int Dimensions,
    int DocumentCount,
    int ChunkCount,
    int VectorCount,
    decimal CompletionRate,
    long DocumentSizeBytes,
    double? AverageDurationMs,
    double? LatestDurationMs,
    DateTime? LatestEmbeddedAt,
    bool HasData);
