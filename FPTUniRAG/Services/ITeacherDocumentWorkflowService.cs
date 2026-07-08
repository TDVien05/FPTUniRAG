namespace FPTUniRAG.Services;

public interface ITeacherDocumentWorkflowService
{
    Task<TeacherUploadContextDto?> GetUploadContextAsync(
        string teacherEmail,
        Guid subjectId,
        CancellationToken cancellationToken = default);

    Task<TeacherDocumentUploadResult> UploadAsync(
        string teacherEmail,
        TeacherDocumentUploadCommand command,
        CancellationToken cancellationToken = default);

    Task<TeacherDocumentDetailDto?> GetDocumentDetailAsync(
        string teacherEmail,
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task<TeacherDocumentUploadResult> RetryEmbeddingSyncAsync(
        string teacherEmail,
        Guid documentId,
        CancellationToken cancellationToken = default);
}
