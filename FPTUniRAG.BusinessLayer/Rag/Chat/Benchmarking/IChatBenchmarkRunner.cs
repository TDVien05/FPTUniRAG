using FPTUniRAG.BusinessLayer.Common;

namespace FPTUniRAG.BusinessLayer.Rag.Chat.Benchmarking;

public interface IChatBenchmarkRunner
{
    /// <summary>
    /// Queues one run per model. Returns immediately; progress is tracked in the database
    /// so the page can poll it and it survives an application restart.
    /// </summary>
    Task<ChatBenchmarkStartResult> StartAsync(
        Guid subjectId,
        IReadOnlyList<string> modelNames,
        string promptText,
        Guid adminUserId,
        CancellationToken cancellationToken = default);
}

public sealed record ChatBenchmarkStartResult(bool Succeeded, string Message, IReadOnlyList<Guid> RunIds)
{
    public static ChatBenchmarkStartResult Failure(string message) => new(false, message, []);

    public static ChatBenchmarkStartResult Success(string message, IReadOnlyList<Guid> runIds) => new(true, message, runIds);

    public OperationResult ToOperationResult() => new(Succeeded, Message);
}
