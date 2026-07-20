using FPTUniRAG.BusinessLayer.AdminDashboard;
using FPTUniRAG.DataAccessLayer.Repositories.Reporting;
using Xunit;

namespace FPTUniRAG.Tests.AdminDashboard;

public sealed class StudentChatReportServiceTests
{
    [Fact]
    public async Task GetReportAsync_NormalizesInvalidDatesAndSearch()
    {
        var repository = new FakeRepository();
        var service = new StudentChatReportService(repository);

        var report = await service.GetReportAsync(new StudentChatReportQuery(
            "  student@example.com  ",
            null,
            new DateOnly(2026, 7, 20),
            new DateOnly(2026, 7, 19),
            0,
            null));

        Assert.Equal("student@example.com", report.Search);
        Assert.Equal(1, report.Page);
        Assert.Null(report.FromDate);
        Assert.Null(report.ToDate);
        Assert.NotNull(report.ValidationMessage);
        Assert.Null(repository.LastFilter!.FromInclusive);
        Assert.Null(repository.LastFilter.ToExclusive);
    }

    private sealed class FakeRepository : IStudentChatReportRepository
    {
        public StudentChatReportFilterRecord? LastFilter { get; private set; }

        public Task<StudentChatReportSearchRecord> SearchSessionsAsync(StudentChatReportFilterRecord filter, CancellationToken cancellationToken = default)
        {
            LastFilter = filter;
            return Task.FromResult(new StudentChatReportSearchRecord(0, 0, 0, 0, 0, null, []));
        }

        public Task<StudentChatReportSessionRecord?> GetSessionDetailAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StudentChatReportSessionRecord?>(null);

        public Task<IReadOnlyList<StudentChatReportSubjectRecord>> GetSubjectsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StudentChatReportSubjectRecord>>([]);
    }
}
