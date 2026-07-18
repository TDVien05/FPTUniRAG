using FPTUniRAG.BusinessLayer.Rag.Chunking;
using FPTUniRAG.BusinessLayer.Rag.Configuration;
using FPTUniRAG.BusinessLayer.Rag.Embeddings;
using FPTUniRAG.BusinessLayer.Subjects;
using FPTUniRAG.DataAccessLayer.Repositories.Documents;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.Extensions.Options;

namespace FPTUniRAG.BusinessLayer.Rag.Ingestion;

public sealed class TeacherDocumentWorkflowService : ITeacherDocumentWorkflowService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentTextExtractor _documentTextExtractor;
    private readonly IFixedChunkingService _fixedChunkingService;
    private readonly ISemanticChunkingService _semanticChunkingService;
    private readonly IOpenRouterEmbeddingService _openRouterEmbeddingService;
    private readonly IChunkEmbeddingStore _chunkEmbeddingStore;
    private readonly RagIngestionOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly IDocumentProcessingQueue _processingQueue;
    private readonly ILogger<TeacherDocumentWorkflowService> _logger;
    private readonly string _embeddingTableName;

    public TeacherDocumentWorkflowService(
        IDocumentRepository documentRepository,
        IDocumentTextExtractor documentTextExtractor,
        IFixedChunkingService fixedChunkingService,
        ISemanticChunkingService semanticChunkingService,
        IOpenRouterEmbeddingService openRouterEmbeddingService,
        IChunkEmbeddingStore chunkEmbeddingStore,
        IOptions<RagIngestionOptions> options,
        IWebHostEnvironment environment,
        IDocumentProcessingQueue processingQueue,
        ILogger<TeacherDocumentWorkflowService> logger)
    {
        _documentRepository = documentRepository;
        _documentTextExtractor = documentTextExtractor;
        _fixedChunkingService = fixedChunkingService;
        _semanticChunkingService = semanticChunkingService;
        _openRouterEmbeddingService = openRouterEmbeddingService;
        _chunkEmbeddingStore = chunkEmbeddingStore;
        _options = options.Value;
        _environment = environment;
        _processingQueue = processingQueue;
        _logger = logger;
        _embeddingTableName = ResolveTableName(_options.PostgresVector.TableName);
    }

    public async Task<TeacherUploadContextDto?> GetUploadContextAsync(
        string teacherEmail,
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        var link = await _documentRepository.GetManagedSubjectAsync(teacherEmail, subjectId, cancellationToken);
        var subject = link?.Subject;

        return subject is null
            ? null
            : new TeacherUploadContextDto(
                subject.SubjectId,
                subject.SubjectCode,
                subject.SubjectName,
                subject.Description,
                SubjectChunkingStrategies.Normalize(subject.DefaultChunkingStrategy),
                subject.DefaultFixedChunkSize,
                NormalizeAllowedFileTypes());
    }

    public async Task<TeacherDocumentUploadResult> UploadAsync(
        string teacherEmail,
        TeacherDocumentUploadCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.File is null || command.File.Length == 0)
        {
            return new TeacherDocumentUploadResult(false, "Please choose a file to upload.", null);
        }

        var subjectLink = await _documentRepository.GetManagedSubjectAsync(teacherEmail, command.SubjectId, cancellationToken);

        if (subjectLink is null)
        {
            return new TeacherDocumentUploadResult(false, "You do not manage the selected subject.", null);
        }

        var normalizedExtension = Path.GetExtension(command.File.FileName).ToLowerInvariant();
        var allowedFileTypes = NormalizeAllowedFileTypes();
        if (!allowedFileTypes.Contains(normalizedExtension, StringComparer.OrdinalIgnoreCase))
        {
            return new TeacherDocumentUploadResult(
                false,
                $"Unsupported file type '{normalizedExtension}'. Allowed types: {string.Join(", ", allowedFileTypes)}.",
                null);
        }

        var normalizedChapterTitle = command.ChapterTitle.Trim();
        var chapter = await _documentRepository.FindChapterAsync(command.SubjectId, normalizedChapterTitle, cancellationToken);

        if (chapter is not null)
        {
            if (await _documentRepository.ChapterHasDocumentAsync(chapter.ChapterId, cancellationToken))
            {
                return new TeacherDocumentUploadResult(
                    false,
                    "This chapter already has a document. Choose a new chapter name or remove the existing document first.",
                    null);
            }
        }
        else
        {
            chapter = await _documentRepository.CreateChapterAsync(
                command.SubjectId, normalizedChapterTitle, CreateDatabaseTimestamp(), cancellationToken);
        }

        var resolvedChunking = ResolveChunkingConfiguration(subjectLink.Subject);
        var relativeFilePath = await SaveUploadedFileAsync(command.File, subjectLink.Subject.SubjectCode, cancellationToken);
        var document = new Document
        {
            DocumentId = Guid.NewGuid(),
            SubjectId = command.SubjectId,
            ChapterId = chapter.ChapterId,
            Title = command.Title.Trim(),
            FileUrl = relativeFilePath,
            FileType = normalizedExtension,
            ChunkingStrategy = resolvedChunking.Strategy,
            ChunkSize = resolvedChunking.StoredChunkSize,
            ChunkOverlap = resolvedChunking.StoredChunkOverlap,
            UploadedTeacher = subjectLink.TeacherId,
            Status = "processing",
            CreatedAt = CreateDatabaseTimestamp()
        };

        var job = new ProcessingJob
        {
            JobId = Guid.NewGuid(),
            DocumentId = document.DocumentId,
            JobStatus = "queued",
            ProgressPercent = 0,
            ProcessingStage = "queued"
        };

        document.Status = "queued";

        await _documentRepository.AddDocumentAsync(document, job, cancellationToken);

        await _processingQueue.QueueAsync(document.DocumentId, cancellationToken);
        return new TeacherDocumentUploadResult(true, "Document uploaded and queued for processing.", document.DocumentId);
    }

    public async Task<TeacherDocumentUploadResult> RetryEmbeddingSyncAsync(
        string teacherEmail,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetManagedDocumentAsync(teacherEmail, documentId, cancellationToken);

        if (document is null)
        {
            return new TeacherDocumentUploadResult(false, "The selected document is unavailable.", null);
        }

        var hasChunks = await _documentRepository.HasChunksAsync(document.DocumentId, cancellationToken);

        if (!hasChunks)
        {
            return new TeacherDocumentUploadResult(false, "This document does not have stored chunks to retry.", document.DocumentId);
        }

        var processingJob = document.ProcessingJobs
            .OrderByDescending(item => item.StartedAt)
            .FirstOrDefault();

        if (processingJob is null)
        {
            processingJob = new ProcessingJob
            {
                JobId = Guid.NewGuid(),
                DocumentId = document.DocumentId,
                JobStatus = "processing",
                StartedAt = CreateDatabaseTimestamp()
            };
        }
        else
        {
            processingJob.JobStatus = "queued";
            processingJob.StartedAt = null;
            processingJob.FinishedAt = null;
            processingJob.ErrorMessage = null;
        }

        processingJob.ProgressPercent = 0;
        processingJob.ProcessingStage = "queued";
        document.Status = "queued";
        await _documentRepository.QueueDocumentAsync(document, processingJob, cancellationToken);

        await _processingQueue.QueueAsync(document.DocumentId, cancellationToken);
        return new TeacherDocumentUploadResult(true, "Embedding sync queued for processing.", document.DocumentId);
    }

    public async Task<TeacherDocumentUploadResult> DeleteChapterAsync(
        string teacherEmail,
        Guid subjectId,
        Guid chapterId,
        CancellationToken cancellationToken = default)
    {
        var managesSubject = await _documentRepository.ManagesSubjectAsync(teacherEmail, subjectId, cancellationToken);

        if (!managesSubject)
        {
            return new TeacherDocumentUploadResult(false, "You do not manage the selected subject.", null);
        }

        var chapter = await _documentRepository.GetChapterWithDocumentAsync(subjectId, chapterId, cancellationToken);

        if (chapter is null)
        {
            return new TeacherDocumentUploadResult(false, "The selected chapter is unavailable.", null);
        }

        var documentId = chapter.Document?.DocumentId;
        var fileUrl = chapter.Document?.FileUrl;

        await _documentRepository.DeleteChapterAsync(chapterId, documentId, _embeddingTableName, cancellationToken);

        if (!string.IsNullOrWhiteSpace(fileUrl))
        {
            try
            {
                var absoluteFilePath = GetAbsoluteDocumentPath(fileUrl);
                if (File.Exists(absoluteFilePath))
                {
                    File.Delete(absoluteFilePath);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Chapter {ChapterId} was deleted but its uploaded file could not be removed.", chapterId);
            }
        }

        return new TeacherDocumentUploadResult(true, $"Chapter '{chapter.ChapterTitle}' and its related data were deleted.", null);
    }

    public async Task<TeacherDocumentProcessingStatusDto?> GetProcessingStatusAsync(
        string teacherEmail,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetManagedDocumentAsync(teacherEmail, documentId, cancellationToken);
        var job = document?.ProcessingJobs.OrderByDescending(item => item.StartedAt).FirstOrDefault();

        if (document is null || job is null)
        {
            return null;
        }

        var isQueued = job.JobStatus == "queued";
        var queuePosition = isQueued ? _processingQueue.GetQueuePosition(document.DocumentId) : null;

        return new TeacherDocumentProcessingStatusDto(document.DocumentId,
            job.JobStatus ?? document.Status ?? "unknown", job.ProgressPercent,
            job.ProcessingStage ?? job.JobStatus ?? document.Status ?? "unknown", job.ErrorMessage,
            job.JobStatus == "completed", job.JobStatus == "failed",
            queuePosition, _processingQueue.QueueDepth);
    }

    public async Task ProcessDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetDocumentForProcessingAsync(documentId, cancellationToken);

        if (document is null)
        {
            return;
        }

        var job = document.ProcessingJobs
            .OrderByDescending(item => item.StartedAt ?? DateTime.MinValue)
            .FirstOrDefault();

        if (job is null)
        {
            throw new InvalidOperationException($"No processing job exists for document {documentId}.");
        }

        job.JobStatus = "processing";
        job.StartedAt ??= CreateDatabaseTimestamp();
        job.FinishedAt = null;
        job.ErrorMessage = null;
        document.Status = "processing";
        await UpdateProgressAsync(document, job, 0, "queued", cancellationToken);

        DocumentEmbeddingRun? embeddingRun = null;
        try
        {
            var orderedChunks = (await _documentRepository.GetChunksAsync(documentId, cancellationToken)).ToList();

            if (orderedChunks.Count == 0)
            {
                await UpdateProgressAsync(document, job, 15, "extracting", cancellationToken);
                var filePath = GetAbsoluteDocumentPath(document.FileUrl);
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException("The stored document file could not be found.", filePath);
                }

                string content;
                await using (var stream = File.OpenRead(filePath))
                {
                    content = await _documentTextExtractor.ExtractTextAsync(
                        stream,
                        Path.GetFileName(filePath),
                        async (processedPages, totalPages, token) =>
                        {
                            var percent = totalPages <= 0
                                ? 15
                                : Math.Clamp(15 + processedPages * 20 / totalPages, 15, 34);
                            await UpdateProgressAsync(document, job, percent, "extracting", token);
                        },
                        cancellationToken);
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    throw new InvalidOperationException("No readable content was extracted from the uploaded file.");
                }

                await UpdateProgressAsync(document, job, 35, "chunking", cancellationToken);
                var chunkContents = document.ChunkingStrategy == SubjectChunkingStrategies.Semantic
                    ? _semanticChunkingService.CreateChunks(content, _options.Semantic.MaxChunkSize, _options.Semantic.MinChunkSize)
                    : _fixedChunkingService.CreateChunks(content, document.ChunkSize, document.ChunkOverlap);

                if (chunkContents.Count == 0)
                {
                    throw new InvalidOperationException("The uploaded document did not produce any chunks.");
                }

                orderedChunks = chunkContents
                    .Select((chunkContent, index) => new Chunk
                    {
                        ChunkId = Guid.NewGuid(),
                        DocumentId = document.DocumentId,
                        ChunkIndex = index,
                        Content = chunkContent,
                        CreatedAt = CreateDatabaseTimestamp()
                    })
                    .ToList();
                await _documentRepository.ReplaceChunksAsync(documentId, orderedChunks, cancellationToken);
            }
            else
            {
                await UpdateProgressAsync(document, job, 35, "chunking", cancellationToken);
            }

            await UpdateProgressAsync(document, job, 75, "embedding", cancellationToken);
            embeddingRun = new DocumentEmbeddingRun
            {
                EmbeddingRunId = Guid.NewGuid(),
                DocumentId = document.DocumentId,
                ChunkCount = orderedChunks.Count,
                DocumentSizeBytes = TryGetDocumentSize(document.FileUrl),
                StartedAt = CreateDatabaseTimestamp(),
                EmbeddingModel = "pending",
                Status = "processing"
            };
            await _documentRepository.AddEmbeddingRunAsync(embeddingRun, cancellationToken);

            var embeddings = await _openRouterEmbeddingService.CreateEmbeddingsAsync(
                orderedChunks.Select(chunk => chunk.Content).ToList(),
                cancellationToken);
            await SaveEmbeddingsForChunksAsync(orderedChunks, embeddings, cancellationToken);

            embeddingRun.EmbeddingModel = embeddings.Model;
            embeddingRun.EmbeddingDimensions = embeddings.Dimensions;
            embeddingRun.VectorCount = embeddings.Vectors.Count;
            embeddingRun.Status = "completed";
            embeddingRun.CompletedAt = CreateDatabaseTimestamp();
            await UpdateProgressAsync(document, job, 95, "saving", cancellationToken);

            document.Status = "completed";
            job.JobStatus = "completed";
            job.ProgressPercent = 100;
            job.ProcessingStage = "completed";
            job.FinishedAt = CreateDatabaseTimestamp();
            await _documentRepository.SaveProcessingStateAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            if (embeddingRun is not null)
            {
                embeddingRun.Status = "failed";
                embeddingRun.ErrorMessage = exception.Message;
                embeddingRun.CompletedAt = CreateDatabaseTimestamp();
            }

            document.Status = "failed";
            job.JobStatus = "failed";
            job.ProcessingStage = "failed";
            job.ErrorMessage = exception.Message;
            job.FinishedAt = CreateDatabaseTimestamp();
            await _documentRepository.SaveProcessingStateAsync(cancellationToken);
        }
    }

    private async Task UpdateProgressAsync(
        Document document,
        ProcessingJob job,
        int progressPercent,
        string stage,
        CancellationToken cancellationToken)
    {
        document.Status = stage == "completed" ? "completed" : "processing";
        job.ProgressPercent = progressPercent;
        job.ProcessingStage = stage;
        await _documentRepository.SaveProcessingStateAsync(cancellationToken);
    }

    public async Task<TeacherDocumentDetailDto?> GetDocumentDetailAsync(
        string teacherEmail,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetManagedDocumentAsync(teacherEmail, documentId, cancellationToken);
        return document is null ? null : new TeacherDocumentDetailDto(
                document.DocumentId,
                document.SubjectId,
                document.Subject.SubjectCode,
                document.Subject.SubjectName,
                document.Chapter.ChapterTitle,
                document.Title,
                document.FileType ?? string.Empty,
                document.FileUrl,
                document.Status ?? "unknown",
                document.ChunkingStrategy,
                document.ChunkSize,
                document.ChunkOverlap,
                document.Chunks.Count(),
                document.CreatedAt,
                document.Chunks
                    .OrderBy(chunk => chunk.ChunkIndex)
                    .Select(chunk => new TeacherDocumentChunkDto(
                        chunk.ChunkId,
                        chunk.ChunkIndex,
                        chunk.Content))
                    .ToList());
    }

    private IReadOnlyList<string> NormalizeAllowedFileTypes()
    {
        return _options.AllowedFileTypes
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim().StartsWith('.') ? item.Trim().ToLowerInvariant() : "." + item.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private SubjectChunkingConfiguration ResolveChunkingConfiguration(Subject subject)
    {
        var strategy = SubjectChunkingStrategies.Normalize(subject.DefaultChunkingStrategy);
        if (!SubjectChunkingStrategies.IsSupported(strategy))
        {
            throw new InvalidOperationException($"Subject '{subject.SubjectCode}' has an unsupported chunking strategy '{subject.DefaultChunkingStrategy}'.");
        }

        if (string.Equals(strategy, SubjectChunkingStrategies.Fixed, StringComparison.OrdinalIgnoreCase))
        {
            if (subject.DefaultFixedChunkSize <= 0)
            {
                throw new InvalidOperationException($"Subject '{subject.SubjectCode}' must have a fixed chunk size greater than zero.");
            }

            if (_options.FixedChunkOverlap < 0 || _options.FixedChunkOverlap >= subject.DefaultFixedChunkSize)
            {
                throw new InvalidOperationException("RagIngestion:FixedChunkOverlap must be zero or greater and smaller than the subject fixed chunk size.");
            }

            return new SubjectChunkingConfiguration(strategy, subject.DefaultFixedChunkSize, _options.FixedChunkOverlap);
        }

        if (_options.Semantic.MaxChunkSize <= 0)
        {
            throw new InvalidOperationException("RagIngestion:Semantic:MaxChunkSize must be greater than zero.");
        }

        if (_options.Semantic.MinChunkSize <= 0 || _options.Semantic.MinChunkSize > _options.Semantic.MaxChunkSize)
        {
            throw new InvalidOperationException("RagIngestion:Semantic:MinChunkSize must be greater than zero and smaller than or equal to RagIngestion:Semantic:MaxChunkSize.");
        }

        return new SubjectChunkingConfiguration(strategy, _options.Semantic.MaxChunkSize, 0);
    }

    private async Task<string> SaveUploadedFileAsync(IFormFile file, string subjectCode, CancellationToken cancellationToken)
    {
        var storageRoot = _options.StorageRoot.Replace('/', Path.DirectorySeparatorChar);
        var absoluteRoot = Path.IsPathRooted(storageRoot)
            ? storageRoot
            : Path.Combine(_environment.ContentRootPath, storageRoot);

        var safeSubjectCode = string.Concat(subjectCode.Where(character => char.IsLetterOrDigit(character) || character is '-' or '_'));
        var folderPath = Path.Combine(absoluteRoot, safeSubjectCode, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folderPath);

        var safeFileName = string.Concat(file.FileName.Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));

        var destinationPath = Path.Combine(folderPath, safeFileName);
        await using var stream = File.Create(destinationPath);
        await file.CopyToAsync(stream, cancellationToken);

        return Path.GetRelativePath(_environment.ContentRootPath, destinationPath).Replace('\\', '/');
    }

    private static DateTime CreateDatabaseTimestamp()
    {
        return DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
    }

    private long? TryGetDocumentSize(string relativeFilePath)
    {
        var absolutePath = GetAbsoluteDocumentPath(relativeFilePath);
        return File.Exists(absolutePath) ? new FileInfo(absolutePath).Length : null;
    }

    private string GetAbsoluteDocumentPath(string relativeFilePath)
    {
        return Path.IsPathRooted(relativeFilePath)
            ? relativeFilePath
            : Path.Combine(_environment.ContentRootPath, relativeFilePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string ResolveTableName(string? configuredTableName)
    {
        var tableName = string.IsNullOrWhiteSpace(configuredTableName)
            ? "chunk_embeddings"
            : configuredTableName.Trim();

        if (!tableName.All(character => char.IsLetterOrDigit(character) || character == '_'))
        {
            throw new InvalidOperationException("RagIngestion:PostgresVector:TableName must contain only letters, digits, or underscores.");
        }

        return tableName;
    }

    private async Task SaveEmbeddingsForChunksAsync(
        IReadOnlyList<Chunk> chunks,
        EmbeddingBatchResult embeddings,
        CancellationToken cancellationToken)
    {
        await _chunkEmbeddingStore.SaveEmbeddingsAsync(
            chunks
                .Select((chunk, index) => (chunk.ChunkId, embeddings.Vectors[index]))
                .ToList(),
            embeddings.Model,
            cancellationToken);
    }

    private sealed record SubjectChunkingConfiguration(
        string Strategy,
        int StoredChunkSize,
        int StoredChunkOverlap);
}
