using FPTUniRAG.DataAccessLayer.Context;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.BusinessLayer.AdminDashboard;

public sealed class AdminDashboardService : IAdminDashboardService
{
    private const int RecentSubjectLimit = 4;
    private readonly AppDbContext _dbContext;

    public AdminDashboardService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AdminDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var totalSubjects = await _dbContext.Subjects
            .AsNoTracking()
            .CountAsync(cancellationToken);

        var subjectsWithDocuments = await _dbContext.Subjects
            .AsNoTracking()
            .CountAsync(subject => subject.Documents.Any(), cancellationToken);

        var recentSubjectRows = await _dbContext.Subjects
            .AsNoTracking()
            .Select(subject => new
            {
                subject.SubjectId,
                subject.SubjectCode,
                subject.SubjectName,
                HeaderTeacherName = subject.TeacherSubjects
                    .Where(link => link.IsHeadOfDepartment)
                    .Select(link => link.Teacher.FullName)
                    .FirstOrDefault(),
                LastUpdatedAt = subject.Documents.Max(document => document.CreatedAt) ?? subject.CreatedAt,
                LatestDocumentStatus = subject.Documents
                    .OrderByDescending(document => document.CreatedAt)
                    .ThenByDescending(document => document.DocumentId)
                    .Select(document => document.Status)
                    .FirstOrDefault(),
                DocumentCount = subject.Documents.Count()
            })
            .OrderByDescending(subject => subject.LastUpdatedAt)
            .ThenBy(subject => subject.SubjectCode)
            .Take(RecentSubjectLimit)
            .ToListAsync(cancellationToken);

        var recentSubjects = recentSubjectRows
            .Select(subject => new AdminDashboardSubjectDto(
                subject.SubjectId,
                subject.SubjectCode,
                subject.SubjectName,
                subject.HeaderTeacherName,
                subject.LastUpdatedAt,
                subject.LatestDocumentStatus,
                subject.DocumentCount))
            .ToList();

        return new AdminDashboardDto(totalSubjects, subjectsWithDocuments, recentSubjects);
    }
}
