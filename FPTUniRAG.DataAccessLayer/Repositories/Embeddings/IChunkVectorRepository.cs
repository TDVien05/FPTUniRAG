namespace FPTUniRAG.DataAccessLayer.Repositories.Embeddings;

public interface IChunkVectorRepository
{
    Task SaveAsync(string tableName, int batchSize, IReadOnlyList<(Guid ChunkId, float[] Embedding)> embeddings, string model, CancellationToken cancellationToken = default);
    Task<bool> SubjectHasContentAsync(string tableName, Guid subjectId, string model, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChunkVectorRecord>> GetSubjectChunksAsync(string tableName, Guid subjectId, string model, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<Guid>> GetUsableSubjectIdsAsync(string tableName, CancellationToken cancellationToken = default);
    Task DeleteDocumentVectorsAsync(string tableName, Guid documentId, CancellationToken cancellationToken = default);
}

public sealed record ChunkVectorRecord(Guid ChunkId, float[] Embedding, string Content, int ChunkIndex, Guid DocumentId,
    string DocumentTitle, string SubjectCode, string SubjectName, string ChapterTitle);
