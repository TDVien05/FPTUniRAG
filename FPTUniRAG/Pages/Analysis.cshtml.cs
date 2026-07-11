using FPTUniRAG.DataAccessLayer.Context;
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

    public IReadOnlyList<PlanAnalyticsRow> PlanAnalytics { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
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
            .Where(transaction => transaction.PaymentStatus == "paid")
            .Select(transaction => new PaidTransactionSnapshot(transaction.UserId, transaction.PlanId, transaction.Amount))
            .ToListAsync(cancellationToken);

        ActiveSubscriberCount = activeSubscriptions
            .Select(subscription => subscription.UserId)
            .Distinct()
            .Count();
        PaidPurchaseCount = paidTransactions.Count;
        PaidRevenue = paidTransactions.Sum(transaction => transaction.Amount);

        var activeByPlan = activeSubscriptions
            .GroupBy(subscription => subscription.PlanId)
            .ToDictionary(group => group.Key, group => group.Select(item => item.UserId).Distinct().Count());
        var paidByPlan = paidTransactions
            .GroupBy(transaction => transaction.PlanId)
            .ToDictionary(group => group.Key, group => new
            {
                Count = group.Select(item => item.UserId).Distinct().Count(),
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
}
