using FPTUniRAG.BusinessLayer.Common;

namespace FPTUniRAG.BusinessLayer.Subjects;

public interface ISubjectManagementService
{
    Task<IReadOnlyList<SubjectListItemDto>> GetSubjectsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubjectTeacherAssignmentListItemDto>> GetSubjectsForTeacherAssignmentAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TeacherHeaderSubjectDashboardItemDto>> GetHeaderSubjectsForTeacherAsync(
        string teacherEmail,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TeacherDocumentManagementItemDto>> GetDocumentManagementItemsForTeacherAsync(
        string teacherEmail,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubjectHeaderAssignmentListItemDto>> GetSubjectsForHeaderAssignmentAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TeacherOptionDto>> GetTeacherOptionsAsync(CancellationToken cancellationToken = default);

    Task<SubjectEditDto?> GetSubjectForEditAsync(Guid subjectId, CancellationToken cancellationToken = default);

    Task<SubjectDeletePreviewDto?> GetSubjectDeletePreviewAsync(Guid subjectId, CancellationToken cancellationToken = default);

    Task<(bool Succeeded, string Message, Guid? SubjectId)> CreateSubjectAsync(
        UpsertSubjectRequest request,
        CancellationToken cancellationToken = default);

    Task<OperationResult> UpdateSubjectAsync(
        Guid subjectId,
        UpsertSubjectRequest request,
        CancellationToken cancellationToken = default);

    Task<OperationResult> AssignHeaderTeacherAsync(
        Guid subjectId,
        Guid teacherId,
        CancellationToken cancellationToken = default);

    Task<OperationResult> AssignTeacherAsync(
        Guid subjectId,
        Guid teacherId,
        CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteSubjectAsync(Guid subjectId, CancellationToken cancellationToken = default);
}
