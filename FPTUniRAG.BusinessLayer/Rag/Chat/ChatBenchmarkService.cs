using FPTUniRAG.BusinessLayer.Rag.Chat.Benchmarking;
using FPTUniRAG.DataAccessLayer.Repositories.Chat;

namespace FPTUniRAG.BusinessLayer.Rag.Chat;

public sealed class ChatBenchmarkService : IChatBenchmarkService
{
    private const string ChatFeatureName = "student_chat";
    private const int MaxSessionRows = 12;

    private readonly IStudentChatRepository _chatRepository;
    private readonly IChatBenchmarkRepository _benchmarkRepository;

    public ChatBenchmarkService(IStudentChatRepository chatRepository, IChatBenchmarkRepository benchmarkRepository)
    {
        _chatRepository = chatRepository;
        _benchmarkRepository = benchmarkRepository;
    }

    public async Task<IReadOnlyList<ChatBenchmarkRunSummary>> GetBatchSummariesAsync(Guid? batchId, CancellationToken cancellationToken = default)
    {
        var runs = await _benchmarkRepository.GetBatchAsync(batchId, cancellationToken);
        return runs.Select(ChatBenchmarkAggregation.Summarize).ToArray();
    }

    public async Task<IReadOnlyList<ChatBenchmarkBatchRecord>> GetRecentBatchesAsync(int limit, CancellationToken cancellationToken = default) =>
        await _benchmarkRepository.GetRecentBatchesAsync(limit, cancellationToken);

    public async Task<ChatBenchmarkSummary> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var runs = await _chatRepository.GetUsageRunsAsync(ChatFeatureName, cancellationToken);

        var modelRows = runs
            .Where(run => !string.IsNullOrWhiteSpace(run.Model))
            .GroupBy(run => run.Model!, StringComparer.OrdinalIgnoreCase)
            .Select(BuildModelRow)
            .OrderByDescending(row => row.LastUsedAt)
            .ToArray();

        var sessionRows = runs
            .Where(run => run.SessionId.HasValue)
            .GroupBy(run => run.SessionId!.Value)
            .Select(BuildSessionRow)
            .OrderByDescending(row => row.LastUsedAt)
            .Take(MaxSessionRows)
            .ToArray();

        return new ChatBenchmarkSummary(modelRows, sessionRows);
    }

    private static ChatBenchmarkRow BuildModelRow(IGrouping<string, ChatUsageRunRecord> group)
    {
        var runs = group.ToList();
        var responseTimes = runs
            .Where(run => run.ResponseTimeMs.HasValue)
            .Select(run => (double)run.ResponseTimeMs!.Value)
            .ToArray();

        return new ChatBenchmarkRow(
            group.Key,
            runs.Sum(run => run.RequestCount),
            runs.Sum(run => run.PromptTokens),
            runs.Sum(run => run.CompletionTokens),
            runs.Sum(run => run.TotalTokens),
            runs.Average(run => run.PromptTokens),
            runs.Average(run => run.CompletionTokens),
            responseTimes.Length == 0 ? null : responseTimes.Average(),
            runs.Max(run => run.UsedAt));
    }

    private static ChatSessionBenchmarkRow BuildSessionRow(IGrouping<Guid, ChatUsageRunRecord> group)
    {
        var runs = group.ToList();
        var latest = runs.OrderByDescending(run => run.UsedAt).First();
        var label = !string.IsNullOrWhiteSpace(latest.StudentName)
            ? (string.IsNullOrWhiteSpace(latest.SubjectCode) ? latest.StudentName : $"{latest.StudentName} · {latest.SubjectCode}")
            : $"Session {group.Key.ToString()[..8]}";

        return new ChatSessionBenchmarkRow(
            group.Key,
            label,
            latest.Model,
            runs.Sum(run => run.PromptTokens),
            runs.Sum(run => run.CompletionTokens),
            runs.Sum(run => run.TotalTokens),
            runs.Sum(run => run.RequestCount),
            runs.Max(run => run.UsedAt));
    }
}
