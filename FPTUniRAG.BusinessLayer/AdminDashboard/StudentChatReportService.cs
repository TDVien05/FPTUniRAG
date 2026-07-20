using FPTUniRAG.DataAccessLayer.Repositories.Reporting;

namespace FPTUniRAG.BusinessLayer.AdminDashboard;

public sealed class StudentChatReportService(IStudentChatReportRepository repository) : IStudentChatReportService
{
    private const int PageSize = 20;
    private const int MaxSearchLength = 100;

    public async Task<StudentChatReportDto> GetReportAsync(StudentChatReportQuery query, CancellationToken cancellationToken = default)
    {
        var search = NormalizeSearch(query.Search);
        var page = Math.Max(1, query.Page);
        string? validationMessage = null;
        var fromDate = query.FromDate;
        var toDate = query.ToDate;

        if (fromDate.HasValue && toDate.HasValue && toDate.Value < fromDate.Value)
        {
            validationMessage = "To date must be on or after From date.";
            fromDate = null;
            toDate = null;
        }

        var filter = BuildFilter(search, query.SubjectId, fromDate, toDate, page);
        var result = await repository.SearchSessionsAsync(filter, cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(result.TotalCount / (double)PageSize));

        if (page > totalPages)
        {
            page = totalPages;
            filter = BuildFilter(search, query.SubjectId, fromDate, toDate, page);
            result = await repository.SearchSessionsAsync(filter, cancellationToken);
        }

        var sessionId = query.SessionId ?? result.Sessions.FirstOrDefault()?.SessionId;
        StudentChatSessionAnalysisDto? selectedSession = null;
        if (sessionId.HasValue)
        {
            var detail = await repository.GetSessionDetailAsync(sessionId.Value, cancellationToken);
            if (detail is null)
            {
                validationMessage ??= "The selected student chat session is unavailable.";
            }
            else
            {
                selectedSession = StudentChatTurnReconstructor.BuildSession(detail);
            }
        }

        var subjects = await repository.GetSubjectsAsync(cancellationToken);

        return new StudentChatReportDto(
            search,
            query.SubjectId,
            fromDate,
            toDate,
            page,
            PageSize,
            totalPages,
            validationMessage,
            new StudentChatReportSummaryDto(
                result.TotalCount,
                result.PromptCount,
                result.AnsweredCount,
                result.PromptTokens,
                result.CompletionTokens,
                result.AverageResponseTimeMs),
            result.Sessions.Select(MapRow).ToArray(),
            subjects.Select(subject => new StudentChatReportSubjectDto(subject.SubjectId, subject.SubjectCode, subject.SubjectName)).ToArray(),
            selectedSession);
    }

    private static StudentChatReportFilterRecord BuildFilter(
        string search,
        Guid? subjectId,
        DateOnly? fromDate,
        DateOnly? toDate,
        int page) =>
        new(
            search,
            subjectId,
            fromDate.HasValue ? DatabaseTimestamp(fromDate.Value) : null,
            toDate.HasValue ? DatabaseTimestamp(toDate.Value.AddDays(1)) : null,
            page,
            PageSize);

    private static StudentChatSessionRowDto MapRow(StudentChatReportSessionRowRecord row) =>
        new(
            row.SessionId,
            row.StudentName,
            row.StudentEmail,
            row.StudentCode,
            row.SubjectCode,
            row.SubjectName,
            row.StartedAt,
            row.LastActivityAt,
            row.PromptCount,
            row.AnsweredCount,
            row.PromptTokens,
            row.CompletionTokens,
            row.AverageResponseTimeMs);

    private static string NormalizeSearch(string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= MaxSearchLength ? normalized : normalized[..MaxSearchLength];
    }

    private static DateTime DatabaseTimestamp(DateOnly date) =>
        DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
}
