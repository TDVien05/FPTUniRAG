namespace FPTUniRAG.BusinessLayer.Subjects;

public sealed record SubjectListItemDto(
    Guid SubjectId,
    string SubjectCode,
    string SubjectName,
    string? Description,
    string DefaultChunkingStrategy,
    int DefaultFixedChunkSize,
    DateTime? CreatedAt);

public sealed record SubjectEditDto(
    Guid SubjectId,
    string SubjectCode,
    string SubjectName,
    string? Description,
    string DefaultChunkingStrategy,
    int DefaultFixedChunkSize,
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
    string? Description,
    string DefaultChunkingStrategy,
    int DefaultFixedChunkSize);

public sealed record SubjectTeacherAssignmentListItemDto(
    Guid SubjectId,
    string SubjectCode,
    string SubjectName,
    IReadOnlyList<TeacherAssignmentDto> AssignedTeachers);

public sealed record TeacherHeaderSubjectDashboardItemDto(
    Guid SubjectId,
    string SubjectCode,
    string SubjectName,
    string? Description,
    string DefaultChunkingStrategy,
    int DefaultFixedChunkSize,
    int DocumentCount,
    int ChapterCount);

public sealed record TeacherDocumentManagementItemDto(
    Guid SubjectId,
    string SubjectCode,
    string SubjectName,
    int DocumentCount,
    DateTime? LastUpdatedAt,
    Guid? LatestDocumentId,
    string? LatestDocumentTitle,
    string? LatestDocumentStatus,
    IReadOnlyList<TeacherSubjectDocumentDto> Documents);

public sealed record TeacherSubjectDocumentDto(
    Guid DocumentId,
    Guid ChapterId,
    string ChapterTitle,
    string DocumentTitle,
    string Status,
    string? ProcessingError,
    int ChunkCount,
    DateTime? CreatedAt);

public sealed record SubjectHeaderAssignmentListItemDto(
    Guid SubjectId,
    string SubjectCode,
    string SubjectName,
    Guid? CurrentHeaderTeacherId,
    string? CurrentHeaderTeacherName,
    string? CurrentHeaderTeacherEmail);

public sealed record TeacherAssignmentDto(
    Guid TeacherId,
    string FullName,
    string? Email,
    bool IsHeader);

public sealed record TeacherOptionDto(
    Guid TeacherId,
    string FullName,
    string? Email);
