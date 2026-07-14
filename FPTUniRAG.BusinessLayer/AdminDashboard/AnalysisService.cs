using FPTUniRAG.DataAccessLayer.Repositories.Reporting;

namespace FPTUniRAG.BusinessLayer.AdminDashboard;

public sealed class AnalysisService(IAdminReportingRepository repository) : IAnalysisService
{
    public async Task<AnalysisDashboardDto> GetAnalysisAsync(string? requested, CancellationToken cancellationToken = default)
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var range = CreateRange(requested, now);
        var data = await repository.GetAnalysisDataAsync(now, range.Start, range.End, cancellationToken);
        var activeByPlan = data.ActiveSubscriptions.GroupBy(x => x.PlanId).ToDictionary(g => g.Key, g => g.Select(x => x.UserId).Distinct().Count());
        var paidByPlan = data.PaidTransactions.GroupBy(x => x.PlanId).ToDictionary(g => g.Key, g => (g.Count(), g.Sum(x => x.Amount)));
        var plans = data.Plans.Select(p => new PlanAnalyticsDto(p.PlanName, p.MonthlyPrice, activeByPlan.GetValueOrDefault(p.PlanId), paidByPlan.GetValueOrDefault(p.PlanId).Item1, paidByPlan.GetValueOrDefault(p.PlanId).Item2)).ToList();
        var totals = new long[range.Slots]; foreach (var item in data.TokenUsage) { var i = range.Index(item.UsedAt); if (i >= 0 && i < totals.Length) totals[i] += item.TotalTokens; }
        var max = totals.DefaultIfEmpty().Max();
        var trend = totals.Select((value, index) => new TokenUsageTrendDto(Label(range.Period, index), value, max == 0 ? 0 : decimal.Round(value * 100m / max, 2))).ToList();
        return new(range.Period, range.Label, data.ActiveSubscriptions.Select(x => x.UserId).Distinct().Count(), data.PaidTransactions.Count, data.PaidTransactions.Sum(x => x.Amount), plans, trend);
    }

    private sealed record Range(string Period, string Label, DateTime Start, DateTime End, int Slots, Func<DateTime, int> Index);
    private static Range CreateRange(string? value, DateTime now) => value?.Trim().ToLowerInvariant() switch
    { "month" => new("month", now.ToString("MMMM yyyy"), new(now.Year, now.Month, 1), new DateTime(now.Year, now.Month, 1).AddMonths(1), DateTime.DaysInMonth(now.Year, now.Month), d => d.Day - 1),
      "year" => new("year", now.Year.ToString(), new(now.Year, 1, 1), new DateTime(now.Year, 1, 1).AddYears(1), 12, d => d.Month - 1),
      _ => new("day", "Today", now.Date, now.Date.AddDays(1), 24, d => d.Hour) };
    private static string Label(string period, int index) => period switch { "day" => $"{index:00}:00", "month" => (index + 1).ToString(), "year" => new DateTime(2000, index + 1, 1).ToString("MMM"), _ => "" };
}
