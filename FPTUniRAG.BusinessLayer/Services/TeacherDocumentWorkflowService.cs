using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using FPTUniRAG.BusinessLayer.Subjects;
using FPTUniRAG.BusinessLayer.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FPTUniRAG.BusinessLayer.Services;

public sealed class TeacherDocumentWorkflowService : ITeacherDocumentWorkflowService
{
    private readonly AppDbContext _dbContext;
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
        AppDbContext dbContext,
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
        _dbContext = dbContext;
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
        var subject = await GetHeaderSubjectQuery(teacherEmail)
            .Where(item => item.SubjectId == subjectId)
            .Select(item => new
            {
                item.SubjectId,
                item.Subject.SubjectCode,
                item.Subject.SubjectName,
                item.Subject.Description,
                item.Subject.DefaultChunkingStrategy,
                item.Subject.DefaultFixedChunkSize
            })
            .FirstOrDefaultAsync(cancellationToken);

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

        var subjectLink = await GetHeaderSubjectQuery(teacherEmail)
            .Include(link => link.Teacher)
            .Include(link => link.Subject)
            .FirstOrDefaultAsync(link => link.SubjectId == command.SubjectId, cancellationToken);

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
        var chapter = await _dbContext.Chapters
            .FirstOrDefaultAsync(
                item => item.SubjectId == command.SubjectId
                    && item.ChapterTitle.ToLower() == normalizedChapterTitle.ToLower(),
                cancellationToken);

        if (chapter is not null)
        {
            var existingDocument = await _dbContext.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.ChapterId == chapter.ChapterId, cancellationToken);

            if (existingDocument is not null)
            {
                return new TeacherDocumentUploadResult(
                    false,
                    "This chapter already has a document. Choose a new chapter name or remove the existing document first.",
                    null);
            }
        }
        else
        {
            var nextOrder = await _dbContext.Chapters
                .Where(item => item.SubjectId == command.SubjectId)
                .Select(item => (int?)item.ChapterOrder)
                .MaxAsync(cancellationToken) ?? 0;

            chapter = new Chapter
            {
                ChapterId = Guid.NewGuid(),
                SubjectId = command.SubjectId,
                ChapterTitle = normalizedChapterTitle,
                ChapterOrder = nextOrder + 1,
                CreatedAt = CreateDatabaseTimestamp()
            };

            _dbContext.Chapters.Add(chapter);
            await _dbContext.SaveChangesAsync(cancellationToken);
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

        _dbContext.Documents.Add(document);
        _dbContext.ProcessingJobs.Add(job);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _processingQueue.QueueAsync(document.DocumentId, cancellationToken);
        return new TeacherDocumentUploadResult(true, "Document uploaded and queued for processing.", document.DocumentId);
    }

    public async Task<TeacherDocumentUploadResult> RetryEmbeddingSyncAsync(
        string teacherEmail,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await GetHeaderSubjectQuery(teacherEmail)
            .SelectMany(link => link.Subject.Documents)
            .Include(item => item.ProcessingJobs)
            .FirstOrDefaultAsync(item => item.DocumentId == documentId, cancellationToken);

        if (document is null)
        {
            return new TeacherDocumentUploadResult(false, "The selected document is unavailable.", null);
        }

        var hasChunks = await _dbContext.Chunks.AnyAsync(
            chunk => chunk.DocumentId == document.DocumentId,
            cancellationToken);

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
            _dbContext.ProcessingJobs.Add(processingJob);
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
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _processingQueue.QueueAsync(document.DocumentId, cancellationToken);
        return new TeacherDocumentUploadResult(true, "Embedding sync queued for processing.", document.DocumentId);
    }

    public async Task<TeacherDocumentUploadResult> DeleteChapterAsync(
        string teacherEmail,
        Guid subjectId,
        Guid chapterId,
        CancellationToken cancellationToken = default)
    {
        var managesSubject = await GetHeaderSubjectQuery(teacherEmail)
            .AnyAsync(link => link.SubjectId == subjectId, cancellationToken);

        if (!managesSubject)
        {
            return new TeacherDocumentUploadResult(false, "You do not manage the selected subject.", null);
        }

        var chapter = await _dbContext.Chapters
            .Include(item => item.Document)
            .FirstOrDefaultAsync(
                item => item.ChapterId == chapterId && item.SubjectId == subjectId,
                cancellationToken);

        if (chapter is null)
        {
            return new TeacherDocumentUploadResult(false, "The selected chapter is unavailable.", null);
        }

        var documentId = chapter.Document?.DocumentId;
        var fileUrl = chapter.Document?.FileUrl;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        await _dbContext.TestQuestions
            .Where(question => question.ChapterId == chapterId)
            .ExecuteDeleteAsync(cancellationToken);

        if (documentId.HasValue)
        {
            await DeleteEmbeddingsForDocumentAsync(documentId.Value, cancellationToken);

            await _dbContext.DocumentEmbeddingRuns
                .Where(run => run.DocumentId == documentId.Value)
                .ExecuteDeleteAsync(cancellationToken);

            await _dbContext.ProcessingJobs
                .Where(job => job.DocumentId == documentId.Value)
                .ExecuteDeleteAsync(cancellationToken);

            await _dbContext.Chunks
                .Where(chunk => chunk.DocumentId == documentId.Value)
                .ExecuteDeleteAsync(cancellationToken);

            await _dbContext.Documents
                .Where(document => document.DocumentId == documentId.Value)
                .ExecuteDeleteAsync(cancellationToken);
        }

        await _dbContext.Chapters
            .Where(item => item.ChapterId == chapterId)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

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
        return await GetHeaderSubjectQuery(teacherEmail)
            .SelectMany(link => link.Subject.Documents)
            .Where(document => document.DocumentId == documentId)
            .SelectMany(document => document.ProcessingJobs
                .OrderByDescending(job => job.StartedAt)
                .Take(1)
                .Select(job => new TeacherDocumentProcessingStatusDto(
                    document.DocumentId,
                    job.JobStatus ?? document.Status ?? "unknown",
                    job.ProgressPercent,
                    job.ProcessingStage ?? job.JobStatus ?? document.Status ?? "unknown",
                    job.ErrorMessage,
                    job.JobStatus == "completed",
                    job.JobStatus == "failed")))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task ProcessDocumentAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var document = await _dbContext.Documents
            .Include(item => item.ProcessingJobs)
            .FirstOrDefaultAsync(item => item.DocumentId == documentId, cancellationToken);

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
            var orderedChunks = await _dbContext.Chunks
                .Where(chunk => chunk.DocumentId == documentId)
                .OrderBy(chunk => chunk.ChunkIndex)
                .ToListAsync(cancellationToken);

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
                _dbContext.Chunks.AddRange(orderedChunks);
                await _dbContext.SaveChangesAsync(cancellationToken);
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
            _dbContext.DocumentEmbeddingRuns.Add(embeddingRun);
            await _dbContext.SaveChangesAsync(cancellationToken);

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
            await _dbContext.SaveChangesAsync(cancellationToken);
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
            await _dbContext.SaveChangesAsync(cancellationToken);
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
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<TeacherDocumentDetailDto?> GetDocumentDetailAsync(
        string teacherEmail,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        return await GetHeaderSubjectQuery(teacherEmail)
            .SelectMany(link => link.Subject.Documents)
            .Where(document => document.DocumentId == documentId)
            .Select(document => new TeacherDocumentDetailDto(
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
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private IQueryable<TeacherSubject> GetHeaderSubjectQuery(string teacherEmail)
    {
        var normalizedEmail = teacherEmail.Trim();
        return _dbContext.TeacherSubjects
            .AsQueryable()
            .Where(link =>
                link.IsHeadOfDepartment
                && link.Teacher.Email != null
                && link.Teacher.Email.ToLower() == normalizedEmail.ToLower());
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

    private async Task DeleteEmbeddingsForDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        try
        {
#pragma warning disable EF1002 // The configured table name is validated before interpolation.
            await _dbContext.Database.ExecuteSqlRawAsync(
                $"DELETE FROM {_embeddingTableName} WHERE chunk_id IN (SELECT chunk_id FROM chunks WHERE document_id = @documentId)",
                [new NpgsqlParameter<Guid>("documentId", documentId)],
                cancellationToken);
#pragma warning restore EF1002
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            _logger.LogDebug(exception, "Embedding table {EmbeddingTableName} was not present while deleting chapter data.", _embeddingTableName);
        }
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
