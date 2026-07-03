namespace FPTUniRAG.BusinessLayer.Subjects;

public sealed record SubjectListItemDto(
    Guid SubjectId,
    string SubjectCode,
    string SubjectName,
    string? Description,
    DateTime? CreatedAt);

public sealed record SubjectEditDto(
    Guid SubjectId,
    string SubjectCode,
    string SubjectName,
    string? Description,
    DateTime? CreatedAt);

public sealed record SubjectDeletePreviewDto(
    Guid SubjectId,
    string SubjectCode,
    string SubjectName,
    string? Description,
    DateTime? CreatedAt,
    SubjectDeleteCountsDto Counts);

public sealed record SubjectDeleteCountsDto(
    int TeacherAssignments,
    int Sessions,
    int Messages,
    int Chapters,
    int Documents,
    int ProcessingJobs,
    int Chunks,
    int TestQuestions);

public sealed record UpsertSubjectRequest(
    string SubjectCode,
    string SubjectName,
    string? Description);

public sealed record SubjectHeaderAssignmentListItemDto(
    Guid SubjectId,
    string SubjectCode,
    string SubjectName,
    Guid? CurrentHeaderTeacherId,
    string? CurrentHeaderTeacherName,
    string? CurrentHeaderTeacherEmail);

public sealed record TeacherOptionDto(
    Guid TeacherId,
    string FullName,
    string? Email);
