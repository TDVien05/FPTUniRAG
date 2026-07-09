namespace FPTUniRAG.Services;

public interface IStudentChunkRetrievalService
{
    Task<bool> SubjectHasUsableContentAsync(
        Guid subjectId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudentRetrievedChunk>> RetrieveRelevantChunksAsync(
        Guid subjectId,
        string query,
        int limit,
        CancellationToken cancellationToken = default);
}
