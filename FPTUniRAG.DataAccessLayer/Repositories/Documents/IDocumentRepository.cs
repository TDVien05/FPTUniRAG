using FPTUniRAG.DataAccessLayer.Entities;

namespace FPTUniRAG.DataAccessLayer.Repositories.Documents;

public interface IDocumentRepository
{
    Task<TeacherSubject?> GetManagedSubjectAsync(string teacherEmail, Guid subjectId, CancellationToken cancellationToken = default);
    Task<Document?> GetManagedDocumentAsync(string teacherEmail, Guid documentId, CancellationToken cancellationToken = default);
    Task<bool> ManagesSubjectAsync(string teacherEmail, Guid subjectId, CancellationToken cancellationToken = default);
    Task<Chapter?> FindChapterAsync(Guid subjectId, string title, CancellationToken cancellationToken = default);
    Task<bool> ChapterHasDocumentAsync(Guid chapterId, CancellationToken cancellationToken = default);
    Task<Chapter> CreateChapterAsync(Guid subjectId, string title, DateTime createdAt, CancellationToken cancellationToken = default);
    Task AddDocumentAsync(Document document, ProcessingJob job, CancellationToken cancellationToken = default);
    Task<bool> HasChunksAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task QueueDocumentAsync(Document document, ProcessingJob job, CancellationToken cancellationToken = default);
    Task<Chapter?> GetChapterWithDocumentAsync(Guid subjectId, Guid chapterId, CancellationToken cancellationToken = default);
    Task DeleteChapterAsync(Guid chapterId, Guid? documentId, string embeddingTableName, CancellationToken cancellationToken = default);
    Task<Document?> GetDocumentForProcessingAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Chunk>> GetChunksAsync(Guid documentId, CancellationToken cancellationToken = default);
    Task ReplaceChunksAsync(Guid documentId, IReadOnlyList<Chunk> chunks, CancellationToken cancellationToken = default);
    Task AddEmbeddingRunAsync(DocumentEmbeddingRun run, CancellationToken cancellationToken = default);
    Task SaveProcessingStateAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetPendingDocumentIdsAsync(CancellationToken cancellationToken = default);
}
