namespace FPTUniRAG.BusinessLayer.AdminDashboard;

public interface IAnalysisService
{
    Task<AnalysisDashboardDto> GetAnalysisAsync(string? period, CancellationToken cancellationToken = default);
}

public sealed record AnalysisDashboardDto(string Period, string PeriodLabel, int ActiveSubscriberCount, int PaidPurchaseCount,
    decimal PaidRevenue, IReadOnlyList<PlanAnalyticsDto> Plans, IReadOnlyList<TokenUsageTrendDto> TokenUsageTrend);
public sealed record PlanAnalyticsDto(string PlanName, decimal MonthlyPrice, int ActiveStudentCount, int PaidPurchaseCount, decimal PaidRevenue);
public sealed record TokenUsageTrendDto(string Label, long TotalTokens, decimal Percentage);
