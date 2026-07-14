using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.DataAccessLayer.Repositories.Chat;

public sealed class StudentChatRepository(AppDbContext context) : IStudentChatRepository
{
    public async Task<IReadOnlyList<ChatSessionSummaryRecord>> GetSessionsAsync(Guid userId, Guid subjectId, CancellationToken cancellationToken = default)
    {
        var sessionRows = await context.Sessions.AsNoTracking()
            .Where(session => session.UserId == userId && session.SubjectId == subjectId)
            .Select(session => new
            {
                session.SessionId,
                SubjectId = session.SubjectId!.Value,
                SubjectCode = session.Subject != null ? session.Subject.SubjectCode : "",
                SubjectName = session.Subject != null ? session.Subject.SubjectName : "",
                session.StartedAt,
                LastMessageAt = session.Messages.Max(message => message.CreatedAt),
                PreviewText = session.Messages
                    .OrderByDescending(message => message.CreatedAt)
                    .ThenByDescending(message => message.MessageId)
                    .Select(message => message.MessageContent)
                    .FirstOrDefault()
            })
            .OrderByDescending(session => session.LastMessageAt ?? session.StartedAt)
            .ToListAsync(cancellationToken);

        return sessionRows
            .Select(session => new ChatSessionSummaryRecord(
                session.SessionId,
                session.SubjectId,
                session.SubjectCode,
                session.SubjectName,
                session.StartedAt,
                session.LastMessageAt,
                session.PreviewText))
            .ToList();
    }

    public Task<ChatSessionRecord?> GetSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default) =>
        context.Sessions.AsNoTracking().Where(s => s.SessionId == sessionId && s.UserId == userId && s.SubjectId != null)
            .Select(s => new ChatSessionRecord(s.SessionId, s.SubjectId!.Value, s.Subject != null ? s.Subject.SubjectCode : "", s.Subject != null ? s.Subject.SubjectName : "", s.StartedAt))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Message>> GetMessagesAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        await context.Messages.AsNoTracking().Where(m => m.SessionId == sessionId).OrderBy(m => m.CreatedAt).ThenBy(m => m.MessageId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Message>> GetRecentMessagesAsync(Guid sessionId, int limit, CancellationToken cancellationToken = default) =>
        await context.Messages.AsNoTracking().Where(m => m.SessionId == sessionId).OrderByDescending(m => m.CreatedAt).Take(limit).OrderBy(m => m.CreatedAt).ToListAsync(cancellationToken);

    public async Task<ChatCitationChunkRecord?> GetCitationChunkAsync(Guid userId, Guid sessionId, Guid documentId, int chunkIndex, CancellationToken cancellationToken = default)
    {
        var subjectId = await context.Sessions.AsNoTracking().Where(s => s.SessionId == sessionId && s.UserId == userId && s.SubjectId != null).Select(s => s.SubjectId).FirstOrDefaultAsync(cancellationToken);
        if (subjectId is null) return null;
        return await context.Chunks.AsNoTracking().Where(c => c.DocumentId == documentId && c.ChunkIndex == chunkIndex && c.Document.SubjectId == subjectId && (c.Document.Status ?? "").ToLower() == "completed")
            .Select(c => new ChatCitationChunkRecord(c.ChunkId, c.DocumentId, c.Document.Title, c.Document.Subject.SubjectCode, c.Document.Subject.SubjectName, c.Document.Chapter.ChapterTitle, c.ChunkIndex, c.Content))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ChatSubjectRecord>> SearchSubjectsAsync(string? query, int limit, CancellationToken cancellationToken = default)
    {
        var subjects = context.Subjects.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query)) subjects = subjects.Where(s => EF.Functions.ILike(s.SubjectCode, $"%{query}%") || EF.Functions.ILike(s.SubjectName, $"%{query}%") || (s.Description != null && EF.Functions.ILike(s.Description, $"%{query}%")));
        return await subjects.OrderBy(s => s.SubjectCode).Take(limit).Select(s => new ChatSubjectRecord(s.SubjectId, s.SubjectCode, s.SubjectName, s.Description)).ToListAsync(cancellationToken);
    }

    public Task<ChatSubjectRecord?> GetSubjectAsync(Guid subjectId, CancellationToken cancellationToken = default) =>
        context.Subjects.AsNoTracking().Where(s => s.SubjectId == subjectId).Select(s => new ChatSubjectRecord(s.SubjectId, s.SubjectCode, s.SubjectName, s.Description)).FirstOrDefaultAsync(cancellationToken);

    public Task<Session?> FindOwnedSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default) =>
        context.Sessions.FirstOrDefaultAsync(s => s.SessionId == sessionId && s.UserId == userId, cancellationToken);

    public async Task SaveUserMessageAsync(Session? newSession, Message message, CancellationToken cancellationToken = default)
    { if (newSession is not null) context.Sessions.Add(newSession); context.Messages.Add(message); await context.SaveChangesAsync(cancellationToken); }

    public async Task SaveAssistantResponseAsync(Message message, TokenUsageLog usage, CancellationToken cancellationToken = default)
    { context.Messages.Add(message); context.TokenUsageLogs.Add(usage); await context.SaveChangesAsync(cancellationToken); }

    public async Task<IReadOnlyList<string>> GetCitationJsonAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        await context.Messages.AsNoTracking().Where(m => m.SessionId == sessionId && m.SenderRole == "assistant" && m.CitationsJson != null)
            .OrderByDescending(m => m.CreatedAt).Select(m => m.CitationsJson!).ToListAsync(cancellationToken);

    public async Task<ChatQuotaRecord> GetQuotaAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var entitlement = await context.StudentActiveChatEntitlements.AsNoTracking().FirstOrDefaultAsync(i => i.UserId == userId, cancellationToken);
        var usage = await context.StudentTokenUsageCurrentMonths.AsNoTracking().FirstOrDefaultAsync(i => i.UserId == userId, cancellationToken);
        return new ChatQuotaRecord(entitlement?.PlanId, entitlement?.PlanCode, entitlement?.MonthlyTokenLimit, usage?.TotalTokensUsedThisMonth ?? 0);
    }
}
