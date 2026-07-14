using FPTUniRAG.DataAccessLayer.Context;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.BusinessLayer.Services;

public sealed class DocumentProcessingBackgroundService : BackgroundService
{
    private readonly IDocumentProcessingQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentProcessingBackgroundService> _logger;

    public DocumentProcessingBackgroundService(
        IDocumentProcessingQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentProcessingBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverPendingJobsAsync(stoppingToken);

        await foreach (var documentId in ReadQueueAsync(stoppingToken))
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var workflow = scope.ServiceProvider.GetRequiredService<ITeacherDocumentWorkflowService>();
                await workflow.ProcessDocumentAsync(documentId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Document processing failed for {DocumentId}", documentId);
            }
        }
    }

    private async Task RecoverPendingJobsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pendingDocumentIds = await dbContext.ProcessingJobs
            .Where(job => job.JobStatus == "queued" || job.JobStatus == "processing")
            .Select(job => job.DocumentId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var documentId in pendingDocumentIds)
        {
            await _queue.QueueAsync(documentId, cancellationToken);
        }
    }

    private async IAsyncEnumerable<Guid> ReadQueueAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            yield return await _queue.DequeueAsync(cancellationToken);
        }
    }
}
