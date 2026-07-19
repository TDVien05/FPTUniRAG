using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FPTUniRAG.DataAccessLayer.Repositories.Chat;

public sealed class ChatBenchmarkRepository(AppDbContext context) : IChatBenchmarkRepository
{
    public async Task<Guid> CreateRunAsync(Guid batchId, string modelName, Guid? subjectId, int promptCount, Guid executedBy, CancellationToken cancellationToken = default)
    {
        var run = new ChatBenchmarkRun
        {
            ChatBenchmarkRunId = Guid.NewGuid(),
            BatchId = batchId,
            ModelName = modelName,
            SubjectId = subjectId,
            PromptCount = promptCount,
            Status = "queued",
            StartedAt = Timestamp(),
            ExecutedBy = executedBy
        };

        context.ChatBenchmarkRuns.Add(run);
        await context.SaveChangesAsync(cancellationToken);
        return run.ChatBenchmarkRunId;
    }

    public async Task MarkRunRunningAsync(Guid runId, CancellationToken cancellationToken = default) =>
        await context.ChatBenchmarkRuns
            .Where(run => run.ChatBenchmarkRunId == runId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(run => run.Status, "running"), cancellationToken);

    public async Task AppendResultAsync(ChatBenchmarkResultInput result, CancellationToken cancellationToken = default)
    {
        context.ChatBenchmarkResults.Add(new ChatBenchmarkResult
        {
            ResultId = Guid.NewGuid(),
            ChatBenchmarkRunId = result.RunId,
            PromptText = result.PromptText,
            AnswerText = result.AnswerText,
            RetrievedChunkCount = result.RetrievedChunkCount,
            PromptTokens = result.PromptTokens,
            CompletionTokens = result.CompletionTokens,
            TotalTokens = result.TotalTokens,
            ResponseTimeMs = result.ResponseTimeMs,
            IsSuccess = result.IsSuccess,
            ErrorMessage = result.ErrorMessage,
            CreatedAt = Timestamp()
        });
        await context.SaveChangesAsync(cancellationToken);

        // Counters are incremented in SQL so progress stays correct regardless of
        // what else the tracked context happens to be holding.
        var successIncrement = result.IsSuccess ? 1 : 0;
        await context.ChatBenchmarkRuns
            .Where(run => run.ChatBenchmarkRunId == result.RunId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.CompletedCount, run => run.CompletedCount + 1)
                .SetProperty(run => run.SuccessCount, run => run.SuccessCount + successIncrement),
                cancellationToken);
    }

    public async Task CompleteRunAsync(Guid runId, string status, string? errorMessage, CancellationToken cancellationToken = default)
    {
        var completedAt = Timestamp();
        await context.ChatBenchmarkRuns
            .Where(run => run.ChatBenchmarkRunId == runId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, status)
                .SetProperty(run => run.CompletedAt, completedAt)
                .SetProperty(run => run.ErrorMessage, errorMessage),
                cancellationToken);
    }

    public async Task<IReadOnlyList<ChatBenchmarkRunRecord>> GetBatchAsync(Guid? batchId, CancellationToken cancellationToken = default)
    {
        try
        {
            var resolvedBatchId = batchId ?? await context.ChatBenchmarkRuns.AsNoTracking()
                .OrderByDescending(run => run.StartedAt)
                .Select(run => run.BatchId)
                .FirstOrDefaultAsync(cancellationToken);

            if (resolvedBatchId is null)
            {
                return [];
            }

            return await ProjectRuns(context.ChatBenchmarkRuns.AsNoTracking()
                .Where(run => run.BatchId == resolvedBatchId)
                .OrderBy(run => run.ModelName))
                .ToListAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<ChatBenchmarkBatchRecord>> GetRecentBatchesAsync(int limit, CancellationToken cancellationToken = default)
    {
        try
        {
            // Grouped over scalar columns only so the query translates; the prompt text is
            // then attached from one result row per batch.
            var batches = await context.ChatBenchmarkRuns.AsNoTracking()
                .Where(run => run.BatchId != null)
                .GroupBy(run => run.BatchId!.Value)
                .Select(group => new
                {
                    BatchId = group.Key,
                    StartedAt = group.Max(run => run.StartedAt),
                    ModelCount = group.Count()
                })
                .OrderByDescending(batch => batch.StartedAt)
                .Take(limit)
                .ToListAsync(cancellationToken);

            if (batches.Count == 0)
            {
                return [];
            }

            var batchIds = batches.Select(batch => batch.BatchId).ToList();
            var details = await context.ChatBenchmarkRuns.AsNoTracking()
                .Where(run => run.BatchId != null && batchIds.Contains(run.BatchId!.Value))
                .Select(run => new
                {
                    BatchId = run.BatchId!.Value,
                    SubjectCode = run.Subject != null ? run.Subject.SubjectCode : null,
                    PromptText = run.Results.Select(result => result.PromptText).FirstOrDefault()
                })
                .ToListAsync(cancellationToken);

            return batches
                .Select(batch =>
                {
                    var detail = details.FirstOrDefault(item => item.BatchId == batch.BatchId
                        && !string.IsNullOrWhiteSpace(item.PromptText))
                        ?? details.FirstOrDefault(item => item.BatchId == batch.BatchId);

                    return new ChatBenchmarkBatchRecord(
                        batch.BatchId,
                        batch.StartedAt,
                        detail?.SubjectCode,
                        detail?.PromptText,
                        batch.ModelCount);
                })
                .ToArray();
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return [];
        }
    }

    private static IQueryable<ChatBenchmarkRunRecord> ProjectRuns(IQueryable<ChatBenchmarkRun> query) =>
        query.Select(run => new ChatBenchmarkRunRecord(
            run.ChatBenchmarkRunId,
            run.ModelName,
            run.SubjectId,
            run.Subject != null ? run.Subject.SubjectCode : null,
            run.PromptCount,
            run.CompletedCount,
            run.SuccessCount,
            run.Status,
            run.StartedAt,
            run.CompletedAt,
            run.ErrorMessage,
            run.Results
                .OrderBy(result => result.CreatedAt)
                .Select(result => new ChatBenchmarkResultRecord(
                    result.ResultId, result.PromptText, result.AnswerText, result.RetrievedChunkCount,
                    result.PromptTokens, result.CompletionTokens, result.TotalTokens, result.ResponseTimeMs,
                    result.IsSuccess, result.ErrorMessage))
                .ToList()));

    private static DateTime Timestamp() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
}
