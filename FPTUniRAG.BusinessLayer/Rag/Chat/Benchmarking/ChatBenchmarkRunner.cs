using FPTUniRAG.DataAccessLayer.Entities;
using FPTUniRAG.DataAccessLayer.Repositories.Chat;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FPTUniRAG.BusinessLayer.Rag.Chat.Benchmarking;

public sealed class ChatBenchmarkRunner : IChatBenchmarkRunner
{
    private const int RetrievalLimit = 8;
    private const int MaxPromptsPerRun = 25;
    private const int MaxModelsPerRun = 5;

    private readonly IChatBenchmarkRepository _benchmarkRepository;
    private readonly IStudentChatRepository _chatRepository;
    private readonly IStudentChunkRetrievalService _retrievalService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<ChatBenchmarkRunner> _logger;

    public ChatBenchmarkRunner(
        IChatBenchmarkRepository benchmarkRepository,
        IStudentChatRepository chatRepository,
        IStudentChunkRetrievalService retrievalService,
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime applicationLifetime,
        ILogger<ChatBenchmarkRunner> logger)
    {
        _benchmarkRepository = benchmarkRepository;
        _chatRepository = chatRepository;
        _retrievalService = retrievalService;
        _scopeFactory = scopeFactory;
        _applicationLifetime = applicationLifetime;
        _logger = logger;
    }

    public async Task<ChatBenchmarkStartResult> StartAsync(
        Guid subjectId,
        IReadOnlyList<string> modelNames,
        string promptText,
        Guid adminUserId,
        CancellationToken cancellationToken = default)
    {
        var prompt = promptText?.Trim() ?? string.Empty;
        if (prompt.Length == 0)
        {
            return ChatBenchmarkStartResult.Failure("Write a prompt to benchmark.");
        }

        var models = modelNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (models.Length == 0)
        {
            return ChatBenchmarkStartResult.Failure("Select at least one model to benchmark.");
        }

        if (models.Length > MaxModelsPerRun)
        {
            return ChatBenchmarkStartResult.Failure($"Benchmark at most {MaxModelsPerRun} models at a time.");
        }

        var subject = await _chatRepository.GetSubjectAsync(subjectId, cancellationToken);
        if (subject is null)
        {
            return ChatBenchmarkStartResult.Failure("That subject no longer exists.");
        }

        if (!await _retrievalService.SubjectHasUsableContentAsync(subjectId, cancellationToken))
        {
            return ChatBenchmarkStartResult.Failure(
                $"{subject.SubjectCode} has no embedded documents yet, so there is nothing to retrieve against.");
        }

        // Every model started by this press shares a batch id, so the results view can
        // show this benchmark alone rather than accumulating earlier ones.
        var batchId = Guid.NewGuid();
        var runIds = new List<Guid>(models.Length);
        foreach (var model in models)
        {
            runIds.Add(await _benchmarkRepository.CreateRunAsync(batchId, model, subjectId, 1, adminUserId, cancellationToken));
        }

        var stoppingToken = _applicationLifetime.ApplicationStopping;
        _ = Task.Run(() => ExecuteAsync(subject, models, prompt, runIds, stoppingToken), stoppingToken);

        return ChatBenchmarkStartResult.Success(
            $"Benchmarking this prompt against {models.Length} model(s) — {models.Length} request(s).",
            runIds);
    }

    private async Task ExecuteAsync(
        ChatSubjectRecord subject,
        IReadOnlyList<string> models,
        string promptText,
        IReadOnlyList<Guid> runIds,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var benchmarkRepository = scope.ServiceProvider.GetRequiredService<IChatBenchmarkRepository>();
        var retrievalService = scope.ServiceProvider.GetRequiredService<IStudentChunkRetrievalService>();
        var completionService = scope.ServiceProvider.GetRequiredService<IOpenRouterChatCompletionService>();

        // Retrieval happens once and the resulting context is reused for every model, so
        // each model answers byte-identical input — the point of a controlled test.
        StudentChatService.StudentChatPromptContext? promptContext = null;
        var chunkCount = 0;
        try
        {
            var retrieval = await retrievalService.RetrieveRelevantChunksAsync(
                subject.SubjectId, promptText, RetrievalLimit, cancellationToken);

            promptContext = StudentChatService.BuildCompletionMessages(
                subject.SubjectCode,
                subject.SubjectName,
                [],
                retrieval.Chunks,
                promptText);

            chunkCount = retrieval.Chunks.Count;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Benchmark retrieval failed for subject {SubjectId}", subject.SubjectId);
        }

        for (var index = 0; index < models.Count; index++)
        {
            var model = models[index];
            var runId = runIds[index];

            try
            {
                await benchmarkRepository.MarkRunRunningAsync(runId, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
                await RunSinglePromptAsync(benchmarkRepository, completionService, runId, model, promptText, promptContext, chunkCount, cancellationToken);

                await benchmarkRepository.CompleteRunAsync(runId, "completed", null, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await CompleteQuietlyAsync(benchmarkRepository, runId, "failed", "The application shut down before the run finished.");
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Chat benchmark run {RunId} for model {Model} failed", runId, model);
                await CompleteQuietlyAsync(benchmarkRepository, runId, "failed", exception.Message);
            }
        }
    }

    private static async Task RunSinglePromptAsync(
        IChatBenchmarkRepository benchmarkRepository,
        IOpenRouterChatCompletionService completionService,
        Guid runId,
        string model,
        string promptText,
        StudentChatService.StudentChatPromptContext? context,
        int chunkCount,
        CancellationToken cancellationToken)
    {
        if (context is null)
        {
            await benchmarkRepository.AppendResultAsync(new ChatBenchmarkResultInput(
                runId, promptText, null, 0, 0, 0, 0, null, false,
                "Retrieval failed for this prompt."), cancellationToken);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var completion = await completionService.StreamCompletionAsync(
                context.Messages,
                (_, _) => Task.CompletedTask,
                model,
                cancellationToken);
            stopwatch.Stop();

            await benchmarkRepository.AppendResultAsync(new ChatBenchmarkResultInput(
                runId,
                promptText,
                completion.Content,
                chunkCount,
                completion.PromptTokens,
                completion.CompletionTokens,
                completion.TotalTokens,
                (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue),
                true,
                null), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();

            // A failure is a result, not an abort: it is what makes success rate real.
            await benchmarkRepository.AppendResultAsync(new ChatBenchmarkResultInput(
                runId,
                promptText,
                null,
                chunkCount,
                0,
                0,
                0,
                (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue),
                false,
                Truncate(exception.Message, 500)), CancellationToken.None);
        }
    }

    private async Task CompleteQuietlyAsync(IChatBenchmarkRepository repository, Guid runId, string status, string? errorMessage)
    {
        try
        {
            await repository.CompleteRunAsync(runId, status, errorMessage, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Unable to mark chat benchmark run {RunId} as {Status}", runId, status);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
