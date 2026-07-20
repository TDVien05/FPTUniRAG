using FPTUniRAG.DataAccessLayer.Context;
using FPTUniRAG.DataAccessLayer.Entities;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.DataAccessLayer.Repositories.Reporting;

public sealed class StudentChatReportRepository(AppDbContext context) : IStudentChatReportRepository
{
    private const string StudentRole = "student";
    private const string StudentChatFeature = "student_chat";
    private const string LikeEscapeCharacter = "\\";

    // Without this, a search term containing % or _ is treated as a wildcard instead of literal text.
    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public async Task<StudentChatReportSearchRecord> SearchSessionsAsync(
        StudentChatReportFilterRecord filter,
        CancellationToken cancellationToken = default)
    {
        var sessions = ApplyFilters(context.Sessions.AsNoTracking(), filter);

        var totalCount = await sessions.CountAsync(cancellationToken);
        var promptCount = await sessions.SumAsync(
            session => session.Messages.Count(message => message.SenderRole == "student"),
            cancellationToken);
        var answeredCount = await sessions.SumAsync(
            session => session.Messages.Count(message => message.SenderRole == "assistant"),
            cancellationToken);
        var promptTokens = await sessions.SumAsync(
            session => session.TokenUsageLogs
                .Where(usage => usage.FeatureName == StudentChatFeature)
                .Sum(usage => (long?)usage.PromptTokens) ?? 0,
            cancellationToken);
        var completionTokens = await sessions.SumAsync(
            session => session.TokenUsageLogs
                .Where(usage => usage.FeatureName == StudentChatFeature)
                .Sum(usage => (long?)usage.CompletionTokens) ?? 0,
            cancellationToken);
        var averageResponseTimeMs = await sessions
            .SelectMany(session => session.TokenUsageLogs)
            .Where(usage => usage.FeatureName == StudentChatFeature && usage.ResponseTimeMs.HasValue)
            .AverageAsync(usage => (double?)usage.ResponseTimeMs, cancellationToken);

        var rows = await sessions
            .OrderByDescending(session => session.Messages.Max(message => message.CreatedAt))
            .ThenByDescending(session => session.StartedAt)
            .ThenByDescending(session => session.SessionId)
            .Select(session => new StudentChatReportSessionRowRecord(
                session.SessionId,
                session.UserId,
                session.User.FullName,
                session.User.Email,
                session.User.StudentCode,
                session.SubjectId,
                session.Subject != null ? session.Subject.SubjectCode : null,
                session.Subject != null ? session.Subject.SubjectName : null,
                session.StartedAt,
                session.Messages.Max(message => message.CreatedAt) ?? session.StartedAt,
                session.Messages.Count(message => message.SenderRole == "student"),
                session.Messages.Count(message => message.SenderRole == "assistant"),
                session.TokenUsageLogs
                    .Where(usage => usage.FeatureName == StudentChatFeature)
                    .Sum(usage => (long?)usage.PromptTokens) ?? 0,
                session.TokenUsageLogs
                    .Where(usage => usage.FeatureName == StudentChatFeature)
                    .Sum(usage => (long?)usage.CompletionTokens) ?? 0,
                session.TokenUsageLogs
                    .Where(usage => usage.FeatureName == StudentChatFeature && usage.ResponseTimeMs.HasValue)
                    .Average(usage => (double?)usage.ResponseTimeMs)))
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new StudentChatReportSearchRecord(
            totalCount,
            promptCount,
            answeredCount,
            promptTokens,
            completionTokens,
            averageResponseTimeMs,
            rows);
    }

    public async Task<StudentChatReportSessionRecord?> GetSessionDetailAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var header = await context.Sessions.AsNoTracking()
            .Where(session => session.SessionId == sessionId
                && session.User.Role != null
                && session.User.Role.ToLower() == StudentRole)
            .Select(session => new
            {
                session.SessionId,
                StudentId = session.UserId,
                StudentName = session.User.FullName,
                StudentEmail = session.User.Email,
                session.User.StudentCode,
                session.SubjectId,
                SubjectCode = session.Subject != null ? session.Subject.SubjectCode : null,
                SubjectName = session.Subject != null ? session.Subject.SubjectName : null,
                session.StartedAt,
                session.EndedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        var messages = await context.Messages.AsNoTracking()
            .Where(message => message.SessionId == sessionId)
            .OrderBy(message => message.CreatedAt)
            .ThenBy(message => message.MessageId)
            .Select(message => new StudentChatReportMessageRecord(
                message.MessageId,
                message.SenderRole,
                message.MessageContent,
                message.CitationsJson,
                message.CreatedAt))
            .ToListAsync(cancellationToken);

        var usage = await context.TokenUsageLogs.AsNoTracking()
            .Where(item => item.SessionId == sessionId && item.FeatureName == StudentChatFeature)
            .OrderBy(item => item.UsedAt)
            .ThenBy(item => item.TokenUsageId)
            .Select(item => new StudentChatReportUsageRecord(
                item.TokenUsageId,
                item.MessageId,
                item.ProviderName,
                item.ModelName,
                item.PromptTokens,
                item.CompletionTokens,
                item.TotalTokens,
                item.RequestCount,
                item.ResponseTimeMs,
                item.UsedAt,
                item.MetadataJson))
            .ToListAsync(cancellationToken);

        return new StudentChatReportSessionRecord(
            header.SessionId,
            header.StudentId,
            header.StudentName,
            header.StudentEmail,
            header.StudentCode,
            header.SubjectId,
            header.SubjectCode,
            header.SubjectName,
            header.StartedAt,
            header.EndedAt,
            messages,
            usage);
    }

    public async Task<IReadOnlyList<StudentChatReportSubjectRecord>> GetSubjectsAsync(CancellationToken cancellationToken = default) =>
        await context.Subjects.AsNoTracking()
            .OrderBy(subject => subject.SubjectCode)
            .Select(subject => new StudentChatReportSubjectRecord(subject.SubjectId, subject.SubjectCode, subject.SubjectName))
            .ToListAsync(cancellationToken);

    private static IQueryable<Session> ApplyFilters(IQueryable<Session> sessions, StudentChatReportFilterRecord filter)
    {
        sessions = sessions.Where(session =>
            session.User.Role != null
            && session.User.Role.ToLower() == StudentRole
            && session.Messages.Any());

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{EscapeLikePattern(filter.Search!)}%";
            sessions = sessions.Where(session =>
                EF.Functions.ILike(session.User.FullName, pattern, LikeEscapeCharacter)
                || EF.Functions.ILike(session.User.Email, pattern, LikeEscapeCharacter)
                || (session.User.StudentCode != null && EF.Functions.ILike(session.User.StudentCode, pattern, LikeEscapeCharacter)));
        }

        if (filter.SubjectId.HasValue)
        {
            sessions = sessions.Where(session => session.SubjectId == filter.SubjectId.Value);
        }

        if (filter.FromInclusive.HasValue)
        {
            sessions = sessions.Where(session =>
                (session.Messages.Max(message => message.CreatedAt) ?? session.StartedAt) >= filter.FromInclusive.Value);
        }

        if (filter.ToExclusive.HasValue)
        {
            sessions = sessions.Where(session =>
                (session.Messages.Max(message => message.CreatedAt) ?? session.StartedAt) < filter.ToExclusive.Value);
        }

        return sessions;
    }
}
