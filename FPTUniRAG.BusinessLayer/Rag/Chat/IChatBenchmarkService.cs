using FPTUniRAG.BusinessLayer.Rag.Chat.Benchmarking;
using FPTUniRAG.DataAccessLayer.Repositories.Chat;

namespace FPTUniRAG.BusinessLayer.Rag.Chat;

public interface IChatBenchmarkService
{
    /// <summary>Observational production usage, grouped by model and by session.</summary>
    Task<ChatBenchmarkSummary> GetSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>One benchmark press — one row per model. Defaults to the most recent.</summary>
    Task<IReadOnlyList<ChatBenchmarkRunSummary>> GetBatchSummariesAsync(Guid? batchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatBenchmarkBatchRecord>> GetRecentBatchesAsync(int limit, CancellationToken cancellationToken = default);
}

public sealed record ChatBenchmarkSummary(
    IReadOnlyList<ChatBenchmarkRow> ModelRows,
    IReadOnlyList<ChatSessionBenchmarkRow> SessionRows);

public sealed record ChatBenchmarkRow(
    string Model,
    int RequestCount,
    long PromptTokens,
    long CompletionTokens,
    long TotalTokens,
    double AveragePromptTokens,
    double AverageCompletionTokens,
    double? AverageResponseTimeMs,
    DateTime? LastUsedAt);

public sealed record ChatSessionBenchmarkRow(
    Guid SessionId,
    string SessionLabel,
    string? Model,
    long PromptTokens,
    long CompletionTokens,
    long TotalTokens,
    int RequestCount,
    DateTime LastUsedAt);
