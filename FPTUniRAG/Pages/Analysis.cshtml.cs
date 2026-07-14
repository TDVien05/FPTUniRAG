using FPTUniRAG.BusinessLayer.AdminDashboard;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace FPTUniRAG.Pages;

public class AnalysisModel(IAnalysisService analysisService) : PageModel
{
    public int ActiveSubscriberCount { get; private set; }
    public int PaidPurchaseCount { get; private set; }
    public decimal PaidRevenue { get; private set; }
    [BindProperty(SupportsGet = true)] public string? Period { get; set; }
    public string SelectedPeriod { get; private set; } = "day";
    public string SelectedPeriodLabel { get; private set; } = "Today";
    public IReadOnlyList<PlanAnalyticsRow> PlanAnalytics { get; private set; } = [];
    public IReadOnlyList<TokenUsageTrendPoint> TokenUsageTrend { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await analysisService.GetAnalysisAsync(Period, cancellationToken);
        ActiveSubscriberCount = result.ActiveSubscriberCount; PaidPurchaseCount = result.PaidPurchaseCount; PaidRevenue = result.PaidRevenue;
        SelectedPeriod = result.Period; SelectedPeriodLabel = result.PeriodLabel;
        PlanAnalytics = result.Plans.Select(x => new PlanAnalyticsRow(x.PlanName, x.MonthlyPrice, x.ActiveStudentCount, x.PaidPurchaseCount, x.PaidRevenue)).ToList();
        TokenUsageTrend = result.TokenUsageTrend.Select(x => new TokenUsageTrendPoint(x.Label, x.TotalTokens, x.Percentage)).ToList();
    }

    public sealed record PlanAnalyticsRow(string PlanName, decimal MonthlyPrice, int ActiveStudentCount, int PaidPurchaseCount, decimal PaidRevenue);
    public sealed record TokenUsageTrendPoint(string Label, long TotalTokens, decimal Percentage);
}
