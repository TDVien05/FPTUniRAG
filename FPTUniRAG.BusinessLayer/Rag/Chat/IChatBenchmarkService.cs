namespace FPTUniRAG.BusinessLayer.Rag.Chat;

public interface IChatBenchmarkService
{
    Task<ChatBenchmarkSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
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
