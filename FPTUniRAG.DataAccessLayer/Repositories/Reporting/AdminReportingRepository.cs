using FPTUniRAG.DataAccessLayer.Context;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.DataAccessLayer.Repositories.Reporting;

public sealed class AdminReportingRepository(AppDbContext context) : IAdminReportingRepository
{
    public async Task<AnalysisDataRecord> GetAnalysisDataAsync(DateTime now, DateTime start, DateTime end, CancellationToken cancellationToken = default)
    {
        var plans = await context.SubscriptionPlans.AsNoTracking().OrderBy(p => p.MonthlyPrice).ThenBy(p => p.PlanName).Select(p => new AnalysisPlanRecord(p.PlanId, p.PlanName, p.MonthlyPrice)).ToListAsync(cancellationToken);
        var subscriptions = await context.StudentSubscriptions.AsNoTracking().Where(s => s.SubscriptionStatus == "active" && (s.ExpiresAt == null || s.ExpiresAt > now)).Select(s => new AnalysisSubscriptionRecord(s.UserId, s.PlanId)).ToListAsync(cancellationToken);
        var payments = await context.StripeCheckoutTransactions.AsNoTracking().Where(t => t.PaymentStatus == "paid" && (t.ConfirmedAt ?? t.CreatedAt) >= start && (t.ConfirmedAt ?? t.CreatedAt) < end).Select(t => new AnalysisPaymentRecord(t.UserId, t.PlanId, t.Amount)).ToListAsync(cancellationToken);
        var usage = await context.TokenUsageLogs.AsNoTracking().Where(u => u.UsedAt >= start && u.UsedAt < end).Select(u => new AnalysisTokenRecord(u.UsedAt, u.TotalTokens)).ToListAsync(cancellationToken);
        return new AnalysisDataRecord(plans, subscriptions, payments, usage);
    }

    public async Task<AdminDashboardRecord> GetDashboardAsync(int recentSubjectLimit, CancellationToken cancellationToken = default)
    {
        var total = await context.Subjects.AsNoTracking().CountAsync(cancellationToken);
        var withDocuments = await context.Subjects.AsNoTracking().CountAsync(subject => subject.Documents.Any(), cancellationToken);
        var recentRows = await context.Subjects.AsNoTracking()
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
            .Take(recentSubjectLimit)
            .ToListAsync(cancellationToken);

        var recent = recentRows
            .Select(subject => new RecentSubjectRecord(
                subject.SubjectId,
                subject.SubjectCode,
                subject.SubjectName,
                subject.HeaderTeacherName,
                subject.LastUpdatedAt,
                subject.LatestDocumentStatus,
                subject.DocumentCount))
            .ToList();
        return new AdminDashboardRecord(total, withDocuments, recent);
    }
}
