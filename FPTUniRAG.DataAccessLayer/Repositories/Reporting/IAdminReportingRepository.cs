namespace FPTUniRAG.DataAccessLayer.Repositories.Reporting;

public interface IAdminReportingRepository
{
    Task<AdminDashboardRecord> GetDashboardAsync(int recentSubjectLimit, CancellationToken cancellationToken = default);
    Task<AnalysisDataRecord> GetAnalysisDataAsync(DateTime now, DateTime start, DateTime end, CancellationToken cancellationToken = default);
}

public sealed record AdminDashboardRecord(int TotalSubjects, int SubjectsWithDocuments, IReadOnlyList<RecentSubjectRecord> RecentSubjects);
public sealed record RecentSubjectRecord(Guid SubjectId, string SubjectCode, string SubjectName, string? HeaderTeacherName, DateTime? LastUpdatedAt, string? LatestDocumentStatus, int DocumentCount);
public sealed record AnalysisDataRecord(IReadOnlyList<AnalysisPlanRecord> Plans, IReadOnlyList<AnalysisSubscriptionRecord> ActiveSubscriptions, IReadOnlyList<AnalysisPaymentRecord> PaidTransactions, IReadOnlyList<AnalysisTokenRecord> TokenUsage);
public sealed record AnalysisPlanRecord(Guid PlanId, string PlanName, decimal MonthlyPrice);
public sealed record AnalysisSubscriptionRecord(Guid UserId, Guid PlanId);
public sealed record AnalysisPaymentRecord(Guid UserId, Guid PlanId, decimal Amount);
public sealed record AnalysisTokenRecord(DateTime UsedAt, long TotalTokens);
