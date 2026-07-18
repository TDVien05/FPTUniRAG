namespace FPTUniRAG.BusinessLayer.Rag.Chat;

public interface IStudentChunkRetrievalService
{
    Task<bool> SubjectHasUsableContentAsync(
        Guid subjectId,
        CancellationToken cancellationToken = default);

    Task<StudentChunkRetrievalResult> RetrieveRelevantChunksAsync(
        Guid subjectId,
        string query,
        int limit,
        CancellationToken cancellationToken = default);
}

public sealed record StudentChunkRetrievalResult(
    IReadOnlyList<StudentRetrievedChunk> Chunks,
    bool UsedLexicalFallback);
