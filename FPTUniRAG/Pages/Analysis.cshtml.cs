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
    public IReadOnlyList<PlanPurchaseRow> Purchases { get; private set; } = [];
    public IReadOnlyList<TokenUsageTrendPoint> TokenUsageTrend { get; private set; } = [];
    public IReadOnlyList<PlanOverviewRow> PlanOverview { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var result = await analysisService.GetAnalysisAsync(Period, cancellationToken);
        ActiveSubscriberCount = result.ActiveSubscriberCount; PaidPurchaseCount = result.PaidPurchaseCount; PaidRevenue = result.PaidRevenue;
        SelectedPeriod = result.Period; SelectedPeriodLabel = result.PeriodLabel;
        Purchases = result.Purchases.Select(x => new PlanPurchaseRow(x.StudentName, x.StudentEmail, x.PlanName, x.Amount, x.PurchasedAt)).ToList();
        TokenUsageTrend = result.TokenUsageTrend.Select(x => new TokenUsageTrendPoint(x.Label, x.TotalTokens, x.Percentage)).ToList();
        PlanOverview = result.PlanOverview.Select(x => new PlanOverviewRow(x.PlanCode, x.PlanName, x.Description, x.MonthlyPrice, x.MonthlyTokenLimit, x.IsActive, x.HasAdvancedModels, x.HasPrioritySupport, x.HasFileUpload, x.HasHistoryExport)).ToList();
    }

    public sealed record PlanPurchaseRow(string StudentName, string StudentEmail, string PlanName, decimal Amount, DateTime PurchasedAt);
    public sealed record TokenUsageTrendPoint(string Label, long TotalTokens, decimal Percentage);
    public sealed record PlanOverviewRow(string PlanCode, string PlanName, string? Description, decimal MonthlyPrice, long MonthlyTokenLimit, bool IsActive,
        bool HasAdvancedModels, bool HasPrioritySupport, bool HasFileUpload, bool HasHistoryExport);
}
