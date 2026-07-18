using FPTUniRAG.DataAccessLayer.Repositories.Documents;

namespace FPTUniRAG.BusinessLayer.Rag.Ingestion;

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
        var repository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var pendingDocumentIds = await repository.GetPendingDocumentIdsAsync(cancellationToken);

        foreach (var documentId in pendingDocumentIds)
        {
            await _queue.QueueAsync(documentId, cancellationToken);
        }
    }

    private async IAsyncEnumerable<Guid> ReadQueueAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Guid documentId;

            try
            {
                documentId = await _queue.DequeueAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                yield break;
            }

            yield return documentId;
        }
    }
}
