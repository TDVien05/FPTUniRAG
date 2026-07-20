using FPTUniRAG.DataAccessLayer.Entities;

namespace FPTUniRAG.DataAccessLayer.Repositories.Chat;

public interface IStudentChatRepository
{
    Task<IReadOnlyList<ChatSessionSummaryRecord>> GetSessionsAsync(Guid userId, Guid subjectId, CancellationToken cancellationToken = default);
    Task<ChatSessionRecord?> GetSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Message>> GetMessagesAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Message>> GetRecentMessagesAsync(Guid sessionId, int limit, CancellationToken cancellationToken = default);
    Task<ChatCitationChunkRecord?> GetCitationChunkAsync(Guid userId, Guid sessionId, Guid documentId, int chunkIndex, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatSubjectRecord>> SearchSubjectsAsync(string? query, int limit, CancellationToken cancellationToken = default);
    Task<ChatSubjectRecord?> GetSubjectAsync(Guid subjectId, CancellationToken cancellationToken = default);
    Task<Session?> FindOwnedSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
    Task SaveUserMessageAsync(Session? newSession, Message message, CancellationToken cancellationToken = default);
    Task SaveAssistantResponseAsync(Message message, TokenUsageLog usage, CancellationToken cancellationToken = default);
    Task<string?> GetMessageCitationsJsonAsync(Guid userId, Guid sessionId, Guid messageId, CancellationToken cancellationToken = default);
    Task<ChatQuotaRecord> GetQuotaAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatUsageRunRecord>> GetUsageRunsAsync(string featureName, CancellationToken cancellationToken = default);
}

public sealed record ChatUsageRunRecord(
    Guid? SessionId,
    string? SubjectCode,
    string? StudentName,
    string? Model,
    long PromptTokens,
    long CompletionTokens,
    long TotalTokens,
    int RequestCount,
    int? ResponseTimeMs,
    DateTime UsedAt);

public sealed record ChatSessionSummaryRecord(Guid SessionId, Guid SubjectId, string SubjectCode, string SubjectName, DateTime? StartedAt, DateTime? LastMessageAt, string? PreviewText);
public sealed record ChatSessionRecord(Guid SessionId, Guid SubjectId, string SubjectCode, string SubjectName, DateTime? StartedAt);
public sealed record ChatSubjectRecord(Guid SubjectId, string SubjectCode, string SubjectName, string? Description);
public sealed record ChatCitationChunkRecord(Guid ChunkId, Guid DocumentId, string Title, string SubjectCode, string SubjectName, string ChapterTitle, int ChunkIndex, string Content);
public sealed record ChatQuotaRecord(Guid? PlanId, string? PlanCode, long? MonthlyTokenLimit, decimal TokensUsedThisMonth);
