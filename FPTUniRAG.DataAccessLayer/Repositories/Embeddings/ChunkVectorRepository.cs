using FPTUniRAG.DataAccessLayer.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace FPTUniRAG.DataAccessLayer.Repositories.Embeddings;

public sealed class ChunkVectorRepository(AppDbContext context) : IChunkVectorRepository
{
    public async Task SaveAsync(string tableName, int batchSize, IReadOnlyList<(Guid ChunkId, float[] Embedding)> embeddings, string model, CancellationToken cancellationToken = default)
    {
        ValidateTableName(tableName);
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if (embeddings.Count == 0) return;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var dbTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        await using (var create = connection.CreateCommand())
        {
            create.Transaction = dbTransaction;
            create.CommandText = $"""
                CREATE TABLE IF NOT EXISTS {tableName} (
                    chunk_id uuid PRIMARY KEY, embedding_model character varying(255) NOT NULL,
                    embedding_dimensions integer NOT NULL, embedding real[] NOT NULL,
                    created_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    updated_at timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    CONSTRAINT fk_{tableName}_chunk FOREIGN KEY (chunk_id) REFERENCES chunks(chunk_id) ON DELETE CASCADE);
                CREATE INDEX IF NOT EXISTS idx_{tableName}_dimensions ON {tableName} (embedding_dimensions);
                """;
            await create.ExecuteNonQueryAsync(cancellationToken);
        }
        var timestamp = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        foreach (var item in embeddings)
        {
            if (item.Embedding.Length == 0) throw new InvalidOperationException("Embedding vectors cannot be empty.");
            await using var command = connection.CreateCommand();
            command.Transaction = dbTransaction;
            command.CommandText = $"""
                INSERT INTO {tableName} (chunk_id, embedding_model, embedding_dimensions, embedding, created_at, updated_at)
                VALUES (@chunkId, @model, @dimensions, @embedding, @createdAt, @updatedAt)
                ON CONFLICT (chunk_id) DO UPDATE SET embedding_model=EXCLUDED.embedding_model,
                embedding_dimensions=EXCLUDED.embedding_dimensions, embedding=EXCLUDED.embedding, updated_at=EXCLUDED.updated_at;
                """;
            command.Parameters.Add(new NpgsqlParameter<Guid>("chunkId", item.ChunkId));
            command.Parameters.Add(new NpgsqlParameter<string>("model", model));
            command.Parameters.Add(new NpgsqlParameter<int>("dimensions", item.Embedding.Length));
            command.Parameters.Add(new NpgsqlParameter<float[]>("embedding", NpgsqlDbType.Array | NpgsqlDbType.Real) { Value = item.Embedding });
            command.Parameters.Add(new NpgsqlParameter<DateTime>("createdAt", timestamp));
            command.Parameters.Add(new NpgsqlParameter<DateTime>("updatedAt", timestamp));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> SubjectHasContentAsync(string tableName, Guid subjectId, string model, CancellationToken cancellationToken = default)
    {
        ValidateTableName(tableName);
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT EXISTS (SELECT 1 FROM {tableName} ce INNER JOIN chunks c ON c.chunk_id=ce.chunk_id INNER JOIN documents d ON d.document_id=c.document_id WHERE d.subject_id=@subjectId AND lower(coalesce(d.status,''))='completed' AND ce.embedding_model=@model);";
        command.Parameters.Add(new NpgsqlParameter("subjectId", subjectId)); command.Parameters.Add(new NpgsqlParameter("model", model));
        await OpenAsync(command, cancellationToken);
        try { return await command.ExecuteScalarAsync(cancellationToken) is true; }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable) { return false; }
    }

    public async Task<IReadOnlyList<ChunkVectorRecord>> GetSubjectChunksAsync(string tableName, Guid subjectId, string model, CancellationToken cancellationToken = default)
    {
        ValidateTableName(tableName);
        var rows = new List<ChunkVectorRecord>();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT ce.chunk_id,ce.embedding,c.content,c.chunk_index,d.document_id,d.title,s.subject_code,s.subject_name,ch.chapter_title FROM {tableName} ce INNER JOIN chunks c ON c.chunk_id=ce.chunk_id INNER JOIN documents d ON d.document_id=c.document_id INNER JOIN subjects s ON s.subject_id=d.subject_id INNER JOIN chapters ch ON ch.chapter_id=d.chapter_id WHERE d.subject_id=@subjectId AND lower(coalesce(d.status,''))='completed' AND ce.embedding_model=@model;";
        command.Parameters.Add(new NpgsqlParameter("subjectId", subjectId)); command.Parameters.Add(new NpgsqlParameter("model", model));
        await OpenAsync(command, cancellationToken);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) rows.Add(new(reader.GetGuid(0), reader.GetFieldValue<float[]>(1), reader.GetString(2), reader.GetInt32(3), reader.GetGuid(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetString(8)));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable) { }
        return rows;
    }

    public async Task<IReadOnlyList<ChunkSimilarityRecord>> SearchSimilarChunksAsync(string tableName, Guid subjectId, string model, float[] queryEmbedding, int limit, CancellationToken cancellationToken = default)
    {
        ValidateTableName(tableName);
        if (queryEmbedding.Length == 0) return [];
        var rows = new List<ChunkSimilarityRecord>();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"""
            SELECT chunk_id, content, chunk_index, document_id, title, subject_code, subject_name, chapter_title, similarity FROM (
                SELECT ce.chunk_id, c.content, c.chunk_index, d.document_id, d.title, s.subject_code, s.subject_name, ch.chapter_title,
                       1 - (ce.embedding::vector <=> @queryEmbedding::real[]::vector) AS similarity
                FROM {tableName} ce
                INNER JOIN chunks c ON c.chunk_id = ce.chunk_id
                INNER JOIN documents d ON d.document_id = c.document_id
                INNER JOIN subjects s ON s.subject_id = d.subject_id
                INNER JOIN chapters ch ON ch.chapter_id = d.chapter_id
                WHERE d.subject_id = @subjectId AND lower(coalesce(d.status,'')) = 'completed' AND ce.embedding_model = @model
            ) ranked
            WHERE similarity > 0
            ORDER BY similarity DESC, title, chunk_index
            LIMIT @limit;
            """;
        command.Parameters.Add(new NpgsqlParameter("subjectId", subjectId));
        command.Parameters.Add(new NpgsqlParameter("model", model));
        command.Parameters.Add(new NpgsqlParameter<float[]>("queryEmbedding", NpgsqlDbType.Array | NpgsqlDbType.Real) { Value = queryEmbedding });
        command.Parameters.Add(new NpgsqlParameter("limit", limit));
        await OpenAsync(command, cancellationToken);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetGuid(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetDouble(8)));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable) { }
        return rows;
    }

    public async Task<IReadOnlySet<Guid>> GetUsableSubjectIdsAsync(string tableName, CancellationToken cancellationToken = default)
    {
        ValidateTableName(tableName);
        var ids = new HashSet<Guid>();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT DISTINCT d.subject_id FROM {tableName} ce INNER JOIN chunks c ON c.chunk_id=ce.chunk_id INNER JOIN documents d ON d.document_id=c.document_id WHERE lower(coalesce(d.status,''))='completed';";
        await OpenAsync(command, cancellationToken);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) ids.Add(reader.GetGuid(0));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable) { }
        return ids;
    }

    public Task DeleteDocumentVectorsAsync(string tableName, Guid documentId, CancellationToken cancellationToken = default)
    {
        ValidateTableName(tableName);
#pragma warning disable EF1002
        return context.Database.ExecuteSqlRawAsync($"DELETE FROM {tableName} WHERE chunk_id IN (SELECT chunk_id FROM chunks WHERE document_id = @documentId)", [new NpgsqlParameter<Guid>("documentId", documentId)], cancellationToken);
#pragma warning restore EF1002
    }

    private static async Task OpenAsync(System.Data.Common.DbCommand command, CancellationToken token)
    { if (command.Connection is not null && command.Connection.State != System.Data.ConnectionState.Open) await command.Connection.OpenAsync(token); }
    private static void ValidateTableName(string value)
    { if (string.IsNullOrWhiteSpace(value) || !value.All(c => char.IsLetterOrDigit(c) || c == '_')) throw new InvalidOperationException("Invalid embedding table name."); }
}
