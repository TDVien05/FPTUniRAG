using System.Data.Common;
using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace FPTUniRAG.Services;

public sealed class PostgresChunkEmbeddingStore : IChunkEmbeddingStore
{
    private readonly AppDbContext _dbContext;
    private readonly RagIngestionOptions _options;
    private readonly string _tableName;

    public PostgresChunkEmbeddingStore(AppDbContext dbContext, IOptions<RagIngestionOptions> options)
    {
        _dbContext = dbContext;
        _options = options.Value;
        _tableName = ResolveTableName(_options.PostgresVector.TableName);
    }

    public async Task SaveEmbeddingsAsync(
        IReadOnlyList<(Guid ChunkId, float[] Embedding)> embeddings,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions();

        if (embeddings.Count == 0)
        {
            return;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        var dbTransaction = transaction.GetDbTransaction();

        await EnsureStorageTableAsync(connection, dbTransaction, cancellationToken);

        var timestamp = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        foreach (var batch in Batch(embeddings, Math.Max(1, _options.PostgresVector.BatchSize)))
        {
            foreach (var (chunkId, embedding) in batch)
            {
                await UpsertEmbeddingAsync(connection, dbTransaction, chunkId, embedding, timestamp, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task EnsureStorageTableAsync(
        NpgsqlConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (NpgsqlTransaction)transaction;
        command.CommandText =
            $"""
            CREATE TABLE IF NOT EXISTS {_tableName} (
                chunk_id uuid PRIMARY KEY,
                embedding_model character varying(255) NOT NULL,
                embedding_dimensions integer NOT NULL,
                embedding real[] NOT NULL,
                created_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT fk_{_tableName}_chunk
                    FOREIGN KEY (chunk_id)
                    REFERENCES chunks(chunk_id)
                    ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_{_tableName}_dimensions
                ON {_tableName} (embedding_dimensions);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task UpsertEmbeddingAsync(
        NpgsqlConnection connection,
        DbTransaction transaction,
        Guid chunkId,
        float[] embedding,
        DateTime timestamp,
        CancellationToken cancellationToken)
    {
        if (embedding.Length == 0)
        {
            throw new InvalidOperationException("Embedding vectors cannot be empty.");
        }

        await using var command = connection.CreateCommand();
        command.Transaction = (NpgsqlTransaction)transaction;
        command.CommandText =
            $"""
            INSERT INTO {_tableName} (
                chunk_id,
                embedding_model,
                embedding_dimensions,
                embedding,
                created_at,
                updated_at)
            VALUES (
                @chunkId,
                @embeddingModel,
                @embeddingDimensions,
                @embedding,
                @createdAt,
                @updatedAt)
            ON CONFLICT (chunk_id) DO UPDATE
            SET
                embedding_model = EXCLUDED.embedding_model,
                embedding_dimensions = EXCLUDED.embedding_dimensions,
                embedding = EXCLUDED.embedding,
                updated_at = EXCLUDED.updated_at;
            """;

        command.Parameters.Add(new NpgsqlParameter<Guid>("chunkId", chunkId));
        command.Parameters.Add(new NpgsqlParameter<string>("embeddingModel", _options.OpenRouter.EmbeddingModel));
        command.Parameters.Add(new NpgsqlParameter<int>("embeddingDimensions", embedding.Length));
        command.Parameters.Add(new NpgsqlParameter<float[]>("embedding", NpgsqlDbType.Array | NpgsqlDbType.Real)
        {
            Value = embedding
        });
        command.Parameters.Add(new NpgsqlParameter<DateTime>("createdAt", timestamp));
        command.Parameters.Add(new NpgsqlParameter<DateTime>("updatedAt", timestamp));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.OpenRouter.EmbeddingModel))
        {
            throw new InvalidOperationException("RagIngestion:OpenRouter:EmbeddingModel is missing in appsettings.json.");
        }

        if (_options.PostgresVector.BatchSize <= 0)
        {
            throw new InvalidOperationException("RagIngestion:PostgresVector:BatchSize must be greater than zero.");
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

    private static IEnumerable<IReadOnlyList<(Guid ChunkId, float[] Embedding)>> Batch(
        IReadOnlyList<(Guid ChunkId, float[] Embedding)> source,
        int batchSize)
    {
        for (var index = 0; index < source.Count; index += batchSize)
        {
            yield return source.Skip(index).Take(batchSize).ToList();
        }
    }
}
