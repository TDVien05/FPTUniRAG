namespace FPTUniRAG.BusinessLayer.AdminDashboard;

public interface IStudentChatReportService
{
    Task<StudentChatReportDto> GetReportAsync(StudentChatReportQuery query, CancellationToken cancellationToken = default);
}

public sealed record StudentChatReportQuery(
    string? Search,
    Guid? SubjectId,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int Page,
    Guid? SessionId);

public sealed record StudentChatReportDto(
    string Search,
    Guid? SubjectId,
    DateOnly? FromDate,
    DateOnly? ToDate,
    int Page,
    int PageSize,
    int TotalPages,
    string? ValidationMessage,
    StudentChatReportSummaryDto Summary,
    IReadOnlyList<StudentChatSessionRowDto> Sessions,
    IReadOnlyList<StudentChatReportSubjectDto> Subjects,
    StudentChatSessionAnalysisDto? SelectedSession);

public sealed record StudentChatReportSummaryDto(
    int SessionCount,
    int PromptCount,
    int AnsweredCount,
    long PromptTokens,
    long CompletionTokens,
    double? AverageResponseTimeMs);

public sealed record StudentChatSessionRowDto(
    Guid SessionId,
    string StudentName,
    string StudentEmail,
    string? StudentCode,
    string? SubjectCode,
    string? SubjectName,
    DateTime? StartedAt,
    DateTime? LastActivityAt,
    int PromptCount,
    int AnsweredCount,
    long PromptTokens,
    long CompletionTokens,
    double? AverageResponseTimeMs);

public sealed record StudentChatReportSubjectDto(Guid SubjectId, string SubjectCode, string SubjectName);

public sealed record StudentChatSessionAnalysisDto(
    Guid SessionId,
    string StudentName,
    string StudentEmail,
    string? StudentCode,
    string? SubjectCode,
    string? SubjectName,
    DateTime? StartedAt,
    DateTime? LastActivityAt,
    int PromptCount,
    int AnsweredCount,
    long PromptTokens,
    long CompletionTokens,
    double? AverageResponseTimeMs,
    long? DurationMs,
    IReadOnlyList<StudentChatTurnReportDto> Turns);

public sealed record StudentChatTurnReportDto(
    int TurnNumber,
    Guid? PromptMessageId,
    Guid? AnswerMessageId,
    string? PromptText,
    string? AnswerText,
    DateTime? PromptAt,
    DateTime? AnswerAt,
    string Status,
    string? ProviderName,
    string? ModelName,
    long? PromptTokens,
    long? CompletionTokens,
    long? TotalTokens,
    int? RequestCount,
    int? ResponseTimeMs,
    int? RetrievalCount,
    IReadOnlyList<StudentChatReportCitationDto> Citations);

public sealed record StudentChatReportCitationDto(
    int CitationNumber,
    string DocumentTitle,
    string SubjectCode,
    string SubjectName,
    string ChapterTitle,
    int ChunkIndex,
    double SimilarityScore);
