using FPTUniRAG.DataAccessLayer.Repositories.Reporting;

namespace FPTUniRAG.BusinessLayer.AdminDashboard;

public sealed class AdminDashboardService : IAdminDashboardService
{
    private const int RecentSubjectLimit = 4;
    private readonly IAdminReportingRepository _reportingRepository;

    public AdminDashboardService(IAdminReportingRepository reportingRepository)
    {
        _reportingRepository = reportingRepository;
    }

    public async Task<AdminDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var dashboard = await _reportingRepository.GetDashboardAsync(RecentSubjectLimit, cancellationToken);

        var recentSubjects = dashboard.RecentSubjects
            .Select(subject => new AdminDashboardSubjectDto(
                subject.SubjectId,
                subject.SubjectCode,
                subject.SubjectName,
                subject.HeaderTeacherName,
                subject.LastUpdatedAt,
                subject.LatestDocumentStatus,
                subject.DocumentCount))
            .ToList();

        return new AdminDashboardDto(dashboard.TotalSubjects, dashboard.SubjectsWithDocuments, recentSubjects);
    }
}
