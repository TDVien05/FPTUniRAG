using FPTUniRAG.DataAccessLayer.Context;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.Services;

public sealed class EmbeddingBenchmarkService : IEmbeddingBenchmarkService
{
    private readonly AppDbContext _dbContext;
    private readonly IEmbeddingConfigurationService _configurationService;

    public EmbeddingBenchmarkService(AppDbContext dbContext, IEmbeddingConfigurationService configurationService)
    {
        _dbContext = dbContext;
        _configurationService = configurationService;
    }

    public async Task<IReadOnlyList<EmbeddingBenchmarkRow>> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var models = _configurationService.GetAvailableModels();
        var runs = await _dbContext.DocumentEmbeddingRuns
            .AsNoTracking()
            .OrderByDescending(run => run.StartedAt)
            .Select(run => new RunSnapshot(
                run.EmbeddingModel,
                run.EmbeddingDimensions,
                run.DocumentId,
                run.DocumentSizeBytes,
                run.ChunkCount,
                run.VectorCount,
                run.StartedAt,
                run.CompletedAt,
                run.Status))
            .ToListAsync(cancellationToken);

        return models.Select(model => BuildRow(model.Model, model.Dimensions, runs
            .Where(run => string.Equals(run.Model, model.Model, StringComparison.OrdinalIgnoreCase)))).ToArray();
    }

    private static EmbeddingBenchmarkRow BuildRow(
        string model,
        int dimensions,
        IEnumerable<RunSnapshot> modelRuns)
    {
        var runs = modelRuns.ToList();
        var successful = runs.Where(run => string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase)).ToList();
        var latestByDocument = successful
            .GroupBy(run => run.DocumentId)
            .Select(group => group.OrderByDescending(run => run.StartedAt).First())
            .ToList();

        if (runs.Count == 0)
        {
            return new EmbeddingBenchmarkRow(model, dimensions, 0, 0, 0, 0, 0, null, null, null, false);
        }

        var latestRun = successful.OrderByDescending(run => run.CompletedAt ?? run.StartedAt).FirstOrDefault();
        var durations = successful
            .Where(run => run.CompletedAt.HasValue)
            .Select(run => (run.CompletedAt!.Value - run.StartedAt).TotalMilliseconds)
            .ToArray();
        var totalDocuments = runs.Select(run => run.DocumentId).Distinct().Count();

        return new EmbeddingBenchmarkRow(
            model,
            dimensions,
            latestByDocument.Count,
            latestByDocument.Sum(run => run.ChunkCount),
            latestByDocument.Sum(run => run.VectorCount),
            totalDocuments == 0 ? 0 : decimal.Round(latestByDocument.Count * 100m / totalDocuments, 2),
            latestByDocument.Sum(run => run.DocumentSizeBytes ?? 0),
            durations.Length == 0 ? null : durations.Average(),
            latestRun?.CompletedAt is null
                ? null
                : (latestRun.CompletedAt.Value - latestRun.StartedAt).TotalMilliseconds,
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
