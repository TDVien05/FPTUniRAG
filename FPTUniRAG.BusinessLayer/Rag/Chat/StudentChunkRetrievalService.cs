using FPTUniRAG.BusinessLayer.Rag.Configuration;
using FPTUniRAG.BusinessLayer.Rag.Embeddings;
using FPTUniRAG.DataAccessLayer.Repositories.Embeddings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FPTUniRAG.BusinessLayer.Rag.Chat;

public sealed class StudentChunkRetrievalService : IStudentChunkRetrievalService
{
    private readonly IChunkVectorRepository _vectorRepository;
    private readonly IOpenRouterEmbeddingService _embeddingService;
    private readonly IEmbeddingConfigurationService _embeddingConfigurationService;
    private readonly ILogger<StudentChunkRetrievalService> _logger;
    private readonly string _tableName;

    public StudentChunkRetrievalService(
        IChunkVectorRepository vectorRepository,
        IOpenRouterEmbeddingService embeddingService,
        IEmbeddingConfigurationService embeddingConfigurationService,
        IOptions<RagIngestionOptions> options,
        ILogger<StudentChunkRetrievalService> logger)
    {
        _vectorRepository = vectorRepository;
        _embeddingService = embeddingService;
        _embeddingConfigurationService = embeddingConfigurationService;
        _logger = logger;
        _tableName = ResolveTableName(options.Value.PostgresVector.TableName);
    }

    public async Task<bool> SubjectHasUsableContentAsync(
        Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        var embeddingConfiguration = await _embeddingConfigurationService.GetCurrentAsync(cancellationToken);
        return await _vectorRepository.SubjectHasContentAsync(
            _tableName, subjectId, embeddingConfiguration.Model, cancellationToken);
    }

    public async Task<StudentChunkRetrievalResult> RetrieveRelevantChunksAsync(
        Guid subjectId,
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query.Trim();
        if (string.IsNullOrWhiteSpace(normalizedQuery) || limit <= 0)
        {
            return new StudentChunkRetrievalResult([], false);
        }

        float[]? queryEmbedding = null;
        var embeddingConfiguration = await _embeddingConfigurationService.GetCurrentAsync(cancellationToken);
        try
        {
            queryEmbedding = (await _embeddingService.CreateEmbeddingsAsync([normalizedQuery], cancellationToken)).Vectors.SingleOrDefault();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Student chat query embedding failed. Falling back to lexical retrieval.");
        }

        var useVectorSearch = queryEmbedding is { Length: > 0 };
        var queryTerms = Tokenize(normalizedQuery);
        if (!useVectorSearch && queryTerms.Count == 0)
        {
            return new StudentChunkRetrievalResult([], true);
        }

        var repositoryRows = await _vectorRepository.GetSubjectChunksAsync(
            _tableName, subjectId, embeddingConfiguration.Model, cancellationToken);
        var rows = repositoryRows.Select(row => new StudentRetrievedChunkWithEmbedding(
            row.ChunkId, row.Embedding, row.Content, row.ChunkIndex, row.DocumentId,
            row.DocumentTitle, row.SubjectCode, row.SubjectName, row.ChapterTitle)).ToList();

        var chunks = rows
            .Select(row => new StudentRetrievedChunk(
                row.ChunkId,
                row.Content,
                row.ChunkIndex,
                row.DocumentId,
                row.DocumentTitle,
                row.SubjectCode,
                row.SubjectName,
                row.ChapterTitle,
                useVectorSearch
                    ? ComputeCosineSimilarity(queryEmbedding!, row.Embedding)
                    : ComputeKeywordScore(queryTerms, row)))
            .Where(item => item.SimilarityScore > 0)
            .OrderByDescending(item => item.SimilarityScore)
            .ThenBy(item => item.DocumentTitle)
            .ThenBy(item => item.ChunkIndex)
            .Take(limit)
            .ToList();

        return new StudentChunkRetrievalResult(chunks, !useVectorSearch);
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

    private static double ComputeCosineSimilarity(float[] left, float[] right)
    {
        if (left.Length == 0 || right.Length == 0 || left.Length != right.Length)
        {
            return 0;
        }

        double dot = 0;
        double leftMagnitudeSquared = 0;
        double rightMagnitudeSquared = 0;

        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftMagnitudeSquared += left[index] * left[index];
            rightMagnitudeSquared += right[index] * right[index];
        }

        if (leftMagnitudeSquared <= 0 || rightMagnitudeSquared <= 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftMagnitudeSquared) * Math.Sqrt(rightMagnitudeSquared));
    }

    private static IReadOnlyList<string> Tokenize(string value)
    {
        return value
            .Split([
                ' ', '\t', '\r', '\n',
                '.', ',', ';', ':', '!', '?',
                '(', ')', '[', ']', '{', '}',
                '/', '\\', '-', '_', '"', '\''
            ], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.Trim().ToLowerInvariant())
            .Where(term => term.Length >= 2)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static double ComputeKeywordScore(
        IReadOnlyList<string> queryTerms,
        StudentRetrievedChunkWithEmbedding row)
    {
        if (queryTerms.Count == 0)
        {
            return 0;
        }

        var content = row.Content.ToLowerInvariant();
        var documentTitle = row.DocumentTitle.ToLowerInvariant();
        var chapterTitle = row.ChapterTitle.ToLowerInvariant();

        double score = 0;
        foreach (var term in queryTerms)
        {
            if (documentTitle.Contains(term, StringComparison.Ordinal))
            {
                score += 3;
            }

            if (chapterTitle.Contains(term, StringComparison.Ordinal))
            {
                score += 2;
            }

            if (content.Contains(term, StringComparison.Ordinal))
            {
                score += 1;
            }
        }

        return score / queryTerms.Count;
    }

    private sealed record StudentRetrievedChunkWithEmbedding(
        Guid ChunkId,
        float[] Embedding,
        string Content,
        int ChunkIndex,
        Guid DocumentId,
        string DocumentTitle,
        string SubjectCode,
        string SubjectName,
        string ChapterTitle);
}
