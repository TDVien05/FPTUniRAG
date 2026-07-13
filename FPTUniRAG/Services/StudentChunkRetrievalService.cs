using FPTUniRAG.DataAccessLayer.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FPTUniRAG.Services;

public sealed class StudentChunkRetrievalService : IStudentChunkRetrievalService
{
    private readonly AppDbContext _dbContext;
    private readonly IOpenRouterEmbeddingService _embeddingService;
    private readonly IEmbeddingConfigurationService _embeddingConfigurationService;
    private readonly ILogger<StudentChunkRetrievalService> _logger;
    private readonly string _tableName;

    public StudentChunkRetrievalService(
        AppDbContext dbContext,
        IOpenRouterEmbeddingService embeddingService,
        IEmbeddingConfigurationService embeddingConfigurationService,
        IOptions<Options.RagIngestionOptions> options,
        ILogger<StudentChunkRetrievalService> logger)
    {
        _dbContext = dbContext;
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
        await using var command = _dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            $"""
            SELECT EXISTS (
                SELECT 1
                FROM {_tableName} ce
                INNER JOIN chunks c ON c.chunk_id = ce.chunk_id
                INNER JOIN documents d ON d.document_id = c.document_id
                WHERE d.subject_id = @subjectId
                  AND lower(coalesce(d.status, '')) = 'completed'
                  AND ce.embedding_model = @embeddingModel
            );
            """;

        command.Parameters.Add(new NpgsqlParameter("subjectId", subjectId));
        command.Parameters.Add(new NpgsqlParameter("embeddingModel", embeddingConfiguration.Model));

        if (command.Connection is not null && command.Connection.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync(cancellationToken);
        }

        try
        {
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result is true || (result is bool boolResult && boolResult);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<StudentRetrievedChunk>> RetrieveRelevantChunksAsync(
        Guid subjectId,
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query.Trim();
        if (string.IsNullOrWhiteSpace(normalizedQuery) || limit <= 0)
        {
            return [];
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
            return [];
        }

        var rows = new List<StudentRetrievedChunkWithEmbedding>();

        await using var command = _dbContext.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            $"""
            SELECT
                ce.chunk_id,
                ce.embedding,
                c.content,
                c.chunk_index,
                d.document_id,
                d.title,
                s.subject_code,
                s.subject_name,
                ch.chapter_title
            FROM {_tableName} ce
            INNER JOIN chunks c ON c.chunk_id = ce.chunk_id
            INNER JOIN documents d ON d.document_id = c.document_id
            INNER JOIN subjects s ON s.subject_id = d.subject_id
            INNER JOIN chapters ch ON ch.chapter_id = d.chapter_id
            WHERE d.subject_id = @subjectId
              AND lower(coalesce(d.status, '')) = 'completed'
              AND ce.embedding_model = @embeddingModel;
            """;

        command.Parameters.Add(new NpgsqlParameter("subjectId", subjectId));
        command.Parameters.Add(new NpgsqlParameter("embeddingModel", embeddingConfiguration.Model));

        if (command.Connection is not null && command.Connection.State != System.Data.ConnectionState.Open)
        {
            await command.Connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new StudentRetrievedChunkWithEmbedding(
                    reader.GetGuid(0),
                    reader.GetFieldValue<float[]>(1),
                    reader.GetString(2),
                    reader.GetInt32(3),
                    reader.GetGuid(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8)));
            }
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return [];
        }

        return rows
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
