using FPTUniRAG.DataAccessLayer.Repositories.Embeddings;

namespace FPTUniRAG.BusinessLayer.Rag.Embeddings;

public sealed class EmbeddingBenchmarkService : IEmbeddingBenchmarkService
{
    // pgvector stores each component as a 4-byte float; used to estimate vector storage.
    private const int BytesPerVectorComponent = 4;

    private readonly IEmbeddingRepository _embeddingRepository;
    private readonly IEmbeddingConfigurationService _configurationService;

    public EmbeddingBenchmarkService(IEmbeddingRepository embeddingRepository, IEmbeddingConfigurationService configurationService)
    {
        _embeddingRepository = embeddingRepository;
        _configurationService = configurationService;
    }

    public async Task<IReadOnlyList<EmbeddingBenchmarkRow>> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var models = _configurationService.GetAvailableModels();
        var repositoryRuns = await _embeddingRepository.GetRunsAsync(cancellationToken);
        var runs = repositoryRuns.Select(run => new RunSnapshot(run.Model, run.Dimensions, run.DocumentId,
            run.DocumentSizeBytes, run.ChunkCount, run.VectorCount, run.StartedAt, run.CompletedAt, run.Status)).ToList();

        return models.Select(model => BuildRow(model.Model, model.Dimensions, runs
            .Where(run => string.Equals(run.Model, model.Model, StringComparison.OrdinalIgnoreCase)))).ToArray();
    }

    private static EmbeddingBenchmarkRow BuildRow(
        string model,
        int dimensions,
        IEnumerable<RunSnapshot> modelRuns)
    {
        var runs = modelRuns.ToList();

        if (runs.Count == 0)
        {
            return new EmbeddingBenchmarkRow(model, 0, 0, dimensions, null, null, 0, 0, null, false);
        }

        var successful = runs.Where(run => string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase)).ToList();

        // Only the latest successful run per document counts towards totals; retries and
        // re-embeddings stay in history but must not be double-counted.
        var latestByDocument = successful
            .GroupBy(run => run.DocumentId)
            .Select(group => group.OrderByDescending(run => run.StartedAt).First())
            .ToList();

        var chunkCount = latestByDocument.Sum(run => run.ChunkCount);
        var vectorCount = latestByDocument.Sum(run => run.VectorCount);

        // Embedding time and throughput are derived from the same run set as the chunk count,
        // otherwise the throughput denominator would include superseded runs.
        var timedRuns = latestByDocument.Where(run => run.CompletedAt.HasValue).ToList();
        var embeddingTimeMs = timedRuns.Count == 0
            ? (double?)null
            : timedRuns.Sum(run => (run.CompletedAt!.Value - run.StartedAt).TotalMilliseconds);
        var timedChunks = timedRuns.Sum(run => run.ChunkCount);
        var chunksPerSecond = embeddingTimeMs is null or <= 0 || timedChunks == 0
            ? (double?)null
            : timedChunks / (embeddingTimeMs.Value / 1000d);

        var latestRun = successful.OrderByDescending(run => run.CompletedAt ?? run.StartedAt).FirstOrDefault();

        return new EmbeddingBenchmarkRow(
            model,
            latestByDocument.Count,
            chunkCount,
            dimensions,
            embeddingTimeMs,
            chunksPerSecond,
            (long)vectorCount * dimensions * BytesPerVectorComponent,
            decimal.Round(successful.Count * 100m / runs.Count, 2),
            latestRun?.CompletedAt,
            successful.Count > 0);
    }

    private sealed record RunSnapshot(
        string Model,
        int Dimensions,
        Guid DocumentId,
        long? DocumentSizeBytes,
        int ChunkCount,
        int VectorCount,
        DateTime StartedAt,
        DateTime? CompletedAt,
        string Status);
}
