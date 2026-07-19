using FPTUniRAG.BusinessLayer.Subscriptions;

namespace FPTUniRAG.BusinessLayer.AdminDashboard;

public interface IAnalysisService
{
    Task<AnalysisDashboardDto> GetAnalysisAsync(string? period, CancellationToken cancellationToken = default);
}

public sealed record AnalysisDashboardDto(string Period, string PeriodLabel, int ActiveSubscriberCount, int PaidPurchaseCount,
    decimal PaidRevenue, IReadOnlyList<PlanPurchaseDto> Purchases, IReadOnlyList<TokenUsageTrendDto> TokenUsageTrend,
    IReadOnlyList<ManagedSubscriptionPlanDto> PlanOverview);
public sealed record PlanPurchaseDto(string StudentName, string StudentEmail, string PlanName, decimal Amount, DateTime PurchasedAt);
public sealed record TokenUsageTrendDto(string Label, long TotalTokens, decimal Percentage);
