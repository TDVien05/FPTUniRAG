using FPTUniRAG.DataAccessLayer.Repositories.Chat;

namespace FPTUniRAG.BusinessLayer.Rag.Chat.Benchmarking;

/// <summary>
/// Pure aggregation over stored benchmark results. Kept separate from the runner so the
/// maths can be unit tested without any database or HTTP dependency.
/// </summary>
public static class ChatBenchmarkAggregation
{
    public static ChatBenchmarkRunSummary Summarize(ChatBenchmarkRunRecord run)
    {
        var results = run.Results;
        var successful = results.Where(result => result.IsSuccess).ToArray();

        // Latency is only meaningful for successful calls: a request that failed after
        // 30 ms would otherwise look like the fastest answer in the set.
        var latencies = successful
            .Where(result => result.ResponseTimeMs.HasValue)
            .Select(result => (double)result.ResponseTimeMs!.Value)
            .OrderBy(value => value)
            .ToArray();

        return new ChatBenchmarkRunSummary(
            run.RunId,
            run.ModelName,
            run.SubjectCode,
            run.Status,
            run.PromptCount,
            run.CompletedCount,
            successful.Length,
            results.Count == 0 ? 0 : decimal.Round(successful.Length * 100m / results.Count, 2),
            Percentile(latencies, 0.5),
            Percentile(latencies, 0.95),
            latencies.Length == 0 ? null : latencies.Average(),
            successful.Sum(result => result.PromptTokens),
            successful.Sum(result => result.CompletionTokens),
            successful.Length == 0 ? null : successful.Average(result => (double)result.TotalTokens),
            successful.Length == 0 ? null : successful.Average(result => (double)result.RetrievedChunkCount),
            run.StartedAt,
            run.CompletedAt,
            run.ErrorMessage,
            results);
    }

    /// <summary>
    /// Nearest-rank percentile over an ascending-sorted set. With a single value it returns
    /// that value rather than interpolating against nothing.
    /// </summary>
    public static double? Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return null;
        }

        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        var rank = (int)Math.Ceiling(percentile * sortedValues.Count);
        var index = Math.Clamp(rank - 1, 0, sortedValues.Count - 1);
        return sortedValues[index];
    }
}

public sealed record ChatBenchmarkRunSummary(
    Guid RunId,
    string ModelName,
    string? SubjectCode,
    string Status,
    int PromptCount,
    int CompletedCount,
    int SuccessCount,
    decimal SuccessRate,
    double? P50LatencyMs,
    double? P95LatencyMs,
    double? AverageLatencyMs,
    long PromptTokens,
    long CompletionTokens,
    double? AverageTotalTokens,
    double? AverageRetrievedChunks,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? ErrorMessage,
    IReadOnlyList<ChatBenchmarkResultRecord> Results)
{
    public bool IsFinished => Status is "completed" or "failed";

    public int ProgressPercent => PromptCount == 0
        ? 100
        : Math.Clamp(CompletedCount * 100 / PromptCount, 0, 100);
}
