using FPTUniRAG.DataAccessLayer.Context;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace FPTUniRAG.Pages;

public class AnalysisModel : PageModel
{
    private readonly AppDbContext _dbContext;

    public AnalysisModel(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public int ActiveSubscriberCount { get; private set; }

    public int PaidPurchaseCount { get; private set; }

    public decimal PaidRevenue { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Period { get; set; }

    public string SelectedPeriod { get; private set; } = "day";

    public string SelectedPeriodLabel { get; private set; } = "Today";

    public IReadOnlyList<PlanAnalyticsRow> PlanAnalytics { get; private set; } = [];

    public IReadOnlyList<TokenUsageTrendPoint> TokenUsageTrend { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var analyticsRange = CreateAnalyticsRange(Period, now);
        SelectedPeriod = analyticsRange.Period;
        SelectedPeriodLabel = analyticsRange.Label;

        var plans = await _dbContext.SubscriptionPlans
            .AsNoTracking()
            .OrderBy(plan => plan.MonthlyPrice)
            .ThenBy(plan => plan.PlanName)
            .Select(plan => new PlanSnapshot(plan.PlanId, plan.PlanName, plan.MonthlyPrice))
            .ToListAsync(cancellationToken);

        var activeSubscriptions = await _dbContext.StudentSubscriptions
            .AsNoTracking()
            .Where(subscription =>
                subscription.SubscriptionStatus == "active" &&
                (subscription.ExpiresAt == null || subscription.ExpiresAt > now))
            .Select(subscription => new SubscriptionSnapshot(subscription.UserId, subscription.PlanId))
            .ToListAsync(cancellationToken);

        var paidTransactions = await _dbContext.StripeCheckoutTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.PaymentStatus == "paid" &&
                (transaction.ConfirmedAt ?? transaction.CreatedAt) >= analyticsRange.Start &&
                (transaction.ConfirmedAt ?? transaction.CreatedAt) < analyticsRange.End)
            .Select(transaction => new PaidTransactionSnapshot(transaction.UserId, transaction.PlanId, transaction.Amount))
            .ToListAsync(cancellationToken);

        var tokenUsage = await _dbContext.TokenUsageLogs
            .AsNoTracking()
            .Where(usage => usage.UsedAt >= analyticsRange.Start && usage.UsedAt < analyticsRange.End)
            .Select(usage => new TokenUsageSnapshot(usage.UsedAt, usage.TotalTokens))
            .ToListAsync(cancellationToken);

        ActiveSubscriberCount = activeSubscriptions
            .Select(subscription => subscription.UserId)
            .Distinct()
            .Count();
        PaidPurchaseCount = paidTransactions.Count;
        PaidRevenue = paidTransactions.Sum(transaction => transaction.Amount);
        TokenUsageTrend = BuildTokenUsageTrend(analyticsRange, tokenUsage);

        var activeByPlan = activeSubscriptions
            .GroupBy(subscription => subscription.PlanId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.UserId).Distinct().Count());
        var paidByPlan = paidTransactions
            .GroupBy(transaction => transaction.PlanId)
            .ToDictionary(group => group.Key, group => new
            {
                Count = group.Count(),
                Revenue = group.Sum(item => item.Amount)
            });

        PlanAnalytics = plans
            .Select(plan =>
            {
                paidByPlan.TryGetValue(plan.PlanId, out var paid);
                return new PlanAnalyticsRow(
                    plan.PlanName,
                    plan.MonthlyPrice,
                    activeByPlan.GetValueOrDefault(plan.PlanId),
                    paid?.Count ?? 0,
                    paid?.Revenue ?? 0);
            })
            .ToArray();
    }

    public sealed record PlanAnalyticsRow(
        string PlanName,
        decimal MonthlyPrice,
        int ActiveStudentCount,
        int PaidPurchaseCount,
        decimal PaidRevenue);

    private sealed record PlanSnapshot(Guid PlanId, string PlanName, decimal MonthlyPrice);

    private sealed record SubscriptionSnapshot(Guid UserId, Guid PlanId);

    private sealed record PaidTransactionSnapshot(Guid UserId, Guid PlanId, decimal Amount);

    private sealed record TokenUsageSnapshot(DateTime UsedAt, long TotalTokens);

    public sealed record TokenUsageTrendPoint(string Label, long TotalTokens, decimal Percentage);

    private sealed record AnalyticsRange(string Period, string Label, DateTime Start, DateTime End, int SlotCount, Func<DateTime, int> GetSlotIndex);

    private static AnalyticsRange CreateAnalyticsRange(string? requestedPeriod, DateTime now)
    {
        return requestedPeriod?.Trim().ToLowerInvariant() switch
        {
            "month" => CreateMonthRange(now),
            "year" => CreateYearRange(now),
            _ => CreateDayRange(now)
        };
    }

    private static AnalyticsRange CreateDayRange(DateTime now)
    {
        var start = now.Date;
        return new AnalyticsRange("day", "Today", start, start.AddDays(1), 24, usedAt => usedAt.Hour);
    }

    private static AnalyticsRange CreateMonthRange(DateTime now)
    {
        var start = new DateTime(now.Year, now.Month, 1);
        return new AnalyticsRange("month", now.ToString("MMMM yyyy"), start, start.AddMonths(1), DateTime.DaysInMonth(now.Year, now.Month), usedAt => usedAt.Day - 1);
    }

    private static AnalyticsRange CreateYearRange(DateTime now)
    {
        var start = new DateTime(now.Year, 1, 1);
        return new AnalyticsRange("year", now.Year.ToString(), start, start.AddYears(1), 12, usedAt => usedAt.Month - 1);
    }

    private static IReadOnlyList<TokenUsageTrendPoint> BuildTokenUsageTrend(AnalyticsRange range, IReadOnlyList<TokenUsageSnapshot> tokenUsage)
    {
        var totals = new long[range.SlotCount];
        foreach (var usage in tokenUsage)
        {
            var slotIndex = range.GetSlotIndex(usage.UsedAt);
            if (slotIndex >= 0 && slotIndex < totals.Length)
            {
                totals[slotIndex] += usage.TotalTokens;
            }
        }

        var maximum = totals.DefaultIfEmpty(0).Max();
        return totals.Select((total, index) => new TokenUsageTrendPoint(
            GetTrendLabel(range.Period, index),
            total,
            maximum == 0 ? 0 : decimal.Round(total * 100m / maximum, 2)))
            .ToArray();
    }

    private static string GetTrendLabel(string period, int index) => period switch
    {
        "day" => $"{index:00}:00",
        "month" => (index + 1).ToString(),
        "year" => new DateTime(2000, index + 1, 1).ToString("MMM"),
        _ => string.Empty
    };
}
