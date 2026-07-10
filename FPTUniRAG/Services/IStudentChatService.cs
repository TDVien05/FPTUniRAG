namespace FPTUniRAG.Services;

public interface IStudentChatService
{
    Task<StudentChatPageDto> GetDashboardAsync(
        Guid userId,
        string studentName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudentChatSessionSummaryDto>> GetSessionsAsync(
        Guid userId,
        Guid? subjectId,
        CancellationToken cancellationToken = default);

    Task<StudentChatSessionDetailDto?> GetSessionDetailAsync(
        Guid userId,
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task<StudentChatCitationDetailDto?> GetCitationDetailAsync(
        Guid userId,
        Guid sessionId,
        Guid documentId,
        int chunkIndex,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StudentSubjectOptionDto>> SearchSubjectsAsync(
        string? query,
        CancellationToken cancellationToken = default);

    Task StreamMessageAsync(
        Guid userId,
        StudentChatSendRequest request,
        Func<string, object, CancellationToken, Task> writeEvent,
        CancellationToken cancellationToken = default);
}
