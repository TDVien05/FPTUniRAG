using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using FPTUniRAG.BusinessLayer.Subjects;
using FPTUniRAG.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FPTUniRAG.Services;

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

    public TeacherDocumentWorkflowService(
        AppDbContext dbContext,
        IDocumentTextExtractor documentTextExtractor,
        IFixedChunkingService fixedChunkingService,
        ISemanticChunkingService semanticChunkingService,
        IOpenRouterEmbeddingService openRouterEmbeddingService,
        IChunkEmbeddingStore chunkEmbeddingStore,
        IOptions<RagIngestionOptions> options,
        IWebHostEnvironment environment)
    {
        _dbContext = dbContext;
        _documentTextExtractor = documentTextExtractor;
        _fixedChunkingService = fixedChunkingService;
        _semanticChunkingService = semanticChunkingService;
        _openRouterEmbeddingService = openRouterEmbeddingService;
        _chunkEmbeddingStore = chunkEmbeddingStore;
        _options = options.Value;
        _environment = environment;
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
            JobStatus = "processing",
            StartedAt = CreateDatabaseTimestamp()
        };

        _dbContext.Documents.Add(document);
        _dbContext.ProcessingJobs.Add(job);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var chunksPersisted = false;

        try
        {
            string content;
            await using (var stream = command.File.OpenReadStream())
            {
                content = await _documentTextExtractor.ExtractTextAsync(stream, command.File.FileName, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException("No readable content was extracted from the uploaded file.");
            }

            var chunkContents = resolvedChunking.Strategy switch
            {
                SubjectChunkingStrategies.Semantic => _semanticChunkingService.CreateChunks(
                    content,
                    _options.Semantic.MaxChunkSize,
                    _options.Semantic.MinChunkSize),
                _ => _fixedChunkingService.CreateChunks(content, document.ChunkSize, document.ChunkOverlap)
            };
            if (chunkContents.Count == 0)
            {
                throw new InvalidOperationException("The uploaded document did not produce any chunks.");
            }

            var chunkEntities = chunkContents
                .Select((chunkContent, index) => new Chunk
                {
                    ChunkId = Guid.NewGuid(),
                    DocumentId = document.DocumentId,
                    ChunkIndex = index,
                    Content = chunkContent,
                    CreatedAt = CreateDatabaseTimestamp()
                })
                .ToList();

            _dbContext.Chunks.AddRange(chunkEntities);
            await _dbContext.SaveChangesAsync(cancellationToken);
            chunksPersisted = true;

            var embeddings = await _openRouterEmbeddingService.CreateEmbeddingsAsync(chunkContents, cancellationToken);
            await SaveEmbeddingsForChunksAsync(chunkEntities, embeddings, cancellationToken);

            document.Status = "completed";
            job.JobStatus = "completed";
            job.FinishedAt = CreateDatabaseTimestamp();
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new TeacherDocumentUploadResult(true, "Document uploaded and chunked successfully.", document.DocumentId);
        }
        catch (Exception exception)
        {
            document.Status = "failed";
            job.JobStatus = "failed";
            job.ErrorMessage = exception.Message;
            job.FinishedAt = CreateDatabaseTimestamp();
            await _dbContext.SaveChangesAsync(cancellationToken);

            return chunksPersisted
                ? new TeacherDocumentUploadResult(
                    true,
                    "Document and chunks were saved, but vector sync failed. You can retry the embedding sync from Document Management.",
                    document.DocumentId)
                : new TeacherDocumentUploadResult(false, exception.Message, document.DocumentId);
        }
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

        var orderedChunks = await _dbContext.Chunks
            .Where(chunk => chunk.DocumentId == document.DocumentId)
            .OrderBy(chunk => chunk.ChunkIndex)
            .ToListAsync(cancellationToken);

        if (orderedChunks.Count == 0)
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
            processingJob.JobStatus = "processing";
            processingJob.StartedAt = CreateDatabaseTimestamp();
            processingJob.FinishedAt = null;
            processingJob.ErrorMessage = null;
        }

        document.Status = "processing";
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var chunkContents = orderedChunks
                .Select(chunk => chunk.Content)
                .ToList();

            var embeddings = await _openRouterEmbeddingService.CreateEmbeddingsAsync(chunkContents, cancellationToken);
            await SaveEmbeddingsForChunksAsync(orderedChunks, embeddings, cancellationToken);

            document.Status = "completed";
            processingJob.JobStatus = "completed";
            processingJob.FinishedAt = CreateDatabaseTimestamp();
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new TeacherDocumentUploadResult(true, "Embedding sync completed successfully.", document.DocumentId);
        }
        catch (Exception exception)
        {
            document.Status = "failed";
            processingJob.JobStatus = "failed";
            processingJob.ErrorMessage = exception.Message;
            processingJob.FinishedAt = CreateDatabaseTimestamp();
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new TeacherDocumentUploadResult(false, exception.Message, document.DocumentId);
        }
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

    private async Task SaveEmbeddingsForChunksAsync(
        IReadOnlyList<Chunk> chunks,
        IReadOnlyList<float[]> embeddings,
        CancellationToken cancellationToken)
    {
        await _chunkEmbeddingStore.SaveEmbeddingsAsync(
            chunks
                .Select((chunk, index) => (chunk.ChunkId, embeddings[index]))
                .ToList(),
            cancellationToken);
    }

    private sealed record SubjectChunkingConfiguration(
        string Strategy,
        int StoredChunkSize,
        int StoredChunkOverlap);
}
