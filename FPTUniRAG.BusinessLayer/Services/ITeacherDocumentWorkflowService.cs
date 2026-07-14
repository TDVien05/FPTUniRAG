namespace FPTUniRAG.BusinessLayer.Services;

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

    Task<TeacherDocumentUploadResult> DeleteChapterAsync(
        string teacherEmail,
        Guid subjectId,
        Guid chapterId,
        CancellationToken cancellationToken = default);

    Task<TeacherDocumentProcessingStatusDto?> GetProcessingStatusAsync(
        string teacherEmail,
        Guid documentId,
        CancellationToken cancellationToken = default);

    Task ProcessDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);
}
