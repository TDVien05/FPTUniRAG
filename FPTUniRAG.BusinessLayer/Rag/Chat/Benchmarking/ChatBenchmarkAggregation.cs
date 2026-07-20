using FPTUniRAG.DataAccessLayer.Repositories.Chat;

namespace FPTUniRAG.BusinessLayer.Rag.Chat.Benchmarking;

/// <summary>
/// Pure aggregation over stored benchmark results. Kept separate from the runner so the
/// maths can be unit tested without any database or HTTP dependency.
/// </summary>
public static class ChatBenchmarkAggregation
{
    public static IReadOnlyList<ChatBenchmarkHealthPoint> SummarizeHealth(IEnumerable<ChatBenchmarkRunRecord> runs) =>
        runs
            .Where(run => run.BatchId.HasValue)
            .GroupBy(run => run.BatchId!.Value)
            .Where(group => group.All(run => run.IsFinished()))
            .Select(group => BuildHealthPoint(group.Key, group.ToArray()))
            .OrderBy(point => point.StartedAt)
            .ToArray();

    public static ChatBenchmarkRunSummary Summarize(ChatBenchmarkRunRecord run)
    {
        var storedResults = run.Results;
        var successful = storedResults.Where(result => result.IsSuccess).ToArray();

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
            storedResults.Count == 0 ? 0 : decimal.Round(successful.Length * 100m / storedResults.Count, 2),
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
            storedResults.Select(MapResult).ToArray());
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

    private static ChatBenchmarkHealthPoint BuildHealthPoint(Guid batchId, IReadOnlyList<ChatBenchmarkRunRecord> runs)
    {
        var results = runs.SelectMany(run => run.Results).ToArray();
        var successful = results.Where(result => result.IsSuccess).ToArray();
        var latencies = successful
            .Where(result => result.ResponseTimeMs.HasValue)
            .Select(result => (double)result.ResponseTimeMs!.Value)
            .OrderBy(value => value)
            .ToArray();
        var completedAt = runs.Max(run => run.CompletedAt);
        var startedAt = runs.Min(run => run.StartedAt);

        return new ChatBenchmarkHealthPoint(
            batchId,
            startedAt,
            runs.Select(run => run.SubjectCode).FirstOrDefault(code => !string.IsNullOrWhiteSpace(code)),
            results.Length,
            successful.Length,
            results.Length - successful.Length,
            runs.Count(run => run.Status == "failed"),
            results.Length == 0 ? 0 : decimal.Round(successful.Length * 100m / results.Length, 2),
            Percentile(latencies, 0.5),
            Percentile(latencies, 0.95),
            successful.Sum(result => result.PromptTokens),
            successful.Sum(result => result.CompletionTokens),
            successful.Length == 0 ? null : successful.Average(result => (double)result.RetrievedChunkCount),
            completedAt.HasValue ? (long)Math.Round((completedAt.Value - startedAt).TotalMilliseconds) : null);
    }

    private static bool IsFinished(this ChatBenchmarkRunRecord run) => run.Status is "completed" or "failed";

    private static ChatBenchmarkResult MapResult(ChatBenchmarkResultRecord result) =>
        new(
            result.ResultId,
            result.PromptText,
            result.AnswerText,
            result.RetrievedChunkCount,
            result.PromptTokens,
            result.CompletionTokens,
            result.TotalTokens,
            result.ResponseTimeMs,
            result.IsSuccess,
            result.ErrorMessage);
}

public sealed record ChatBenchmarkResult(
    Guid ResultId,
    string PromptText,
    string? AnswerText,
    int RetrievedChunkCount,
    long PromptTokens,
    long CompletionTokens,
    long TotalTokens,
    int? ResponseTimeMs,
    bool IsSuccess,
    string? ErrorMessage);

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
    IReadOnlyList<ChatBenchmarkResult> Results)
{
    public bool IsFinished => Status is "completed" or "failed";

    public int ProgressPercent => PromptCount == 0
        ? 100
        : Math.Clamp(CompletedCount * 100 / PromptCount, 0, 100);
}
