using FPTUniRAG.BusinessLayer.Rag.Configuration;
using FPTUniRAG.DataAccessLayer.Repositories.Embeddings;
using Microsoft.Extensions.Options;

namespace FPTUniRAG.BusinessLayer.Rag.Embeddings;

public sealed class PostgresChunkEmbeddingStore : IChunkEmbeddingStore
{
    private readonly IChunkVectorRepository _repository;
    private readonly RagIngestionOptions _options;
    private readonly string _tableName;

    public PostgresChunkEmbeddingStore(IChunkVectorRepository repository, IOptions<RagIngestionOptions> options)
    {
        _repository = repository;
        _options = options.Value;
        _tableName = ResolveTableName(_options.PostgresVector.TableName);
    }

    public Task SaveEmbeddingsAsync(IReadOnlyList<(Guid ChunkId, float[] Embedding)> embeddings, string embeddingModel,
        CancellationToken cancellationToken = default)
    {
        if (_options.PostgresVector.BatchSize <= 0)
            throw new InvalidOperationException("RagIngestion:PostgresVector:BatchSize must be greater than zero.");
        return _repository.SaveAsync(_tableName, _options.PostgresVector.BatchSize, embeddings, embeddingModel, cancellationToken);
    }

    private static string ResolveTableName(string? configuredTableName)
    {
        var tableName = string.IsNullOrWhiteSpace(configuredTableName) ? "chunk_embeddings" : configuredTableName.Trim();
        if (!tableName.All(character => char.IsLetterOrDigit(character) || character == '_'))
            throw new InvalidOperationException("RagIngestion:PostgresVector:TableName must contain only letters, digits, or underscores.");
        return tableName;
    }
}
