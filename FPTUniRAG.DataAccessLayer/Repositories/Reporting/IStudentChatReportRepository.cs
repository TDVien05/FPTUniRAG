namespace FPTUniRAG.DataAccessLayer.Repositories.Reporting;

public interface IStudentChatReportRepository
{
    Task<StudentChatReportSearchRecord> SearchSessionsAsync(StudentChatReportFilterRecord filter, CancellationToken cancellationToken = default);
    Task<StudentChatReportSessionRecord?> GetSessionDetailAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StudentChatReportSubjectRecord>> GetSubjectsAsync(CancellationToken cancellationToken = default);
}

public sealed record StudentChatReportFilterRecord(
    string? Search,
    Guid? SubjectId,
    DateTime? FromInclusive,
    DateTime? ToExclusive,
    int Page,
    int PageSize);

public sealed record StudentChatReportSearchRecord(
    int TotalCount,
    int PromptCount,
    int AnsweredCount,
    long PromptTokens,
    long CompletionTokens,
    double? AverageResponseTimeMs,
    IReadOnlyList<StudentChatReportSessionRowRecord> Sessions);

public sealed record StudentChatReportSessionRowRecord(
    Guid SessionId,
    Guid StudentId,
    string StudentName,
    string StudentEmail,
    string? StudentCode,
    Guid? SubjectId,
    string? SubjectCode,
    string? SubjectName,
    DateTime? StartedAt,
    DateTime? LastActivityAt,
    int PromptCount,
    int AnsweredCount,
    long PromptTokens,
    long CompletionTokens,
    double? AverageResponseTimeMs);

public sealed record StudentChatReportSessionRecord(
    Guid SessionId,
    Guid StudentId,
    string StudentName,
    string StudentEmail,
    string? StudentCode,
    Guid? SubjectId,
    string? SubjectCode,
    string? SubjectName,
    DateTime? StartedAt,
    DateTime? EndedAt,
    IReadOnlyList<StudentChatReportMessageRecord> Messages,
    IReadOnlyList<StudentChatReportUsageRecord> Usage);

public sealed record StudentChatReportMessageRecord(
    Guid MessageId,
    string SenderRole,
    string MessageContent,
    string? CitationsJson,
    DateTime? CreatedAt);

public sealed record StudentChatReportUsageRecord(
    Guid TokenUsageId,
    Guid? MessageId,
    string? ProviderName,
    string? ModelName,
    long PromptTokens,
    long CompletionTokens,
    long TotalTokens,
    int RequestCount,
    int? ResponseTimeMs,
    DateTime UsedAt,
    string? MetadataJson);

public sealed record StudentChatReportSubjectRecord(Guid SubjectId, string SubjectCode, string SubjectName);
