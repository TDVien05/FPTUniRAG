namespace FPTUniRAG.BusinessLayer.AdminDashboard;

public sealed record AdminDashboardDto(
    int TotalSubjects,
    int SubjectsWithDocuments,
    IReadOnlyList<AdminDashboardSubjectDto> RecentSubjects);

public sealed record AdminDashboardSubjectDto(
    Guid SubjectId,
    string SubjectCode,
    string SubjectName,
    string? HeaderTeacherName,
    DateTime? LastUpdatedAt,
    string? LatestDocumentStatus,
    int DocumentCount);
