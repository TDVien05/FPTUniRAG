namespace FPTUniRAG.DataAccessLayer.Repositories.Chat;

public interface IChatBenchmarkRepository
{
    Task<Guid> CreateRunAsync(Guid batchId, string modelName, Guid? subjectId, int promptCount, Guid executedBy, CancellationToken cancellationToken = default);

    Task MarkRunRunningAsync(Guid runId, CancellationToken cancellationToken = default);

    Task AppendResultAsync(ChatBenchmarkResultInput result, CancellationToken cancellationToken = default);

    Task CompleteRunAsync(Guid runId, string status, string? errorMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs from one benchmark press. With no batch id the most recent one is returned, so
    /// running three models yields exactly three rows rather than those three plus history.
    /// </summary>
    Task<IReadOnlyList<ChatBenchmarkRunRecord>> GetBatchAsync(Guid? batchId, CancellationToken cancellationToken = default);

    /// <summary>Headline of each past benchmark, newest first, for the history picker.</summary>
    Task<IReadOnlyList<ChatBenchmarkBatchRecord>> GetRecentBatchesAsync(int limit, CancellationToken cancellationToken = default);

}

public sealed record ChatBenchmarkBatchRecord(
    Guid BatchId,
    DateTime StartedAt,
    string? SubjectCode,
    string? PromptText,
    int ModelCount);

public sealed record ChatBenchmarkResultInput(
    Guid RunId,
    string PromptText,
    string? AnswerText,
    int RetrievedChunkCount,
    long PromptTokens,
    long CompletionTokens,
    long TotalTokens,
    int? ResponseTimeMs,
    bool IsSuccess,
    string? ErrorMessage);

public sealed record ChatBenchmarkRunRecord(
    Guid RunId,
    string ModelName,
    Guid? SubjectId,
    string? SubjectCode,
    int PromptCount,
    int CompletedCount,
    int SuccessCount,
    string Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? ErrorMessage,
    IReadOnlyList<ChatBenchmarkResultRecord> Results);

public sealed record ChatBenchmarkResultRecord(
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
